using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VRShootingGallery.AnatomyDemo;
using VRShootingGallery.EditorTools.Anatomy;

namespace VRShootingGallery.EditorTools
{
    /// <summary>
    /// Builds the ear hologram: a right ear from the auricle to the brainstem, projected in the middle of
    /// the CAVE as a medical diagram in light — labelled, explodable, and working. A tone played through
    /// it runs down the canal, rocks the ossicles and peaks on the basilar membrane at the place for its
    /// pitch; turning your head lights the semicircular canal that senses it.
    /// </summary>
    /// <remarks>
    /// <para>Every mesh is procedural, built by <see cref="EarAnatomy"/> and <see cref="CochlearSection"/>
    /// in true millimetres and written to one mesh asset. There is no imported model to license or lose,
    /// and the anatomy's numbers sit next to their reasons.</para>
    ///
    /// <para>The scene is a copy of the physics scene for the same reason the other demos' are: that scene
    /// already carries the whole MiddleVR side of the setup, and rebuilding it from scratch would be
    /// several chances to get it subtly wrong.</para>
    /// </remarks>
    static class EarHologramBuilder
    {
        const string k_CreateMenu = "VR Shooting Gallery/Anatomy/Create Ear Hologram Scene";
        const string k_BuildMenu = "VR Shooting Gallery/Anatomy/Rebuild Ear Hologram";

        const string k_TemplateScene = "Assets/_Project/Scenes/Demos/Physics MiddleVR.unity";
        public const string ScenePath = "Assets/_Project/Scenes/Demos/EarHologram.unity";

        const string k_RoomRoot = "CAVE_User_Room";
        public const string DemoRoot = "Ear_Hologram";
        const string k_GroundName = "Ground";

        const string k_HologramShader = "VR Demo/Hologram";
        const string k_AdditiveShader = "VR Demo/Hologram Additive";
        const string k_GridShader = "VR Demo/Hologram Grid";
        const string k_PanelShader = "VR Demo/Hologram Panel";

        const string k_MaterialFolder = "Assets/_Project/Art/Materials/Anatomy";
        const string k_MeshPath = "Assets/_Project/Art/Meshes/EarHologram.asset";

        // ---------------------------------------------------------------- stage

        /// <summary>
        /// Where the model floats: centred left-right, at chest height, a little in front of room centre
        /// so a viewer standing in the middle of the CAVE has it at arm's length with the front screen
        /// behind it.
        /// </summary>
        static readonly Vector3 k_ModelHome = new(0.12f, 1.32f, 0.42f);

        /// <summary>Where it stands aside to while the section is up beside it.</summary>
        static readonly Vector3 k_ModelBeside = new(-0.32f, 1.32f, 0.42f);

        static readonly Vector3 k_SectionHome = new(0.9f, 1.45f, 0.5f);
        static readonly Vector3 k_PanelHome = new(-1.5f, 1.45f, 0.15f);
        static readonly Vector2 k_PanelSize = new(0.64f, 0.96f);

        /// <summary>
        /// Metres per millimetre of anatomy: the whole ear a little over a metre across, the cochlea a
        /// hand's width. Zooming in on a part goes from there.
        /// </summary>
        const float k_Scale = 0.016f;

        /// <summary>
        /// The model-space point the overview frames, millimetres: just lateral of the eardrum, so the
        /// auricle fills the left of the view and the middle and inner ear sit right of centre with room
        /// for their labels.
        /// </summary>
        static readonly Vector3 k_OverviewFocus = new(-4f, 1f, -1.5f);

        /// <summary>
        /// How much further than the anatomy's own offsets the exploded view spreads the parts. The
        /// offsets are written in anatomical millimetres; spread like this, the inner ear clears the
        /// ossicles by a hand's width at the default scale.
        /// </summary>
        const float k_ExplodeSpread = 1.7f;

        /// <summary>Metres per canal radius in the section: the duct about 0.36 m across.</summary>
        const float k_SectionScale = 0.18f;

        // Only used when a wall is missing from the room — the CAVE as this project ships it.
        const float k_FallbackLeftX = -1.778f;
        const float k_FallbackRightX = 1.778f;
        const float k_FallbackFrontZ = 1.1113f;
        const float k_FallbackBackZ = -1.1113f;
        const float k_FallbackCeiling = 2.196f;

        /// <summary>The order the Next button walks: outside in, then the section.</summary>
        static readonly string[] k_Order =
        {
            "Auricle", "Ear_Canal", "Tympanic_Membrane", "Malleus", "Incus", "Stapes", "Eustachian_Tube",
            "Oval_Window", "Round_Window", "Cochlea", "Vestibule", "Utricle", "Saccule", "Anterior_Canal",
            "Posterior_Canal", "Lateral_Canal", "Cochlear_Nerve", "Vestibular_Nerve", "Vestibulocochlear_Nerve",
            "Facial_Nerve", "Chorda_Tympani",
            "Scala_Vestibuli", "Scala_Media", "Scala_Tympani", "Reissners_Membrane", "Basilar_Membrane",
            "Organ_Of_Corti", "Inner_Hair_Cells", "Outer_Hair_Cells", "Tectorial_Membrane", "Stria_Vascularis",
            "Spiral_Ligament", "Spiral_Lamina", "Spiral_Ganglion",
        };

