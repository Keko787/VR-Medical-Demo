using System.Collections.Generic;
using UnityEngine;

namespace VRShootingGallery.EditorTools.Anatomy
{
    /// <summary>
    /// A magnified slice of the cochlear duct — the textbook cross-section of one turn, with the organ of
    /// Corti sitting on the basilar membrane between the three fluid chambers. Each tissue is a 2D
    /// profile turned through a thin wedge about the modiolus, so the slice has the spiral's real
    /// curvature and a little depth instead of being a flat card, and the rows of hair cells run along it.
    /// </summary>
    /// <remarks>
    /// <para>Units are the bony canal's radius: the canal is a unit circle centred on the origin, x
    /// pointing away from the modiolus and y toward the apex. The modiolus axis is at x = −<see cref="k_TurnRadius"/>,
    /// so everything here is offset by that before it is turned, and shifted back after — the wedge ends
    /// up centred on the canal, which is where the section's labels and focus should gather.</para>
    ///
    /// <para><c>uv.x</c> on every part is where that point sits across the basilar membrane, 0 at the
    /// spiral lamina's tip and 1 at the spiral ligament. The hologram shader's vibration profile is
    /// sin(πu), so giving the basilar membrane, the organ of Corti and its hair cells an amplitude
    /// bows them all together, most in the middle — and a smaller amplitude on the tectorial membrane,
    /// hinged at the limbus, makes the shear that bends the stereocilia.</para>
    /// </remarks>
    static class CochlearSection
    {
        /// <summary>Distance from the modiolus axis to the canal's centre, in canal radii.</summary>
        const float k_TurnRadius = 2.2f;

        /// <summary>
        /// Half the slice's thickness, in degrees round the spiral. Thin on purpose: the slice is read
        /// face-on, like a histology section, and every tissue boundary glows where it is seen edge-on —
        /// a thick wedge smears each outline across its whole depth.
        /// </summary>
        const float k_HalfAngle = 6f;

        const int k_Steps = 6;

        const float k_LaminaTipX = -0.36f;
        const float k_LigamentX = 0.92f;

        /// <summary>Top of the basilar membrane at <paramref name="x"/>.</summary>
        static float Membrane(float x) => Mathf.Lerp(0f, -0.105f, (x - k_LaminaTipX) / (k_LigamentX - k_LaminaTipX));

        static float AcrossMembrane(Vector2 p) => Mathf.Clamp01((p.x - k_LaminaTipX) / (k_LigamentX - k_LaminaTipX));

        /// <summary>Height of the organ of Corti above the membrane, by x.</summary>
        static readonly Vector2[] k_CortiProfile =
        {
            new(-0.30f, 0.00f), new(-0.26f, 0.10f), new(-0.20f, 0.17f), new(-0.10f, 0.21f), new(0.00f, 0.25f),
            new(0.08f, 0.27f), new(0.20f, 0.265f), new(0.32f, 0.25f), new(0.42f, 0.22f), new(0.50f, 0.16f),
            new(0.57f, 0.09f), new(0.62f, 0.00f),
        };

        static float CortiHeight(float x)
        {
            for (int i = 0; i < k_CortiProfile.Length - 1; i++)
            {
                var a = k_CortiProfile[i];
                var b = k_CortiProfile[i + 1];
                if (x >= a.x && x <= b.x)
                    return Mathf.Lerp(a.y, b.y, Mathf.SmoothStep(0f, 1f, (x - a.x) / (b.x - a.x)));
            }

            return 0f;
        }

        static float CortiTop(float x) => Membrane(x) + CortiHeight(x);

        /// <summary>Inner edge of the spiral ligament, by angle round the canal (degrees).</summary>
        static float LigamentInner(float degrees)
        {
            float t = Mathf.InverseLerp(-35f, 45f, degrees);
            return 0.995f - 0.13f * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
        }

        const float k_StriaThickness = 0.065f;

