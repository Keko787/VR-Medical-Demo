using UnityEngine;
using UnityEngine.Rendering;

namespace VRShootingGallery.TrackingDemo
{
    /// <summary>
    /// A sheet of water the size of the CAVE floor. Anything that touches it (<see cref="Splash"/>)
    /// starts a ring that spreads, meets other rings, bounces off the walls and fades. The surface is
    /// a height field run as a discrete wave equation, and the mesh is rebuilt from it every frame:
    /// vertices displaced, normals derived, and the height written into vertex colour for the shader
    /// to tint.
    /// </summary>
    /// <remarks>
    /// <b>Why simulate rather than draw rings.</b> Interference and reflection are what make water
    /// read as water. A ring that crosses the room, comes back off the front wall and cuts through
    /// the next one is free here; drawing that with analytic rings would mean adding, clipping and
    /// fading each one by hand, and the result still would not cross itself right.
    ///
    /// <b>Why a fixed step.</b> This scheme's wave speed is a fixed number of cells per step, so if
    /// steps were tied to frames the water would slow down whenever the frame rate did — and in the
    /// CAVE the Editor runs at about 14 FPS. An accumulator keeps the metres-per-second constant, and
    /// a cap on steps per frame turns a stall into dropped time rather than a spiral of catching up.
    ///
    /// <b>Why the height rides in vertex colour.</b> The floor screen is at floor level, so a few
    /// centimetres of relief is only part of what the viewer sees; the shader tints crests, troughs
    /// and steep slopes as well. Vertex colour is the cheapest channel to carry that in: it is set in
    /// the same call as the vertices and needs no texture upload.
    /// </remarks>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DefaultExecutionOrder(50)]   // after anything that splashes this frame
    public class RippleFloor : MonoBehaviour
    {
        [Header("Sheet")]
        [SerializeField, Tooltip("Extent along local X, metres. The CAVE floor screen is 3.556 wide.")]
        float m_Width = 3.556f;

        [SerializeField, Tooltip("Extent along local Z, metres. The CAVE floor screen is 2.2225 deep.")]
        float m_Depth = 2.2225f;

        [SerializeField, Tooltip("Grid spacing, metres. Rings are drawn as displaced vertices, so this has " +
            "to be well under a ring's wavelength for the rings to look round.")]
        float m_CellSize = 0.025f;

        [Header("Water")]
        [SerializeField, Tooltip("Simulation steps per second. The wave speed is fixed per step, so with the " +
            "stiffness this sets the speed in metres per second.")]
        float m_StepsPerSecond = 60f;

        [SerializeField, Range(0.1f, 1.9f), Tooltip("Pull toward the neighbours' average, per step. Wave " +
            "speed goes with its square root; at 2 and above the scheme blows up.")]
        float m_Stiffness = 1f;

        [SerializeField, Range(0.9f, 1f), Tooltip("Velocity kept per step. 0.99 lets a ring cross the room " +
            "and come back off the wall before it has faded.")]
        float m_Damping = 0.99f;

        [SerializeField, Tooltip("Height at which the shader's crest colour saturates, metres. Troughs " +
            "saturate at the same depth.")]
        float m_CrestHeight = 0.02f;

        [SerializeField, Tooltip("Hard cap on displacement either way, metres. A big splash, or two rings " +
            "meeting, briefly overshoots any sensible crest.")]
        float m_MaxHeight = 0.06f;

        [SerializeField, Tooltip("How steep a slope reads as foam in the shader. Dimensionless gain on the " +
            "surface gradient.")]
        float m_FoamGain = 1.5f;

        [SerializeField, Tooltip("Most steps run in one frame. A frame longer than this many steps drops " +
            "the remainder rather than trying to catch up.")]
        int m_MaxStepsPerFrame = 8;

        int m_Nx, m_Nz;
        float m_StepX, m_StepZ;   // the grid's actual spacing: the sheet is a whole number of cells
        float[] m_Height, m_Velocity;

        Mesh m_Mesh;
        Vector3[] m_Vertices, m_Normals;
        Color32[] m_Colors;

        float m_Accumulator;
        bool m_Dirty;

        public float Width => m_Width;
        public float Depth => m_Depth;
        public float CellSize => m_CellSize;

