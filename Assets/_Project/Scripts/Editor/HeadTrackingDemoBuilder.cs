using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VRShootingGallery.TrackingDemo;

namespace VRShootingGallery.EditorTools
{
    /// <summary>
    /// Builds the head-tracking demo: the CAVE floor becomes a sheet of water that ripples from
    /// under the viewer's feet, and a cloud of bubbles rises from around their head. Nothing to
    /// hold and nothing to shoot — the viewer is the only input, which is the point of the scene.
    /// </summary>
    /// <remarks>
    /// Why these two effects. Positional tracking is easiest to feel through something anchored to
    /// where you are, and the floor is the one screen the viewer is always in contact with: rings
    /// spreading from your feet, and a wake behind you as you walk, say "the room knows where I am"
    /// without a word of explanation. Head tracking — the viewpoint following the eyes — needs
    /// something with height off the screens to show parallax, and the bubbles are that: they sit
    /// at head height, drift up, and stay behind when you move, so a step to the side is a step
    /// through them. The water's specular highlight does the same job on the floor, sliding over
    /// the surface as the head moves.
    ///
    /// The scene is a copy of the physics scene for the same reason the marble run's and the Hot
    /// Wheels track's are: that scene already carries the whole MiddleVR side of the setup, and
    /// rebuilding it from scratch would be several chances to get it subtly wrong.
    /// </remarks>
    static class HeadTrackingDemoBuilder
    {
        const string k_CreateMenu = "VR Shooting Gallery/Tracking/Create Head Tracking Demo Scene";
        const string k_BuildMenu = "VR Shooting Gallery/Tracking/Rebuild Head Tracking Demo";

        const string k_TemplateScene = "Assets/_Project/Scenes/Demos/Physics MiddleVR.unity";
        const string k_ScenePath = "Assets/_Project/Scenes/Demos/HeadTracking.unity";

        const string k_RoomRoot = "CAVE_User_Room";
        const string k_DemoRoot = "Tracking_Demo";
        const string k_FloorName = "Ripple_Floor";
        const string k_BubblesName = "Head_Bubbles";
        const string k_GroundName = "Ground";

        const string k_FloorShader = "VR Demo/Ripple Floor";
        const string k_BubbleShader = "VR Demo/Bubble";
        const string k_FloorMaterialPath = "Assets/_Project/Art/Materials/M_RippleFloor.mat";
        const string k_BubbleMaterialPath = "Assets/_Project/Art/Materials/M_Bubble.mat";
        const string k_MeshPath = "Assets/_Project/Art/Meshes/RippleFloor.asset";

        // ---------------------------------------------------------------- floor

        /// <summary>
        /// Height of the water's rest surface above the CAVE floor. Zero puts the crests and troughs
        /// either side of the physical floor, which is what "the floor ripples" means; sink it to
        /// make a pool the viewer stands over.
        /// </summary>
        const float k_SurfaceHeight = 0f;

        /// <summary>
        /// Grid spacing. 2.5 cm across the 3.556 m floor is 143 × 90 vertices — cheap to push every
        /// frame, and fine enough that a 10 cm ring is drawn as a curve rather than a polygon.
        /// </summary>
        const float k_CellSize = 0.025f;

        // ---------------------------------------------------------------- bubbles

        /// <summary>Where the cloud sits until the head is found — a standing viewer at room centre.</summary>
        static readonly Vector3 k_BubbleStandIn = new(0f, 1.6f, 0f);

        /// <summary>
        /// Radius of the shell the bubbles are born on. Wider than a head so they come into view at
        /// the edges rather than in front of the eyes, where a billboard would be an obvious flat card.
        /// </summary>
        const float k_BubbleShellRadius = 0.32f;

        const float k_BubblesPerSecond = 28f;

        /// <summary>
        /// Extra bubbles per metre the head moves. This is the positional half of the effect: stand
        /// still and the cloud is sparse, walk and it thickens into a trail.
        /// </summary>
        const float k_BubblesPerMetre = 40f;