        /// <summary>
        /// Each material's look when it is first made: tint, body, rim. After that the material asset is
        /// left alone, so a colour tuned in the Inspector survives a rebuild.
        /// </summary>
        static readonly Dictionary<string, (Color Tint, float Body, float Rim)> k_Looks = new()
        {
            { "Outer", (new Color(0.30f, 0.85f, 1.00f), 0.045f, 0.85f) },
            { "Membrane", (new Color(1.00f, 0.50f, 0.72f), 0.10f, 0.90f) },
            { "Bone", (new Color(1.00f, 0.78f, 0.36f), 0.09f, 0.90f) },
            { "Cochlea", (new Color(0.86f, 0.45f, 1.00f), 0.08f, 0.95f) },
            { "Vestibular", (new Color(0.52f, 0.62f, 1.00f), 0.08f, 0.95f) },
            { "Otolith", (new Color(0.55f, 1.00f, 0.85f), 0.08f, 0.80f) },
            { "Nerve", (new Color(1.00f, 0.88f, 0.35f), 0.07f, 0.85f) },
            { "Facial", (new Color(1.00f, 0.58f, 0.25f), 0.07f, 0.85f) },
            { "Airway", (new Color(0.35f, 1.00f, 0.62f), 0.045f, 0.75f) },
            { "Perilymph", (new Color(0.35f, 0.55f, 1.00f), 0.10f, 0.30f) },
            { "Endolymph", (new Color(0.30f, 1.00f, 0.80f), 0.10f, 0.30f) },
            { "Corti", (new Color(0.92f, 0.70f, 1.00f), 0.16f, 0.60f) },
            { "HairCell", (new Color(1.00f, 0.92f, 0.45f), 0.25f, 0.70f) },
            { "Tectorial", (new Color(0.75f, 1.00f, 1.00f), 0.18f, 0.60f) },
            { "Stria", (new Color(1.00f, 0.38f, 0.42f), 0.18f, 0.60f) },
            { "Tissue", (new Color(0.90f, 0.70f, 0.90f), 0.12f, 0.45f) },
            { "SectionBone", (new Color(1.00f, 0.78f, 0.36f), 0.06f, 0.45f) },
        };

        readonly struct Room
        {
            public readonly float LeftX, RightX, FrontZ, BackZ, Ceiling;

            public Room(float leftX, float rightX, float frontZ, float backZ, float ceiling)
            {
                LeftX = leftX;
                RightX = rightX;
                FrontZ = frontZ;
                BackZ = backZ;
                Ceiling = ceiling;
            }

            public float Width => RightX - LeftX;
            public float Depth => FrontZ - BackZ;
            public Vector3 Centre => new((LeftX + RightX) * 0.5f, 0f, (FrontZ + BackZ) * 0.5f);
        }

        sealed class Materials
        {
            public readonly Dictionary<string, Material> Parts = new();
            public Material Line, Beam, Grid, Panel;
        }

        // ================================================================== menus

        [MenuItem(k_CreateMenu, false, 600)]
        static void CreateScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(k_TemplateScene) == null)
            {
                Debug.LogError($"[EarHologram] Missing the template scene '{k_TemplateScene}'.");
                return;
            }