        void Awake()
        {
            // A fresh mesh, never the one the builder saved for the Editor to show: this one is
            // rewritten every frame, and that would otherwise be a saved asset being edited.
            m_Mesh = BuildMesh(m_Width, m_Depth, m_CellSize, out m_Nx, out m_Nz);
            m_Mesh.name = "RippleFloor (runtime)";
            m_Mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = m_Mesh;

            m_StepX = m_Width / (m_Nx - 1);
            m_StepZ = m_Depth / (m_Nz - 1);

            int count = m_Nx * m_Nz;
            m_Height = new float[count];
            m_Velocity = new float[count];
            m_Vertices = m_Mesh.vertices;
            m_Normals = m_Mesh.normals;
            m_Colors = m_Mesh.colors32;
        }

        void OnDestroy()
        {
            if (m_Mesh != null)
                Destroy(m_Mesh);
        }

        void Update()
        {
            float step = 1f / Mathf.Max(1f, m_StepsPerSecond);
            m_Accumulator += Time.deltaTime;

            int steps = 0;
            while (m_Accumulator >= step && steps < m_MaxStepsPerFrame)
            {
                Step();
                m_Accumulator -= step;
                steps++;
            }

            // Still behind after the cap: the frame stalled. Forget the backlog.
            if (m_Accumulator >= step)
                m_Accumulator = 0f;

            if (steps > 0 || m_Dirty)
                Upload();
        }

        // ------------------------------------------------------------------ public surface

        /// <summary>
        /// True if <paramref name="worldPoint"/>, dropped straight onto the sheet, lands on it. Height
        /// above the sheet is ignored — this is "is the viewer over the water", not "in it".
        /// </summary>
        public bool Contains(Vector3 worldPoint)
        {
            var local = transform.InverseTransformPoint(worldPoint);
            return Mathf.Abs(local.x) <= m_Width * 0.5f && Mathf.Abs(local.z) <= m_Depth * 0.5f;
        }

        /// <summary>
        /// Presses the surface down under <paramref name="worldPoint"/> — a smooth dish
        /// <paramref name="radius"/> metres across and <paramref name="amplitude"/> metres deep. The
        /// rebound is what makes the ring. The point is projected onto the sheet, so callers can pass
        /// the head itself and get the splash under the feet.
        /// </summary>
        public void Splash(Vector3 worldPoint, float amplitude, float radius)
        {
            if (m_Height == null || radius <= 0f)
                return;

            var local = transform.InverseTransformPoint(worldPoint);
            float cx = (local.x + m_Width * 0.5f) / m_StepX;
            float cz = (local.z + m_Depth * 0.5f) / m_StepZ;
            float cells = radius / m_StepX;
            int reach = Mathf.CeilToInt(cells);

            // Edge cells are the boundary and stay at zero, so the loop stops one short of them.
            int i0 = Mathf.Max(1, Mathf.FloorToInt(cx) - reach);
            int i1 = Mathf.Min(m_Nx - 2, Mathf.CeilToInt(cx) + reach);
            int j0 = Mathf.Max(1, Mathf.FloorToInt(cz) - reach);
            int j1 = Mathf.Min(m_Nz - 2, Mathf.CeilToInt(cz) + reach);

            for (int j = j0; j <= j1; j++)
            {
                float dz = j - cz;
                for (int i = i0; i <= i1; i++)
                {
                    float dx = i - cx;
                    float d = Mathf.Sqrt(dx * dx + dz * dz) / cells;
                    if (d >= 1f)
                        continue;

                    // Raised cosine: flat at the centre, tangent to the surface at the rim, so the
                    // dish has no edge for the wave to pick up as a second, sharper ring.
                    float weight = 0.5f * (1f + Mathf.Cos(Mathf.PI * d));
                    m_Height[j * m_Nx + i] -= amplitude * weight;
                }
            }

            m_Dirty = true;
        }

        /// <summary>Flattens the water instantly.</summary>
        public void Still()
        {
            if (m_Height == null)
                return;

            System.Array.Clear(m_Height, 0, m_Height.Length);
            System.Array.Clear(m_Velocity, 0, m_Velocity.Length);
            m_Dirty = true;
        }

        // ------------------------------------------------------------------ simulation

        /// <summary>
        /// One step of the wave equation on the grid: every interior cell is pulled toward the average
        /// of its four neighbours, and the velocity that builds up is damped. Two passes, so every
        /// cell sees the same generation of heights — updating in place would make the wave lean in
        /// the direction the loop runs.
        /// </summary>
        void Step()
        {
            int nx = m_Nx, nz = m_Nz;
            float[] h = m_Height, v = m_Velocity;
            float stiffness = m_Stiffness, damping = m_Damping;

            for (int j = 1; j < nz - 1; j++)
            {
                int row = j * nx;
                for (int i = 1; i < nx - 1; i++)
                {
                    int c = row + i;
                    float average = (h[c - 1] + h[c + 1] + h[c - nx] + h[c + nx]) * 0.25f;
                    v[c] = (v[c] + (average - h[c]) * stiffness) * damping;
                }
            }

            float max = m_MaxHeight;
            for (int c = 0; c < h.Length; c++)
                h[c] = Mathf.Clamp(h[c] + v[c], -max, max);
        }

