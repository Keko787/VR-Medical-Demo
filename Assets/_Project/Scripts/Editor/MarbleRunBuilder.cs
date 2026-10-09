using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRShootingGallery.PhysicsDemo;

namespace VRShootingGallery.EditorTools
{
    /// <summary>
    /// Builds the marble run: six troughs bolted around the inside of the CAVE room, so a marble
    /// released at the ceiling circles the viewer twice on its way down and finishes in a catch
    /// tray on the floor in front of them. Every wall the room has gets used, which is the point —
    /// in a CAVE the action has to be on the screens, not in the middle of the floor.
    ///
    /// The track is derived from the room's own walls rather than authored in absolute space, so
    /// moving or resizing <c>CAVE_User_Room</c> and rebuilding puts the run back where it belongs.
    /// Everything it makes lives under one <c>Marble_Run</c> object and is registered with Undo.
    /// </summary>
    static class MarbleRunBuilder
    {
        const string k_CreateMenu = "VR Shooting Gallery/Marble Run/Create Marble Run Scene";
        const string k_BuildMenu = "VR Shooting Gallery/Marble Run/Rebuild Marble Run";

        // The marble run scene starts as a copy of the physics scene, which already carries the
        // whole MiddleVR side of the setup: MVRManager pointed at the CAVE .vrx, the MVR_URP
        // settings prefab, the base camera MiddleVR clones onto its viewports, and the room itself.
        // Rebuilding any of that from scratch would be five chances to get it subtly wrong.
        const string k_TemplateScene = "Assets/_Project/Scenes/Demos/Physics MiddleVR.unity";
        const string k_ScenePath = "Assets/_Project/Scenes/Demos/MarbleRun MiddleVR.unity";

        const string k_RoomRoot = "CAVE_User_Room";
        const string k_RunRoot = "Marble_Run";
        const string k_TrackName = "Track";
        const string k_RackName = "Rack";
        const string k_MarblesName = "Marbles";
        const string k_BasinName = "Catch_Basin";
        const string k_SignName = "Run_Sign";

        const string k_BedMaterialPath = "Assets/_Project/Art/Materials/M_MarbleTrack.mat";
        const string k_RailMaterialPath = "Assets/_Project/Art/Materials/M_MarbleRail.mat";
        const string k_MarbleMaterialPath = "Assets/_Project/Art/Materials/M_Marble_{0:00}.mat";
        const string k_MarblePhysicsPath = "Assets/_Project/Art/Materials/PM_Marble.physicMaterial";
        const string k_ShaderReferencePath = "Assets/_Project/Art/Materials/M_CAVE_Room.mat";

        // ---------------------------------------------------------------- track shape

        /// <summary>
        /// How far the trough's centre line sits from the wall. Half the bed width plus a hair, so
        /// the outer rail all but touches the wall and the run reads as bolted to it.
        /// </summary>
        const float k_Inset = 0.11f;

        const float k_BedWidth = 0.18f;

        // Thick enough that a 2 m/s marble cannot step through it between two 50 Hz ticks even
        // before continuous detection gets involved.
        const float k_BedThickness = 0.04f;

        const float k_RailHeight = 0.07f;
        const float k_RailThickness = 0.02f;

        /// <summary>
        /// Gentle on purpose. The corners are drops, and a marble arriving at a drop faster than
        /// the catcher can absorb bounces back out of the trough — around 6° every leg finishes at
        /// roughly 2 m/s, which the catcher stops dead.
        /// </summary>
        const float k_SlopeDegrees = 6.5f;

        /// <summary>Height of the first leg's bed where the marbles are racked.</summary>
        const float k_StartHeight = 2.05f;

        /// <summary>
        /// How far a leg sits below the one feeding it. Legs never overlap — each ends at the near
        /// bed edge of the next — so this only has to be enough for the marble to be clearly
        /// falling rather than rolling across the join.
        /// </summary>
        const float k_CornerDrop = 0.07f;

        /// <summary>
        /// The stretch at the head of a leg where marbles arrive from above. Over this span the
        /// inner rail drops to a kerb (they fly in over it) and the outer rail is replaced by a tall
        /// catcher (they are stopped by it).
        ///
        /// It has to span the feeding leg's bed, but it is longer than that: the trough is
        /// single-file at 0.14 m clear, so a race arrives as a queue, and this is sized to hold the
        /// whole field nose-to-tail. Shorter and the back of the pack has to stack on top of the
        /// front of it, which is the one way a marble gets high enough to leave the trough.
        /// </summary>
        const float k_EntryLength = 0.3f;