        public static List<PartSpec> Build(out float turnRadius, out float halfAngle)
        {
            turnRadius = k_TurnRadius;
            halfAngle = k_HalfAngle;
            var parts = new List<PartSpec>();

            PartSpec Add(string key, string name, string material, MeshData mesh, Vector2 anchor, string description,
                int tier = 0)
            {
                var part = new PartSpec
                {
                    Key = key,
                    DisplayName = name,
                    Region = "Cochlear duct · Section A",
                    Material = material,
                    Mesh = mesh,
                    Anchor = Face(anchor),
                    Description = description,
                    Tier = tier,
                };
                parts.Add(part);
                return part;
            }

            // ------------------------------------------------ the three chambers

            var vestibuli = new List<Vector2>();
            vestibuli.AddRange(Arc(0.97f, 40f, 173f, 40));
            vestibuli.Add(new Vector2(-0.96f, 0.12f));
            vestibuli.Add(new Vector2(-0.8f, 0.15f));
            vestibuli.Add(new Vector2(-0.66f, 0.21f));
            vestibuli.Add(new Vector2(-0.545f, 0.225f));
            Add("Scala_Vestibuli", "Scala vestibuli", "Perilymph", Turn(vestibuli), new Vector2(-0.1f, 0.72f),
                "The upper chamber, filled with perilymph — a fluid like the fluid round the brain. The " +
                "stapes' push at the oval window enters the cochlea here and runs up the spiral to the " +
                "apex.");

            var media = new List<Vector2> { new(-0.525f, 0.195f) };
            var reissnerEnd = Polar(LigamentInner(40f) - k_StriaThickness, 40f);
            media.Add(reissnerEnd);
            for (int i = 1; i <= 20; i++)
            {
                float deg = Mathf.Lerp(40f, -6f, i / 20f);
                media.Add(Polar(LigamentInner(deg) - k_StriaThickness, deg));
            }

            for (float x = 0.84f; x > 0.62f; x -= 0.05f)
                media.Add(new Vector2(x, Membrane(x) + 0.004f));
            for (int i = k_CortiProfile.Length - 1; i >= 0; i--)
            {
                float x = k_CortiProfile[i].x;
                media.Add(new Vector2(x, CortiTop(x) + 0.006f));
            }

            media.Add(new Vector2(-0.34f, 0.03f));
            media.Add(new Vector2(-0.42f, 0.11f));
            Add("Scala_Media", "Scala media (cochlear duct)", "Endolymph", Turn(media), new Vector2(0.55f, 0.3f),
                "The middle chamber, filled with endolymph: rich in potassium and held at +80 mV by the " +
                "stria vascularis. That charge is the battery the hair cells run on. The organ of Corti " +
                "sits on its floor.");

            var tympani = new List<Vector2>();
            tympani.AddRange(Arc(0.97f, -8f, -174f, 40));
            tympani.Add(new Vector2(-0.96f, -0.1f));
            tympani.Add(new Vector2(-0.4f, -0.065f));
            tympani.Add(new Vector2(k_LaminaTipX, -0.045f));
            tympani.Add(new Vector2(k_LigamentX, Membrane(k_LigamentX) - 0.045f));
            Add("Scala_Tympani", "Scala tympani", "Perilymph", Turn(tympani), new Vector2(0.1f, -0.62f),
                "The lower chamber of perilymph. It joins the scala vestibuli at the apex, through the " +
                "helicotrema, and ends at the round window — where the pressure the stapes put in comes " +
                "back out.");

            // ------------------------------------------------ membranes

            var reissner = new List<Vector2>
            {
                new(-0.535f, 0.205f), reissnerEnd + new Vector2(0.004f, -0.01f),
                reissnerEnd + new Vector2(0.004f, 0.01f), new(-0.54f, 0.228f),
            };
            Add("Reissners_Membrane", "Reissner's membrane", "Membrane", Turn(reissner), new Vector2(0.25f, 0.43f),
                "Also called the vestibular membrane: just two cells thick, it keeps the potassium-rich " +
                "endolymph of the scala media apart from the perilymph above.");

            var basilar = new List<Vector2>
            {
                new(k_LaminaTipX, 0.0f), new(k_LigamentX, Membrane(k_LigamentX)),
                new(k_LigamentX, Membrane(k_LigamentX) - 0.035f), new(k_LaminaTipX, -0.035f),
            };
            var basilarPart = Add("Basilar_Membrane", "Basilar membrane", "Membrane", Turn(basilar), new Vector2(0.72f, -0.12f),
                "The springy floor the organ of Corti stands on. Narrow and stiff at the base, five times " +
                "wider and floppier at the apex, it resonates at a different pitch at every point along " +
                "the spiral. Here, at section A, it answers best to about 2 kHz.");
            basilarPart.Landmarks.Add(("Vibrates here at 2 kHz", Face(new Vector2(0.28f, -0.06f))));

            // ------------------------------------------------ organ of Corti

            var organ = new List<Vector2>();
            foreach (var p in k_CortiProfile)
                organ.Add(new Vector2(p.x, CortiTop(p.x)));
            for (int i = k_CortiProfile.Length - 1; i >= 0; i--)
                organ.Add(new Vector2(k_CortiProfile[i].x, Membrane(k_CortiProfile[i].x) + 0.002f));

            var organMesh = Turn(organ);

            // The pillar cells: an A-frame round the tunnel of Corti.
            float bmInner = Membrane(-0.04f), bmOuter = Membrane(0.17f);
            organMesh.Append(Turn(Strip(new Vector2(-0.04f, bmInner), new Vector2(0.065f, CortiTop(0.065f) - 0.01f), 0.014f)));
            organMesh.Append(Turn(Strip(new Vector2(0.17f, bmOuter), new Vector2(0.075f, CortiTop(0.075f) - 0.01f), 0.014f)));

            Add("Organ_Of_Corti", "Organ of Corti", "Corti", organMesh, new Vector2(0.5f, 0.1f),
                "The sensory organ itself: a ridge of hair cells and their supporting cells running the " +
                "full 35 mm of the spiral. The pillar cells arch over the tunnel of Corti; the tops of " +
                "the cells form a stiff plate, the reticular lamina, that the stereocilia poke through.");

            // ------------------------------------------------ hair cells

            var inner = new MeshData();
            var outer = new MeshData();
            int columns = Mathf.RoundToInt(k_HalfAngle * 2f / 2.8f);
            for (int c = 0; c < columns; c++)
            {
                float angle = Mathf.Lerp(-k_HalfAngle + 1.4f, k_HalfAngle - 1.4f, c / (float)(columns - 1));

                inner.Append(HairCell(-0.12f, 0.14f, 0.046f, angle, 0.035f, false));
                for (int row = 0; row < 3; row++)
                {
                    float x = 0.2f + row * 0.085f;

                    // The rows are staggered, as they are on the real reticular lamina.
                    float stagger = (row - 1) * 0.9f;
                    outer.Append(HairCell(x, 0.16f, 0.032f, angle + stagger, TectorialUnderside(x) - CortiTop(x), true));
                }
            }

            var ihc = Add("Inner_Hair_Cells", "Inner hair cells", "HairCell", inner, new Vector2(-0.12f, CortiTop(-0.12f)),
                "One row of about 3,500 flask-shaped cells — the real sensors. When their stereocilia " +
                "bend, ion channels open, the cell depolarises and releases transmitter onto the fibres of " +
                "the cochlear nerve. Nine in ten of those fibres come from here.");

            var ohc = Add("Outer_Hair_Cells", "Outer hair cells", "HairCell", outer, new Vector2(0.28f, CortiTop(0.28f)),
                "Three rows, about 12,000 cells, whose stereocilia are embedded in the tectorial membrane. " +
                "They change length with every cycle of sound — electromotility — pumping energy back " +
                "into the membrane: the cochlear amplifier, and the source of the ear's own faint sounds.");

            // ------------------------------------------------ tectorial membrane

            var tectorial = new List<Vector2>
            {
                new(-0.6f, 0.205f), new(-0.5f, 0.25f), new(-0.3f, 0.3f), new(-0.05f, 0.33f), new(0.2f, 0.335f),
                new(0.42f, 0.305f), new(0.47f, 0.275f), new(0.3f, TectorialUnderside(0.3f)),
                new(0.05f, TectorialUnderside(0.05f)), new(-0.2f, 0.25f), new(-0.42f, 0.215f), new(-0.52f, 0.2f),
            };
            Add("Tectorial_Membrane", "Tectorial membrane", "Tectorial", Turn(tectorial), new Vector2(0.05f, 0.34f),
                "A gel flap hinged at the spiral limbus and resting on the outer hair cells' stereocilia. " +
                "The basilar membrane and this lid pivot about different points, so as they rise and fall " +
                "together they slide past each other — and that shear is what bends the stereocilia.");

            // ------------------------------------------------ outer wall

            var ligament = new List<Vector2>();
            ligament.AddRange(Arc(0.995f, -35f, 45f, 30));
            for (int i = 0; i <= 30; i++)
            {
                float deg = Mathf.Lerp(45f, -35f, i / 30f);
                ligament.Add(Polar(LigamentInner(deg), deg));
            }

            Add("Spiral_Ligament", "Spiral ligament", "Tissue", Turn(ligament), Polar(0.93f, -25f),
                "Connective tissue lining the outer wall of the canal. It anchors the basilar membrane " +
                "and keeps it under tension.", tier: 2);

            var stria = new List<Vector2>();
            for (int i = 0; i <= 20; i++)
            {
                float deg = Mathf.Lerp(-4f, 38f, i / 20f);
                stria.Add(Polar(LigamentInner(deg), deg));
            }

            for (int i = 0; i <= 20; i++)
            {
                float deg = Mathf.Lerp(38f, -4f, i / 20f);
                stria.Add(Polar(LigamentInner(deg) - k_StriaThickness, deg));
            }

            Add("Stria_Vascularis", "Stria vascularis", "Stria", Turn(stria), Polar(0.83f, 20f),
                "A band of capillaries and pump cells on the outer wall of the scala media. It secretes " +
                "the endolymph and charges it to +80 mV — the highest resting potential in the body.");

            // ------------------------------------------------ bone and nerve

            var lamina = new List<Vector2>
            {
                new(-1.02f, -0.09f), new(-0.38f, -0.055f), new(-0.34f, -0.01f), new(-0.36f, 0.03f),
                new(-0.44f, 0.12f), new(-0.52f, 0.2f), new(-0.64f, 0.21f), new(-0.8f, 0.16f), new(-1.02f, 0.12f),
            };
            var wall = Turn(lamina);
            var upper = new List<Vector2>();
            upper.AddRange(Arc(1.16f, 0f, 180f, 48));
            upper.AddRange(Arc(1.0f, 180f, 0f, 48));
            var lower = new List<Vector2>();
            lower.AddRange(Arc(1.16f, 180f, 360f, 48));
            lower.AddRange(Arc(1.0f, 360f, 180f, 48));
            wall.Append(Turn(upper));
            wall.Append(Turn(lower));

            var bone = Add("Spiral_Lamina", "Bony canal and spiral lamina", "SectionBone", wall, new Vector2(-0.75f, 0.04f),
                "The otic capsule — the densest bone in the body — walls the canal. From the modiolus a " +
                "thin shelf, the osseous spiral lamina, reaches halfway across, capped by the spiral " +
                "limbus where the tectorial membrane is hinged.", tier: 2);
            bone.Landmarks.Add(("Spiral limbus", Face(new Vector2(-0.62f, 0.2f))));

            var ganglion = Turn(Ellipse(new Vector2(-1.52f, -0.02f), new Vector2(0.2f, 0.16f), 24));
            foreach (float angle in new[] { -k_HalfAngle * 0.6f, 0f, k_HalfAngle * 0.6f })
            {
                var fibre = new List<Vector3>
                {
                    Place(new Vector2(-1.9f, -0.34f), angle),
                    Place(new Vector2(-1.6f, -0.05f), angle),
                    Place(new Vector2(-1.3f, -0.01f), angle),
                    Place(new Vector2(-0.9f, -0.02f), angle),
                    Place(new Vector2(-0.45f, -0.02f), angle),
                    Place(new Vector2(-0.22f, Membrane(-0.22f) + 0.03f), angle),
                    Place(new Vector2(-0.13f, CortiTop(-0.12f) - 0.13f), angle),
                };
                ganglion.Append(MeshKit.Tube(Curves.Smooth(fibre, 0.02f), _ => 0.011f, 6));
            }

            FixU(ganglion);
            Add("Spiral_Ganglion", "Spiral ganglion", "Nerve", ganglion, new Vector2(-1.52f, -0.2f),
                "The cell bodies of the cochlear nerve, coiled inside the modiolus in Rosenthal's canal. " +
                "Their fibres reach out through the spiral lamina to the hair cells.");

            return parts;
        }

