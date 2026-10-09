using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VRShootingGallery.EditorTools.Anatomy
{
    /// <summary>One labelled, pickable piece of the hologram, as the builder will make it.</summary>
    sealed class PartSpec
    {
        public string Key;
        public string DisplayName;
        public string Region;
        public string Description;
        public string Material;
        public MeshData Mesh;
        public Vector3 Anchor;
        public Vector3 Explode;
        /// <summary>0 labelled in the overview, 1 when zoomed or exploded, 2 only in the All mode.</summary>
        public int Tier;

        /// <summary>The model yaw the part is best seen from, if it has one.</summary>
        public float? ViewYaw;

        public bool Pickable = true;

        /// <summary>Fade in over u = x..y and out over u = z..w; the default leaves the part whole.</summary>
        public Vector4 EndFade = new(-2f, -1f, 2f, 3f);
        public readonly List<(string Name, Vector3 Point)> Landmarks = new();
    }

    /// <summary>
    /// The geometry of a right ear in millimetres, from the auricle to the brainstem end of the
    /// vestibulocochlear nerve, plus the handful of points and axes the animation needs.
    /// </summary>
    /// <remarks>
    /// <para>The frame is the patient's head: <b>+X medial</b> (toward the midline), <b>+Y superior</b>,
    /// <b>−Z anterior</b>. Looked at along +Z — standing in front of the patient — that is the textbook
    /// coronal view of a right ear: auricle on the left, cochlea on the right. The eardrum's centre is
    /// the origin.</para>
    ///
    /// <para>Nothing is placed in absolute coordinates if it can be placed relative to what it touches.
    /// The ossicles hang off the eardrum's frame, the stapes off the incus, the vestibule off the stapes
    /// footplate, the canals and cochlea off the vestibule, the nerves off those. Move the eardrum and
    /// the chain follows; that is the only way a hand-authored middle ear stays 4 mm deep.</para>
    ///
    /// <para>Dimensions are the standard adult figures — eardrum ~9 mm, canal ~25 mm, stapes ~3 mm,
    /// cochlea 2⅝ turns and ~9 mm across — with two licences taken for legibility at hologram scale:
    /// the membranous canals are drawn at the bony canal's bore, and the cochlear turns are separated
    /// by a hair so the spiral reads as a spiral.</para>
    /// </remarks>
    sealed class EarAnatomy
    {
        // Anatomical directions in the model frame.
        static readonly Vector3 Lateral = Vector3.left;
        static readonly Vector3 Medial = Vector3.right;
        static readonly Vector3 Superior = Vector3.up;
        static readonly Vector3 Inferior = Vector3.down;
        static readonly Vector3 Anterior = Vector3.back;
        static readonly Vector3 Posterior = Vector3.forward;

        public readonly List<PartSpec> Parts = new();

        // ---------------------------------------------------------------- what the animation needs

        /// <summary>Eardrum centre and its lateral-facing normal.</summary>
        public Vector3 DrumCentre, DrumNormal;

        /// <summary>The ossicular axis: malleus and incus rock about the line through these two points.</summary>
        public Vector3 OssicleAxisA, OssicleAxisB;

        public Vector3 Umbo, StapesAxis, Lenticular;

        /// <summary>The round window's outward normal, into the middle ear.</summary>
        public Vector3 RoundWindowNormal;

        /// <summary>Where sound arrives from, and where it enters the canal.</summary>
        public Vector3 SoundOrigin, CanalEntrance;

        /// <summary>Plane normal of each semicircular canal, and the sign that turns a head rotation about
        /// that normal into flow along the canal's u.</summary>
        public Vector3 AnteriorCanalNormal, PosteriorCanalNormal, LateralCanalNormal;
        public float AnteriorCanalSign, PosteriorCanalSign, LateralCanalSign;

        /// <summary>Where along the cochlea (u, 0 base → 1 apex) the cross-section is cut, and its pose.</summary>
        public float SectionU;
        public Vector3 SectionPoint, SectionTangent;
        public float SectionTubeRadius;

        public Vector3 CochleaBase, CochleaApex;

        // ---------------------------------------------------------------- internal state

        Vector3 m_DrumUp, m_DrumAnterior;
        List<Vector3> m_CanalPath;
        Vector3 m_LateralProcess, m_MalleusNeck, m_MalleusHead, m_AnteriorProcessTip;
        Vector3 m_IncusBody, m_ShortProcessTip, m_LongProcessTip;
        Vector3 m_StapesHead, m_Footplate;
        Vector3 m_Vestibule;
        Vector3 m_TiltedAnterior, m_TiltedSuperior;
        Vector3 m_ModiolusBase, m_CochlearAxis;
        List<Vector3> m_CochleaPath;
        Vector3 m_TrunkStart, m_TrunkEnd, m_Geniculate;
        List<Vector3> m_FacialPath;

        public static EarAnatomy Build()
        {
            var anatomy = new EarAnatomy();
            var clock = Stopwatch.StartNew();

            anatomy.Drum();
            anatomy.Canal();
            anatomy.Auricle();
            anatomy.Ossicles();
            anatomy.Labyrinth();
            anatomy.Windows();
            anatomy.Nerves();
            anatomy.FacialNerve();
            anatomy.EustachianTube();

            Debug.Log($"[EarHologram] Anatomy meshed in {clock.ElapsedMilliseconds} ms: " +
                      $"{anatomy.Parts.Count} parts.");
            return anatomy;
        }

        PartSpec Add(string key, string name, string region, string material, MeshData mesh, string description)
        {
            var part = new PartSpec
            {
                Key = key,
                DisplayName = name,
                Region = region,
                Material = material,
                Mesh = mesh,
                Description = description,
                Anchor = mesh.Nearest(mesh.Centroid()),
            };
            Parts.Add(part);
            return part;
        }

        // ================================================================== outer ear

        void Drum()
        {
            DrumCentre = Vector3.zero;

            // The canal's last stretch runs almost straight medially; the drum is tipped 35° from
            // square to it — top lateral, bottom medial — and turned a little to face forward. That
            // leaves the deep anteroinferior recess the real canal has under the drum.
            var along = Medial;
            DrumNormal = (-along * Mathf.Cos(35f * Mathf.Deg2Rad)
                          + Inferior * (Mathf.Sin(35f * Mathf.Deg2Rad) * 0.93f)
                          + Anterior * (Mathf.Sin(35f * Mathf.Deg2Rad) * 0.37f)).normalized;

            m_DrumUp = Vector3.ProjectOnPlane(Superior, DrumNormal).normalized;
            m_DrumAnterior = Vector3.Cross(DrumNormal, m_DrumUp);
            if (Vector3.Dot(m_DrumAnterior, Anterior) < 0f)
                m_DrumAnterior = -m_DrumAnterior;

            const float radiusUp = 4.6f, radiusSide = 4.0f, depth = 1.7f;
            var apex = new Vector2(-0.35f, 0.25f);
            Umbo = DrumCentre + m_DrumUp * apex.x + m_DrumAnterior * apex.y - DrumNormal * depth;

            var mesh = MeshKit.Membrane(DrumCentre, DrumNormal, m_DrumUp, radiusUp, radiusSide, depth, apex, 14, 64);

            // The fibrocartilaginous annulus round its edge.
            mesh.Append(RimOnly(MeshKit.Torus(DrumCentre, DrumNormal, m_DrumUp, radiusUp, radiusSide, 0.16f, 72, 6)));

            var drum = Add("Tympanic_Membrane", "Tympanic membrane", "Middle ear · Eardrum", "Membrane", mesh,
                "The eardrum: a cone of skin, collagen and mucosa about 9 mm across and 0.1 mm thick that " +
                "closes off the ear canal. Sound pressure makes it vibrate, and the handle of the malleus, " +
                "fixed along its inner surface, rides every movement to its tip at the umbo.");
            drum.Anchor = DrumCentre + m_DrumUp * -2.6f + m_DrumAnterior * 1.8f - DrumNormal * 0.8f;
            drum.ViewYaw = -90f;
            drum.Explode = Lateral * 2.5f;
            drum.Landmarks.Add(("Umbo", Umbo));
            drum.Landmarks.Add(("Pars flaccida", DrumCentre + m_DrumUp * 3.9f + m_DrumAnterior * 0.3f));
        }

        void Canal()
        {
            // S-shaped, from the concha inward. The outer, cartilaginous third runs medially, up and
            // back; the inner, bony two thirds medially, forward and down. Read as control points from
            // the concha.
            var control = new List<Vector3>
            {
                new(-27.0f, -0.2f, -1.4f),
                new(-24.5f, 0.8f, -0.6f),
                new(-21.0f, 2.0f, 0.6f),
                new(-17.0f, 2.4f, 1.5f),
                new(-12.0f, 1.8f, 1.6f),
                new(-6.5f, 0.8f, 0.9f),
                new(-2.5f, 0.2f, 0.25f),
                new(1.5f, -0.2f, -0.3f),
                new(4.5f, -0.5f, -0.6f),
            };
            m_CanalPath = Curves.Smooth(control, 0.3f);
            CanalEntrance = m_CanalPath[0];

            float Radius(float u)
            {
                // Flared at the concha, pinched at the isthmus where cartilage meets bone.
                if (u < 0.08f) return Mathf.Lerp(4.4f, 3.8f, u / 0.08f);
                if (u < 0.5f) return Mathf.Lerp(3.8f, 3.0f, Mathf.SmoothStep(0f, 1f, (u - 0.08f) / 0.42f));
                return Mathf.Lerp(3.0f, 3.85f, Mathf.SmoothStep(0f, 1f, (u - 0.5f) / 0.5f));
            }

            var mesh = MeshKit.Tube(m_CanalPath, Radius, 40);

            // Cut the medial end off along the eardrum's plane, so the canal ends where the drum is —
            // obliquely, with the long floor running on under it.
            var end = Curves.TangentAt(m_CanalPath, 1f);
            float along = Vector3.Dot(end, DrumNormal);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var v = mesh.Positions[i];
                float s = Vector3.Dot(v - DrumCentre, DrumNormal);
                if (s < 0f)
                    mesh.Positions[i] = v - end * (s / along);
            }

            var canal = Add("Ear_Canal", "External auditory canal", "Outer ear · Ear canal", "Outer", mesh,
                "An S-shaped tube about 2.5 cm long — cartilage in its outer third, bone in its inner two " +
                "thirds. Like an organ pipe closed at one end it resonates near 3 kHz, boosting the " +
                "frequencies of speech by around 10 dB, and its glands make cerumen, earwax.");
            canal.Anchor = Curves.At(m_CanalPath, 0.35f) + Superior * 3.0f;
            canal.Explode = Lateral * 7f;
            canal.Landmarks.Add(("Isthmus", Curves.At(m_CanalPath, 0.5f) + Inferior * 2.9f));
        }

        void Auricle()
        {
            // The auricle's plane: facing laterally and a little forward, its free back edge flared 25°
            // off the head. (s, t) in the sheet are (backward along the ear, upward); height is toward
            // the viewer of the ear's outer face.
            float flare = 25f * Mathf.Deg2Rad;
            var back = (Posterior * Mathf.Cos(flare) + Lateral * Mathf.Sin(flare)).normalized;
            var up = Superior;
            var outward = (Lateral * Mathf.Cos(flare) + Anterior * Mathf.Sin(flare)).normalized;

            var outline = new Vector2[]
            {
                new(-7.2f, 9f), new(-7.6f, 14f), new(-7.2f, 19.5f), new(-5.6f, 24.5f), new(-2.6f, 28.8f),
                new(1.8f, 31.6f), new(7f, 32.4f), new(12f, 31f), new(16.4f, 27.6f), new(19.6f, 22.4f),
                new(21.3f, 16f), new(21.8f, 9f), new(21.2f, 2f), new(19.6f, -4.5f), new(17f, -10.5f),
                new(13.4f, -15.5f), new(9.6f, -20f), new(5.6f, -24.4f), new(1.2f, -26.6f), new(-2.8f, -25.8f),
                new(-5.6f, -22.4f), new(-6.6f, -17.6f), new(-6.4f, -12.5f), new(-6.8f, -8.5f), new(-8.2f, -4f),
                new(-8.4f, 1f), new(-7.8f, 5f),
            };

            // First pass without an origin, to find how deep the concha is where the canal opens.
            var entrance2 = new Vector2(1f, -1f);
            var probe = NewAuricleSheet(Vector3.zero, back, up, outward, outline);
            var origin = CanalEntrance - (back * entrance2.x + up * entrance2.y + outward * probe.Height(entrance2));
            var sheet = NewAuricleSheet(origin, back, up, outward, outline);

            Vector3 On(float s, float t, float lift = 0f) => sheet.Point(new Vector2(s, t), lift);

            var shape = new SdfShape { Blend = 1.1f };
            const int part = 0;

            shape.NewGroup();
            shape.Sheet(sheet, part);

            // Helix: the rolled rim, from its crus inside the concha, up the front, over the top and
            // down the back into the lobule.
            var helix2 = new Vector2[]
            {
                new(3.5f, 4.4f), new(-1f, 4.6f), new(-4.5f, 6f), new(-6.4f, 9.5f), new(-6.6f, 14f),
                new(-6.2f, 19.5f), new(-4.6f, 24f), new(-1.8f, 27.8f), new(2f, 30.3f), new(7f, 31.1f),
                new(11.8f, 29.8f), new(15.6f, 26.6f), new(18.5f, 21.8f), new(20.1f, 16f), new(20.6f, 9f),
                new(20f, 2f), new(18.4f, -4.5f), new(15.8f, -10.2f), new(12.2f, -15.2f), new(8.8f, -19f),
            };
            var helixControl = new List<Vector3>();
            for (int i = 0; i < helix2.Length; i++)
            {
                float lift = i < 3 ? 0.4f : 1.1f;
                helixControl.Add(On(helix2[i].x, helix2[i].y, lift));
            }

            var helix = Curves.Smooth(helixControl, 0.35f);
            shape.NewGroup();
            shape.Chain(helix, u =>
            {
                if (u < 0.1f) return Mathf.Lerp(1.0f, 1.7f, u / 0.1f);
                if (u < 0.75f) return Mathf.Lerp(1.7f, 2.3f, Mathf.SmoothStep(0f, 1f, (u - 0.1f) / 0.3f));
                return Mathf.Lerp(2.3f, 1.3f, (u - 0.75f) / 0.25f);
            }, part);

            // Antihelix: the Y-shaped ridge inside the helix, forking into its two crura round the
            // triangular fossa.
            var body = new List<Vector3>
            {
                On(11.5f, -8f, 0.7f), On(14.6f, -2f, 0.7f), On(15.8f, 5f, 0.7f), On(14.6f, 11.5f, 0.7f),
                On(12.6f, 15.5f, 0.6f), On(12f, 20.5f, 0.5f), On(9.8f, 25f, 0.4f),
            };
            var inferiorCrus = new List<Vector3> { On(12.6f, 15.5f, 0.6f), On(8.6f, 16.2f, 0.5f), On(3.6f, 15f, 0.3f) };
            shape.NewGroup();
            shape.Chain(Curves.Smooth(body, 0.35f), u => Mathf.Lerp(1.35f, 0.9f, u), part);
            shape.Chain(Curves.Smooth(inferiorCrus, 0.35f), u => Mathf.Lerp(1.0f, 0.75f, u), part);

            var frame = Quaternion.LookRotation(outward, up);

            // Tragus: the flap in front of the canal that you press to close it.
            var tragus = On(-6.4f, -1.5f, 1.2f) + back * 0.8f;
            shape.NewGroup();
            shape.Ellipsoid(tragus, frame, new Vector3(2.2f, 3.4f, 1.8f), part);

            var antitragus = On(10.2f, -10f, 1.0f);
            shape.NewGroup();
            shape.Ellipsoid(antitragus, frame, new Vector3(2.6f, 2.0f, 1.4f), part);

            var lobule = On(1.2f, -19.5f, 0.2f);
            shape.NewGroup();
            shape.Ellipsoid(lobule, frame, new Vector3(6.6f, 6.6f, 2.4f), part);

            // The opening of the canal through the floor of the concha.
            var into = Curves.TangentAt(m_CanalPath, 0f);
            shape.NewGroup();
            shape.RoundCone(CanalEntrance - into * 6f, CanalEntrance + into * 5f, 3.7f, 3.7f, part, subtract: true);

            var mesh = shape.Polygonize(0.4f)[part];

            var auricle = Add("Auricle", "Auricle (pinna)", "Outer ear", "Outer", mesh,
                "The visible ear: a single plate of elastic cartilage under skin, folded into ridges and " +
                "hollows. It gathers sound into the canal, and because its folds colour a sound " +
                "differently depending on where it comes from, it is how you tell above from below and " +
                "front from back.");
            auricle.Anchor = On(14f, 24f, 1.5f);
            auricle.ViewYaw = -75f;
            auricle.Explode = Lateral * 14f + Anterior * 2f;
            auricle.Landmarks.Add(("Helix", On(18.6f, 21.6f, 2.8f)));
            auricle.Landmarks.Add(("Antihelix", On(15.2f, 3f, 2f)));
            auricle.Landmarks.Add(("Concha", On(5f, -3f, 0.5f)));
            auricle.Landmarks.Add(("Tragus", tragus + outward * 1.5f));
            auricle.Landmarks.Add(("Antitragus", antitragus + outward * 1.2f));
            auricle.Landmarks.Add(("Lobule", lobule + outward * 2.2f + up * -3f));
            auricle.Landmarks.Add(("Triangular fossa", On(8.3f, 20.2f, 0.4f)));
            auricle.Landmarks.Add(("Scaphoid fossa", On(18.5f, 10f, 0.2f)));

            SoundOrigin = CanalEntrance + outward * 55f + back * 6f + up * 4f;
        }

        static HeightSheet NewAuricleSheet(Vector3 origin, Vector3 back, Vector3 up, Vector3 outward, Vector2[] outline)
        {
            var sheet = new HeightSheet(origin, back, up, outward, outline, 1.6f, 0.8f);

            // Concha: the deep bowl round the canal (cavum) and its shallower upper bay (cymba),
            // divided by the crus of the helix.
            sheet.AddBump(new Vector2(4.5f, -1.5f), new Vector2(7.5f, 6.5f), -6.5f);
            sheet.AddBump(new Vector2(5.5f, 8f), new Vector2(6f, 3.2f), -3.8f);
            sheet.AddRidge(new Vector2[] { new(-4f, 5f), new(1f, 4.6f), new(6f, 4.8f), new(10f, 5.8f) }, 1.6f, 2.6f);

            // Antihelix and its crura.
            sheet.AddRidge(new Vector2[] { new(11.5f, -8f), new(14.6f, -2f), new(15.8f, 5f), new(14.6f, 11.5f), new(12.6f, 15.5f) }, 2.6f, 2.2f);
            sheet.AddRidge(new Vector2[] { new(12.6f, 15.5f), new(12f, 20.5f), new(9.8f, 25f) }, 2.0f, 1.8f);
            sheet.AddRidge(new Vector2[] { new(12.6f, 15.5f), new(8.6f, 16.2f), new(3.6f, 15f) }, 1.8f, 1.6f);

            // Scaphoid fossa, the groove inside the helix; triangular fossa, between the crura.
            sheet.AddRidge(new Vector2[] { new(19.2f, -4f), new(19.4f, 6f), new(18f, 16f), new(14.8f, 23.5f), new(9.5f, 28.6f), new(3.5f, 29.2f) }, 2.0f, -1.2f);
            sheet.AddBump(new Vector2(8.3f, 20.2f), new Vector2(3.2f, 2.8f), -1.9f);
            return sheet;
        }

        // ================================================================== middle ear

        void Ossicles()
        {
            // ------------------------------------------------ malleus

            m_LateralProcess = DrumCentre + m_DrumUp * 3.55f + m_DrumAnterior * 0.55f - DrumNormal * 0.3f;
            m_MalleusNeck = m_LateralProcess + Superior * 1.4f + Medial * 0.5f + Posterior * 0.1f;
            m_MalleusHead = m_MalleusNeck + Superior * 1.9f + Medial * 0.3f;
            m_AnteriorProcessTip = m_MalleusNeck + Anterior * 2.3f + Inferior * 0.25f + Medial * 0.1f;

            var malleus = new SdfShape { Blend = 0.35f };
            malleus.NewGroup();
            var handle = Curves.Smooth(new List<Vector3>
            {
                Umbo - DrumNormal * 0.2f,
                Vector3.Lerp(Umbo, m_LateralProcess, 0.5f) - DrumNormal * 0.28f,
                m_LateralProcess - DrumNormal * 0.1f,
            }, 0.1f);
            malleus.Chain(handle, u => Mathf.Lerp(0.34f, 0.42f, u), 0);
            malleus.RoundCone(m_LateralProcess - DrumNormal * 0.1f, m_LateralProcess + DrumNormal * 0.4f, 0.45f, 0.24f, 0);
            malleus.NewGroup();
            malleus.RoundCone(m_LateralProcess, m_MalleusNeck, 0.45f, 0.55f, 0);
            malleus.RoundCone(m_MalleusNeck, m_AnteriorProcessTip, 0.32f, 0.07f, 0);
            malleus.NewGroup();
            malleus.Ellipsoid(m_MalleusHead, Quaternion.Euler(0f, 0f, -12f), new Vector3(1.1f, 1.5f, 1.2f), 0);

            var malleusMesh = malleus.Polygonize(0.07f)[0];
            var malleusPart = Add("Malleus", "Malleus (hammer)", "Middle ear · Ossicle", "Bone", malleusMesh,
                "The first and largest of the three ossicles, about 8 mm long. Its handle is bound into " +
                "the eardrum; its head sits up in the attic of the middle ear, locked to the incus by a " +
                "saddle joint, so the two move together as one lever.");
            malleusPart.Anchor = malleusMesh.Nearest(m_MalleusHead + Superior * 1.5f);
            malleusPart.Tier = 1;
            malleusPart.ViewYaw = -65f;
            malleusPart.Explode = new Vector3(-0.9f, 2.4f, -0.9f);
            malleusPart.Landmarks.Add(("Head", m_MalleusHead + Anterior * 1.2f));
            malleusPart.Landmarks.Add(("Handle (manubrium)", Vector3.Lerp(Umbo, m_LateralProcess, 0.4f)));
            malleusPart.Landmarks.Add(("Anterior process", m_AnteriorProcessTip));

            // ------------------------------------------------ incus

            m_IncusBody = m_MalleusHead + Posterior * 1.55f + Medial * 0.35f + Inferior * 0.2f;
            m_ShortProcessTip = m_IncusBody + Posterior * 2.6f + Inferior * 0.3f + Medial * 0.2f;
            m_LongProcessTip = m_IncusBody + Inferior * 4.6f + Posterior * 0.2f + Medial * 0.6f;
            Lenticular = m_LongProcessTip + Medial * 0.7f + Inferior * 0.05f;

            var incus = new SdfShape { Blend = 0.35f };
            incus.NewGroup();
            incus.Ellipsoid(m_IncusBody, Quaternion.Euler(0f, 0f, 8f), new Vector3(1.05f, 1.35f, 1.05f), 0);
            incus.NewGroup();
            incus.RoundCone(m_IncusBody, m_ShortProcessTip, 0.75f, 0.28f, 0);
            incus.NewGroup();
            var longProcess = Curves.Smooth(new List<Vector3>
            {
                m_IncusBody + Inferior * 0.6f,
                m_IncusBody + Inferior * 3.0f + Posterior * 0.35f + Medial * 0.25f,
                m_LongProcessTip,
                m_LongProcessTip + Medial * 0.4f + Inferior * 0.1f,
                Lenticular,
            }, 0.1f);
            incus.Chain(longProcess, u => u < 0.8f ? Mathf.Lerp(0.55f, 0.32f, u / 0.8f) : 0.24f, 0);
            incus.NewGroup();
            incus.Ellipsoid(Lenticular, Quaternion.identity, new Vector3(0.28f, 0.36f, 0.36f), 0);

            var incusMesh = incus.Polygonize(0.07f)[0];
            var incusPart = Add("Incus", "Incus (anvil)", "Middle ear · Ossicle", "Bone", incusMesh,
                "The middle ossicle. Its body meets the malleus head; its long process reaches down " +
                "beside the malleus handle to the stapes. Because its arm is shorter than the malleus " +
                "handle, the pair act as a lever that trades movement for about 1.3× more force.");
            incusPart.Anchor = incusMesh.Nearest(m_ShortProcessTip + Superior * 1f);
            incusPart.Tier = 1;
            incusPart.ViewYaw = -65f;
            incusPart.Explode = new Vector3(0.2f, 3.3f, 1.8f);
            incusPart.Landmarks.Add(("Body", m_IncusBody + Superior * 1.2f));
            incusPart.Landmarks.Add(("Short process", m_ShortProcessTip));
            incusPart.Landmarks.Add(("Long process", Vector3.Lerp(m_IncusBody, m_LongProcessTip, 0.55f)));

            OssicleAxisA = m_AnteriorProcessTip;
            OssicleAxisB = m_ShortProcessTip;

            // ------------------------------------------------ stapes

            StapesAxis = (Medial + Inferior * 0.08f).normalized;
            m_StapesHead = Lenticular + StapesAxis * 0.5f;
            m_Footplate = m_StapesHead + StapesAxis * 3.1f;
            var neckEnd = m_StapesHead + StapesAxis * 0.55f;

            var stapes = new SdfShape { Blend = 0.15f };
            stapes.NewGroup();
            stapes.Ellipsoid(m_StapesHead, Quaternion.identity, new Vector3(0.34f, 0.36f, 0.42f), 0);
            stapes.RoundCone(m_StapesHead, neckEnd, 0.3f, 0.26f, 0);
            stapes.NewGroup();
            var anteriorCrus = Curves.Smooth(new List<Vector3>
            {
                neckEnd + Anterior * 0.15f,
                neckEnd + StapesAxis * 1.0f + Anterior * 0.95f,
                m_Footplate - StapesAxis * 0.2f + Anterior * 1.2f,
            }, 0.06f);
            stapes.Chain(anteriorCrus, u => Mathf.Lerp(0.17f, 0.14f, u), 0);
            stapes.NewGroup();
            var posteriorCrus = Curves.Smooth(new List<Vector3>
            {
                neckEnd + Posterior * 0.15f,
                neckEnd + StapesAxis * 0.9f + Posterior * 1.2f,
                m_Footplate - StapesAxis * 0.2f + Posterior * 1.3f,
            }, 0.06f);
            stapes.Chain(posteriorCrus, u => Mathf.Lerp(0.18f, 0.15f, u), 0);
            stapes.NewGroup();
            stapes.Ellipsoid(m_Footplate, Quaternion.FromToRotation(Vector3.right, StapesAxis),
                new Vector3(0.2f, 0.66f, 1.48f), 0);

            var stapesMesh = stapes.Polygonize(0.05f)[0];
            var stapesPart = Add("Stapes", "Stapes (stirrup)", "Middle ear · Ossicle", "Bone", stapesMesh,
                "The smallest bone in the body, about 3 mm tall. Its footplate fills the oval window and " +
                "pistons into the inner ear. Funnelling the eardrum's ~55 mm² onto this ~3 mm² plate is " +
                "most of the middle ear's ~20× pressure gain — without it, 99.9% of sound would bounce " +
                "off the fluid of the inner ear.");
            stapesPart.Anchor = stapesMesh.Nearest(m_StapesHead + Inferior * 1f);
            stapesPart.Tier = 1;
            stapesPart.ViewYaw = -40f;
            stapesPart.Explode = new Vector3(0.4f, -1.4f, 1.0f);
            stapesPart.Landmarks.Add(("Head", m_StapesHead + Superior * 0.6f));
            stapesPart.Landmarks.Add(("Crura", neckEnd + StapesAxis * 1.1f + Posterior * 1.3f));
            stapesPart.Landmarks.Add(("Footplate", m_Footplate + Inferior * 0.7f));
        }

        // ================================================================== inner ear

        void Labyrinth()
        {
            // The vestibule sits just medial of the oval window: the footplate is its lateral wall.
            m_Vestibule = m_Footplate + StapesAxis * 1.55f + Superior * 0.1f + Posterior * 0.3f;
            var v = m_Vestibule;

            // The canals and the lateral canal's plane are tipped 30° anterior-end-up from the
            // horizontal — the lateral canal really is — and the three canals are near enough at right
            // angles to one another.
            var tilt = Quaternion.AngleAxis(30f, Vector3.right);
            m_TiltedAnterior = tilt * Anterior;
            m_TiltedSuperior = tilt * Superior;
            var a = m_TiltedAnterior;
            var s = m_TiltedSuperior;
            var m = Medial;

            // The anterior canal stands in the plane square to the petrous ridge (normal anteromedial);
            // the posterior canal in the plane along it (normal anterolateral). Each vertical canal is
            // parallel to the other ear's opposite one.
            LateralCanalNormal = s;
            AnteriorCanalNormal = (a + m).normalized;
            PosteriorCanalNormal = (a - m).normalized;

            const int cochleaPart = 0, vestibulePart = 1, anteriorPart = 2, posteriorPart = 3, lateralPart = 4;
            const float bore = 0.5f, ampulla = 1.0f;

            var shape = new SdfShape { Blend = 0.45f };

            shape.NewGroup();
            shape.Ellipsoid(v, Quaternion.identity, new Vector3(1.5f, 2.3f, 2.0f), vestibulePart);

            // ------------------------------------------------ semicircular canals

            // The common crus rises from the back of the vestibule, up, back and medial; the two
            // vertical canals leave its top in their own planes. The lateral canal's chord runs from its
            // ampulla at the front of the vestibule back to its posteromedial limb, so the loop bulges
            // posterolaterally toward the mastoid rather than out into the middle ear.
            var aPlane = (a - m).normalized;
            var pPlane = (a + m).normalized;
            var commonCrusTop = v + s * 3.0f - a * 1.8f + m * 1.5f;
            var ampAnterior = commonCrusTop + aPlane * 4.6f - s * 0.8f;
            var ampPosterior = commonCrusTop - s * 4.8f - pPlane * 0.57f;
            var ampLateral = v + s * 2.0f + a * 1.4f - m * 1.1f;
            var lateralEnd = ampLateral - a * 3.3f + m * 2.2f;

            var anteriorArc = ArcThrough(ampAnterior, commonCrusTop, AnteriorCanalNormal, s, 245f, out var anteriorCentre);
            var posteriorArc = ArcThrough(commonCrusTop, ampPosterior, PosteriorCanalNormal, -pPlane, 250f, out var posteriorCentre);
            var lateralArc = ArcThrough(ampLateral, lateralEnd, LateralCanalNormal, -a - m, 250f, out var lateralCentre);

            // The posterior arc is authored crus-first; its ampulla is at the bottom, so turn it round
            // to run ampulla-first like the other two.
            posteriorArc.Reverse();

            float CanalRadius(float u) => bore + (ampulla - bore) * Mathf.Exp(-Sq((u - 0.07f) / 0.06f));

            var anteriorPath = Curves.Smooth(Join(new[] { v + a * 0.6f + s * 1.0f - m * 0.5f }, anteriorArc), 0.12f);
            shape.NewGroup();
            shape.Chain(anteriorPath, CanalRadius, anteriorPart);

            var posteriorPath = Curves.Smooth(Join(new[] { v - s * 0.9f - a * 0.9f + m * 0.5f }, posteriorArc), 0.12f);
            shape.NewGroup();
            shape.Chain(posteriorPath, CanalRadius, posteriorPart);

            // The common crus: anterior and posterior canals' shared, non-ampullated limb.
            var crus = Curves.Smooth(new List<Vector3>
            {
                commonCrusTop, commonCrusTop - s * 1.5f - a * 0.1f, v + s * 1.3f - a * 1.0f + m * 0.6f,
            }, 0.12f);
            shape.Chain(crus, _ => bore * 1.15f, posteriorPart, 1f, 1f);

            var lateralPath = Curves.Smooth(Join(new[] { v + a * 0.6f + s * 0.7f - m * 0.5f }, lateralArc,
                new[] { v - a * 1.0f + s * 0.5f + m * 0.5f }), 0.12f);
            shape.NewGroup();
            shape.Chain(lateralPath, CanalRadius, lateralPart);

            AnteriorCanalSign = FlowSign(anteriorPath, anteriorCentre, AnteriorCanalNormal);
            PosteriorCanalSign = FlowSign(posteriorPath, posteriorCentre, PosteriorCanalNormal);
            LateralCanalSign = FlowSign(lateralPath, lateralCentre, LateralCanalNormal);

            // ------------------------------------------------ cochlea

            // Apex forward, outward and a little down; the basal turn bulges into the middle ear as
            // the promontory, just medial of the umbo.
            m_CochlearAxis = (Lateral * 0.45f + Inferior * 0.3f + Anterior * 0.84f).normalized;
            m_ModiolusBase = new Vector3(Umbo.x + 5.6f, v.y - 2.0f, v.z - 4.6f);

            var hookStart = v + Anterior * 0.9f + Inferior * 1.1f;
            var p = Vector3.ProjectOnPlane(hookStart - m_ModiolusBase, m_CochlearAxis).normalized;
            var q = Vector3.Cross(m_CochlearAxis, p);
            if (Vector3.Dot(q, Lateral) < 0f)
                q = -q;

            const float turns = 2.6f, baseRadius = 3.4f, apexRadius = 0.6f, height = 3.3f;
            float span = turns * Mathf.PI * 2f;
            var spiral = new List<Vector3> { v + Anterior * 0.4f + Inferior * 0.6f };
            for (int i = 0; i <= 260; i++)
            {
                float f = i / 260f;
                float theta = f * span;
                float r = Mathf.Lerp(baseRadius, apexRadius, Mathf.Pow(f, 0.8f));
                spiral.Add(m_ModiolusBase + m_CochlearAxis * (height * f)
                           + (p * Mathf.Cos(theta) + q * Mathf.Sin(theta)) * r);
            }

            m_CochleaPath = Curves.Smooth(spiral, 0.1f);
            float CochlearBore(float u) => Mathf.Lerp(1.0f, 0.45f, Mathf.Pow(u, 0.7f));
            shape.NewGroup();
            shape.Chain(m_CochleaPath, CochlearBore, cochleaPart);

            CochleaBase = m_CochleaPath[0];
            CochleaApex = m_CochleaPath[^1];

            // The 2 kHz place — where the cross-section is taken.
            SectionU = 1f - Mathf.Log10(2000f / 165.4f + 0.88f) / 2.1f;
            SectionPoint = Curves.At(m_CochleaPath, SectionU);
            SectionTangent = Curves.TangentAt(m_CochleaPath, SectionU);
            SectionTubeRadius = CochlearBore(SectionU);

            var meshes = shape.Polygonize(0.12f);

            var cochlea = Add("Cochlea", "Cochlea", "Inner ear · Hearing", "Cochlea", meshes[cochleaPart],
                "The organ of hearing: a fluid-filled spiral of about two and a half turns, the size of a pea. Sound " +
                "sends a travelling wave along the basilar membrane inside it. High pitches peak near the " +
                "base and low pitches near the apex, so every place along the spiral hears its own " +
                "frequency — a piano keyboard, rolled up.");
            cochlea.Anchor = meshes[cochleaPart].Nearest(m_ModiolusBase + m_CochlearAxis * 2f - q * 5f + Anterior * 2f);
            cochlea.ViewYaw = -28f;
            cochlea.Explode = Medial * 6f + Anterior * 3f + Inferior * 1f;
            cochlea.Landmarks.Add(("Base · high pitch (20 kHz)", Curves.At(m_CochleaPath, 0.05f)));
            cochlea.Landmarks.Add(("Apex · low pitch (20 Hz)", CochleaApex));
            cochlea.Landmarks.Add(("Section A · 2 kHz place", SectionPoint));

            var vestibule = Add("Vestibule", "Vestibule", "Inner ear · Balance", "Vestibular", meshes[vestibulePart],
                "The central chamber of the inner ear, behind the oval window, joining the cochlea in " +
                "front to the semicircular canals behind. It houses the utricle and saccule, the " +
                "otolith organs.");
            vestibule.Anchor = meshes[vestibulePart].Nearest(v + Inferior * 3f + Posterior * 1f);
            vestibule.Tier = 1;
            vestibule.ViewYaw = -80f;
            vestibule.Explode = Medial * 6f;

            var anterior = Add("Anterior_Canal", "Anterior semicircular canal", "Inner ear · Balance", "Vestibular",
                meshes[anteriorPart],
                "One of three fluid-filled loops set at right angles. Also called the superior canal, it " +
                "senses rotation in its own plane — chiefly nodding. When the head turns, the fluid lags " +
                "behind, bending the gel cupula in the canal's swollen ampulla and the hair cells beneath it.");
            anterior.Anchor = meshes[anteriorPart].Nearest(anteriorCentre + s * 4f);
            anterior.Tier = 1;
            // Face-on to each canal's plane: the anterior canal's normal points anteromedially, the
            // posterior's anterolaterally, and the lateral canal's up and back — seen from behind and above.
            anterior.ViewYaw = 50f;
            anterior.Explode = Medial * 6f + s * 1.5f;
            anterior.Landmarks.Add(("Ampulla", ampAnterior));
            anterior.Landmarks.Add(("Common crus", commonCrusTop - s * 1f));

            var posterior = Add("Posterior_Canal", "Posterior semicircular canal", "Inner ear · Balance", "Vestibular",
                meshes[posteriorPart],
                "Senses rotation in its plane — tilting the head toward the shoulder, and nodding. It is " +
                "the canal usually behind benign positional vertigo (BPPV), when loose otoconia drift in " +
                "from the utricle and keep moving after the head has stopped.");
            posterior.Anchor = meshes[posteriorPart].Nearest(posteriorCentre - (a + m).normalized * 4f);
            posterior.Tier = 1;
            posterior.ViewYaw = -50f;
            posterior.Explode = Medial * 6f - (a + m).normalized * 1.5f;
            posterior.Landmarks.Add(("Ampulla", ampPosterior));

            var lateral = Add("Lateral_Canal", "Lateral semicircular canal", "Inner ear · Balance", "Vestibular",
                meshes[lateralPart],
                "The horizontal canal, tipped about 30° up at the front. It senses the head turning side " +
                "to side — shaking it 'no'. The same canal in the other ear works as its mirror: one is " +
                "excited while the other is inhibited.");
            lateral.Anchor = meshes[lateralPart].Nearest(lateralCentre + Lateral * 3f);
            lateral.Tier = 1;
            lateral.ViewYaw = 180f;
            lateral.Explode = Medial * 6f + Lateral * 1.5f + Posterior * 0.5f;
            lateral.Landmarks.Add(("Ampulla", ampLateral));

            // ------------------------------------------------ otolith organs

            var sacs = new SdfShape { Blend = 0.2f };
            sacs.NewGroup();
            var utricleCentre = v + s * 0.7f - a * 0.4f;
            var sacculeCentre = v - s * 0.95f + a * 0.55f;
            sacs.Ellipsoid(utricleCentre, tilt, new Vector3(0.75f, 0.85f, 1.25f), 0);
            sacs.NewGroup();
            sacs.Ellipsoid(sacculeCentre, tilt, new Vector3(0.55f, 0.8f, 0.75f), 1);
            var sacMeshes = sacs.Polygonize(0.1f);

            var utricle = Add("Utricle", "Utricle", "Inner ear · Balance", "Otolith", sacMeshes[0],
                "An otolith organ lying almost horizontal. Its hair cells carry a gel layer weighted with " +
                "calcium-carbonate crystals, so it senses gravity and forward, backward and sideways " +
                "acceleration — the tilt of your head, a car pulling away.");
            utricle.Tier = 2;
            utricle.Pickable = false;
            utricle.Anchor = utricleCentre + s * 0.9f;
            utricle.Explode = Medial * 6f;

            var saccule = Add("Saccule", "Saccule", "Inner ear · Balance", "Otolith", sacMeshes[1],
                "The utricle's partner, standing nearly vertical: it senses up-and-down acceleration, " +
                "such as a lift starting or stopping.");
            saccule.Tier = 2;
            saccule.Pickable = false;
            saccule.Anchor = sacculeCentre - s * 0.8f;
            saccule.Explode = Medial * 6f;
        }

        void Windows()
        {
            // Rims carry u = 1, where the vibration profile sin(πu) is zero, so they stay put while the
            // membranes inside them move.
            var oval = RimOnly(MeshKit.Torus(m_Footplate - StapesAxis * 0.05f, StapesAxis, Anterior, 1.62f, 0.84f, 0.13f, 56, 6));
            var ovalMembrane = MeshKit.Membrane(m_Footplate, -StapesAxis, Superior, 0.84f, 1.62f, 0f, Vector2.zero, 3, 40);
            oval.Append(ovalMembrane);

            var ovalPart = Add("Oval_Window", "Oval window", "Middle ear · Inner ear boundary", "Membrane", oval,
                "The opening from the middle ear into the vestibule, sealed round the stapes footplate by " +
                "the annular ligament. Each push of the stapes here sends a pressure wave into the " +
                "cochlea's fluid.");
            ovalPart.Anchor = m_Footplate + Anterior * 1.6f;
            ovalPart.Explode = Medial * 6f;
            ovalPart.Tier = 2;

            // The round window: on the basal turn, under and behind the oval window, facing back into
            // the middle ear.
            float u = 0.035f;
            var centreLine = Curves.At(m_CochleaPath, u);
            var tangent = Curves.TangentAt(m_CochleaPath, u);
            var facing = Vector3.ProjectOnPlane(Lateral + Posterior * 0.5f + Inferior * 0.35f, tangent).normalized;
            var centre = centreLine + facing * 0.9f;
            var round = MeshKit.Membrane(centre, facing, tangent, 0.95f, 0.78f, -0.18f, Vector2.zero, 6, 40);
            round.Append(RimOnly(MeshKit.Torus(centre, facing, tangent, 0.95f, 0.78f, 0.09f, 48, 5)));
            RoundWindowNormal = facing;

            var roundPart = Add("Round_Window", "Round window", "Middle ear · Inner ear boundary", "Membrane", round,
                "A membrane at the base of the cochlea. Fluid cannot be squeezed, so each time the stapes " +
                "pushes in here the round window bulges out — the pressure release that lets the " +
                "cochlear fluid move at all.");
            roundPart.Anchor = centre + facing * 0.6f;
            roundPart.Explode = Medial * 6f + Anterior * 1.5f;
            roundPart.Tier = 2;
        }

        void Nerves()
        {
            var v = m_Vestibule;
            m_TrunkStart = v + Medial * 7.5f + Superior * 0.6f + Anterior * 0.3f;
            m_TrunkEnd = m_TrunkStart + Medial * 11f + Superior * 1.2f + Posterior * 0.8f;

            const int cochlear = 0, vestibular = 1, trunk = 2;
            var shape = new SdfShape { Blend = 0.5f };

            // Cochlear nerve: from the core of the spiral (the modiolus), out through its base and
            // medially down the internal acoustic meatus.
            var cochlearPath = Curves.Smooth(new List<Vector3>
            {
                m_ModiolusBase + m_CochlearAxis * 2.6f,
                m_ModiolusBase + m_CochlearAxis * 1.2f,
                m_ModiolusBase - m_CochlearAxis * 0.3f,
                m_ModiolusBase - m_CochlearAxis * 1.0f + Medial * 1.2f,
                Vector3.Lerp(m_ModiolusBase, m_TrunkStart, 0.65f) + Medial * 0.8f + Inferior * 0.3f,
                m_TrunkStart + Anterior * 0.5f + Inferior * 0.4f,
            }, 0.12f);
            shape.NewGroup();
            shape.Chain(cochlearPath, u => u < 0.3f ? Mathf.Lerp(0.18f, 0.72f, u / 0.3f) : Mathf.Lerp(0.72f, 0.8f, (u - 0.3f) / 0.7f), cochlear);

            // Vestibular nerve: superior division from the utricle and the anterior and lateral
            // ampullae, inferior division from the saccule and posterior ampulla, meeting at the
            // vestibular (Scarpa's) ganglion.
            var ganglion = m_TrunkStart + Superior * 0.8f + Posterior * 0.9f + Lateral * 2.2f;
            var superiorDivision = Curves.Smooth(new List<Vector3>
            {
                v + m_TiltedSuperior * 1.2f + m_TiltedAnterior * 0.9f + Medial * 0.9f,
                v + m_TiltedSuperior * 1.3f + Medial * 2.6f,
                ganglion + Superior * 0.2f,
            }, 0.12f);
            var inferiorDivision = Curves.Smooth(new List<Vector3>
            {
                v - m_TiltedSuperior * 1.2f - m_TiltedAnterior * 0.4f + Medial * 1.0f,
                v - m_TiltedSuperior * 0.6f + Medial * 2.8f,
                ganglion + Inferior * 0.4f,
            }, 0.12f);
            shape.NewGroup();
            shape.Chain(superiorDivision, _ => 0.48f, vestibular);
            shape.NewGroup();
            shape.Chain(inferiorDivision, _ => 0.42f, vestibular);
            shape.NewGroup();
            shape.Ellipsoid(ganglion, Quaternion.identity, new Vector3(1.2f, 0.85f, 0.85f), vestibular);
            shape.NewGroup();
            shape.RoundCone(ganglion, m_TrunkStart + Superior * 0.3f, 0.8f, 0.9f, vestibular);

            var trunkPath = Curves.Smooth(new List<Vector3>
            {
                m_TrunkStart,
                Vector3.Lerp(m_TrunkStart, m_TrunkEnd, 0.5f) + Superior * 0.3f,
                m_TrunkEnd,
            }, 0.15f);
            shape.NewGroup();
            shape.Chain(trunkPath, u => Mathf.Lerp(1.25f, 1.4f, u), trunk);

            var meshes = shape.Polygonize(0.12f);

            var cochlearPart = Add("Cochlear_Nerve", "Cochlear nerve", "Nerves · Hearing", "Nerve", meshes[cochlear],
                "About 30,000 fibres, most of them wired to the inner hair cells, gathered from the spiral " +
                "ganglion in the cochlea's hollow core (the modiolus). It carries both what you hear and " +
                "when — its fibres fire in step with low-pitched sounds.");
            cochlearPart.Anchor = meshes[cochlear].Nearest(Curves.At(cochlearPath, 0.7f) + Inferior * 1.5f);
            cochlearPart.Tier = 1;
            cochlearPart.Explode = Medial * 9f + Anterior * 2f;

            var vestibularPart = Add("Vestibular_Nerve", "Vestibular nerve", "Nerves · Balance", "Nerve", meshes[vestibular],
                "Carries balance signals from the three canals, the utricle and the saccule, through its " +
                "superior and inferior divisions and the vestibular (Scarpa's) ganglion.");
            vestibularPart.Anchor = meshes[vestibular].Nearest(ganglion + Superior * 1.5f);
            vestibularPart.Tier = 1;
            vestibularPart.Explode = Medial * 9f + Superior * 1.5f;
            vestibularPart.Landmarks.Add(("Vestibular ganglion", ganglion));

            var trunkPart = Add("Vestibulocochlear_Nerve", "Vestibulocochlear nerve (CN VIII)", "Nerves · Cranial nerve VIII",
                "Nerve", meshes[trunk],
                "Cranial nerve VIII: the cochlear and vestibular nerves bound together, running through " +
                "the internal acoustic meatus of the temporal bone to the brainstem, where hearing and " +
                "balance part ways again.");
            trunkPart.Anchor = meshes[trunk].Nearest(Curves.At(trunkPath, 0.55f) + Superior * 2f);
            trunkPart.Explode = Medial * 12f;
            trunkPart.EndFade = new Vector4(-2f, -1f, 0.45f, 1f);
        }

        void FacialNerve()
        {
            var v = m_Vestibule;
            m_Geniculate = v + Anterior * 2.6f + Superior * 3.3f + Lateral * 0.9f;

            var above = m_Footplate + Superior * 1.15f + Lateral * 1.1f;
            var secondGenu = above + Posterior * 3.4f + Inferior * 1.2f + Lateral * 0.2f;
            var control = new List<Vector3>
            {
                m_TrunkEnd + Superior * 1.8f + Anterior * 1.3f,
                m_TrunkStart + Superior * 1.9f + Anterior * 1.3f,
                Vector3.Lerp(m_TrunkStart, m_Geniculate, 0.5f) + Superior * 1.1f + Anterior * 0.4f,
                m_Geniculate,
                m_Geniculate + Posterior * 1.1f + Lateral * 0.9f + Inferior * 0.45f,
                above,
                above + Posterior * 2.0f + Inferior * 0.1f,
                secondGenu,
                secondGenu + Inferior * 5f + Posterior * 0.5f + Lateral * 0.3f,
                secondGenu + Inferior * 11f + Posterior * 0.4f + Lateral * 0.7f,
            };
            m_FacialPath = Curves.Smooth(control, 0.15f);

            // A swelling at the geniculate ganglion.
            float gu = NearestFraction(m_FacialPath, m_Geniculate);
            var mesh = MeshKit.Tube(m_FacialPath, u => 0.52f + 0.3f * Mathf.Exp(-Sq((u - gu) / 0.035f)), 14);

            var facial = Add("Facial_Nerve", "Facial nerve (CN VII)", "Nerves · Cranial nerve VII", "Facial", mesh,
                "Not part of hearing, but it threads right through the ear: along the inner wall of the " +
                "middle ear, just above the oval window, then down behind it to the face. It moves the " +
                "muscles of expression, which is why ear surgeons map it before they touch anything.");
            facial.Anchor = Curves.At(m_FacialPath, NearestFraction(m_FacialPath, secondGenu) + 0.12f) + Lateral * 0.8f;
            facial.Explode = Medial * 4f + Superior * 4f;
            facial.EndFade = new Vector4(0f, 0.14f, 0.86f, 1f);
            facial.Landmarks.Add(("Geniculate ganglion", m_Geniculate));

            // Chorda tympani: leaves the facial nerve on its way down and crosses the middle ear on the
            // inside of the eardrum, between the malleus handle and the long process of the incus.
            float branch = NearestFraction(m_FacialPath, secondGenu) + 0.2f;
            var chorda = Curves.Smooth(new List<Vector3>
            {
                Curves.At(m_FacialPath, branch),
                DrumCentre + m_DrumUp * 2.4f - m_DrumAnterior * 3.7f - DrumNormal * 0.9f,
                DrumCentre + m_DrumUp * 2.9f - m_DrumAnterior * 0.2f - DrumNormal * 1.25f,
                DrumCentre + m_DrumUp * 3.2f + m_DrumAnterior * 3.4f - DrumNormal * 0.9f,
                DrumCentre + m_DrumUp * 3.5f + m_DrumAnterior * 6.5f + Medial * 0.5f,
            }, 0.12f);
            var chordaMesh = MeshKit.Tube(chorda, _ => 0.17f, 8);
            var chordaPart = Add("Chorda_Tympani", "Chorda tympani", "Nerves · Branch of CN VII", "Facial", chordaMesh,
                "A slender branch of the facial nerve that crosses the middle ear between the malleus and " +
                "incus, carrying taste from the front two thirds of the tongue — which is why middle-ear " +
                "surgery can leave a metallic taste.");
            chordaPart.Anchor = Curves.At(chorda, 0.5f);
            chordaPart.Explode = Superior * 3f + Lateral * 1f;
            chordaPart.Tier = 2;
            chordaPart.EndFade = new Vector4(-2f, -1f, 0.75f, 1f);
        }

        void EustachianTube()
        {
            var start = DrumCentre + m_DrumAnterior * 3.6f + Medial * 2.6f + Inferior * 0.8f;
            var along = (Anterior * 0.62f + Medial * 0.55f + Inferior * 0.56f).normalized;
            var path = Curves.Smooth(new List<Vector3>
            {
                start,
                start + along * 5f + Superior * 0.3f,
                start + along * 12f,
                start + along * 21f + Inferior * 0.8f,
                start + along * 30f + Inferior * 1.2f,
                start + along * 36f + Inferior * 1.0f + Medial * 0.5f,
            }, 0.25f);

            float Radius(float u)
            {
                if (u < 0.3f) return Mathf.Lerp(1.15f, 0.6f, Mathf.SmoothStep(0f, 1f, u / 0.3f));
                return Mathf.Lerp(0.6f, 2.6f, Mathf.SmoothStep(0f, 1f, (u - 0.3f) / 0.7f));
            }

            var mesh = MeshKit.Tube(path, Radius, 20, squash: 0.62f);
            var tube = Add("Eustachian_Tube", "Eustachian (auditory) tube", "Middle ear · Airway", "Airway", mesh,
                "Joins the middle ear to the back of the nose. Closed at rest, it opens when you swallow " +
                "or yawn to equalise pressure on both sides of the eardrum — the 'pop' on a descending " +
                "plane — and to drain the middle ear.");
            tube.Anchor = Curves.At(path, 0.45f) + Inferior * 1.6f;
            tube.Explode = Anterior * 5f + Inferior * 3f;
            tube.EndFade = new Vector4(-2f, -1f, 0.5f, 1f);
        }

        // ================================================================== helpers

        public void LogKeyPoints()
        {
            var sb = new System.Text.StringBuilder("[EarHologram] Key points (mm):").AppendLine();
            void P(string name, Vector3 v) => sb.AppendLine($"  {name,-22} ({v.x,6:0.00}, {v.y,6:0.00}, {v.z,6:0.00})");
            P("DrumNormal", DrumNormal);
            P("Umbo", Umbo);
            P("LateralProcess", m_LateralProcess);
            P("MalleusHead", m_MalleusHead);
            P("IncusBody", m_IncusBody);
            P("ShortProcessTip", m_ShortProcessTip);
            P("LongProcessTip", m_LongProcessTip);
            P("Lenticular", Lenticular);
            P("StapesHead", m_StapesHead);
            P("Footplate", m_Footplate);
            P("Vestibule", m_Vestibule);
            P("ModiolusBase", m_ModiolusBase);
            P("CochleaApex", CochleaApex);
            P("TrunkStart", m_TrunkStart);
            P("Geniculate", m_Geniculate);
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// A circular arc of <paramref name="degrees"/> from <paramref name="p0"/> to <paramref name="p1"/>
        /// in the plane with <paramref name="normal"/>, bulging toward <paramref name="bulge"/>. For more
        /// than 180° the chord is the short way across and the arc goes the long way round.
        /// </summary>
        static List<Vector3> ArcThrough(Vector3 p0, Vector3 p1, Vector3 normal, Vector3 bulge, float degrees,
            out Vector3 centre)
        {
            normal.Normalize();
            var chord = p1 - p0;
            float length = chord.magnitude;
            var side = Vector3.Cross(normal, chord).normalized;
            if (Vector3.Dot(side, bulge) < 0f)
                side = -side;

            float minor = (360f - degrees) * 0.5f * Mathf.Deg2Rad;
            float radius = length / (2f * Mathf.Sin(minor));
            centre = (p0 + p1) * 0.5f + side * (radius * Mathf.Cos(minor));

            var from = (p0 - centre).normalized;
            var axis = normal;
            if ((centre + Quaternion.AngleAxis(degrees, axis) * from * radius - p1).sqrMagnitude >
                (centre + Quaternion.AngleAxis(-degrees, axis) * from * radius - p1).sqrMagnitude)
                axis = -axis;

            return Curves.Arc(centre, axis, from, radius, degrees, Mathf.CeilToInt(degrees / 6f));
        }

        /// <summary>
        /// +1 when the canal's u runs the way a positive rotation about its normal carries the wall,
        /// −1 otherwise. v = ω × r in Unity's frame just as in a right-handed one.
        /// </summary>
        static float FlowSign(List<Vector3> path, Vector3 centre, Vector3 normal)
        {
            var mid = Curves.At(path, 0.5f);
            var tangent = Curves.TangentAt(path, 0.5f);
            return Mathf.Sign(Vector3.Dot(tangent, Vector3.Cross(normal, mid - centre)));
        }

        static List<Vector3> Join(params IEnumerable<Vector3>[] pieces)
        {
            var result = new List<Vector3>();
            foreach (var piece in pieces)
                result.AddRange(piece);

            return result;
        }

        static float NearestFraction(List<Vector3> path, Vector3 target)
        {
            var fractions = Curves.ArcFractions(path);
            float best = float.MaxValue, result = 0f;
            for (int i = 0; i < path.Count; i++)
            {
                float d = (path[i] - target).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    result = fractions[i];
                }
            }

            return result;
        }

        static MeshData RimOnly(MeshData mesh)
        {
            for (int i = 0; i < mesh.Uvs.Count; i++)
                mesh.Uvs[i] = new Vector2(1f, mesh.Uvs[i].y);

            return mesh;
        }

        static float Sq(float x) => x * x;
    }
}