        const float k_CatcherHeight = 0.18f;

        /// <summary>
        /// Kerb across the doorway. The doorway has to be open at marble height for the marble
        /// flying in over it, but a race arrives as a pack: four marbles jostling in one entry
        /// pocket can shove one of their number sideways, and without this it goes out through the
        /// doorway and onto the floor. Taller than a resting marble's centre so it contains one
        /// being pushed, low enough that an arriving one — whose underside crosses this plane a
        /// whole corner drop up — sails over it.
        /// </summary>
        const float k_DoorwayKerb = 0.04f;

        /// <summary>Where the last leg stops pouring, in room space. The catch tray starts just past it.</summary>
        const float k_DischargeX = -0.55f;

        // ---------------------------------------------------------------- marbles

        const float k_MarbleRadius = 0.035f;
        const float k_MarbleMass = 0.05f;
        const int k_MarbleCount = 4;

        /// <summary>
        /// Distance along the first leg to the front marble on the rack, and between marbles. The
        /// spacing is barely more than a marble, so a race starts as a tight pack rather than strung
        /// out over half a metre of the first leg.
        /// </summary>
        const float k_RackFirst = 0.12f;
        const float k_RackSpacing = 0.09f;

        /// <summary>
        /// How see-through the rails, catchers and basin walls are. The marbles spend the whole run
        /// on the far side of something — the trough is bolted to the wall and viewed from inside
        /// the room, so the inner rail is always between the viewer and the marble, and the 0.18 m
        /// catchers are worse. The beds stay opaque; a marble needs a surface to read against.
        /// </summary>
        const float k_RailAlpha = 0.3f;

        /// <summary>Read out by the sign when a marble wins, in the same order as the colours.</summary>
        static readonly string[] k_MarbleNames = { "RED", "AMBER", "BLUE", "GREEN" };

        // Fallbacks, only used when a wall is missing from the room. These are the CAVE room as
        // this project ships it.
        const float k_FallbackLeftX = -1.778f;
        const float k_FallbackRightX = 1.778f;
        const float k_FallbackFrontZ = 1.1113f;
        const float k_FallbackBackZ = -1.1113f;

        static readonly Color[] k_MarbleColors =
        {
            new(0.92f, 0.29f, 0.24f),
            new(0.98f, 0.75f, 0.18f),
            new(0.27f, 0.72f, 0.94f),
            new(0.45f, 0.85f, 0.42f),
        };

        // ================================================================== menus

        [MenuItem(k_CreateMenu, false, 300)]
        static void CreateScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(k_TemplateScene) == null)
            {
                Debug.LogError($"[MarbleRun] Missing the template scene '{k_TemplateScene}'.");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath) != null &&
                !EditorUtility.DisplayDialog("Rebuild the marble run scene?",
                    $"'{k_ScenePath}' already exists and will be overwritten with a fresh copy of the " +
                    "physics scene plus a newly built run.\n\nAnything you added to it by hand is lost.",
                    "Overwrite", "Cancel"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.OpenScene(k_TemplateScene, OpenSceneMode.Single);

            // Saved to the new path before anything is touched, so the template on disk is never at
            // risk from a build that goes wrong half way through.
            if (!EditorSceneManager.SaveScene(scene, k_ScenePath))
            {
                Debug.LogError($"[MarbleRun] Could not save the new scene to '{k_ScenePath}'.");
                return;
            }

            scene = SceneManager.GetActiveScene();
            StripTemplate(scene);

            if (!Build(scene))
                return;

            EditorSceneManager.SaveScene(scene);
            RegisterInBuildSettings();

            Debug.Log($"[MarbleRun] Built '{scene.name}'. Enter play mode and press wand button 1 " +
                      "(or the 1 key at a desk) to release the marbles.");
        }