        // ================================================================== pieces

        /// <summary>
        /// One hair cell standing on the organ of Corti: a body reaching up to the reticular lamina and a
        /// tuft of stereocilia on top — a straight staircase for an inner hair cell, a V for an outer one.
        /// </summary>
        static MeshData HairCell(float x, float length, float radius, float angle, float cilia, bool outer)
        {
            float top = CortiTop(x) - 0.004f;
            var bottom = new Vector2(x, top - length);
            var apex = new Vector2(x, top);

            var body = new List<Vector3> { Place(bottom, angle), Place(apex, angle) };
            var mesh = MeshKit.Tube(Curves.Resample(body, length / 6f), u =>
            {
                // Rounded both ends; an inner hair cell is flask-shaped, fuller at its base.
                float round = Mathf.Sqrt(Mathf.Clamp01(Mathf.Sin(u * Mathf.PI) * 1.6f));
                float flask = outer ? 1f : Mathf.Lerp(1.15f, 0.75f, u);
                return radius * flask * Mathf.Max(round, 0.35f);
            }, 8, capStart: true, capEnd: true);

            for (int k = -1; k <= 1; k++)
            {
                float offset = k * radius * 0.55f;
                float height = outer ? cilia * (0.85f + 0.15f * Mathf.Abs(k)) : cilia * (0.6f + 0.25f * (k + 1));
                var from = new Vector2(x + (outer ? Mathf.Abs(k) * radius * 0.4f : offset), top);
                float twist = outer ? k * 0.9f : 0f;
                var start = Place(from, angle + twist);
                var end = Place(from + new Vector2(0f, height), angle + twist);
                mesh.Append(MeshKit.Tube(new List<Vector3> { start, end }, _ => 0.006f, 4, capEnd: true));
            }

            FixU(mesh, x);
            return mesh;
        }

