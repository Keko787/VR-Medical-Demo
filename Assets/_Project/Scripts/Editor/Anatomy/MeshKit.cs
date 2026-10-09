using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRShootingGallery.EditorTools.Anatomy
{
    /// <summary>
    /// A mesh under construction: plain lists, so builders can append, merge and transform freely
    /// before anything becomes a <see cref="Mesh"/>.
    /// </summary>
    sealed class MeshData
    {
        public readonly List<Vector3> Positions = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Vector2> Uvs = new();
        public readonly List<int> Triangles = new();

        public int VertexCount => Positions.Count;

        public int AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            Positions.Add(position);
            Normals.Add(normal);
            Uvs.Add(uv);
            return Positions.Count - 1;
        }

        public void AddTriangle(int a, int b, int c)
        {
            Triangles.Add(a);
            Triangles.Add(b);
            Triangles.Add(c);
        }

        public void Append(MeshData other)
        {
            int offset = Positions.Count;
            Positions.AddRange(other.Positions);
            Normals.AddRange(other.Normals);
            Uvs.AddRange(other.Uvs);
            foreach (int t in other.Triangles)
                Triangles.Add(t + offset);
        }

        public MeshData Transformed(Matrix4x4 matrix)
        {
            var copy = new MeshData();
            var normalMatrix = matrix.inverse.transpose;
            for (int i = 0; i < Positions.Count; i++)
                copy.AddVertex(matrix.MultiplyPoint3x4(Positions[i]),
                    normalMatrix.MultiplyVector(Normals[i]).normalized, Uvs[i]);

            copy.Triangles.AddRange(Triangles);
            return copy;
        }

        public Vector3 Centroid()
        {
            if (Positions.Count == 0)
                return Vector3.zero;

            var sum = Vector3.zero;
            foreach (var p in Positions)
                sum += p;

            return sum / Positions.Count;
        }

        public Bounds Bounds()
        {
            if (Positions.Count == 0)
                return new Bounds();

            var b = new Bounds(Positions[0], Vector3.zero);
            foreach (var p in Positions)
                b.Encapsulate(p);

            return b;
        }

        /// <summary>The vertex closest to <paramref name="target"/> — for anchoring labels on the surface.</summary>
        public Vector3 Nearest(Vector3 target)
        {
            var best = target;
            float bestDistance = float.MaxValue;
            foreach (var p in Positions)
            {
                float d = (p - target).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = p;
                }
            }

            return best;
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh
            {
                name = name,
                indexFormat = Positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.SetVertices(Positions);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>Smooth curves through control points, resampled evenly along their length.</summary>
    static class Curves
    {
        /// <summary>
        /// A centripetal Catmull–Rom spline through <paramref name="control"/>, resampled so the points
        /// are <paramref name="spacing"/> apart. Centripetal because the uniform kind loops and cusps
        /// wherever control points bunch up, and anatomy is full of tight turns next to long runs.
        /// </summary>
        public static List<Vector3> Smooth(IReadOnlyList<Vector3> control, float spacing)
        {
            var dense = new List<Vector3>();
            if (control.Count < 2)
            {
                dense.AddRange(control);
                return dense;
            }

            // Mirrored end points, so the curve starts and ends on the first and last control points
            // heading the way the first and last spans do.
            var pts = new List<Vector3> { control[0] * 2f - control[1] };
            pts.AddRange(control);
            pts.Add(control[^1] * 2f - control[^2]);

            for (int i = 1; i < pts.Count - 2; i++)
            {
                const int steps = 24;
                for (int s = 0; s < steps; s++)
                    dense.Add(CatmullRom(pts[i - 1], pts[i], pts[i + 1], pts[i + 2], s / (float)steps));
            }

            dense.Add(control[^1]);
            return Resample(dense, spacing);
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            static float Knot(float ti, Vector3 a, Vector3 b) => ti + Mathf.Pow(Mathf.Max((b - a).magnitude, 1e-4f), 0.5f);

            float t0 = 0f;
            float t1 = Knot(t0, p0, p1);
            float t2 = Knot(t1, p1, p2);
            float t3 = Knot(t2, p2, p3);
            float tt = Mathf.Lerp(t1, t2, t);

            var a1 = (t1 - tt) / (t1 - t0) * p0 + (tt - t0) / (t1 - t0) * p1;
            var a2 = (t2 - tt) / (t2 - t1) * p1 + (tt - t1) / (t2 - t1) * p2;
            var a3 = (t3 - tt) / (t3 - t2) * p2 + (tt - t2) / (t3 - t2) * p3;
            var b1 = (t2 - tt) / (t2 - t0) * a1 + (tt - t0) / (t2 - t0) * a2;
            var b2 = (t3 - tt) / (t3 - t1) * a2 + (tt - t1) / (t3 - t1) * a3;
            return (t2 - tt) / (t2 - t1) * b1 + (tt - t1) / (t2 - t1) * b2;
        }

        public static List<Vector3> Resample(IReadOnlyList<Vector3> points, float spacing)
        {
            float length = Length(points);
            int count = Mathf.Max(2, Mathf.CeilToInt(length / Mathf.Max(spacing, 1e-4f)) + 1);
            var result = new List<Vector3>(count);

            int segment = 0;
            float walked = 0f;
            for (int n = 0; n < count; n++)
            {
                float target = length * n / (count - 1);
                while (segment < points.Count - 2 &&
                       walked + Vector3.Distance(points[segment], points[segment + 1]) < target)
                {
                    walked += Vector3.Distance(points[segment], points[segment + 1]);
                    segment++;
                }

                float span = Vector3.Distance(points[segment], points[segment + 1]);
                float t = span > 1e-6f ? Mathf.Clamp01((target - walked) / span) : 0f;
                result.Add(Vector3.Lerp(points[segment], points[segment + 1], t));
            }

            return result;
        }

        public static float Length(IReadOnlyList<Vector3> points)
        {
            float length = 0f;
            for (int i = 1; i < points.Count; i++)
                length += Vector3.Distance(points[i - 1], points[i]);

            return length;
        }

        /// <summary>Fraction of the total length reached at each point: 0 at the first, 1 at the last.</summary>
        public static float[] ArcFractions(IReadOnlyList<Vector3> points)
        {
            var result = new float[points.Count];
            float total = Length(points);
            float walked = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                walked += Vector3.Distance(points[i - 1], points[i]);
                result[i] = total > 0f ? walked / total : 0f;
            }

            return result;
        }

        /// <summary>The point <paramref name="fraction"/> of the way along the polyline.</summary>
        public static Vector3 At(IReadOnlyList<Vector3> points, float fraction)
        {
            float target = Length(points) * Mathf.Clamp01(fraction);
            float walked = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                float span = Vector3.Distance(points[i - 1], points[i]);
                if (walked + span >= target)
                    return Vector3.Lerp(points[i - 1], points[i], span > 0f ? (target - walked) / span : 0f);

                walked += span;
            }

            return points[^1];
        }

        public static Vector3 TangentAt(IReadOnlyList<Vector3> points, float fraction)
        {
            float step = 0.5f / Mathf.Max(points.Count, 2);
            return (At(points, Mathf.Min(1f, fraction + step)) - At(points, Mathf.Max(0f, fraction - step))).normalized;
        }

        /// <summary>
        /// Points on a circular arc. <paramref name="from"/> is a unit vector from the centre to the
        /// start; the arc turns about <paramref name="axis"/> by <paramref name="degrees"/>.
        /// </summary>
        public static List<Vector3> Arc(Vector3 centre, Vector3 axis, Vector3 from, float radius, float degrees,
            int steps)
        {
            var result = new List<Vector3>(steps + 1);
            for (int i = 0; i <= steps; i++)
                result.Add(centre + Quaternion.AngleAxis(degrees * i / steps, axis) * from * radius);

            return result;
        }
    }

    /// <summary>Swept and turned shapes: tubes, rings, discs, membranes and solids of revolution.</summary>
    static class MeshKit
    {
        /// <summary>
        /// A tube swept along <paramref name="path"/>. <c>uv.x</c> runs <paramref name="u0"/> →
        /// <paramref name="u1"/> along it and <c>uv.y</c> 0 → 1 around it. Frames are rotation
        /// minimising (the double reflection method), so a tube that bends in three dimensions does
        /// not twist.
        /// </summary>
        public static MeshData Tube(IReadOnlyList<Vector3> path, Func<float, float> radius, int sides,
            float u0 = 0f, float u1 = 1f, bool capStart = false, bool capEnd = false, float squash = 1f)
        {
            var mesh = new MeshData();
            int count = path.Count;
            if (count < 2)
                return mesh;

            var fractions = Curves.ArcFractions(path);
            var tangents = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                var prev = path[Mathf.Max(0, i - 1)];
                var next = path[Mathf.Min(count - 1, i + 1)];
                tangents[i] = (next - prev).normalized;
            }

            var normals = new Vector3[count];
            normals[0] = AnyPerpendicular(tangents[0]);
            for (int i = 0; i < count - 1; i++)
            {
                var v1 = path[i + 1] - path[i];
                float c1 = Vector3.Dot(v1, v1);
                if (c1 < 1e-12f)
                {
                    normals[i + 1] = normals[i];
                    continue;
                }

                var rL = normals[i] - 2f / c1 * Vector3.Dot(v1, normals[i]) * v1;
                var tL = tangents[i] - 2f / c1 * Vector3.Dot(v1, tangents[i]) * v1;
                var v2 = tangents[i + 1] - tL;
                float c2 = Vector3.Dot(v2, v2);
                normals[i + 1] = c2 < 1e-12f ? rL : rL - 2f / c2 * Vector3.Dot(v2, rL) * v2;
            }

            for (int i = 0; i < count; i++)
            {
                var t = tangents[i];
                var n = normals[i];
                var b = Vector3.Cross(t, n);
                float r = radius(fractions[i]);
                float u = Mathf.Lerp(u0, u1, fractions[i]);

                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2f;
                    var dir = n * Mathf.Cos(a) + b * Mathf.Sin(a);
                    var offset = n * (Mathf.Cos(a) * r) + b * (Mathf.Sin(a) * r * squash);
                    mesh.AddVertex(path[i] + offset, dir, new Vector2(u, s / (float)sides));
                }
            }

            int ring = sides + 1;
            for (int i = 0; i < count - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = i * ring + s;
                int b = a + 1;
                int c = a + ring;
                int d = c + 1;
                mesh.AddTriangle(a, b, c);
                mesh.AddTriangle(b, d, c);
            }

            if (capStart)
                Cap(mesh, 0, ring, path[0], -tangents[0], u0, true);
            if (capEnd)
                Cap(mesh, (count - 1) * ring, ring, path[^1], tangents[^1], u1, false);

            return mesh;
        }

        static void Cap(MeshData mesh, int first, int ring, Vector3 centre, Vector3 normal, float u, bool flip)
        {
            int c = mesh.AddVertex(centre, normal, new Vector2(u, 0.5f));
            int start = mesh.VertexCount;
            for (int s = 0; s < ring; s++)
                mesh.AddVertex(mesh.Positions[first + s], normal, new Vector2(u, 0.5f));

            for (int s = 0; s < ring - 1; s++)
            {
                if (flip) mesh.AddTriangle(c, start + s + 1, start + s);
                else mesh.AddTriangle(c, start + s, start + s + 1);
            }
        }

        /// <summary>
        /// A closed elliptical ring of tube: the rim round a membrane, an annular ligament. <c>uv.x</c>
        /// goes round the ring.
        /// </summary>
        public static MeshData Torus(Vector3 centre, Vector3 normal, Vector3 major, float radiusA, float radiusB,
            float tube, int segments, int sides)
        {
            normal.Normalize();
            major = Vector3.ProjectOnPlane(major, normal).normalized;
            var minor = Vector3.Cross(normal, major);

            var mesh = new MeshData();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var point = centre + major * (Mathf.Cos(a) * radiusA) + minor * (Mathf.Sin(a) * radiusB);
                var outward = (major * (Mathf.Cos(a) / radiusA) + minor * (Mathf.Sin(a) / radiusB)).normalized;

                for (int s = 0; s <= sides; s++)
                {
                    float b = s / (float)sides * Mathf.PI * 2f;
                    var dir = outward * Mathf.Cos(b) + normal * Mathf.Sin(b);
                    mesh.AddVertex(point + dir * tube, dir, new Vector2(i / (float)segments, s / (float)sides));
                }
            }

            int ring = sides + 1;
            for (int i = 0; i < segments; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = i * ring + s;
                mesh.AddTriangle(a, a + ring, a + 1);
                mesh.AddTriangle(a + 1, a + ring, a + ring + 1);
            }

            return mesh;
        }

        /// <summary>
        /// An elliptical membrane with a conical dip at its centre — the eardrum, the round window.
        /// <c>uv.x</c> is 0.5 at the centre and 1 at the rim, which is what the hologram shader's
        /// vibration profile (sin πu) and ring signal expect.
        /// </summary>
        public static MeshData Membrane(Vector3 centre, Vector3 normal, Vector3 up, float radiusUp,
            float radiusSide, float depth, Vector2 apexOffset, int rings, int segments)
        {
            normal.Normalize();
            up = Vector3.ProjectOnPlane(up, normal).normalized;
            var side = Vector3.Cross(normal, up);

            var mesh = new MeshData();
            for (int r = 0; r <= rings; r++)
            {
                float rho = r / (float)rings;
                for (int s = 0; s <= segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    var rim = up * (Mathf.Cos(a) * radiusUp) + side * (Mathf.Sin(a) * radiusSide);
                    var apex = up * apexOffset.x + side * apexOffset.y;

                    // Conical, bellied a little outward the way the eardrum's radial fibres bow.
                    float dip = depth * (1f - rho) - depth * 0.18f * Mathf.Sin(rho * Mathf.PI);
                    var p = centre + Vector3.Lerp(apex, rim, rho) - normal * dip;
                    mesh.AddVertex(p, normal, new Vector2(0.5f + 0.5f * rho, s / (float)segments));
                }
            }

            int ring = segments + 1;
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * ring + s;
                mesh.AddTriangle(a, a + ring, a + 1);
                mesh.AddTriangle(a + 1, a + ring, a + ring + 1);
            }

            RecalculateNormals(mesh);
            return mesh;
        }

        /// <summary>A flat ring. <c>uv.x</c> goes round it, <c>uv.y</c> from inner (0) to outer (1) edge.</summary>
        public static MeshData Annulus(Vector3 centre, Vector3 normal, float inner, float outer, int segments)
        {
            normal.Normalize();
            var major = AnyPerpendicular(normal);
            var minor = Vector3.Cross(normal, major);

            var mesh = new MeshData();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = major * Mathf.Cos(a) + minor * Mathf.Sin(a);
                mesh.AddVertex(centre + dir * inner, normal, new Vector2(i / (float)segments, 0f));
                mesh.AddVertex(centre + dir * outer, normal, new Vector2(i / (float)segments, 1f));
            }

            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                mesh.AddTriangle(a, a + 1, a + 2);
                mesh.AddTriangle(a + 1, a + 3, a + 2);
            }

            return mesh;
        }

        /// <summary>
        /// An open cone frustum standing on +Y: the projector's beam. <c>uv.x</c> goes round,
        /// <c>uv.y</c> from the bottom (0) to the top (1).
        /// </summary>
        public static MeshData Frustum(Vector3 baseCentre, float bottomRadius, float topRadius, float height,
            int segments)
        {
            var mesh = new MeshData();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                mesh.AddVertex(baseCentre + dir * bottomRadius, dir, new Vector2(i / (float)segments, 0f));
                mesh.AddVertex(baseCentre + dir * topRadius + Vector3.up * height, dir,
                    new Vector2(i / (float)segments, 1f));
            }

            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                mesh.AddTriangle(a, a + 1, a + 2);
                mesh.AddTriangle(a + 1, a + 3, a + 2);
            }

            return mesh;
        }

        public static MeshData Sphere(Vector3 centre, float radius, int segments, int rings)
        {
            var mesh = new MeshData();
            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings;
                float theta = v * Mathf.PI;
                for (int s = 0; s <= segments; s++)
                {
                    float u = s / (float)segments;
                    float phi = u * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta),
                        Mathf.Sin(theta) * Mathf.Sin(phi));
                    mesh.AddVertex(centre + dir * radius, dir, new Vector2(u, v));
                }
            }

            int ring = segments + 1;
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * ring + s;
                mesh.AddTriangle(a, a + 1, a + ring);
                mesh.AddTriangle(a + 1, a + ring + 1, a + ring);
            }

            return mesh;
        }

        /// <summary>
        /// Turns a closed 2D outline about an axis and caps the two cut faces: a wedge of a ring-shaped
        /// solid, which is what a slice of the cochlear spiral is. The outline is in (r, z): r is
        /// distance from the axis, z along it. The axis runs along local +Y through the origin, and the
        /// wedge spans ±<paramref name="halfAngle"/> degrees about +X. <c>uv.x</c> is
        /// <paramref name="uOf"/>(r, z) — the section uses it for where along the basilar membrane a
        /// vertex sits.
        /// </summary>
        public static MeshData Revolve(IReadOnlyList<Vector2> outline, float halfAngle, int steps,
            Func<Vector2, float> uOf)
        {
            var mesh = new MeshData();
            int n = outline.Count;
            if (n < 3)
                return mesh;

            // Outline normals in 2D, outward, for smooth shading round the section.
            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % n];
                area += a.x * b.y - b.x * a.y;
            }

            float orientation = area >= 0f ? 1f : -1f;
            var normals2 = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var prev = outline[(i + n - 1) % n];
                var next = outline[(i + 1) % n];
                var t = (next - prev).normalized;
                normals2[i] = new Vector2(t.y, -t.x) * orientation;
            }

            Vector3 Place(Vector2 rz, float degrees)
            {
                float a = degrees * Mathf.Deg2Rad;
                return new Vector3(rz.x * Mathf.Cos(a), rz.y, rz.x * Mathf.Sin(a));
            }

            Vector3 PlaceNormal(Vector2 n2, float degrees)
            {
                float a = degrees * Mathf.Deg2Rad;
                return new Vector3(n2.x * Mathf.Cos(a), n2.y, n2.x * Mathf.Sin(a)).normalized;
            }

            // The skin, split at every outline vertex so corners stay crisp.
            for (int st = 0; st <= steps; st++)
            {
                float angle = Mathf.Lerp(-halfAngle, halfAngle, st / (float)steps);
                for (int i = 0; i < n; i++)
                {
                    var p = outline[i];
                    var q = outline[(i + 1) % n];
                    var edge = (q - p).normalized;
                    var n2 = new Vector2(edge.y, -edge.x) * orientation;

                    // Smooth where the outline is smooth, sharp where it turns hard.
                    var np = Vector2.Dot(normals2[i], n2) > 0.8f ? normals2[i] : n2;
                    var nq = Vector2.Dot(normals2[(i + 1) % n], n2) > 0.8f ? normals2[(i + 1) % n] : n2;

                    mesh.AddVertex(Place(p, angle), PlaceNormal(np, angle), new Vector2(uOf(p), st / (float)steps));
                    mesh.AddVertex(Place(q, angle), PlaceNormal(nq, angle), new Vector2(uOf(q), st / (float)steps));
                }
            }

            int stride = n * 2;
            for (int st = 0; st < steps; st++)
            for (int i = 0; i < n; i++)
            {
                int a = st * stride + i * 2;
                int b = a + 1;
                int c = a + stride;
                int d = b + stride;
                if (orientation > 0f)
                {
                    mesh.AddTriangle(a, b, c);
                    mesh.AddTriangle(b, d, c);
                }
                else
                {
                    mesh.AddTriangle(a, c, b);
                    mesh.AddTriangle(b, c, d);
                }
            }

            // The two cut faces.
            var triangles = Triangulate(outline);
            for (int end = 0; end < 2; end++)
            {
                float angle = end == 0 ? -halfAngle : halfAngle;
                var faceNormal = Quaternion.AngleAxis(-angle, Vector3.up) * (end == 0 ? Vector3.back : Vector3.forward);
                int start = mesh.VertexCount;
                foreach (var p in outline)
                    mesh.AddVertex(Place(p, angle), faceNormal, new Vector2(uOf(p), end));

                for (int t = 0; t < triangles.Count; t += 3)
                {
                    int a = start + triangles[t], b = start + triangles[t + 1], c = start + triangles[t + 2];
                    var geometric = Vector3.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
                    if (Vector3.Dot(geometric, faceNormal) >= 0f) mesh.AddTriangle(a, b, c);
                    else mesh.AddTriangle(a, c, b);
                }
            }

            return mesh;
        }

        /// <summary>Ear-clipping triangulation of a simple polygon. Returns index triples.</summary>
        public static List<int> Triangulate(IReadOnlyList<Vector2> polygon)
        {
            var result = new List<int>();
            var remaining = new List<int>();
            for (int i = 0; i < polygon.Count; i++)
                remaining.Add(i);

            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
            }

            bool ccw = area > 0f;

            static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

            static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
            {
                float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
                bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
                bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
                return !(neg && pos);
            }

            int guard = 0;
            while (remaining.Count > 3 && guard++ < 10000)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int ia = remaining[(i + remaining.Count - 1) % remaining.Count];
                    int ib = remaining[i];
                    int ic = remaining[(i + 1) % remaining.Count];
                    var a = polygon[ia];
                    var b = polygon[ib];
                    var c = polygon[ic];

                    float turn = Cross(a, b, c);
                    if (ccw ? turn <= 0f : turn >= 0f)
                        continue;

                    bool ear = true;
                    foreach (int other in remaining)
                    {
                        if (other == ia || other == ib || other == ic)
                            continue;

                        if (Inside(polygon[other], a, b, c))
                        {
                            ear = false;
                            break;
                        }
                    }

                    if (!ear)
                        continue;

                    result.Add(ia);
                    result.Add(ib);
                    result.Add(ic);
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }

                // A degenerate outline: fan whatever is left rather than loop forever.
                if (!clipped)
                    break;
            }

            for (int i = 1; i < remaining.Count - 1; i++)
            {
                result.Add(remaining[0]);
                result.Add(remaining[i]);
                result.Add(remaining[i + 1]);
            }

            return result;
        }

        public static void RecalculateNormals(MeshData mesh)
        {
            var sums = new Vector3[mesh.VertexCount];
            for (int t = 0; t < mesh.Triangles.Count; t += 3)
            {
                int a = mesh.Triangles[t], b = mesh.Triangles[t + 1], c = mesh.Triangles[t + 2];
                var n = Vector3.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
                sums[a] += n;
                sums[b] += n;
                sums[c] += n;
            }

            for (int i = 0; i < sums.Length; i++)
                mesh.Normals[i] = sums[i].sqrMagnitude > 1e-12f ? sums[i].normalized : mesh.Normals[i];
        }

        public static Vector3 AnyPerpendicular(Vector3 v)
        {
            var other = Mathf.Abs(v.y) < 0.9f ? Vector3.up : Vector3.right;
            return Vector3.Cross(v, other).normalized;
        }
    }
}