        /// <summary>
        /// Writes the height field into the mesh: Y from the height, the normal from the central
        /// difference of the four neighbours, and vertex colour R = height (0 trough, ½ rest,
        /// 1 crest), G = slope, for the shader.
        /// </summary>
        void Upload()
        {
            int nx = m_Nx, nz = m_Nz;
            float[] h = m_Height;
            float twoCells = 2f * m_CellSize;
            float crestInverse = 1f / Mathf.Max(0.001f, m_CrestHeight);
            float foamGain = m_FoamGain / twoCells;

            for (int j = 0; j < nz; j++)
            {
                int row = j * nx;
                int down = j > 0 ? -nx : 0;
                int up = j < nz - 1 ? nx : 0;

                for (int i = 0; i < nx; i++)
                {
                    int c = row + i;
                    int left = i > 0 ? -1 : 0;
                    int right = i < nx - 1 ? 1 : 0;

                    float y = h[c];
                    float dx = h[c + left] - h[c + right];
                    float dz = h[c + down] - h[c + up];

                    m_Vertices[c].y = y;

                    // Normal of y = f(x, z) is (−∂f/∂x, 1, −∂f/∂z); the differences above are already
                    // the negatives, over two cells.
                    var n = new Vector3(dx, twoCells, dz);
                    m_Normals[c] = n / n.magnitude;

                    float height = Mathf.Clamp01(0.5f + 0.5f * y * crestInverse);
                    float slope = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz) * foamGain);
                    m_Colors[c] = new Color32((byte)(height * 255f), (byte)(slope * 255f), 0, 255);
                }
            }

            m_Mesh.SetVertices(m_Vertices, 0, m_Vertices.Length, MeshUpdateFlags.DontRecalculateBounds);
            m_Mesh.SetNormals(m_Normals);
            m_Mesh.SetColors(m_Colors);
            m_Dirty = false;
        }

        // ------------------------------------------------------------------ mesh

        /// <summary>
        /// A flat grid centred on the origin, facing up, with the rest-state colour already in the
        /// vertices. The builder saves one of these for the Editor to show; <see cref="Awake"/> makes
        /// its own to animate.
        /// </summary>
        public static Mesh BuildMesh(float width, float depth, float cellSize, out int nx, out int nz)
        {
            cellSize = Mathf.Max(0.005f, cellSize);
            nx = Mathf.Max(2, Mathf.RoundToInt(width / cellSize) + 1);
            nz = Mathf.Max(2, Mathf.RoundToInt(depth / cellSize) + 1);

            int count = nx * nz;
            var vertices = new Vector3[count];
            var normals = new Vector3[count];
            var uvs = new Vector2[count];
            var colors = new Color32[count];

            var rest = new Color32(128, 0, 0, 255);
            float x0 = -width * 0.5f, z0 = -depth * 0.5f;
            float dx = width / (nx - 1), dz = depth / (nz - 1);

            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    int c = j * nx + i;
                    vertices[c] = new Vector3(x0 + i * dx, 0f, z0 + j * dz);
                    normals[c] = Vector3.up;
                    uvs[c] = new Vector2(i / (float)(nx - 1), j / (float)(nz - 1));
                    colors[c] = rest;
                }
            }

            // Two triangles per cell, wound so the face points up: Unity's front face is clockwise
            // seen from the side the normal leaves.
            var triangles = new int[(nx - 1) * (nz - 1) * 6];
            int t = 0;
            for (int j = 0; j < nz - 1; j++)
            {
                for (int i = 0; i < nx - 1; i++)
                {
                    int v00 = j * nx + i;
                    int v10 = v00 + 1;
                    int v01 = v00 + nx;
                    int v11 = v01 + 1;

                    triangles[t++] = v00; triangles[t++] = v01; triangles[t++] = v10;
                    triangles[t++] = v10; triangles[t++] = v01; triangles[t++] = v11;
                }
            }

            var mesh = new Mesh
            {
                name = "RippleFloor",
                indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);

            // Fixed rather than recalculated per frame. Generous in Y so a crest never gets culled
            // at the edge of a viewport.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(width, 0.5f, depth));
            return mesh;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(m_Width, 0f, m_Depth));
        }
    }
}