        static float TectorialUnderside(float x) =>
            x < -0.2f ? Mathf.Lerp(0.2f, 0.25f, Mathf.InverseLerp(-0.52f, -0.2f, x)) : Mathf.Lerp(0.25f, 0.262f, Mathf.InverseLerp(-0.2f, 0.3f, x));

        // ================================================================== geometry

        static Vector2 Polar(float r, float degrees) =>
            new(r * Mathf.Cos(degrees * Mathf.Deg2Rad), r * Mathf.Sin(degrees * Mathf.Deg2Rad));

        static List<Vector2> Arc(float r, float from, float to, int steps)
        {
            var result = new List<Vector2>(steps + 1);
            for (int i = 0; i <= steps; i++)
                result.Add(Polar(r, Mathf.Lerp(from, to, i / (float)steps)));

            return result;
        }

        static List<Vector2> Ellipse(Vector2 centre, Vector2 radii, int steps)
        {
            var result = new List<Vector2>(steps);
            for (int i = 0; i < steps; i++)
            {
                float a = i / (float)steps * Mathf.PI * 2f;
                result.Add(centre + new Vector2(Mathf.Cos(a) * radii.x, Mathf.Sin(a) * radii.y));
            }

            return result;
        }

        /// <summary>A thin quad from <paramref name="a"/> to <paramref name="b"/> — a membrane or a pillar in profile.</summary>
        static List<Vector2> Strip(Vector2 a, Vector2 b, float thickness)
        {
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x) * (thickness * 0.5f);
            return new List<Vector2> { a - n, b - n, b + n, a + n };
        }