        const float k_BubbleRiseMin = 0.06f;
        const float k_BubbleRiseMax = 0.14f;

        // Only used when a wall is missing from the room — the CAVE as this project ships it.
        const float k_FallbackLeftX = -1.778f;
        const float k_FallbackRightX = 1.778f;
        const float k_FallbackFrontZ = 1.1113f;
        const float k_FallbackBackZ = -1.1113f;

        readonly struct Walls
        {
            public readonly float LeftX, RightX, FrontZ, BackZ;

            public Walls(float leftX, float rightX, float frontZ, float backZ)
            {
                LeftX = leftX;
                RightX = rightX;
                FrontZ = frontZ;
                BackZ = backZ;
            }

            public float Width => RightX - LeftX;
            public float Depth => FrontZ - BackZ;
            public Vector3 Centre => new((LeftX + RightX) * 0.5f, 0f, (FrontZ + BackZ) * 0.5f);
        }

        // ================================================================== menus

        [MenuItem(k_CreateMenu, false, 500)]
        static void CreateScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(k_TemplateScene) == null)
            {
                Debug.LogError($"[HeadTracking] Missing the template scene '{k_TemplateScene}'.");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath) != null &&
                !EditorUtility.DisplayDialog("Rebuild the head tracking scene?",
                    $"'{k_ScenePath}' already exists and will be overwritten with a fresh copy of the " +
                    "physics scene plus a newly built demo.\n\nAnything you added to it by hand is lost.",
                    "Overwrite", "Cancel"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.OpenScene(k_TemplateScene, OpenSceneMode.Single);

            // Saved to the new path before anything is touched, so the template on disk is never at
            // risk from a build that goes wrong half way through.
            if (!EditorSceneManager.SaveScene(scene, k_ScenePath))
            {
                Debug.LogError($"[HeadTracking] Could not save the new scene to '{k_ScenePath}'.");
                return;
            }

            scene = SceneManager.GetActiveScene();
            StripTemplate(scene);

            if (!Build(scene))
                return;

            EditorSceneManager.SaveScene(scene);
            RegisterInBuildSettings();

            Debug.Log($"[HeadTracking] Built '{scene.name}'. Enter play mode and walk: the floor ripples " +
                      "from under your feet and bubbles rise from around your head.");
        }

        [MenuItem(k_BuildMenu, false, 510)]
        static void Rebuild()
        {
            var scene = SceneManager.GetActiveScene();
            if (Build(scene))
                EditorSceneManager.MarkSceneDirty(scene);
        }

        // ================================================================== build

        static bool Build(Scene scene)
        {
            var room = FindRoot(scene, k_RoomRoot);
            if (room == null)
            {
                Debug.LogError($"[HeadTracking] No '{k_RoomRoot}' root in '{scene.name}'. Open the head " +
                               "tracking scene (or any scene with the CAVE room) and try again.");
                return false;
            }

            var floorMaterial = Material(k_FloorMaterialPath, k_FloorShader);
            var bubbleMaterial = Material(k_BubbleMaterialPath, k_BubbleShader);
            if (floorMaterial == null || bubbleMaterial == null)
                return false;

            Undo.SetCurrentGroupName("Build Head Tracking Demo");
            int group = Undo.GetCurrentGroup();

            var walls = ReadWalls(room);

            var root = FindOrCreate(room, k_DemoRoot);
            Clear(root);

            var floor = BuildFloor(root, walls, floorMaterial);
            BuildBubbles(root, bubbleMaterial);

            // Reused rather than re-added, so a rebuild does not stack a second source that would
            // splash every ring twice.
            var source = root.GetComponent<HeadRippleSource>();
            if (source == null)
                source = Undo.AddComponent<HeadRippleSource>(root.gameObject);

            var so = new SerializedObject(source);
            SetRef(so, "m_Floor", floor);
            so.ApplyModifiedProperties();

            HideGround(scene);

            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = root.gameObject;

            Debug.Log($"[HeadTracking] Water sheet {walls.Width:0.###} × {walls.Depth:0.###} m at " +
                      $"{k_CellSize * 100f:0.#} cm cells; bubbles at {k_BubblesPerSecond:0}/s plus " +
                      $"{k_BubblesPerMetre:0} per metre walked.", root);
            return true;
        }