            if (!Application.isBatchMode &&
                AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
                !EditorUtility.DisplayDialog("Rebuild the ear hologram scene?",
                    $"'{ScenePath}' already exists and will be overwritten with a fresh copy of the physics " +
                    "scene plus a newly built hologram.\n\nAnything you added to it by hand is lost.",
                    "Overwrite", "Cancel"))
                return;

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            CreateSceneUnattended();
        }

        /// <summary>
        /// Create without asking — for batch mode:
        /// <c>-executeMethod VRShootingGallery.EditorTools.EarHologramBuilder.CreateSceneUnattended</c>
        /// </summary>
        public static void CreateSceneUnattended()
        {
            var scene = EditorSceneManager.OpenScene(k_TemplateScene, OpenSceneMode.Single);

            // Saved to the new path before anything is touched, so the template on disk is never at risk
            // from a build that goes wrong half way through.
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError($"[EarHologram] Could not save the new scene to '{ScenePath}'.");
                return;
            }

            scene = SceneManager.GetActiveScene();
            StripTemplate(scene);

            if (!Build(scene))
                return;

            EditorSceneManager.SaveScene(scene);
            RegisterInBuildSettings();

            Debug.Log($"[EarHologram] Built '{scene.name}'. Enter play mode: point the wand at a part and pull " +
                      "the trigger, press 3 for sound, and turn your head to light the canals.");
        }

        [MenuItem(k_BuildMenu, false, 610)]
        static void Rebuild()
        {
            var scene = SceneManager.GetActiveScene();
            if (Build(scene))
                EditorSceneManager.MarkSceneDirty(scene);
        }

        // ================================================================== build

        static bool Build(Scene scene)
        {
            var roomRoot = FindRoot(scene, k_RoomRoot);
            if (roomRoot == null)
            {
                Debug.LogError($"[EarHologram] No '{k_RoomRoot}' root in '{scene.name}'. Open the ear hologram " +
                               "scene (or any scene with the CAVE room) and try again.");
                return false;
            }

            var materials = BuildMaterials();
            if (materials == null)
                return false;

            var font = DefaultFont();
            if (font == null)
                Debug.LogWarning("[EarHologram] No TMP font asset found. Run Window ▸ TextMeshPro ▸ Import TMP " +
                                 "Essential Resources, then build again — the labels will be blank until then.");

            var anatomy = EarAnatomy.Build();
            var section = CochlearSection.Build(out _, out float halfAngle);

            Undo.SetCurrentGroupName("Build Ear Hologram");
            int group = Undo.GetCurrentGroup();

            var room = ReadRoom(roomRoot);
            var root = FindOrCreate(roomRoot, DemoRoot);
            Clear(root);

            var meshes = new List<Mesh>();

            // ------------------------------------------------ the model

            var pivot = NewChild(root, "Model_Pivot");
            pivot.localPosition = k_ModelHome;
            pivot.localScale = Vector3.one * k_Scale;

            // The model turns and zooms every frame, and its parts' colliders with it. Colliders that move
            // belong on a kinematic body; left static, every move would rebuild the physics scene's
            // static structure.
            var body = Undo.AddComponent<Rigidbody>(pivot.gameObject);
            body.isKinematic = true;
            body.useGravity = false;
            var anatomyFrame = NewChild(pivot, "Anatomy");

            var parts = new Dictionary<string, HologramPart>();
            foreach (var spec in anatomy.Parts)
                parts[spec.Key] = MakePart(anatomyFrame, spec, HologramGroup.Ear, materials, meshes);

            var ring = Save(MeshKit.Annulus(Vector3.zero, Vector3.forward, 0.93f, 1f, 72).ToMesh("Wavefront"), meshes);
            var wavefronts = new List<Renderer>();
            for (int i = 0; i < 3; i++)
            {
                var wave = NewChild(anatomyFrame, $"Sound_Wave_{i}");
                var renderer = Decoration(wave, ring, materials.Line);
                renderer.enabled = false;
                wavefronts.Add(renderer);
            }

            var markerMesh = Save(MeshKit.Torus(anatomy.SectionPoint, anatomy.SectionTangent,
                MeshKit.AnyPerpendicular(anatomy.SectionTangent), anatomy.SectionTubeRadius + 0.35f,
                anatomy.SectionTubeRadius + 0.35f, 0.1f, 48, 6).ToMesh("Section_Marker"), meshes);
            var marker = Decoration(NewChild(anatomyFrame, "Section_Marker"), markerMesh, materials.Line);

            // ------------------------------------------------ the section

            var sectionPivot = NewChild(root, "Section_Pivot");
            sectionPivot.localPosition = k_SectionHome;
            var sectionFrame = NewChild(sectionPivot, "Section");
            sectionFrame.localScale = Vector3.one * k_SectionScale;
            foreach (var spec in section)
                parts[spec.Key] = MakePart(sectionFrame, spec, HologramGroup.Section, materials, meshes);

            float magnification = k_SectionScale * 1000f / anatomy.SectionTubeRadius;
            var sectionTitle = Text(sectionPivot, "Section_Title", new Vector3(0f, 0.42f, 0f), 0.26f,
                $"SECTION A  ·  COCHLEAR DUCT  ·  ×{Mathf.Round(magnification / 10f) * 10f:0}",
                new Color(1f, 0.82f, 1f), TextAlignmentOptions.Center, new Vector2(1.2f, 0.1f), font);

            var callout = Line(root, "Callout", materials.Line, 0.0025f, new Color(1f, 0.8f, 1f, 0.5f));

            // ------------------------------------------------ the room

            BuildProjector(root, meshes, materials, out var spinners);
            BuildLining(root, room, materials.Grid);
            var panel = BuildPanel(root, materials.Panel, font, out var panelTexts);
            var groups = new (string Name, HologramPart Anchor)[]
            {
                ("Ossicles", parts["Incus"]),
                ("Semicircular canals", parts["Anterior_Canal"]),
            };
            var labels = BuildLabels(root, parts.Values, groups, pivot, sectionPivot, materials.Line, meshes, font);
            var pointer = BuildPointer(root, materials.Line, meshes);

            SaveMeshes(meshes);

            // ------------------------------------------------ behaviour

            var ordered = k_Order.Where(parts.ContainsKey).Select(k => parts[k]).ToArray();
            foreach (var key in parts.Keys.Except(k_Order))
                Debug.LogWarning($"[EarHologram] Part '{key}' is not in the Next order and cannot be stepped to.");

            var hearing = GetOrAdd<HearingSimulation>(root);
            WireHearing(hearing, parts, anatomy, wavefronts);

            var balance = GetOrAdd<BalanceSimulation>(root);
            WireBalance(balance, parts, anatomy);

            var hologram = GetOrAdd<EarHologram>(root);
            var so = new SerializedObject(hologram);
            SetRef(so, "m_Pivot", pivot);
            SetRef(so, "m_Anatomy", anatomyFrame);
            SetRef(so, "m_SectionPivot", sectionPivot);
            SetRef(so, "m_SectionMarker", marker);
            SetRef(so, "m_Callout", callout);
            SetRef(so, "m_SectionTitle", sectionTitle);
            SetRefs(so, "m_Spinners", spinners.Cast<Object>().ToArray());
            SetRefs(so, "m_Parts", ordered.Cast<Object>().ToArray());
            SetRef(so, "m_Cochlea", parts["Cochlea"]);
            SetRef(so, "m_Labels", labels);
            SetRef(so, "m_Pointer", pointer);
            SetRef(so, "m_Hearing", hearing);
            SetRef(so, "m_Balance", balance);
            SetRef(so, "m_Panel", panel);
            SetRef(so, "m_PanelTitle", panelTexts[0]);
            SetRef(so, "m_PanelRegion", panelTexts[1]);
            SetRef(so, "m_PanelBody", panelTexts[2]);
            SetRef(so, "m_PanelStatus", panelTexts[3]);
            SetRef(so, "m_PanelControls", panelTexts[4]);
            SetVector3(so, "m_ModelHome", k_ModelHome);
            SetVector3(so, "m_ModelBeside", k_ModelBeside);
            SetVector3(so, "m_SectionHome", k_SectionHome);
            SetVector3(so, "m_PanelHome", k_PanelHome);
            SetFloat(so, "m_Scale", k_Scale);
            SetVector3(so, "m_OverviewFocus", k_OverviewFocus);
            SetFloat(so, "m_SectionHalfAngle", halfAngle);
            so.ApplyModifiedProperties();

            HideGround(scene);
            Settle(hologram, hearing, labels);

            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = root.gameObject;

            int triangles = meshes.Sum(m => (int)m.GetIndexCount(0)) / 3;
            Debug.Log($"[EarHologram] {anatomy.Parts.Count} ear parts and {section.Count} section parts, " +
                      $"{triangles:N0} triangles in all; the model at {k_Scale * 1000f:0.#}× life size, the " +
                      $"section at ×{magnification:0}.", root);
            return true;
        }

        /// <summary>
        /// Runs the demo's frame once, snapped, for a viewer standing where one usually does — so the saved
        /// scene opens with its labels laid out and its panel written, not piled at the room's origin.
        /// </summary>
        static void Settle(EarHologram hologram, HearingSimulation hearing, HologramLabels labels)
        {
            var viewer = new Vector3(0f, 1.62f, -0.55f);
            hearing.Simulate(0f, 10f);
            hologram.Tick(viewer, 0f, 10f);
            foreach (var part in hologram.GetComponentsInChildren<HologramPart>(true))
                part.Apply(10f);
            labels.Layout(viewer, 10f);
        }

        // ================================================================== parts

        static HologramPart MakePart(Transform parent, PartSpec spec, HologramGroup group, Materials materials,
            List<Mesh> meshes)
        {
            var t = NewChild(parent, spec.Key);
            var mesh = Save(spec.Mesh.ToMesh(spec.Key), meshes);

            var filter = Undo.AddComponent<MeshFilter>(t.gameObject);
            filter.sharedMesh = mesh;

            var renderer = Undo.AddComponent<MeshRenderer>(t.gameObject);
            renderer.sharedMaterial = materials.Parts.TryGetValue(spec.Material, out var m) ? m : materials.Parts["Outer"];
            Quiet(renderer);

            if (spec.Pickable)
                Undo.AddComponent<MeshCollider>(t.gameObject).sharedMesh = mesh;

            var part = Undo.AddComponent<HologramPart>(t.gameObject);
            part.Configure(spec.DisplayName, spec.Region, spec.Description, group, spec.Tier, spec.ViewYaw,
                spec.Pickable, spec.Anchor, spec.Explode * k_ExplodeSpread, spec.Mesh.Bounds(), spec.EndFade,
                spec.Landmarks.Select(l => l.Name).ToArray(), spec.Landmarks.Select(l => l.Point).ToArray());
            EditorUtility.SetDirty(part);
            return part;
        }

        static Renderer Decoration(Transform t, Mesh mesh, Material material)
        {
            Undo.AddComponent<MeshFilter>(t.gameObject).sharedMesh = mesh;
            var renderer = Undo.AddComponent<MeshRenderer>(t.gameObject);
            renderer.sharedMaterial = material;
            Quiet(renderer);
            return renderer;
        }

        static void WireHearing(HearingSimulation hearing, Dictionary<string, HologramPart> parts, EarAnatomy anatomy,
            List<Renderer> wavefronts)
        {
            var so = new SerializedObject(hearing);
            SetRef(so, "m_Canal", parts["Ear_Canal"]);
            SetRef(so, "m_Drum", parts["Tympanic_Membrane"]);
            SetRef(so, "m_Malleus", parts["Malleus"]);
            SetRef(so, "m_Incus", parts["Incus"]);
            SetRef(so, "m_Stapes", parts["Stapes"]);
            SetRef(so, "m_OvalWindow", parts["Oval_Window"]);
            SetRef(so, "m_RoundWindow", parts["Round_Window"]);
            SetRef(so, "m_Cochlea", parts["Cochlea"]);
            SetRef(so, "m_CochlearNerve", parts["Cochlear_Nerve"]);
            SetRef(so, "m_Trunk", parts["Vestibulocochlear_Nerve"]);
            SetRef(so, "m_BasilarMembrane", parts["Basilar_Membrane"]);
            SetRef(so, "m_OrganOfCorti", parts["Organ_Of_Corti"]);
            SetRef(so, "m_InnerHairCells", parts["Inner_Hair_Cells"]);
            SetRef(so, "m_OuterHairCells", parts["Outer_Hair_Cells"]);
            SetRef(so, "m_Tectorial", parts["Tectorial_Membrane"]);
            SetRef(so, "m_Ganglion", parts["Spiral_Ganglion"]);
            SetRefs(so, "m_Wavefronts", wavefronts.Cast<Object>().ToArray());

            SetVector3(so, "m_AxisA", anatomy.OssicleAxisA);
            SetVector3(so, "m_AxisB", anatomy.OssicleAxisB);
            SetVector3(so, "m_Umbo", anatomy.Umbo);
            SetVector3(so, "m_Lenticular", anatomy.Lenticular);
            SetVector3(so, "m_StapesAxis", anatomy.StapesAxis);
            SetVector3(so, "m_DrumNormal", anatomy.DrumNormal);
            SetVector3(so, "m_RoundWindowNormal", anatomy.RoundWindowNormal);
            SetVector3(so, "m_SoundOrigin", anatomy.SoundOrigin);
            SetVector3(so, "m_CanalEntrance", anatomy.CanalEntrance);
            SetFloat(so, "m_SectionU", anatomy.SectionU);

            var steps = new (string Text, string[] Parts)[]
            {
                ("The auricle funnels sound down the ear canal", new[] { "Auricle", "Ear_Canal" }),
                ("The eardrum vibrates with it", new[] { "Tympanic_Membrane" }),
                ("The ossicles lever it inward, about 20× stronger", new[] { "Malleus", "Incus", "Stapes" }),
                ("The stapes pumps the oval window; the round window gives", new[] { "Stapes", "Oval_Window", "Round_Window" }),
                ("A travelling wave peaks at the place for its pitch", new[] { "Cochlea", "Basilar_Membrane", "Organ_Of_Corti" }),
                ("Hair cells there fire the cochlear nerve to the brain", new[] { "Inner_Hair_Cells", "Outer_Hair_Cells", "Spiral_Ganglion", "Cochlear_Nerve", "Vestibulocochlear_Nerve" }),
            };

            var array = so.FindProperty("m_Steps");
            array.arraySize = steps.Length;
            for (int i = 0; i < steps.Length; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Text").stringValue = steps[i].Text;
                var list = element.FindPropertyRelative("Parts");
                list.arraySize = steps[i].Parts.Length;
                for (int p = 0; p < steps[i].Parts.Length; p++)
                    list.GetArrayElementAtIndex(p).objectReferenceValue = parts[steps[i].Parts[p]];
            }

            so.ApplyModifiedProperties();
        }

        static void WireBalance(BalanceSimulation balance, Dictionary<string, HologramPart> parts, EarAnatomy anatomy)
        {
            var so = new SerializedObject(balance);
            SetRef(so, "m_AnteriorCanal", parts["Anterior_Canal"]);
            SetRef(so, "m_PosteriorCanal", parts["Posterior_Canal"]);
            SetRef(so, "m_LateralCanal", parts["Lateral_Canal"]);
            SetRef(so, "m_VestibularNerve", parts["Vestibular_Nerve"]);
            SetVector3(so, "m_AnteriorNormal", anatomy.AnteriorCanalNormal);
            SetVector3(so, "m_PosteriorNormal", anatomy.PosteriorCanalNormal);
            SetVector3(so, "m_LateralNormal", anatomy.LateralCanalNormal);
            SetVector3(so, "m_FlowSigns", new Vector3(anatomy.AnteriorCanalSign, anatomy.PosteriorCanalSign,
                anatomy.LateralCanalSign));
            so.ApplyModifiedProperties();
        }

        // ================================================================== the room

        /// <summary>
        /// The projector the hologram rises from: a disc of rings on the floor, turning slowly, and a faint
        /// cone of light up to the model. It gives the model something to stand on — a hologram hanging in
        /// the air with nothing under it reads as a glitch, not a projection.
        /// </summary>
        static void BuildProjector(Transform root, List<Mesh> meshes, Materials materials, out List<Transform> spinners)
        {
            spinners = new List<Transform>();
            var projector = NewChild(root, "Projector");
            projector.localPosition = new Vector3(k_ModelHome.x, 0.004f, k_ModelHome.z);

            var disc = Save(MeshKit.Annulus(Vector3.zero, Vector3.up, 0f, 0.15f, 64).ToMesh("Projector_Disc"), meshes);
            Decoration(NewChild(projector, "Disc"), disc, materials.Line);

            var rings = new (string Name, float Inner, float Outer, int Dashes, float Fill)[]
            {
                ("Ring_Inner", 0.17f, 0.185f, 24, 0.6f),
                ("Ring_Middle", 0.27f, 0.28f, 1, 1f),
                ("Ring_Outer", 0.33f, 0.345f, 60, 0.35f),
            };
            foreach (var r in rings)
            {
                var mesh = Save(DashedRing(r.Inner, r.Outer, r.Dashes, r.Fill).ToMesh($"Projector_{r.Name}"), meshes);
                var t = NewChild(projector, r.Name);
                Decoration(t, mesh, materials.Line);
                if (r.Dashes > 1)
                    spinners.Add(t);
            }

            float coneTop = k_ModelHome.y - 0.52f;
            var cone = Save(MeshKit.Frustum(Vector3.zero, 0.16f, 0.46f, coneTop, 64).ToMesh("Projector_Beam"), meshes);
            Decoration(NewChild(projector, "Beam"), cone, materials.Beam);
        }

        static MeshData DashedRing(float inner, float outer, int dashes, float fill)
        {
            var mesh = new MeshData();
            int steps = Mathf.Max(4, 192 / dashes);
            for (int d = 0; d < dashes; d++)
            {
                float a0 = d / (float)dashes * Mathf.PI * 2f;
                float a1 = a0 + fill / dashes * Mathf.PI * 2f;
                int start = mesh.VertexCount;
                for (int s = 0; s <= steps; s++)
                {
                    float a = Mathf.Lerp(a0, a1, s / (float)steps);
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    mesh.AddVertex(dir * inner, Vector3.up, new Vector2(s / (float)steps, 0f));
                    mesh.AddVertex(dir * outer, Vector3.up, new Vector2(s / (float)steps, 1f));
                }

                for (int s = 0; s < steps; s++)
                {
                    int a = start + s * 2;
                    mesh.AddTriangle(a, a + 1, a + 2);
                    mesh.AddTriangle(a + 1, a + 3, a + 2);
                }
            }

            return mesh;
        }

        /// <summary>
        /// A dark lab: the grid material on quads a few millimetres inside every wall, floor and ceiling.
        /// The template's walls stay as they are behind it; the hologram's additive light needs black to
        /// land on, and grey screens would wash it out.
        /// </summary>
        static void BuildLining(Transform root, in Room room, Material grid)
        {
            var lining = NewChild(root, "Room_Lining");
            const float inset = 0.004f;
            var c = room.Centre;
            float h = room.Ceiling;

            void Panel(string name, Vector3 position, Vector3 euler, Vector2 size)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Undo.RegisterCreatedObjectUndo(go, "Create lining");
                go.name = name;
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(lining, false);
                go.transform.localPosition = position;
                go.transform.localRotation = Quaternion.Euler(euler);
                go.transform.localScale = new Vector3(size.x, size.y, 1f);
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = grid;
                Quiet(renderer);
            }

            Panel("Floor", new Vector3(c.x, inset, c.z), new Vector3(90f, 0f, 0f), new Vector2(room.Width, room.Depth));
            Panel("Ceiling", new Vector3(c.x, h - inset, c.z), new Vector3(-90f, 0f, 0f), new Vector2(room.Width, room.Depth));
            Panel("Front", new Vector3(c.x, h * 0.5f, room.FrontZ - inset), Vector3.zero, new Vector2(room.Width, h));
            Panel("Back", new Vector3(c.x, h * 0.5f, room.BackZ + inset), new Vector3(0f, 180f, 0f), new Vector2(room.Width, h));
            Panel("Left", new Vector3(room.LeftX + inset, h * 0.5f, c.z), new Vector3(0f, -90f, 0f), new Vector2(room.Depth, h));
            Panel("Right", new Vector3(room.RightX - inset, h * 0.5f, c.z), new Vector3(0f, 90f, 0f), new Vector2(room.Depth, h));

            grid.SetVector("_GlowCentre", new Vector4(k_ModelHome.x, k_ModelHome.z, 0f, 1.0f));
            EditorUtility.SetDirty(grid);
        }

        static Transform BuildPanel(Transform root, Material material, TMP_FontAsset font, out TextMeshPro[] texts)
        {
            var panel = NewChild(root, "Info_Panel");
            panel.localPosition = k_PanelHome;

            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Undo.RegisterCreatedObjectUndo(back, "Create panel");
            back.name = "Backing";
            Object.DestroyImmediate(back.GetComponent<Collider>());
            back.transform.SetParent(panel, false);
            back.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            back.transform.localScale = new Vector3(k_PanelSize.x, k_PanelSize.y, 1f);
            var renderer = back.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            Quiet(renderer);
            material.SetVector("_Size", new Vector4(k_PanelSize.x, k_PanelSize.y, 0f, 0f));
            EditorUtility.SetDirty(material);

            float left = -k_PanelSize.x * 0.5f + 0.045f;
            float top = k_PanelSize.y * 0.5f;
            float width = k_PanelSize.x - 0.09f;
            var accent = new Color(0.45f, 0.85f, 1f);

            Text(panel, "Header", new Vector3(left, top - 0.045f, 0f), 0.18f, "EAR ANATOMY  ·  HOLOGRAM",
                new Color(0.4f, 0.7f, 0.85f), TextAlignmentOptions.TopLeft, new Vector2(width, 0.05f), font, true);

            texts = new[]
            {
                Text(panel, "Title", new Vector3(left, top - 0.085f, 0f), 0.42f, "The human ear",
                    Color.white, TextAlignmentOptions.TopLeft, new Vector2(width, 0.09f), font, true),
                Text(panel, "Region", new Vector3(left, top - 0.165f, 0f), 0.2f, "",
                    accent, TextAlignmentOptions.TopLeft, new Vector2(width, 0.05f), font, true),
                Text(panel, "Body", new Vector3(left, top - 0.215f, 0f), 0.24f, "",
                    new Color(0.85f, 0.95f, 1f), TextAlignmentOptions.TopLeft, new Vector2(width, 0.36f), font, true, true),
                Text(panel, "Status", new Vector3(left, top - 0.6f, 0f), 0.21f, "",
                    new Color(0.8f, 0.9f, 1f), TextAlignmentOptions.TopLeft, new Vector2(width, 0.28f), font, true, true),
                Text(panel, "Controls", new Vector3(left, -top + 0.045f, 0f), 0.17f, "",
                    new Color(0.45f, 0.7f, 0.82f), TextAlignmentOptions.BottomLeft, new Vector2(width, 0.05f), font, true, true),
            };
            texts[4].rectTransform.pivot = new Vector2(0f, 0f);
            return panel;
        }

        /// <summary>
        /// One label per part and per landmark, plus the group labels that stand in for several small
        /// parts in the overview. The section's labels all go in its right-hand column: its left is where
        /// the ear stands while the section is up.
        /// </summary>
        static HologramLabels BuildLabels(Transform root, IEnumerable<HologramPart> parts,
            (string Name, HologramPart Anchor)[] groups, Transform earCentre, Transform sectionCentre, Material line,
            List<Mesh> meshes, TMP_FontAsset font)
        {
            var labelsRoot = NewChild(root, "Labels");
            var labels = Undo.AddComponent<HologramLabels>(labelsRoot.gameObject);
            var dot = Save(MeshKit.Sphere(Vector3.zero, 0.5f, 12, 8).ToMesh("Label_Dot"), meshes);

            var entries = new List<HologramLabel>();
            foreach (var part in parts)
            {
                entries.Add(MakeLabel(labelsRoot, part, LabelKind.Part, -1, part.DisplayName, line, dot, font));
                for (int i = 0; i < part.LandmarkCount; i++)
                    entries.Add(MakeLabel(labelsRoot, part, LabelKind.Landmark, i, part.LandmarkName(i), line, dot, font));
            }

            foreach (var (name, anchor) in groups)
                entries.Add(MakeLabel(labelsRoot, anchor, LabelKind.Group, -1, name, line, dot, font));

            labels.Configure(entries, new[]
            {
                new LabelGroup { Group = HologramGroup.Ear, Centre = earCentre, Ring = new Vector2(0.66f, 0.55f) },
                new LabelGroup { Group = HologramGroup.Section, Centre = sectionCentre, Ring = new Vector2(0.34f, 0.3f), Side = 1 },
            });
            EditorUtility.SetDirty(labels);
            return labels;
        }

        static HologramLabel MakeLabel(Transform parent, HologramPart part, LabelKind kind, int landmark,
            string content, Material line, Mesh dot, TMP_FontAsset font)
        {
            string name = kind switch
            {
                LabelKind.Landmark => $"Label_{part.name}_{landmark}",
                LabelKind.Group => $"Label_Group_{content.Replace(' ', '_')}",
                _ => $"Label_{part.name}",
            };
            var text = Text(parent, name, Vector3.zero, 0.4f, content, Color.white, TextAlignmentOptions.Left,
                new Vector2(1.4f, 0.08f), font);
            text.rectTransform.pivot = new Vector2(0f, 0.5f);

            var leader = Line(text.transform, "Leader", line, 0.0022f, new Color(0.55f, 0.85f, 1f, 0.55f));
            var dotTransform = NewChild(text.transform, "Dot");
            dotTransform.localScale = Vector3.one * 0.011f;
            Decoration(dotTransform, dot, line);

            return new HologramLabel
            {
                Part = part, Kind = kind, Landmark = landmark, Text = text, Leader = leader, Dot = dotTransform,
            };
        }

        static HologramPointer BuildPointer(Transform root, Material line, List<Mesh> meshes)
        {
            var t = NewChild(root, "Pointer");
            var pointer = Undo.AddComponent<HologramPointer>(t.gameObject);
            var beam = Line(t, "Beam", line, 0.004f, new Color(0.6f, 0.95f, 1f, 0.8f));
            beam.endColor = new Color(0.6f, 0.95f, 1f, 0.15f);

            var dotMesh = Save(MeshKit.Sphere(Vector3.zero, 0.5f, 16, 10).ToMesh("Pointer_Dot"), meshes);
            var dot = NewChild(t, "Hit");
            dot.localScale = Vector3.one * 0.018f;
            Decoration(dot, dotMesh, line);

            pointer.Configure(beam, dot);
            EditorUtility.SetDirty(pointer);
            return pointer;
        }

        // ================================================================== room and template

        static Room ReadRoom(Transform room)
        {
            float ceiling = k_FallbackCeiling;
            var top = room.Find("Ceiling");
            if (top != null)
                ceiling = top.localPosition.y;

            return new Room(WallCoord(room, "Left_Wall", true, k_FallbackLeftX),
                WallCoord(room, "Right_Wall", true, k_FallbackRightX),
                WallCoord(room, "Front_Wall", false, k_FallbackFrontZ),
                WallCoord(room, "Back_Wall", false, k_FallbackBackZ),
                ceiling);
        }

        static float WallCoord(Transform room, string child, bool readX, float fallback)
        {
            var wall = room.Find(child);
            if (wall != null)
                return readX ? wall.localPosition.x : wall.localPosition.z;

            Debug.LogWarning($"[EarHologram] '{k_RoomRoot}' has no child called '{child}'. Using the stock CAVE " +
                             $"dimension ({fallback:0.###} m) for that side instead.", room);
            return fallback;
        }

        /// <summary>Takes the other demos back out of the copy.</summary>
        static void StripTemplate(Scene scene)
        {
            var room = FindRoot(scene, k_RoomRoot);
            if (room != null)
            {
                for (int i = room.childCount - 1; i >= 0; i--)
                {
                    var child = room.GetChild(i);
                    if (child.name.StartsWith("Tether_Rig") || child.name.StartsWith("Sphere") ||
                        child.name.StartsWith("Marble_Run") || child.name.StartsWith("Hot_Wheels") ||
                        child.name.StartsWith("Tracking_Demo"))
                        Object.DestroyImmediate(child.gameObject);
                }
            }

            foreach (var go in scene.GetRootGameObjects())
                if (go.name.StartsWith("Gun"))
                    Object.DestroyImmediate(go);
        }

        /// <summary>
        /// The template's ground plane sits at floor height, under the lining; switched off rather than
        /// deleted, as the head-tracking demo does, so the lining never z-fights it at a distance.
        /// </summary>
        static void HideGround(Scene scene)
        {
            var ground = FindRoot(scene, k_GroundName);
            if (ground == null || !ground.gameObject.activeSelf)
                return;

            Undo.RecordObject(ground.gameObject, "Hide ground");
            ground.gameObject.SetActive(false);
        }

        static void RegisterInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Any(s => s.path == ScenePath))
                return;

            scenes.Add(new EditorBuildSettingsScene(ScenePath, false));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[EarHologram] Added '{ScenePath}' to the build list, disabled. Tick it in File ▸ Build " +
                      "Settings to build the ear hologram for the CAVE.");
        }

        // ================================================================== assets

        static Materials BuildMaterials()
        {
            var holo = Shader.Find(k_HologramShader);
            var additive = Shader.Find(k_AdditiveShader);
            var gridShader = Shader.Find(k_GridShader);
            var panelShader = Shader.Find(k_PanelShader);
            if (holo == null || additive == null || gridShader == null || panelShader == null)
            {
                Debug.LogError("[EarHologram] One of the hologram shaders was not found. Is " +
                               "Assets/_Project/Art/Shaders present and compiling?");
                return null;
            }

            var result = new Materials();
            foreach (var (key, look) in k_Looks)
            {
                result.Parts[key] = Material($"{k_MaterialFolder}/M_Holo_{key}.mat", holo, m =>
                {
                    m.SetColor("_Color", look.Tint);
                    m.SetFloat("_Body", look.Body);
                    m.SetFloat("_RimStrength", look.Rim);
                });
            }

            result.Line = Material($"{k_MaterialFolder}/M_Holo_Line.mat", additive,
                m => m.SetColor("_Color", new Color(0.6f, 0.9f, 1f, 1f)));
            result.Beam = Material($"{k_MaterialFolder}/M_Holo_Beam.mat", additive, m =>
            {
                m.SetColor("_Color", new Color(0.25f, 0.7f, 0.9f, 0.16f));
                m.SetFloat("_FadeV", 1f);
                m.SetFloat("_FadePower", 1.6f);
                m.SetVector("_Streaks", new Vector4(40f, 0.7f, 0.12f, 0f));
            });
            result.Grid = Material($"{k_MaterialFolder}/M_Holo_Grid.mat", gridShader, _ => { });
            result.Panel = Material($"{k_MaterialFolder}/M_Holo_Panel.mat", panelShader, _ => { });
            return result;
        }

        /// <summary>
        /// A material on one of the demo's shaders, made with <paramref name="initialise"/> the first time
        /// and left alone after that — the one thing a rebuild does put right is a lost shader.
        /// </summary>
        static Material Material(string path, Shader shader, System.Action<Material> initialise)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                if (material.shader != shader)
                {
                    material.shader = shader;
                    EditorUtility.SetDirty(material);
                }

                return material;
            }

            EnsureFolder(path);
            material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            initialise(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Mesh Save(Mesh mesh, List<Mesh> meshes)
        {
            meshes.Add(mesh);
            return mesh;
        }

        /// <summary>
        /// Writes every mesh into one asset, in place: a mesh already there under the same name is
        /// overwritten rather than replaced, so references to it from elsewhere survive the rebuild.
        /// Meshes no longer built are removed.
        /// </summary>
        static void SaveMeshes(List<Mesh> meshes)
        {
            EnsureFolder(k_MeshPath);

            var container = AssetDatabase.LoadAssetAtPath<Mesh>(k_MeshPath);
            var existing = AssetDatabase.LoadAllAssetsAtPath(k_MeshPath).OfType<Mesh>()
                .GroupBy(m => m.name).ToDictionary(g => g.Key, g => g.First());

            if (container == null)
            {
                // The first mesh becomes the asset's main object.
                AssetDatabase.CreateAsset(meshes[0], k_MeshPath);
                container = meshes[0];
                existing[meshes[0].name] = meshes[0];
            }

            var kept = new HashSet<Mesh>();
            for (int i = 0; i < meshes.Count; i++)
            {
                var mesh = meshes[i];
                if (existing.TryGetValue(mesh.name, out var target) && target != mesh)
                {
                    target.Clear();
                    EditorUtility.CopySerialized(mesh, target);
                    EditorUtility.SetDirty(target);
                    Repoint(mesh, target);
                    Object.DestroyImmediate(mesh);
                    meshes[i] = target;
                    kept.Add(target);
                }
                else if (target == mesh)
                {
                    kept.Add(mesh);
                }
                else
                {
                    AssetDatabase.AddObjectToAsset(mesh, container);
                    kept.Add(mesh);
                }
            }

            foreach (var stale in existing.Values.Where(m => !kept.Contains(m) && m != container))
                Object.DestroyImmediate(stale, true);

            AssetDatabase.SaveAssets();
        }

        /// <summary>Everything built this pass that holds <paramref name="from"/> is pointed at <paramref name="to"/>.</summary>
        static void Repoint(Mesh from, Mesh to)
        {
            foreach (var filter in Object.FindObjectsOfType<MeshFilter>(true))
                if (filter.sharedMesh == from)
                    filter.sharedMesh = to;

            foreach (var collider in Object.FindObjectsOfType<MeshCollider>(true))
                if (collider.sharedMesh == from)
                    collider.sharedMesh = to;
        }

        // ================================================================== helpers

        static void Quiet(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static LineRenderer Line(Transform parent, string name, Material material, float width, Color colour)
        {
            var t = NewChild(parent, name);
            var line = Undo.AddComponent<LineRenderer>(t.gameObject);
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.sharedMaterial = material;
            line.startColor = colour;
            line.endColor = colour;
            line.textureMode = LineTextureMode.Stretch;
            Quiet(line);
            return line;
        }

        static TextMeshPro Text(Transform parent, string name, Vector3 localPosition, float size, string content,
            Color colour, TextAlignmentOptions alignment, Vector2 box, TMP_FontAsset font, bool topLeft = false,
            bool wrap = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var text = Undo.AddComponent<TextMeshPro>(go);

            // TMP 3.0.9's Awake throws inside LoadFontAsset when the component is added from an editor
            // script, which leaves the text and its renderer disabled with no material — silently
            // invisible. Wire the font, material and enabled flags up by hand, as the marble run does.
            if (font != null)
            {
                text.font = font;
                text.fontSharedMaterial = font.material;
            }

            text.enabled = true;
            if (go.TryGetComponent<MeshRenderer>(out var renderer))
            {
                renderer.enabled = true;
                Quiet(renderer);
            }

            text.text = content;
            text.fontSize = size;
            text.enableAutoSizing = false;
            text.color = colour;
            text.alignment = alignment;
            text.enableWordWrapping = wrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = true;
            text.rectTransform.sizeDelta = box;
            text.rectTransform.pivot = topLeft ? new Vector2(0f, 1f) : new Vector2(0.5f, 0.5f);
            text.ForceMeshUpdate();
            EditorUtility.SetDirty(text);
            return text;
        }

        static TMP_FontAsset s_Font;

        static TMP_FontAsset DefaultFont()
        {
            if (s_Font != null)
                return s_Font;

            try
            {
                s_Font = TMP_Settings.defaultFontAsset;
            }
            catch (System.Exception)
            {
                s_Font = null;
            }

            if (s_Font == null)
                s_Font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");

            return s_Font;
        }

        static T GetOrAdd<T>(Transform t) where T : Component =>
            t.TryGetComponent<T>(out var existing) ? existing : Undo.AddComponent<T>(t.gameObject);

        static Transform NewChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create hologram object");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        static Transform FindOrCreate(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing == null)
                return NewChild(parent, name);

            existing.localPosition = Vector3.zero;
            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one;
            return existing;
        }

        static void Clear(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
        }

        static Transform FindRoot(Scene scene, string name)
        {
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == name)
                    return go.transform;

            return null;
        }

        static void EnsureFolder(string assetPath)
        {
            string folder = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;

            var parts = folder.Split('/');
            string built = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{built}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(built, parts[i]);

                built = next;
            }
        }

        // ---------------------------------------------------------------- serialization

        static void SetRef(SerializedObject so, string path, Object value)
        {
            var property = Find(so, path);
            if (property != null)
                property.objectReferenceValue = value;
        }

        static void SetRefs(SerializedObject so, string path, Object[] values)
        {
            var property = Find(so, path);
            if (property == null)
                return;

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        static void SetFloat(SerializedObject so, string path, float value)
        {
            var property = Find(so, path);
            if (property != null)
                property.floatValue = value;
        }

        static void SetVector3(SerializedObject so, string path, Vector3 value)
        {
            var property = Find(so, path);
            if (property != null)
                property.vector3Value = value;
        }

        static SerializedProperty Find(SerializedObject so, string path)
        {
            var property = so.FindProperty(path);
            if (property == null)
                Debug.LogWarning($"[EarHologram] {so.targetObject.GetType().Name} has no field '{path}'.");

            return property;
        }
    }
}