        /// <summary>A 2D profile turned through the wedge, in canal-centred coordinates.</summary>
        static MeshData Turn(List<Vector2> outline)
        {
            var shifted = new List<Vector2>(outline.Count);
            foreach (var p in outline)
                shifted.Add(new Vector2(p.x + k_TurnRadius, p.y));

            var mesh = MeshKit.Revolve(shifted, k_HalfAngle, k_Steps,
                rz => AcrossMembrane(new Vector2(rz.x - k_TurnRadius, rz.y)));
            return mesh.Transformed(Matrix4x4.Translate(new Vector3(-k_TurnRadius, 0f, 0f)));
        }

        /// <summary>A profile point placed at <paramref name="degrees"/> round the wedge, canal-centred.</summary>
        static Vector3 Place(Vector2 p, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            float r = p.x + k_TurnRadius;
            return new Vector3(r * Mathf.Cos(a) - k_TurnRadius, p.y, r * Mathf.Sin(a));
        }

        /// <summary>A point on the cut face nearest the viewer, for anchoring labels where they can be seen.</summary>
        static Vector3 Face(Vector2 p) => Place(p, -k_HalfAngle + 0.5f);

        static void FixU(MeshData mesh, float? x = null)
        {
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                float u = x.HasValue
                    ? AcrossMembrane(new Vector2(x.Value, 0f))
                    : AcrossMembrane(new Vector2(mesh.Positions[i].x, 0f));
                mesh.Uvs[i] = new Vector2(u, mesh.Uvs[i].y);
            }
        }
    }
}