        // ================================================================== floor

        /// <summary>
        /// The water: a grid mesh the exact size of the floor, the simulation that drives it, and
        /// the material that colours it. The saved mesh asset is only for the Editor to show a floor
        /// where one will be; at runtime <see cref="RippleFloor"/> builds its own to animate.
        /// </summary>
        static RippleFloor BuildFloor(Transform root, in Walls walls, Material material)
        {
            var floor = NewChild(root, k_FloorName);
            floor.localPosition = walls.Centre + Vector3.up * k_SurfaceHeight;

            var filter = Undo.AddComponent<MeshFilter>(floor.gameObject);
            var renderer = Undo.AddComponent<MeshRenderer>(floor.gameObject);
            var ripple = Undo.AddComponent<RippleFloor>(floor.gameObject);

            filter.sharedMesh = Save(RippleFloor.BuildMesh(walls.Width, walls.Depth, k_CellSize, out _, out _));

            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var so = new SerializedObject(ripple);
            SetFloat(so, "m_Width", walls.Width);
            SetFloat(so, "m_Depth", walls.Depth);
            SetFloat(so, "m_CellSize", k_CellSize);
            so.ApplyModifiedProperties();

            return ripple;
        }

        /// <summary>
        /// Writes the mesh to its asset path, in place if it already exists so the scene's reference
        /// follows the rebuild instead of being left holding the old grid.
        /// </summary>
        static Mesh Save(Mesh mesh)
        {
            EnsureFolder(k_MeshPath);

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(k_MeshPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, k_MeshPath);
                AssetDatabase.SaveAssets();
                return mesh;
            }

            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            Object.DestroyImmediate(mesh);
            return existing;
        }

        /// <summary>
        /// The template's 10 m ground plane sits exactly at the water's rest height, so it would
        /// z-fight the flat patches and poke up through every trough. Switched off rather than
        /// deleted or moved — it is the template's, and nothing in this demo needs a floor collider.
        /// </summary>
        static void HideGround(Scene scene)
        {
            var ground = FindRoot(scene, k_GroundName);
            if (ground == null || !ground.gameObject.activeSelf)
                return;

            Undo.RecordObject(ground.gameObject, "Hide ground");
            ground.gameObject.SetActive(false);
            Debug.Log($"[HeadTracking] Switched off '{k_GroundName}' — the water surface takes its place.", ground);
        }

        // ================================================================== bubbles

        /// <summary>
        /// The cloud around the head: a particle system on a follower, emitting from a shell around
        /// the head in world space so the bubbles stay where they were born and the viewer walks
        /// through them. Everything is set here rather than in a prefab so the numbers sit next to
        /// their reasons.
        /// </summary>
        static void BuildBubbles(Transform root, Material material)
        {
            var bubbles = NewChild(root, k_BubblesName);
            bubbles.localPosition = k_BubbleStandIn;

            Undo.AddComponent<FollowTrackedHead>(bubbles.gameObject);

            var system = Undo.AddComponent<ParticleSystem>(bubbles.gameObject);
            Undo.RecordObject(system, "Configure bubbles");

            var main = system.main;
            main.duration = 5f;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.9f));
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 500;

            // The emitter never leaves the room, but automatic culling can still pause a system
            // whose bounds fall off one CAVE camera, and four cameras disagree about that.
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = k_BubblesPerSecond;
            emission.rateOverDistance = k_BubblesPerMetre;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = k_BubbleShellRadius;
            shape.radiusThickness = 0.35f;

            // Buoyancy as a steady rise rather than negative gravity: gravity would keep
            // accelerating, and a bubble that ends its life at a run reads as a spark.
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(k_BubbleRiseMin, k_BubbleRiseMax);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.05f;
            noise.frequency = 1.5f;
            noise.scrollSpeed = 0.4f;
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var colour = system.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.75f, 0.95f, 1f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(1f, 0.8f),
                    new GradientAlphaKey(0f, 1f),
                });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            // Swells a little as it rises, then pops.
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f),
                new Keyframe(0.15f, 1f),
                new Keyframe(0.92f, 1.15f),
                new Keyframe(1f, 0f)));

            var renderer = bubbles.GetComponent<ParticleSystemRenderer>();
            Undo.RecordObject(renderer, "Configure bubbles");
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // Facing, not View. A view-aligned billboard turns with each camera's view plane, and
            // the four CAVE cameras have four different ones, so a bubble straddling a wall seam
            // would be drawn at two angles. Facing turns it toward the camera position, which all
            // four share, so the seam closes.
            renderer.alignment = ParticleSystemRenderSpace.Facing;
        }

        // ================================================================== room

        static Walls ReadWalls(Transform room) =>
            new(WallCoord(room, "Left_Wall", true, k_FallbackLeftX),
                WallCoord(room, "Right_Wall", true, k_FallbackRightX),
                WallCoord(room, "Front_Wall", false, k_FallbackFrontZ),
                WallCoord(room, "Back_Wall", false, k_FallbackBackZ));

        static float WallCoord(Transform room, string child, bool readX, float fallback)
        {
            var wall = room.Find(child);
            if (wall != null)
                return readX ? wall.localPosition.x : wall.localPosition.z;

            Debug.LogWarning($"[HeadTracking] '{k_RoomRoot}' has no child called '{child}'. Using the stock " +
                             $"CAVE dimension ({fallback:0.###} m) for that side instead.", room);
            return fallback;
        }

        // ================================================================== scene template

        /// <summary>
        /// Takes the other demos back out of the copy. Anything left would sit in the water, and
        /// the gun's projectiles would land on a floor that no longer has a collider.
        /// </summary>
        static void StripTemplate(Scene scene)
        {
            var room = FindRoot(scene, k_RoomRoot);
            if (room != null)
            {
                for (int i = room.childCount - 1; i >= 0; i--)
                {
                    var child = room.GetChild(i);
                    if (child.name.StartsWith("Tether_Rig") || child.name.StartsWith("Sphere") ||
                        child.name.StartsWith("Marble_Run") || child.name.StartsWith("Hot_Wheels"))
                        Object.DestroyImmediate(child.gameObject);
                }
            }

            foreach (var go in scene.GetRootGameObjects())
                if (go.name.StartsWith("Gun"))
                    Object.DestroyImmediate(go);
        }

        static void RegisterInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            foreach (var entry in scenes)
                if (entry.path == k_ScenePath)
                    return;

            scenes.Add(new EditorBuildSettingsScene(k_ScenePath, false));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log($"[HeadTracking] Added '{k_ScenePath}' to the build list, disabled. Tick it in " +
                      "File ▸ Build Settings to build the head tracking demo for the CAVE.");
        }

        // ================================================================== assets

        /// <summary>
        /// A material on one of this demo's shaders. Created with the shader's own defaults the first
        /// time and left alone after that, so colours tuned in the Inspector survive a rebuild — the
        /// one thing a rebuild does put right is a material that has lost its shader.
        /// </summary>
        static Material Material(string path, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[HeadTracking] Shader '{shaderName}' not found. Is Assets/_Project/Art/Shaders " +
                               "present and compiling?");
                return null;
            }

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
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            return material;
        }

        // ================================================================== helpers

        static Transform NewChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create demo object");
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

        static void SetFloat(SerializedObject so, string path, float value)
        {
            var property = Find(so, path);
            if (property != null)
                property.floatValue = value;
        }

        static SerializedProperty Find(SerializedObject so, string path)
        {
            var property = so.FindProperty(path);
            if (property == null)
                Debug.LogWarning($"[HeadTracking] {so.targetObject.GetType().Name} has no field '{path}'.");

            return property;
        }
    }
}