        [MenuItem(k_BuildMenu, false, 310)]
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
                Debug.LogError($"[MarbleRun] No '{k_RoomRoot}' root in '{scene.name}'. Open the marble run " +
                               "scene (or any scene with the CAVE room) and try again.");
                return false;
            }

            Undo.SetCurrentGroupName("Build Marble Run");
            int group = Undo.GetCurrentGroup();

            // Dropped so the track materials are re-authored once per build rather than once per
            // session — otherwise editing a colour or the alpha and rebuilding would do nothing.
            s_TrackMaterials.Clear();

            DefaultFont();

            var run = FindOrCreate(room, k_RunRoot);
            Clear(run);

            var walls = ReadWalls(room);
            var legs = Plan(walls);

            var track = NewChild(run, k_TrackName);
            var built = new List<Bed>(legs.Count);
            float top = k_StartHeight;
            foreach (var leg in legs)
                built.Add(BuildLeg(track, leg, ref top));

            var finish = BuildBasin(run, walls);

            var holds = BuildRack(run, built[0]);
            var marbles = BuildMarbles(run, holds);
            var sign = BuildSign(run, walls);

            Wire(run, marbles, holds, sign, finish);

            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = run.gameObject;

            Debug.Log($"[MarbleRun] {legs.Count} legs, {marbles.Count} marbles. The track drops from " +
                      $"{k_StartHeight:0.##} m to {built[built.Count - 1].End.y:0.##} m over " +
                      $"{TotalLength(built):0.#} m of trough.", run);
            return true;
        }

        // ---------------------------------------------------------------- planning

        /// <summary>The four wall planes, in the room's own space.</summary>
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
        }

        /// <summary>One leg of the run before it has been given a height.</summary>
        readonly struct Leg
        {
            public readonly string Name;

            /// <summary>Ends of the bed on the horizontal plane; the y is filled in by <see cref="Plan"/>.</summary>
            public readonly Vector3 From, To;

            /// <summary>Unit vector from the trough toward the wall it is bolted to.</summary>
            public readonly Vector3 ToWall;

            /// <summary>True when a leg above pours into this one's head, which is what needs a catcher.</summary>
            public readonly bool Receives;

            public Leg(string name, Vector3 from, Vector3 to, Vector3 toWall, bool receives)
            {
                Name = name;
                From = from;
                To = to;
                ToWall = toWall;
                Receives = receives;
            }
        }

        /// <summary>A leg after it has been built — the top surface of its bed, and its frame.</summary>
        readonly struct Bed
        {
            public readonly Vector3 Start, End, Dir, Up;
            public readonly float Length;

            public Bed(Vector3 start, Vector3 end, Vector3 dir, Vector3 up, float length)
            {
                Start = start;
                End = end;
                Dir = dir;
                Up = up;
                Length = length;
            }
        }

        static Walls ReadWalls(Transform room)
        {
            return new Walls(
                WallCoord(room, "Left_Wall", true, k_FallbackLeftX),
                WallCoord(room, "Right_Wall", true, k_FallbackRightX),
                WallCoord(room, "Front_Wall", false, k_FallbackFrontZ),
                WallCoord(room, "Back_Wall", false, k_FallbackBackZ));
        }

        static float WallCoord(Transform room, string child, bool readX, float fallback)
        {
            var wall = room.Find(child);
            if (wall != null)
                return readX ? wall.localPosition.x : wall.localPosition.z;

            Debug.LogWarning($"[MarbleRun] '{k_RoomRoot}' has no child called '{child}'. Using the stock " +
                             $"CAVE dimension ({fallback:0.###} m) for that side instead.", room);
            return fallback;
        }

        /// <summary>
        /// Lays six legs anticlockwise round the room: down the left wall, across the front, back
        /// down the right, across the back, then a second pass along the left and front walls at
        /// waist height.
        ///
        /// The join is the whole trick. A leg <em>ends</em> at the near bed edge of the leg it feeds
        /// and <em>starts</em> at the far bed edge of the leg that feeds it, so no two beds ever
        /// occupy the same ground — and the marble always leaves at the receiving bed's inner edge
        /// travelling straight across it, into the catcher on the far side.
        /// </summary>
        static List<Leg> Plan(Walls w)
        {
            float leftC = w.LeftX + k_Inset;
            float rightC = w.RightX - k_Inset;
            float frontC = w.FrontZ - k_Inset;
            float backC = w.BackZ + k_Inset;
            float half = k_BedWidth * 0.5f;

            var toLeft = new Vector3(-1f, 0f, 0f);
            var toRight = new Vector3(1f, 0f, 0f);
            var toFront = new Vector3(0f, 0f, 1f);
            var toBack = new Vector3(0f, 0f, -1f);

            return new List<Leg>
            {
                new("Leg_1_Left_Upper",
                    new Vector3(leftC, 0f, w.BackZ + half), new Vector3(leftC, 0f, frontC - half),
                    toLeft, false),

                new("Leg_2_Front_Upper",
                    new Vector3(leftC - half, 0f, frontC), new Vector3(rightC - half, 0f, frontC),
                    toFront, true),

                new("Leg_3_Right",
                    new Vector3(rightC, 0f, frontC + half), new Vector3(rightC, 0f, backC + half),
                    toRight, true),

                new("Leg_4_Back",
                    new Vector3(rightC + half, 0f, backC), new Vector3(leftC + half, 0f, backC),
                    toBack, true),

                new("Leg_5_Left_Lower",
                    new Vector3(leftC, 0f, backC - half), new Vector3(leftC, 0f, frontC - half),
                    toLeft, true),

                new("Leg_6_Front_Lower",
                    new Vector3(leftC - half, 0f, frontC), new Vector3(k_DischargeX, 0f, frontC),
                    toFront, true),
            };
        }

        // ---------------------------------------------------------------- legs

        /// <summary>
        /// Builds one trough and hands back where the next one starts: <paramref name="top"/> comes
        /// in as the height of this leg's bed at its head and goes out as the height of the next
        /// leg's, which is this one's tail less the corner drop.
        /// </summary>
        static Bed BuildLeg(Transform track, Leg leg, ref float top)
        {
            float horizontalRun = Vector3.Distance(leg.From, leg.To);
            float drop = horizontalRun * Mathf.Tan(k_SlopeDegrees * Mathf.Deg2Rad);

            var p0 = new Vector3(leg.From.x, top, leg.From.z);
            var p1 = new Vector3(leg.To.x, top - drop, leg.To.z);
            top = p1.y - k_CornerDrop;

            var delta = p1 - p0;
            float length = delta.magnitude;
            var dir = delta / length;

            // LookRotation with the world up hint gives exactly the frame the trough needs: local Z
            // runs down the slope, local Y is the bed's own up, and local X stays horizontal —
            // which is what lets the rails be offset along the wall normal.
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            var up = rot * Vector3.up;
            var mid = (p0 + p1) * 0.5f;

            var root = NewChild(track, leg.Name);

            float railOffset = k_BedWidth * 0.5f - k_RailThickness * 0.5f;
            var outer = leg.ToWall * railOffset;
            var inner = -leg.ToWall * railOffset;

            Box(root, "Bed", mid - up * (k_BedThickness * 0.5f), rot,
                new Vector3(k_BedWidth, k_BedThickness, length), k_BedMaterialPath);

            if (leg.Receives)
            {
                float entry = Mathf.Min(k_EntryLength, length * 0.5f);
                float rest = length - entry;

                // The marble arrives over the inner rail, so the inner rail is cut down to a kerb for
                // the first stretch rather than being there at full height.
                Box(root, "Rail_Inner", mid + inner + up * (k_RailHeight * 0.5f) + dir * (entry * 0.5f), rot,
                    new Vector3(k_RailThickness, k_RailHeight, rest), k_RailMaterialPath);

                Box(root, "Doorway_Kerb", p0 + inner + dir * (entry * 0.5f) + up * (k_DoorwayKerb * 0.5f), rot,
                    new Vector3(k_RailThickness, k_DoorwayKerb, entry), k_RailMaterialPath);

                // The catcher is what turns a fall into a corner: the marble crosses the bed in the
                // air, hits this, and drops onto the bed with its old direction spent.
                Box(root, "Catcher", p0 + outer + dir * (entry * 0.5f) + up * (k_CatcherHeight * 0.5f), rot,
                    new Vector3(k_RailThickness, k_CatcherHeight, entry), k_RailMaterialPath);

                Box(root, "Rail_Outer", p0 + outer + dir * (entry + rest * 0.5f) + up * (k_RailHeight * 0.5f), rot,
                    new Vector3(k_RailThickness, k_RailHeight, rest), k_RailMaterialPath);

                Cap(root, p0, dir, up, rot, k_CatcherHeight);
            }
            else
            {
                Box(root, "Rail_Inner", mid + inner + up * (k_RailHeight * 0.5f), rot,
                    new Vector3(k_RailThickness, k_RailHeight, length), k_RailMaterialPath);

                Box(root, "Rail_Outer", mid + outer + up * (k_RailHeight * 0.5f), rot,
                    new Vector3(k_RailThickness, k_RailHeight, length), k_RailMaterialPath);

                Cap(root, p0, dir, up, rot, k_RailHeight);
            }

            return new Bed(p0, p1, dir, up, length);
        }

        /// <summary>Closes the head of a leg so a marble rattling in the entry cannot roll back out of it.</summary>
        static void Cap(Transform leg, Vector3 p0, Vector3 dir, Vector3 up, Quaternion rot, float height)
        {
            Box(leg, "End_Cap", p0 - dir * (k_RailThickness * 0.5f) + up * (height * 0.5f), rot,
                new Vector3(k_BedWidth, height, k_RailThickness), k_RailMaterialPath);
        }

        // ---------------------------------------------------------------- basin

        /// <summary>
        /// Where the run finishes: an open tray on the floor, in front of the viewer and off to one
        /// side of where they stand. The lip on the incoming side is deliberately low — the last leg
        /// pours over it, and a full-height wall there would be the one thing in the run the marble
        /// hits instead of clears.
        /// </summary>
        /// <summary>Returns the finish line: the tray's inside, in the run's own space.</summary>
        static Bounds BuildBasin(Transform run, Walls w)
        {
            const float floorTop = 0.02f;
            const float thickness = 0.02f;
            const float wallHeight = 0.14f;
            const float lipHeight = 0.08f;

            float frontC = w.FrontZ - k_Inset;
            float lipX = k_DischargeX + 0.03f;
            float farX = k_DischargeX + 1f;
            float nearZ = frontC - 0.28f;
            float farZ = frontC + 0.1f;

            var basin = NewChild(run, k_BasinName);
            var flat = Quaternion.identity;

            var center = new Vector3((lipX + farX) * 0.5f, 0f, (nearZ + farZ) * 0.5f);
            float spanX = farX - lipX;
            float spanZ = farZ - nearZ;

            Box(basin, "Floor", new Vector3(center.x, floorTop - thickness * 0.5f, center.z), flat,
                new Vector3(spanX, thickness, spanZ), k_BedMaterialPath);

            Box(basin, "Wall_Far", new Vector3(farX, floorTop + wallHeight * 0.5f, center.z), flat,
                new Vector3(thickness, wallHeight, spanZ), k_RailMaterialPath);

            Box(basin, "Wall_Near", new Vector3(center.x, floorTop + wallHeight * 0.5f, nearZ), flat,
                new Vector3(spanX, wallHeight, thickness), k_RailMaterialPath);

            Box(basin, "Wall_Back", new Vector3(center.x, floorTop + wallHeight * 0.5f, farZ), flat,
                new Vector3(spanX, wallHeight, thickness), k_RailMaterialPath);

            Box(basin, "Lip_Entry", new Vector3(lipX, floorTop + lipHeight * 0.5f, center.z), flat,
                new Vector3(thickness, lipHeight, spanZ), k_RailMaterialPath);

            // Capped below the wall tops so a marble has to be down in the tray to count, not merely
            // passing through the airspace above it on the way to somewhere else.
            var finish = new Bounds();
            finish.SetMinMax(new Vector3(lipX, 0f, nearZ),
                new Vector3(farX, floorTop + 0.1f, farZ));

            return finish;
        }

        // ---------------------------------------------------------------- marbles

        /// <summary>
        /// Parks the marbles up the first leg, nearest the bottom first. That order is what
        /// <see cref="MarbleRun"/> releases in, and it has to be this way round: a parked marble is
        /// kinematic and therefore solid, so releasing from the back would send the first marble
        /// straight into the ones still waiting.
        /// </summary>
        static List<Transform> BuildRack(Transform run, Bed first)
        {
            var rack = NewChild(run, k_RackName);
            var holds = new List<Transform>(k_MarbleCount);

            for (int i = 0; i < k_MarbleCount; i++)
            {
                float along = k_RackFirst + (k_MarbleCount - 1 - i) * k_RackSpacing;

                var hold = NewChild(rack, $"Hold_{i:00}");

                // A hair above the bed rather than exactly on it, so the marble settles onto the
                // surface when it is released instead of starting the frame inside it.
                hold.localPosition = first.Start + first.Dir * along + first.Up * (k_MarbleRadius + 0.002f);
                holds.Add(hold);
            }

            return holds;
        }

        static List<Rigidbody> BuildMarbles(Transform run, List<Transform> holds)
        {
            var parent = NewChild(run, k_MarblesName);
            var physics = EnsureMarblePhysics();
            var marbles = new List<Rigidbody>(holds.Count);

            for (int i = 0; i < holds.Count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"Marble_{i:00}";
                Undo.RegisterCreatedObjectUndo(go, "Create marble");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = holds[i].localPosition;
                go.transform.localScale = Vector3.one * (k_MarbleRadius * 2f);

                var skin = EnsureMaterial(string.Format(k_MarbleMaterialPath, i + 1),
                    k_MarbleColors[i % k_MarbleColors.Length], 0.85f, 1f, false);

                if (skin != null)
                    go.GetComponent<MeshRenderer>().sharedMaterial = skin;

                go.GetComponent<SphereCollider>().sharedMaterial = physics;

                var body = go.AddComponent<Rigidbody>();
                body.mass = k_MarbleMass;

                // No drag at all. Both kinds brake a rolling ball directly, and a marble run that
                // stalls half way down is worse than one that finishes a shade fast.
                body.drag = 0f;
                body.angularDrag = 0.02f;

                body.interpolation = RigidbodyInterpolation.Interpolate;

                // The beds are 4 cm thick and the marble touches 2 m/s, which is 4 cm of travel per
                // 50 Hz tick. Discrete detection would let it step through the floor of a trough.
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                body.isKinematic = true;
                marbles.Add(body);
            }

            return marbles;
        }

        // ---------------------------------------------------------------- sign

        static TextMeshPro BuildSign(Transform run, Walls w)
        {
            var sign = NewChild(run, k_SignName);

            // Between the two front-wall legs, where nothing crosses it. Text reads correctly when
            // its forward points away from the viewer, and the viewer is on the -Z side of the front
            // wall, so the default rotation is already right.
            sign.localPosition = new Vector3(0f, 0.95f, w.FrontZ - 0.01f);
            sign.localRotation = Quaternion.identity;

            Text(sign, "Title", new Vector3(0f, 0.16f, 0f), 0.9f, "MARBLE RUN", new Color(0.55f, 0.62f, 0.72f));
            return Text(sign, "Status", Vector3.zero, 1.5f, "PRESS  1  TO START", new Color(0.93f, 0.95f, 1f));
        }

        // ---------------------------------------------------------------- wiring

        static void Wire(Transform run, List<Rigidbody> marbles, List<Transform> holds, TextMeshPro status,
            Bounds finish)
        {
            var component = run.GetComponent<MarbleRun>();
            if (component == null)
                component = Undo.AddComponent<MarbleRun>(run.gameObject);

            var so = new SerializedObject(component);
            SetArray(so, "m_Marbles", marbles);
            SetArray(so, "m_Holds", holds);
            SetNames(so, "m_MarbleNames", marbles.Count);
            SetRef(so, "m_StatusText", status);
            SetBounds(so, "m_FinishZone", finish);
            so.ApplyModifiedProperties();
        }

        // ================================================================== scene template

        /// <summary>
        /// Takes the physics demo back out of the copy. The tether rigs and their placeholder
        /// spheres would hang straight through the new track, and the gun's projectiles would knock
        /// marbles off it — a different demo, not this one.
        /// </summary>
        static void StripTemplate(Scene scene)
        {
            var room = FindRoot(scene, k_RoomRoot);
            if (room != null)
            {
                for (int i = room.childCount - 1; i >= 0; i--)
                {
                    var child = room.GetChild(i);
                    if (child.name.StartsWith("Tether_Rig") || child.name.StartsWith("Sphere"))
                        Object.DestroyImmediate(child.gameObject);
                }
            }

            foreach (var go in scene.GetRootGameObjects())
                if (go.name.StartsWith("Gun"))
                    Object.DestroyImmediate(go);
        }

        /// <summary>
        /// Adds the scene to the build list, switched off. Someone building for the CAVE has to tick
        /// exactly one box rather than go hunting for the file, and a disabled entry changes nothing
        /// about what the current build produces.
        /// </summary>
        static void RegisterInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            foreach (var entry in scenes)
                if (entry.path == k_ScenePath)
                    return;

            scenes.Add(new EditorBuildSettingsScene(k_ScenePath, false));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log($"[MarbleRun] Added '{k_ScenePath}' to the build list, disabled. Tick it in " +
                      "File ▸ Build Settings to build the marble run for the CAVE.");
        }

        // ================================================================== assets

        static PhysicMaterial EnsureMarblePhysics()
        {
            var material = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(k_MarblePhysicsPath);
            if (material != null)
                return material;

            EnsureFolder(k_MarblePhysicsPath);

            material = new PhysicMaterial("PM_Marble")
            {
                // Enough grip to roll rather than skid — a sphere only needs about 0.03 at this
                // slope — and barely any bounce, because every corner is a drop and a lively marble
                // spends the whole run rattling instead of rolling.
                dynamicFriction = 0.35f,
                staticFriction = 0.4f,
                bounciness = 0.2f,
                frictionCombine = PhysicMaterialCombine.Average,
                bounceCombine = PhysicMaterialCombine.Average,
            };

            AssetDatabase.CreateAsset(material, k_MarblePhysicsPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        /// <summary>
        /// A material the builder owns. Track pieces pass <paramref name="reauthor"/> because their
        /// look is structural — an alpha the run is designed around, and a stale opaque rail is
        /// exactly the bug this guards against. Marble colours are authored once and then left
        /// alone, so a recolour survives a rebuild.
        /// </summary>
        static Material EnsureMaterial(string path, Color color, float smoothness, float alpha, bool reauthor)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null && !reauthor)
                return material;

            bool created = material == null;
            if (created)
            {
                EnsureFolder(path);

                // The shader is taken from a material already in the project rather than looked up
                // by name, so the run cannot end up on the wrong render pipeline's Lit shader.
                var reference = AssetDatabase.LoadAssetAtPath<Material>(k_ShaderReferencePath);
                var shader = reference != null ? reference.shader : Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    Debug.LogWarning($"[MarbleRun] No shader to build '{path}' from; the piece will render pink.");
                    return null;
                }

                material = new Material(shader);
            }

            color.a = alpha;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);

            SetBlending(material, alpha < 1f);

            if (created)
                AssetDatabase.CreateAsset(material, path);
            else
                EditorUtility.SetDirty(material);

            AssetDatabase.SaveAssets();
            return material;
        }

        /// <summary>
        /// Switches a material between opaque and alpha-blended. There is no API for this — the
        /// Material inspector writes these properties, keywords and the render queue by hand, and a
        /// material that has only had its alpha lowered stays stubbornly, invisibly opaque. Both
        /// URP's property names and the built-in pipeline's are covered, since the shader here is
        /// whatever the room's material happens to use.
        /// </summary>
        static void SetBlending(Material material, bool transparent)
        {
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", transparent ? 1f : 0f);
                if (material.HasProperty("_Blend"))
                    material.SetFloat("_Blend", 0f);            // straight alpha, not premultiplied
            }
            else if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", transparent ? 3f : 0f);
            }

            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)(transparent
                    ? UnityEngine.Rendering.BlendMode.SrcAlpha
                    : UnityEngine.Rendering.BlendMode.One));

            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)(transparent
                    ? UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha
                    : UnityEngine.Rendering.BlendMode.Zero));

            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", transparent ? 0f : 1f);

            if (material.HasProperty("_AlphaClip"))
                material.SetFloat("_AlphaClip", 0f);

            Keyword(material, "_SURFACE_TYPE_TRANSPARENT", transparent);
            Keyword(material, "_ALPHABLEND_ON", transparent);
            Keyword(material, "_ALPHATEST_ON", false);
            Keyword(material, "_ALPHAPREMULTIPLY_ON", false);

            // A see-through rail casting a solid shadow would put back the very stripe across the
            // track that making it see-through was meant to remove.
            material.SetShaderPassEnabled("ShadowCaster", !transparent);

            material.renderQueue = (int)(transparent
                ? UnityEngine.Rendering.RenderQueue.Transparent
                : UnityEngine.Rendering.RenderQueue.Geometry);
        }

        static void Keyword(Material material, string keyword, bool on)
        {
            if (on)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }

        /// <summary>Cached because a build asks for these once per box, which is a few hundred times.</summary>
        static readonly Dictionary<string, Material> s_TrackMaterials = new();

        static Material TrackMaterial(string path)
        {
            if (s_TrackMaterials.TryGetValue(path, out var cached) && cached != null)
                return cached;

            // The bed is matte and desaturated on purpose: a glossy trough throws specular glare
            // across the CAVE screens, and the marbles are the only thing that should catch the eye.
            // The rails go pale and slightly glossy instead — at 30% alpha a dark rail reads as a
            // smear across the marble behind it, where a pale one reads as glass.
            var material = path == k_RailMaterialPath
                ? EnsureMaterial(path, new Color(0.62f, 0.68f, 0.78f), 0.25f, k_RailAlpha, true)
                : EnsureMaterial(path, new Color(0.42f, 0.46f, 0.53f), 0.1f, 1f, true);

            s_TrackMaterials[path] = material;
            return material;
        }

        // ================================================================== helpers

        static Transform Box(Transform parent, string name, Vector3 localPosition, Quaternion localRotation,
            Vector3 size, string materialPath)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, "Create track piece");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = size;

            var material = TrackMaterial(materialPath);
            if (material != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = material;

            return go.transform;
        }

        static Transform NewChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create marble run object");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        static Transform FindOrCreate(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null)
            {
                existing.localPosition = Vector3.zero;
                existing.localRotation = Quaternion.identity;
                existing.localScale = Vector3.one;
                return existing;
            }

            return NewChild(parent, name);
        }

        static void Clear(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
        }

        static float TotalLength(List<Bed> beds)
        {
            float total = 0f;
            foreach (var bed in beds)
                total += bed.Length;

            return total;
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

        static void SetArray<T>(SerializedObject so, string path, List<T> values) where T : Object
        {
            var property = so.FindProperty(path);
            if (property == null)
            {
                Debug.LogWarning($"[MarbleRun] MarbleRun has no field '{path}'.");
                return;
            }

            property.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        static void SetNames(SerializedObject so, string path, int count)
        {
            var property = so.FindProperty(path);
            if (property == null)
            {
                Debug.LogWarning($"[MarbleRun] MarbleRun has no field '{path}'.");
                return;
            }

            property.arraySize = count;
            for (int i = 0; i < count; i++)
                property.GetArrayElementAtIndex(i).stringValue = k_MarbleNames[i % k_MarbleNames.Length];
        }

        static void SetBounds(SerializedObject so, string path, Bounds value)
        {
            var property = so.FindProperty(path);
            if (property != null)
                property.boundsValue = value;
            else
                Debug.LogWarning($"[MarbleRun] MarbleRun has no field '{path}'.");
        }

        static void SetRef(SerializedObject so, string path, Object value)
        {
            var property = so.FindProperty(path);
            if (property != null)
                property.objectReferenceValue = value;
            else
                Debug.LogWarning($"[MarbleRun] MarbleRun has no field '{path}'.");
        }

        // ---------------------------------------------------------------- text

        static TextMeshPro Text(Transform parent, string name, Vector3 localPosition, float size,
            string content, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create sign text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var text = Undo.AddComponent<TextMeshPro>(go);

            // TMP 3.0.9's Awake throws inside LoadFontAsset when the component is added from an
            // editor script, which leaves the text and its renderer disabled with no material —
            // silently invisible. Wire the font, material and enabled flags up by hand.
            var font = DefaultFont();
            if (font != null)
            {
                text.font = font;
                text.fontSharedMaterial = font.material;
            }
            else
            {
                Debug.LogWarning("[MarbleRun] No TMP font asset found. Run Window ▸ TextMeshPro ▸ " +
                                 "Import TMP Essential Resources, then build again.");
            }

            text.enabled = true;
            if (go.TryGetComponent<MeshRenderer>(out var renderer))
                renderer.enabled = true;

            text.text = content;
            text.fontSize = size;
            text.enableAutoSizing = false;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = new Vector2(2.5f, 0.4f);
            text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
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
    }
}
