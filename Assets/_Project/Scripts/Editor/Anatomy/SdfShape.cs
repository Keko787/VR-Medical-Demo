using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace VRShootingGallery.EditorTools.Anatomy
{
    /// <summary>
    /// A solid made of round cones, ellipsoids and height-field sheets, held as a signed distance field
    /// and meshed with surface nets. This is how the organic parts of the ear are made — the ossicles,
    /// the auricle, the bony labyrinth, the nerve bundle — because a smooth union of simple pieces
    /// reads as one grown shape, where the same pieces as separate meshes read as a kit glued together.
    /// </summary>
    /// <remarks>
    /// Every primitive carries a part number, and <see cref="Polygonize"/> hands back one mesh per part:
    /// each quad goes to the part whose primitive is nearest its centre. The labyrinth is meshed once,
    /// as a single surface, and split into cochlea, vestibule and canals that way, so the seams between
    /// them are exact — the same vertices, the same normals — while each can still be lit, labelled and
    /// picked on its own.
    ///
    /// Primitives are grouped. Inside a group they are hard-unioned, which is what lets a chain of
    /// round cones be one smooth tube without the next turn of the cochlea melting into it; between
    /// groups they are smooth-unioned by <see cref="Blend"/>, which is what lets a canal flare into the
    /// vestibule. Subtractive primitives are applied after the union and cut holes.
    ///
    /// Units are whatever the caller works in — the ear is built in millimetres. The field is only
    /// exact near the surface: a spatial hash hands each sample the primitives within reach of it, and
    /// a sample out of reach of all of them is simply "outside", which is all surface nets needs.
    /// </remarks>
    sealed class SdfShape
    {
        enum Kind : byte { RoundCone, Ellipsoid, Sheet }

        struct Primitive
        {
            public Kind Kind;
            public int Group;
            public int Part;
            public bool Subtract;
            public float U0, U1;
            public Vector3 Min, Max;

            // Round cone.
            public Vector3 A, BA;
            public float L2, RR, A2, IL2, R1, R2;

            // Ellipsoid.
            public Vector3 Centre, Radii;
            public Quaternion Inverse;

            // Sheet.
            public int Sheet;
        }

        readonly List<Primitive> m_Primitives = new();
        readonly List<HeightSheet> m_Sheets = new();
        int m_GroupCount;

        // Spatial hash, compressed-row: the primitives of cell c are
        // m_CellItems[m_CellStart[c] .. m_CellStart[c + 1]).
        Primitive[] m_Items;
        int[] m_CellStart;
        int[] m_CellItems;
        Vector3 m_HashMin;
        float m_HashCell;
        int m_HashX, m_HashY, m_HashZ;
        float m_Band;

        /// <summary>Smooth-union radius between groups. Zero is a plain union.</summary>
        public float Blend { get; set; }

        /// <summary>Starts a new group; primitives added after this hard-union with each other.</summary>
        public int NewGroup() => m_GroupCount++;

        public int PrimitiveCount => m_Primitives.Count;

        // ================================================================== building

        /// <summary>
        /// A round cone: a capsule whose two end spheres differ in radius. Chained end to end these make
        /// a tube of varying thickness with no seam, which is what nearly everything here is.
        /// </summary>
        public void RoundCone(Vector3 a, Vector3 b, float ra, float rb, int part, float u0 = 0f, float u1 = 0f,
            bool subtract = false)
        {
            EnsureGroup();

            // A segment too short to have a direction is just a sphere; nudging it keeps the
            // closed-form distance below from dividing by zero.
            if ((b - a).sqrMagnitude < 1e-10f)
                b = a + Vector3.up * 1e-5f;

            // One cone end swallowing the other has no side surface, and the closed form assumes one.
            float length = Vector3.Distance(a, b);
            if (Mathf.Abs(ra - rb) >= length)
            {
                if (ra >= rb) rb = ra - length * 0.999f;
                else ra = rb - length * 0.999f;
            }

            var ba = b - a;
            float l2 = Vector3.Dot(ba, ba);
            float rr = ra - rb;

            var p = new Primitive
            {
                Kind = Kind.RoundCone,
                Group = m_GroupCount - 1,
                Part = part,
                Subtract = subtract,
                U0 = u0,
                U1 = u1,
                A = a,
                BA = ba,
                L2 = l2,
                RR = rr,
                A2 = l2 - rr * rr,
                IL2 = 1f / l2,
                R1 = ra,
                R2 = rb,
                Min = Vector3.Min(a - Vector3.one * ra, b - Vector3.one * rb),
                Max = Vector3.Max(a + Vector3.one * ra, b + Vector3.one * rb),
            };
            m_Primitives.Add(p);
        }

        /// <summary>
        /// A tube along <paramref name="points"/>, one round cone per span, with the radius given as a
        /// function of arc-length fraction and the part's parameter running <paramref name="u0"/> →
        /// <paramref name="u1"/> along it. Stays in the current group, so the spans hard-union.
        /// </summary>
        public void Chain(IReadOnlyList<Vector3> points, Func<float, float> radius, int part, float u0 = 0f,
            float u1 = 1f, bool subtract = false)
        {
            if (points.Count < 2)
                return;

            var fractions = Curves.ArcFractions(points);
            for (int i = 0; i < points.Count - 1; i++)
            {
                float fa = fractions[i], fb = fractions[i + 1];
                RoundCone(points[i], points[i + 1], radius(fa), radius(fb), part,
                    Mathf.Lerp(u0, u1, fa), Mathf.Lerp(u0, u1, fb), subtract);
            }
        }

        public void Ellipsoid(Vector3 centre, Quaternion rotation, Vector3 radii, int part, float u = 0f,
            bool subtract = false)
        {
            EnsureGroup();

            // The rotated box's half-extent along each world axis.
            var m = Matrix4x4.Rotate(rotation);
            var extent = new Vector3(
                Mathf.Abs(m.m00) * radii.x + Mathf.Abs(m.m01) * radii.y + Mathf.Abs(m.m02) * radii.z,
                Mathf.Abs(m.m10) * radii.x + Mathf.Abs(m.m11) * radii.y + Mathf.Abs(m.m12) * radii.z,
                Mathf.Abs(m.m20) * radii.x + Mathf.Abs(m.m21) * radii.y + Mathf.Abs(m.m22) * radii.z);

            m_Primitives.Add(new Primitive
            {
                Kind = Kind.Ellipsoid,
                Group = m_GroupCount - 1,
                Part = part,
                Subtract = subtract,
                U0 = u,
                U1 = u,
                Centre = centre,
                Radii = radii,
                Inverse = Quaternion.Inverse(rotation),
                Min = centre - extent,
                Max = centre + extent,
            });
        }

        public void Sheet(HeightSheet sheet, int part, float u = 0f)
        {
            EnsureGroup();
            sheet.Bounds(out var min, out var max);

            m_Sheets.Add(sheet);
            m_Primitives.Add(new Primitive
            {
                Kind = Kind.Sheet,
                Group = m_GroupCount - 1,
                Part = part,
                U0 = u,
                U1 = u,
                Sheet = m_Sheets.Count - 1,
                Min = min,
                Max = max,
            });
        }

        void EnsureGroup()
        {
            if (m_GroupCount == 0)
                m_GroupCount = 1;
        }

        // ================================================================== evaluation

        static float Distance(in Primitive p, List<HeightSheet> sheets, Vector3 x, out float t)
        {
            switch (p.Kind)
            {
                case Kind.RoundCone:
                {
                    // Inigo Quilez's round cone: one square root, exact everywhere.
                    var pa = x - p.A;
                    float y = Vector3.Dot(pa, p.BA);
                    float z = y - p.L2;
                    var q = pa * p.L2 - p.BA * y;
                    float x2 = Vector3.Dot(q, q);
                    float y2 = y * y * p.L2;
                    float z2 = z * z * p.L2;
                    float k = Mathf.Sign(p.RR) * p.RR * p.RR * x2;

                    t = Mathf.Clamp01(y * p.IL2);

                    if (Mathf.Sign(z) * p.A2 * z2 > k)
                        return Mathf.Sqrt(x2 + z2) * p.IL2 - p.R2;
                    if (Mathf.Sign(y) * p.A2 * y2 < k)
                        return Mathf.Sqrt(x2 + y2) * p.IL2 - p.R1;
                    return (Mathf.Sqrt(x2 * p.A2 * p.IL2) + y * p.RR) * p.IL2 - p.R1;
                }

                case Kind.Ellipsoid:
                {
                    t = 0f;
                    var local = p.Inverse * (x - p.Centre);
                    var r = p.Radii;
                    var n0 = new Vector3(local.x / r.x, local.y / r.y, local.z / r.z);
                    var n1 = new Vector3(local.x / (r.x * r.x), local.y / (r.y * r.y), local.z / (r.z * r.z));
                    float k0 = n0.magnitude;
                    float k1 = n1.magnitude;
                    return k1 < 1e-8f ? -Mathf.Min(r.x, Mathf.Min(r.y, r.z)) : k0 * (k0 - 1f) / k1;
                }

                default:
                    t = 0f;
                    return sheets[p.Sheet].Distance(x);
            }
        }

        /// <summary>Polynomial smooth minimum: blends two distances over a band <paramref name="k"/> wide.</summary>
        public static float SmoothMin(float a, float b, float k)
        {
            if (k <= 0f)
                return Mathf.Min(a, b);

            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
            return Mathf.Min(a, b) - h * h * k * 0.25f;
        }

        public static float SmoothMax(float a, float b, float k) => -SmoothMin(-a, -b, k);

        int CellOf(Vector3 x)
        {
            int i = Mathf.FloorToInt((x.x - m_HashMin.x) / m_HashCell);
            int j = Mathf.FloorToInt((x.y - m_HashMin.y) / m_HashCell);
            int k = Mathf.FloorToInt((x.z - m_HashMin.z) / m_HashCell);
            if (i < 0 || j < 0 || k < 0 || i >= m_HashX || j >= m_HashY || k >= m_HashZ)
                return -1;

            return (k * m_HashY + j) * m_HashX + i;
        }

        /// <summary>
        /// Signed distance at <paramref name="x"/>, and the part and parameter of the nearest primitive.
        /// Only valid after <see cref="Prepare"/>.
        /// </summary>
        public float Evaluate(Vector3 x, out int part, out float u)
        {
            part = -1;
            u = 0f;

            int cell = CellOf(x);
            if (cell < 0)
                return m_Band;

            Span<float> groups = stackalloc float[m_GroupCount];
            for (int g = 0; g < m_GroupCount; g++)
                groups[g] = float.MaxValue;

            float nearest = float.MaxValue;
            float cut = float.MinValue;

            for (int n = m_CellStart[cell], end = m_CellStart[cell + 1]; n < end; n++)
            {
                ref readonly var p = ref m_Items[m_CellItems[n]];
                float d = Distance(in p, m_Sheets, x, out float t);

                if (p.Subtract)
                {
                    cut = Mathf.Max(cut, -d);
                    continue;
                }

                if (d < groups[p.Group])
                    groups[p.Group] = d;

                if (d < nearest)
                {
                    nearest = d;
                    part = p.Part;
                    u = Mathf.Lerp(p.U0, p.U1, t);
                }
            }

            float result = float.MaxValue;
            for (int g = 0; g < m_GroupCount; g++)
            {
                if (groups[g] == float.MaxValue)
                    continue;

                result = result == float.MaxValue ? groups[g] : SmoothMin(result, groups[g], Blend);
            }

            if (result == float.MaxValue)
                return m_Band;

            if (cut > float.MinValue)
                result = SmoothMax(result, cut, Blend * 0.5f);

            return result;
        }

        public float Evaluate(Vector3 x) => Evaluate(x, out _, out _);

        /// <summary>
        /// The parameter of the nearest primitive belonging to <paramref name="part"/> — what a vertex
        /// on that part's side of a seam should carry, even when another part's primitive is nearer.
        /// </summary>
        public float ParameterOnPart(Vector3 x, int part)
        {
            int cell = CellOf(x);
            if (cell < 0)
                return 0f;

            float nearest = float.MaxValue;
            float u = 0f;
            for (int n = m_CellStart[cell], end = m_CellStart[cell + 1]; n < end; n++)
            {
                ref readonly var p = ref m_Items[m_CellItems[n]];
                if (p.Subtract || p.Part != part)
                    continue;

                float d = Distance(in p, m_Sheets, x, out float t);
                if (d < nearest)
                {
                    nearest = d;
                    u = Mathf.Lerp(p.U0, p.U1, t);
                }
            }

            return u;
        }

        Vector3 Gradient(Vector3 x, float h)
        {
            var dx = new Vector3(h, 0f, 0f);
            var dy = new Vector3(0f, h, 0f);
            var dz = new Vector3(0f, 0f, h);
            return new Vector3(
                Evaluate(x + dx) - Evaluate(x - dx),
                Evaluate(x + dy) - Evaluate(x - dy),
                Evaluate(x + dz) - Evaluate(x - dz)) / (2f * h);
        }

        // ================================================================== hash

        /// <summary>
        /// Buckets every primitive into the cells within <paramref name="band"/> of it. A sample then
        /// only has to test its own cell's list — and every primitive not on it is further than the band
        /// away, too far to change the sign or the blend.
        /// </summary>
        void Prepare(Vector3 min, Vector3 max, float band, float cell)
        {
            m_Items = m_Primitives.ToArray();
            m_Band = band;
            m_HashCell = cell;
            m_HashMin = min;
            m_HashX = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / cell));
            m_HashY = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / cell));
            m_HashZ = Mathf.Max(1, Mathf.CeilToInt((max.z - min.z) / cell));

            int cells = m_HashX * m_HashY * m_HashZ;
            var counts = new int[cells + 1];

            void Cover(in Primitive p, Action<int> visit)
            {
                var lo = p.Min - Vector3.one * band;
                var hi = p.Max + Vector3.one * band;
                int i0 = Mathf.Clamp(Mathf.FloorToInt((lo.x - min.x) / cell), 0, m_HashX - 1);
                int j0 = Mathf.Clamp(Mathf.FloorToInt((lo.y - min.y) / cell), 0, m_HashY - 1);
                int k0 = Mathf.Clamp(Mathf.FloorToInt((lo.z - min.z) / cell), 0, m_HashZ - 1);
                int i1 = Mathf.Clamp(Mathf.FloorToInt((hi.x - min.x) / cell), 0, m_HashX - 1);
                int j1 = Mathf.Clamp(Mathf.FloorToInt((hi.y - min.y) / cell), 0, m_HashY - 1);
                int k1 = Mathf.Clamp(Mathf.FloorToInt((hi.z - min.z) / cell), 0, m_HashZ - 1);

                for (int k = k0; k <= k1; k++)
                for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    visit((k * m_HashY + j) * m_HashX + i);
            }

            for (int n = 0; n < m_Items.Length; n++)
                Cover(in m_Items[n], c => counts[c + 1]++);

            for (int c = 0; c < cells; c++)
                counts[c + 1] += counts[c];

            m_CellStart = (int[])counts.Clone();
            m_CellItems = new int[counts[cells]];
            var fill = new int[cells];

            for (int n = 0; n < m_Items.Length; n++)
            {
                int item = n;
                Cover(in m_Items[n], c => m_CellItems[m_CellStart[c] + fill[c]++] = item);
            }
        }

        // ================================================================== surface nets

        /// <summary>
        /// Meshes the solid on a grid of <paramref name="voxel"/> spacing and returns one mesh per part.
        /// Vertices are pulled onto the true surface and take their normals from the field's gradient,
        /// so a coarse grid still gives smooth shading. The parameter <c>u</c> of each vertex goes into
        /// <c>uv.x</c>.
        /// </summary>
        public Dictionary<int, MeshData> Polygonize(float voxel)
        {
            var result = new Dictionary<int, MeshData>();
            if (m_Primitives.Count == 0)
                return result;

            // Bounds of the solid itself, padded so the outermost samples are always outside.
            var min = Vector3.one * float.MaxValue;
            var max = Vector3.one * float.MinValue;
            foreach (var p in m_Primitives)
            {
                if (p.Subtract)
                    continue;

                min = Vector3.Min(min, p.Min);
                max = Vector3.Max(max, p.Max);
            }

            var pad = Vector3.one * (voxel * 3f + Blend);
            min -= pad;
            max += pad;

            float band = Blend + voxel * 3f;
            Prepare(min, max, band, Mathf.Max(voxel * 8f, band));

            int nx = Mathf.CeilToInt((max.x - min.x) / voxel) + 1;
            int ny = Mathf.CeilToInt((max.y - min.y) / voxel) + 1;
            int nz = Mathf.CeilToInt((max.z - min.z) / voxel) + 1;
            int Index(int i, int j, int k) => (k * ny + j) * nx + i;

            var field = new float[nx * ny * nz];
            Parallel.For(0, nz, k =>
            {
                for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                    field[Index(i, j, k)] = Evaluate(min + new Vector3(i, j, k) * voxel);
            });

            // ------------------------------------------------ one vertex per crossed cell

            int cx = nx - 1, cy = ny - 1, cz = nz - 1;
            int CellIndex(int i, int j, int k) => (k * cy + j) * cx + i;

            var cellVertex = new int[cx * cy * cz];
            var positions = new List<Vector3>();
            var cellOf = new List<Vector3Int>();
            Span<float> corner = stackalloc float[8];

            for (int k = 0; k < cz; k++)
            for (int j = 0; j < cy; j++)
            for (int i = 0; i < cx; i++)
            {
                int inside = 0;
                for (int b = 0; b < 8; b++)
                {
                    corner[b] = field[Index(i + (b & 1), j + ((b >> 1) & 1), k + ((b >> 2) & 1))];
                    if (corner[b] < 0f)
                        inside++;
                }

                if (inside == 0 || inside == 8)
                {
                    cellVertex[CellIndex(i, j, k)] = -1;
                    continue;
                }

                // Average of where the surface crosses the cell's edges.
                var sum = Vector3.zero;
                int crossings = 0;
                for (int b = 0; b < 8; b++)
                for (int axis = 1; axis <= 4; axis <<= 1)
                {
                    if ((b & axis) != 0)
                        continue;

                    int c = b | axis;
                    float da = corner[b], db = corner[c];
                    if (da < 0f == db < 0f)
                        continue;

                    float t = da / (da - db);
                    var pa = new Vector3(b & 1, (b >> 1) & 1, (b >> 2) & 1);
                    var pb = new Vector3(c & 1, (c >> 1) & 1, (c >> 2) & 1);
                    sum += Vector3.Lerp(pa, pb, t);
                    crossings++;
                }

                cellVertex[CellIndex(i, j, k)] = positions.Count;
                positions.Add(min + (new Vector3(i, j, k) + sum / crossings) * voxel);
                cellOf.Add(new Vector3Int(i, j, k));
            }

            // ------------------------------------------------ onto the surface, and normals

            var normals = new Vector3[positions.Count];
            var projected = positions.ToArray();
            float h = voxel * 0.5f;

            Parallel.For(0, projected.Length, v =>
            {
                var p = projected[v];
                var cellMin = min + (Vector3)cellOf[v] * voxel - Vector3.one * (voxel * 0.5f);
                var cellMax = cellMin + Vector3.one * (voxel * 2f);

                for (int step = 0; step < 3; step++)
                {
                    var g = Gradient(p, h);
                    float g2 = g.sqrMagnitude;
                    if (g2 < 1e-8f)
                        break;

                    p -= g * (Evaluate(p) / g2);

                    // Kept to the neighbourhood of its own cell: a vertex that wanders into the next
                    // one folds the quads around it.
                    p = Vector3.Max(cellMin, Vector3.Min(cellMax, p));
                }

                projected[v] = p;
                var n = Gradient(p, h);
                normals[v] = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
            });

            // ------------------------------------------------ one quad per crossed grid edge

            var quads = new List<int>();
            for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                float d0 = field[Index(i, j, k)];

                for (int axis = 0; axis < 3; axis++)
                {
                    int i1 = i + (axis == 0 ? 1 : 0);
                    int j1 = j + (axis == 1 ? 1 : 0);
                    int k1 = k + (axis == 2 ? 1 : 0);
                    if (i1 >= nx || j1 >= ny || k1 >= nz)
                        continue;

                    float d1 = field[Index(i1, j1, k1)];
                    if (d0 < 0f == d1 < 0f)
                        continue;

                    // The four cells around this edge, in order round it.
                    int a, b, c, d;
                    if (axis == 0)
                    {
                        if (j < 1 || k < 1 || j >= cy || k >= cz) continue;
                        a = cellVertex[CellIndex(i, j - 1, k - 1)];
                        b = cellVertex[CellIndex(i, j, k - 1)];
                        c = cellVertex[CellIndex(i, j, k)];
                        d = cellVertex[CellIndex(i, j - 1, k)];
                    }
                    else if (axis == 1)
                    {
                        if (i < 1 || k < 1 || i >= cx || k >= cz) continue;
                        a = cellVertex[CellIndex(i - 1, j, k - 1)];
                        b = cellVertex[CellIndex(i, j, k - 1)];
                        c = cellVertex[CellIndex(i, j, k)];
                        d = cellVertex[CellIndex(i - 1, j, k)];
                    }
                    else
                    {
                        if (i < 1 || j < 1 || i >= cx || j >= cy) continue;
                        a = cellVertex[CellIndex(i - 1, j - 1, k)];
                        b = cellVertex[CellIndex(i, j - 1, k)];
                        c = cellVertex[CellIndex(i, j, k)];
                        d = cellVertex[CellIndex(i - 1, j, k)];
                    }

                    if (a < 0 || b < 0 || c < 0 || d < 0)
                        continue;

                    // Front faces point out of the solid, which is along +axis when the edge starts
                    // inside. Unity's front faces wind clockwise, and for a quad a→b→c→d that means
                    // cross(c − a, d − b) points the way the face does.
                    var outward = Vector3.zero;
                    outward[axis] = d0 < 0f ? 1f : -1f;
                    var facing = Vector3.Cross(projected[c] - projected[a], projected[d] - projected[b]);
                    if (Vector3.Dot(facing, outward) < 0f)
                        (b, d) = (d, b);

                    quads.Add(a);
                    quads.Add(b);
                    quads.Add(c);
                    quads.Add(d);
                }
            }

            // ------------------------------------------------ split by part

            int quadCount = quads.Count / 4;
            var quadPart = new int[quadCount];
            Parallel.For(0, quadCount, q =>
            {
                var centre = (projected[quads[q * 4]] + projected[quads[q * 4 + 1]] +
                              projected[quads[q * 4 + 2]] + projected[quads[q * 4 + 3]]) * 0.25f;
                Evaluate(centre, out int part, out _);
                quadPart[q] = part;
            });

            var remap = new int[projected.Length];
            var parts = new HashSet<int>(quadPart);

            foreach (int part in parts)
            {
                Array.Fill(remap, -1);
                var mesh = new MeshData();
                var used = new List<int>();

                int Vertex(int v)
                {
                    if (remap[v] >= 0)
                        return remap[v];

                    remap[v] = mesh.AddVertex(projected[v], normals[v], Vector2.zero);
                    used.Add(v);
                    return remap[v];
                }

                for (int q = 0; q < quadCount; q++)
                {
                    if (quadPart[q] != part)
                        continue;

                    int a = Vertex(quads[q * 4]);
                    int b = Vertex(quads[q * 4 + 1]);
                    int c = Vertex(quads[q * 4 + 2]);
                    int d = Vertex(quads[q * 4 + 3]);

                    // Split along the shorter diagonal: the longer one makes slivers.
                    if ((mesh.Positions[a] - mesh.Positions[c]).sqrMagnitude <=
                        (mesh.Positions[b] - mesh.Positions[d]).sqrMagnitude)
                    {
                        mesh.AddTriangle(a, b, c);
                        mesh.AddTriangle(a, c, d);
                    }
                    else
                    {
                        mesh.AddTriangle(a, b, d);
                        mesh.AddTriangle(b, c, d);
                    }
                }

                // Each copy of a seam vertex takes its own part's parameter, not its neighbour's.
                var uvs = new Vector2[used.Count];
                Parallel.For(0, used.Count, n =>
                {
                    uvs[n] = new Vector2(ParameterOnPart(projected[used[n]], part), 0.5f);
                });

                for (int n = 0; n < used.Count; n++)
                    mesh.Uvs[n] = uvs[n];

                result[part] = mesh;
            }

            return result;
        }
    }

    /// <summary>
    /// A thin shell draped over a height field — the auricle's cartilage. Heights are measured along
    /// the sheet's normal from its plane and built from gaussian bumps and ridges, and the sheet is cut
    /// to a 2D outline. Distances are only approximate (the height field's slope is corrected for, its
    /// curvature is not), which is plenty near a surface this thin.
    /// </summary>
    sealed class HeightSheet
    {
        struct Bump
        {
            public Vector2 Centre, InverseRadii;
            public float Amplitude;
        }

        struct Ridge
        {
            public Vector2[] Points;
            public float InverseWidth, Amplitude;
        }

        readonly Vector3 m_Origin, m_S, m_T, m_N;
        readonly Vector2[] m_Outline;
        readonly float m_Thickness;
        readonly float m_EdgeRound;
        readonly List<Bump> m_Bumps = new();
        readonly List<Ridge> m_Ridges = new();
        float m_MinHeight, m_MaxHeight;

        /// <param name="origin">Where the sheet's (0, 0) sits.</param>
        /// <param name="s">In-plane first axis.</param>
        /// <param name="t">In-plane second axis.</param>
        /// <param name="n">Height axis — the sheet's normal.</param>
        /// <param name="outline">Closed 2D outline in (s, t).</param>
        public HeightSheet(Vector3 origin, Vector3 s, Vector3 t, Vector3 n, Vector2[] outline, float thickness,
            float edgeRound)
        {
            m_Origin = origin;
            m_S = s.normalized;
            m_T = t.normalized;
            m_N = n.normalized;
            m_Outline = outline;
            m_Thickness = thickness;
            m_EdgeRound = edgeRound;
        }

        public void AddBump(Vector2 centre, Vector2 radii, float amplitude)
        {
            m_Bumps.Add(new Bump
            {
                Centre = centre,
                InverseRadii = new Vector2(1f / radii.x, 1f / radii.y),
                Amplitude = amplitude,
            });
            Track(amplitude);
        }

        public void AddRidge(Vector2[] points, float width, float amplitude)
        {
            m_Ridges.Add(new Ridge { Points = points, InverseWidth = 1f / width, Amplitude = amplitude });
            Track(amplitude);
        }

        void Track(float amplitude)
        {
            if (amplitude < 0f) m_MinHeight += amplitude;
            else m_MaxHeight += amplitude;
        }

        /// <summary>The sheet's point at (s, t) on its mid-surface.</summary>
        public Vector3 Point(Vector2 st) => m_Origin + m_S * st.x + m_T * st.y + m_N * Height(st);

        public Vector3 Point(Vector2 st, float lift) => Point(st) + m_N * lift;

        public float Height(Vector2 st)
        {
            float h = 0f;
            foreach (var b in m_Bumps)
            {
                var d = Vector2.Scale(st - b.Centre, b.InverseRadii);
                h += b.Amplitude * Mathf.Exp(-Vector2.Dot(d, d));
            }

            foreach (var r in m_Ridges)
            {
                float d = PolylineDistance(st, r.Points) * r.InverseWidth;
                h += r.Amplitude * Mathf.Exp(-d * d);
            }

            return h;
        }

        public float Distance(Vector3 x)
        {
            var local = x - m_Origin;
            var st = new Vector2(Vector3.Dot(local, m_S), Vector3.Dot(local, m_T));
            float z = Vector3.Dot(local, m_N);

            const float e = 0.25f;
            float h = Height(st);
            float hs = (Height(st + new Vector2(e, 0f)) - Height(st - new Vector2(e, 0f))) / (2f * e);
            float ht = (Height(st + new Vector2(0f, e)) - Height(st - new Vector2(0f, e))) / (2f * e);

            float shell = Mathf.Abs(z - h) / Mathf.Sqrt(1f + hs * hs + ht * ht) - m_Thickness * 0.5f;
            return SdfShape.SmoothMax(shell, Polygon(st, m_Outline), m_EdgeRound);
        }

        public void Bounds(out Vector3 min, out Vector3 max)
        {
            var lo = new Vector2(float.MaxValue, float.MaxValue);
            var hi = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in m_Outline)
            {
                lo = Vector2.Min(lo, p);
                hi = Vector2.Max(hi, p);
            }

            float h0 = m_MinHeight - m_Thickness;
            float h1 = m_MaxHeight + m_Thickness;

            min = Vector3.one * float.MaxValue;
            max = Vector3.one * float.MinValue;
            for (int c = 0; c < 8; c++)
            {
                var corner = m_Origin
                    + m_S * ((c & 1) == 0 ? lo.x : hi.x)
                    + m_T * ((c & 2) == 0 ? lo.y : hi.y)
                    + m_N * ((c & 4) == 0 ? h0 : h1);
                min = Vector3.Min(min, corner);
                max = Vector3.Max(max, corner);
            }
        }

        static float PolylineDistance(Vector2 p, Vector2[] points)
        {
            float best = float.MaxValue;
            for (int i = 0; i < points.Length - 1; i++)
            {
                var a = points[i];
                var ab = points[i + 1] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab));
                best = Mathf.Min(best, (p - (a + ab * t)).sqrMagnitude);
            }

            return Mathf.Sqrt(best);
        }

        /// <summary>Signed distance to a closed polygon, negative inside (Inigo Quilez).</summary>
        public static float Polygon(Vector2 p, Vector2[] v)
        {
            float d = Vector2.Dot(p - v[0], p - v[0]);
            float s = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                var e = v[j] - v[i];
                var w = p - v[i];
                var b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, Vector2.Dot(b, b));

                bool c0 = p.y >= v[i].y, c1 = p.y < v[j].y, c2 = e.x * w.y > e.y * w.x;
                if ((c0 && c1 && c2) || (!c0 && !c1 && !c2))
                    s = -s;
            }

            return s * Mathf.Sqrt(d);
        }
    }
}
