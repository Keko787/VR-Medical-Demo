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
    /// Builds the Hot Wheels slot circuit: a lap that starts on the floor in front of the viewer,
    /// jumps a gap, spirals a turn and a half up the right-hand wall, comes back along the front
    /// wall overhead through a barrel roll and a vertical loop, and plunges down the left-hand wall
    /// to the grid again.
    /// </summary>
    /// <remarks>
    /// <b>Where the circuit is allowed to be.</b> The CAVE has four screens — front, floor, left and
    /// right — and no back one. A point is only visible if the line from the viewer's eye through it
    /// lands on one of those four, which makes the rear of the room above floor level a dead zone:
    /// look behind you at head height and there is nothing there to look at. So the lap is pushed
    /// into the front of the room, and the whole of it lives in <c>z ∈ [-0.09, 0.95]</c> — over a
    /// metre clear of the back wall, and never behind the viewer except at floor level, where the
    /// floor screen picks it up. <see cref="CheckRoom"/> re-checks that on every build rather than
    /// trusting these numbers to stay true if the room is resized.
    ///
    /// <b>Why it closes.</b> Nothing here is authored in absolute space and then nudged until it
    /// meets. The two ends are half-turns of the same radius about centres on the same line, and the
    /// spiral is a 540° turn rather than a 180° one — which leaves in exactly the same place and
    /// pointing exactly the same way as a 180° would, because 540 and 180 differ by a whole turn.
    /// That is what lets a turn and a half of climb drop into the circuit where a plain corner was,
    /// and it is why the lap closes to the millimetre instead of to a fudge factor. The grades are
    /// then solved, not guessed: <see cref="SolveGrade"/> finds the climb angle at which the ramp in,
    /// the helix and the ramp out add up to exactly the height the far end needs.
    /// </remarks>
    static class HotWheelsTrackBuilder
    {
        const string k_CreateMenu = "VR Shooting Gallery/Hot Wheels/Create Hot Wheels Track Scene";
        const string k_BuildMenu = "VR Shooting Gallery/Hot Wheels/Rebuild Hot Wheels Track";

        // Same reasoning as the marble run: the physics scene already carries the whole MiddleVR
        // side of the setup, and rebuilding that from scratch would be several chances to get it
        // subtly wrong.
        const string k_TemplateScene = "Assets/_Project/Scenes/Demos/Physics MiddleVR.unity";
        const string k_ScenePath = "Assets/_Project/Scenes/Demos/HotWheelsTrack.unity";

        const string k_RoomRoot = "CAVE_User_Room";
        const string k_TrackRoot = "Hot_Wheels_Track";

        const string k_BodyMaterialPath = "Assets/_Project/Art/Materials/M_HW_Track.mat";
        const string k_BedMaterialPath = "Assets/_Project/Art/Materials/M_HW_Bed.mat";
        const string k_SupportMaterialPath = "Assets/_Project/Art/Materials/M_HW_Support.mat";
        const string k_TyreMaterialPath = "Assets/_Project/Art/Materials/M_HW_Tyre.mat";
        const string k_CarMaterialPath = "Assets/_Project/Art/Materials/M_HW_Car_{0:00}.mat";
        const string k_MeshPath = "Assets/_Project/Art/Meshes/HotWheelsTrack.asset";
        const string k_ShaderReferencePath = "Assets/_Project/Art/Materials/M_CAVE_Room.mat";

        // ---------------------------------------------------------------- footprint

        /// <summary>
        /// How far the outermost point of the circuit stops short of the left and right walls. The
        /// track is banked hard at exactly those points, so this has to clear the leaning ribbon,
        /// not just its centre line.
        /// </summary>
        const float k_SideMargin = 0.20f;

        /// <summary>How far the front straight sits off the front wall.</summary>
        const float k_FrontMargin = 0.16f;

        /// <summary>
        /// Radius of both half-turns, and therefore half the gap between the two straights. Tight,
        /// which is what a Hot Wheels track looks like, and what buys straights long enough to put
        /// anything on.
        /// </summary>
        const float k_TurnRadius = 0.52f;

        const float k_GroundHeight = 0.05f;
        const float k_HighHeight = 1.10f;

        // ---------------------------------------------------------------- features

        /// <summary>
        /// Radius of the vertical loop. Its footprint is <c>2 × radius</c> along the straight — a
        /// loop reaches as far back from where the car enters as it does forward — which is what
        /// sets how much of the front straight is left for anything else.
        /// </summary>
        const float k_LoopRadius = 0.36f;

        /// <summary>
        /// How much of the loop is left undrawn where it closes back onto the straight beneath it.
        /// Without this the last few degrees of the circle lie in the same plane as the straight and
        /// fight it for depth. A real loop has the same opening for the same reason.
        /// </summary>
        const float k_LoopMouthDeg = 20f;

        const float k_BarrelRollLength = 0.50f;

        /// <summary>Radius of the ramps either side of the jump, and how steeply they throw the car.</summary>
        const float k_RampRadius = 0.40f;

        /// <summary>
        /// How steeply the jump throws the car. Shallow, and for a reason that only shows up at the
        /// far end: range goes as <c>v²·sin(2θ)</c>, and a car arriving off the plunge at 5 m/s
        /// clears three times as much ground as one leaving the grid at 3. Steep enough to read as a
        /// ramp, shallow enough that a fast lap still lands on the straight it took off from.
        /// </summary>
        const float k_RampAngleDeg = 13f;

        /// <summary>The hole in the track. Everything either side of it is a landing.</summary>
        const float k_JumpGap = 0.16f;

        /// <summary>Length of the descending ramp on the far side, which catches a car that lands short.</summary>
        const float k_JumpLanding = 0.09f;

        /// <summary>Radius of the ramps that ease the straights into and out of the two big grades.</summary>
        const float k_HelixEaseRadius = 1.20f;

        const float k_PlungeEaseRadius = 0.90f;

        const float k_HelixBankDeg = 38f;
        const float k_PlungeBankDeg = 55f;

        // ---------------------------------------------------------------- section

        const float k_HalfWidth = 0.10f;
        const float k_RailInner = 0.082f;
        const float k_BedTop = 0.004f;
        const float k_RailTop = 0.030f;
        const float k_Underside = -0.014f;

        /// <summary>Half the distance between the lanes.</summary>
        const float k_LaneOffset = 0.045f;

        /// <summary>How often the extruded ribbon puts down a ring, in metres.</summary>
        const float k_MeshStride = 0.035f;

        // ---------------------------------------------------------------- cars

        const int k_Laps = 5;
        const float k_CarLength = 0.115f;

        static readonly string[] k_CarNames = { "WAND", "PAD" };

        static readonly Color[] k_CarColors =
        {
            new(0.95f, 0.27f, 0.20f),
            new(0.25f, 0.62f, 0.96f),
        };

        // Only used when a wall is missing from the room — the CAVE as this project ships it.
        const float k_FallbackLeftX = -1.778f;
        const float k_FallbackRightX = 1.778f;
        const float k_FallbackFrontZ = 1.1113f;
        const float k_FallbackBackZ = -1.1113f;

        // ================================================================== menus

        [MenuItem(k_CreateMenu, false, 400)]
        static void CreateScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(k_TemplateScene) == null)
            {
                Debug.LogError($"[HotWheels] Missing the template scene '{k_TemplateScene}'.");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath) != null &&
                !EditorUtility.DisplayDialog("Rebuild the Hot Wheels scene?",
                    $"'{k_ScenePath}' already exists and will be overwritten with a fresh copy of the " +
                    "physics scene plus a newly built track.\n\nAnything you added to it by hand is lost.",
                    "Overwrite", "Cancel"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.OpenScene(k_TemplateScene, OpenSceneMode.Single);

            // Saved to the new path before anything is touched, so the template on disk is never at
            // risk from a build that goes wrong half way through.
            if (!EditorSceneManager.SaveScene(scene, k_ScenePath))
            {
                Debug.LogError($"[HotWheels] Could not save the new scene to '{k_ScenePath}'.");
                return;
            }

            scene = SceneManager.GetActiveScene();
            StripTemplate(scene);

            if (!Build(scene))
                return;

            EditorSceneManager.SaveScene(scene);
            RegisterInBuildSettings();

            Debug.Log($"[HotWheels] Built '{scene.name}'. Enter play mode; the wand trigger drives the " +
                      "red car and an Xbox trigger drives the blue one.");
        }

        [MenuItem(k_BuildMenu, false, 410)]
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
                Debug.LogError($"[HotWheels] No '{k_RoomRoot}' root in '{scene.name}'. Open the Hot " +
                               "Wheels scene (or any scene with the CAVE room) and try again.");
                return false;
            }

            Undo.SetCurrentGroupName("Build Hot Wheels Track");
            int group = Undo.GetCurrentGroup();

            s_Materials.Clear();

            var walls = ReadWalls(room);
            var plan = new Plan(walls);

            if (!CheckRoom(plan, walls, room))
                return false;

            var root = FindOrCreate(room, k_TrackRoot);
            Clear(root);

            var track = root.GetComponent<SlotCarTrack>();
            if (track == null)
                track = Undo.AddComponent<SlotCarTrack>(root.gameObject);

            track.Define(plan.Start, Vector3.right, Author(plan), k_LaneOffset);

            BuildRibbon(root, track);
            BuildSupports(root, track, plan, walls);
            BuildStartLine(root, track);

            var cars = BuildCars(root, track);
            var sign = BuildSign(root, walls, out var carText);

            Wire(root, cars, sign, carText);

            // Left in the built scene rather than added when something breaks. It is silent unless
            // a control moves, and the startup report is worth having in the log of a demo that went
            // fine as much as one that did not.
            var sitrep = NewChild(root, "Controller_SitRep");
            Undo.AddComponent<ControllerSitRep>(sitrep.gameObject);

            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = root.gameObject;

            Debug.Log($"[HotWheels] {track.Length:0.##} m lap. Spiral climbs {plan.Climb:0.##} m at " +
                      $"{plan.HelixGradeDeg:0.#}°, plunge drops it at {plan.PlungeGradeDeg:0.#}°. " +
                      $"Loop tops out at {k_HighHeight + 2f * k_LoopRadius:0.##} m.", root);
            return true;
        }

        // ---------------------------------------------------------------- planning

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

        /// <summary>
        /// Every number the circuit is made of, worked out from the room once so that no two places
        /// can disagree about where a straight ends.
        /// </summary>
        readonly struct Plan
        {
            /// <summary>Outermost the centre line ever gets, on either side.</summary>
            public readonly float ReachX;

            /// <summary>Where the two half-turns are centred, along X.</summary>
            public readonly float TurnX;

            public readonly float BackZ, FrontZ;

            /// <summary>Length of each straight — the two turn centres, end to end.</summary>
            public readonly float Straight;

            public readonly float Climb;
            public readonly float HelixGradeDeg, PlungeGradeDeg;

            /// <summary>Plan length eaten by each easing ramp, and the height it gains.</summary>
            public readonly float HelixEase, HelixEaseRise, PlungeEase, PlungeEaseRise;

            /// <summary>Plan length of the whole jump, ramps included.</summary>
            public readonly float Jump;

            /// <summary>
            /// Length of the climbing ramp before the lip. Longer than the descending one on the far
            /// side by exactly <c>R·tan(θ/2)</c>, which is what makes the jump cost no net height:
            /// one arc lifts the car and two put it back down, so the climb has to pay for the extra
            /// arc or the back straight would come out of its own jump lower than it went in.
            /// </summary>
            public readonly float JumpUp;

            /// <summary>Run-up from the grid to the take-off ramp.</summary>
            public readonly float Grid;

            /// <summary>What is left of the front straight once both easing ramps have had their share.</summary>
            public readonly float FrontFree;

            public Plan(Walls w)
            {
                ReachX = Mathf.Min(Mathf.Abs(w.LeftX), Mathf.Abs(w.RightX)) - k_SideMargin;
                TurnX = ReachX - k_TurnRadius;
                FrontZ = w.FrontZ - k_FrontMargin;
                BackZ = FrontZ - 2f * k_TurnRadius;
                Straight = 2f * TurnX;
                Climb = k_HighHeight - k_GroundHeight;

                // The spiral is a turn and a half, so its plan length is three half-circles.
                HelixGradeDeg = SolveGrade(k_HelixEaseRadius, 3f * Mathf.PI * k_TurnRadius, Climb);
                PlungeGradeDeg = SolveGrade(k_PlungeEaseRadius, Mathf.PI * k_TurnRadius, Climb);

                float helix = HelixGradeDeg * Mathf.Deg2Rad;
                float plunge = PlungeGradeDeg * Mathf.Deg2Rad;

                HelixEase = k_HelixEaseRadius * Mathf.Sin(helix);
                HelixEaseRise = k_HelixEaseRadius * (1f - Mathf.Cos(helix));
                PlungeEase = k_PlungeEaseRadius * Mathf.Sin(plunge);
                PlungeEaseRise = k_PlungeEaseRadius * (1f - Mathf.Cos(plunge));

                float ramp = k_RampAngleDeg * Mathf.Deg2Rad;
                JumpUp = k_JumpLanding + k_RampRadius * Mathf.Tan(ramp * 0.5f);
                Jump = 3f * k_RampRadius * Mathf.Sin(ramp)
                       + (JumpUp + k_JumpLanding) * Mathf.Cos(ramp)
                       + k_JumpGap;

                Grid = Straight - PlungeEase - Jump - HelixEase;
                FrontFree = Straight - HelixEase - PlungeEase;
            }

            /// <summary>The grid, which is where the lap is measured from.</summary>
            public Vector3 Start => new(-TurnX + PlungeEase, k_GroundHeight, BackZ);

            /// <summary>Centre of the spiral tower, on the floor.</summary>
            public Vector3 SpiralAxis => new(TurnX, 0f, (BackZ + FrontZ) * 0.5f);

            /// <summary>Centre of the plunge, on the floor.</summary>
            public Vector3 PlungeAxis => new(-TurnX, 0f, (BackZ + FrontZ) * 0.5f);
        }

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

            Debug.LogWarning($"[HotWheels] '{k_RoomRoot}' has no child called '{child}'. Using the stock " +
                             $"CAVE dimension ({fallback:0.###} m) for that side instead.", room);
            return fallback;
        }

        /// <summary>
        /// Refuses to build a circuit the room cannot show. The back wall is the one that matters:
        /// there is no screen behind the viewer, so track that strays back there is not merely tight,
        /// it is invisible. The others are checked too, but those only cost clearance.
        /// </summary>
        static bool CheckRoom(in Plan plan, in Walls walls, Transform room)
        {
            float rearMost = plan.BackZ - k_HalfWidth;
            if (rearMost < walls.BackZ)
            {
                Debug.LogError($"[HotWheels] The circuit would reach {rearMost:0.###} m, past the back " +
                               $"wall at {walls.BackZ:0.###} m — and there is no screen behind the " +
                               "viewer, so that part would be invisible. Widen the room or drop " +
                               "k_TurnRadius.", room);
                return false;
            }

            if (plan.Grid < 0.3f)
            {
                Debug.LogError($"[HotWheels] Only {plan.Grid:0.###} m of run-up from the grid to the " +
                               "take-off ramp — the cars could not clear the jump from a standing " +
                               "start. Shorten the jump or widen the room.", room);
                return false;
            }

            float needed = k_BarrelRollLength + 2f * k_LoopRadius + 0.24f;
            if (plan.FrontFree < needed)
            {
                Debug.LogError($"[HotWheels] The front straight has {plan.FrontFree:0.###} m for a " +
                               $"barrel roll and a loop that need {needed:0.###} m. Drop k_LoopRadius " +
                               "or k_BarrelRollLength.", room);
                return false;
            }

            float ceiling = k_HighHeight + 2f * k_LoopRadius + k_RailTop;
            float roomTop = Mathf.Abs(walls.FrontZ - walls.BackZ) > 0f ? RoomHeight(room) : 2.2225f;
            if (ceiling > roomTop)
                Debug.LogWarning($"[HotWheels] The loop tops out at {ceiling:0.##} m in a room " +
                                 $"{roomTop:0.##} m tall.", room);

            return true;
        }

        static float RoomHeight(Transform room)
        {
            var wall = room.Find("Front_Wall");
            return wall != null ? wall.localScale.y : 2.2225f;
        }

        /// <summary>
        /// Finds the climb angle at which an easing ramp, a helix and a second easing ramp add up to
        /// exactly <paramref name="rise"/>. Bisection rather than algebra because the ramps
        /// contribute <c>R(1 − cos θ)</c> and the helix <c>L·tan θ</c>, which does not rearrange into
        /// anything worth reading. Forty halvings settle it to well under a thousandth of a degree.
        /// </summary>
        static float SolveGrade(float easeRadius, float planLength, float rise)
        {
            float lo = 0f;
            float hi = 70f * Mathf.Deg2Rad;

            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (2f * easeRadius * (1f - Mathf.Cos(mid)) + planLength * Mathf.Tan(mid) < rise)
                    lo = mid;
                else
                    hi = mid;
            }

            return (lo + hi) * 0.5f * Mathf.Rad2Deg;
        }

        // ---------------------------------------------------------------- the circuit

        /// <summary>
        /// The lap, in order, starting on the grid. Read it as a route: floor, jump, up the spiral,
        /// along the front wall through the roll and the loop, then down the plunge to the grid.
        /// </summary>
        static TrackSegment[] Author(in Plan plan)
        {
            var segments = new List<TrackSegment>(24);

            float ramp = k_RampAngleDeg;

            // --- back straight, on the floor: the grid, then the jump.
            //
            // The gap is flat, and that is the whole design. It used to carry on climbing at the
            // take-off angle, which put the far lip six centimetres above the point the car left
            // from — and a projectile launched at angle θ is always *below* the straight line at
            // angle θ from the same point, so the car could never meet it. It flew into the
            // underside of the landing ramp at every speed. Flat, the far lip sits at exactly lip
            // height and the ramp falls away from there, so anything that crosses the hole lands.
            segments.Add(Straight("Grid_Straight", plan.Grid, 0f));
            segments.Add(Pitch("Jump_Takeoff", k_RampRadius, ramp));
            segments.Add(Straight("Jump_Ramp", plan.JumpUp, ramp));
            segments.Add(Gap("Jump_Gap", k_JumpGap, 0f));
            segments.Add(Pitch("Jump_Landing", k_RampRadius, -ramp));
            segments.Add(Straight("Jump_Runout", k_JumpLanding, -ramp));
            segments.Add(Pitch("Jump_Recover", k_RampRadius, ramp));

            // --- right-hand wall: a turn and a half, climbing.
            segments.Add(Pitch("Spiral_Ramp_In", k_HelixEaseRadius, plan.HelixGradeDeg));
            Banked(segments, "Spiral", -540f, plan.HelixGradeDeg, k_HelixBankDeg, 90f);
            segments.Add(Pitch("Spiral_Ramp_Out", k_HelixEaseRadius, -plan.HelixGradeDeg));

            // --- front straight, overhead: barrel roll, then the loop.
            float lead = 0.14f;
            float afterRoll = plan.FrontFree - lead - k_BarrelRollLength;

            // The loop reaches k_LoopRadius back from where the car enters it, so it has to start at
            // least that far past the end of the roll, and finish that far short of the plunge.
            float toLoop = Mathf.Max(k_LoopRadius + 0.04f, (afterRoll - k_LoopRadius) * 0.5f);

            segments.Add(Straight("Front_Lead", lead, 0f));
            segments.Add(new TrackSegment
            {
                Name = "Barrel_Roll",
                Length = k_BarrelRollLength,
                BankInDeg = 0f,
                BankOutDeg = 360f,
            });
            segments.Add(Straight("Front_Mid", toLoop, 0f));
            segments.Add(Pitch("Loop_Mouth_In", k_LoopRadius, k_LoopMouthDeg, hidden: true));
            segments.Add(Pitch("Loop", k_LoopRadius, 360f - 2f * k_LoopMouthDeg));
            segments.Add(Pitch("Loop_Mouth_Out", k_LoopRadius, k_LoopMouthDeg, hidden: true));
            segments.Add(Straight("Front_Out", afterRoll - toLoop, 0f));

            // --- left-hand wall: the drop back to the floor.
            segments.Add(Pitch("Plunge_Ramp_In", k_PlungeEaseRadius, -plan.PlungeGradeDeg));
            Banked(segments, "Plunge", -180f, -plan.PlungeGradeDeg, k_PlungeBankDeg, 45f);
            segments.Add(Pitch("Plunge_Ramp_Out", k_PlungeEaseRadius, plan.PlungeGradeDeg));

            return segments.ToArray();
        }

        /// <summary>
        /// A banked turn, laid down as three pieces so the lean builds through the entry, holds
        /// through the middle and lets go through the exit.
        /// </summary>
        /// <remarks>
        /// Splitting it is not cosmetic. Bank only earns its keep where there is a corner to lean
        /// into: it tilts the surface, and a tilted surface turns any load — including the purely
        /// vertical one of pulling out of a dive — partly sideways onto the slot pin. Carrying 55°
        /// of lean into the straight ramp at the foot of the plunge made that pull-out the heaviest
        /// thing in the lap, which is both wrong and the opposite of what banking is for. The three
        /// pieces integrate exactly as the single one did, because the sweeps and lengths still sum
        /// to the same totals, so nothing about closure changes.
        /// </remarks>
        static void Banked(List<TrackSegment> into, string name, float sweepDeg, float gradeDeg,
            float bankDeg, float easeDeg)
        {
            float radius = k_TurnRadius / Mathf.Cos(gradeDeg * Mathf.Deg2Rad);
            float ease = Mathf.Min(easeDeg, Mathf.Abs(sweepDeg) * 0.25f) * Mathf.Sign(sweepDeg);
            float hold = sweepDeg - 2f * ease;

            Turn($"{name}_In", ease, radius, gradeDeg, 0f, bankDeg);
            Turn($"{name}", hold, radius, gradeDeg, bankDeg, bankDeg);
            Turn($"{name}_Out", ease, radius, gradeDeg, bankDeg, 0f);

            void Turn(string piece, float sweep, float r, float grade, float bankIn, float bankOut) =>
                into.Add(new TrackSegment
                {
                    Name = piece,
                    Length = Mathf.Abs(sweep) * Mathf.Deg2Rad * r,
                    TurnDeg = sweep,
                    GradeDeg = grade,
                    BankInDeg = bankIn,
                    BankOutDeg = bankOut,
                });
        }

        static TrackSegment Straight(string name, float length, float gradeDeg, float bank = 0f) =>
            new() { Name = name, Length = Mathf.Max(0.01f, length), GradeDeg = gradeDeg, BankInDeg = bank, BankOutDeg = bank };

        static TrackSegment Gap(string name, float length, float gradeDeg) =>
            new() { Name = name, Length = length, GradeDeg = gradeDeg, Gap = true };

        /// <summary>
        /// A piece that sweeps pitch — a jump ramp, an easing ramp, or the loop. The length follows
        /// from the radius and the sweep, which is the only way the two can never disagree.
        /// </summary>
        static TrackSegment Pitch(string name, float radius, float sweepDeg, float bankIn = 0f,
            float bankOut = 0f, bool hidden = false) =>
            new()
            {
                Name = name,
                Length = radius * Mathf.Abs(sweepDeg) * Mathf.Deg2Rad,
                LoopDeg = sweepDeg,
                BankInDeg = bankIn,
                BankOutDeg = bankOut,
                Hidden = hidden,
            };

        // ---------------------------------------------------------------- ribbon

        /// <summary>
        /// The track's cross section, as a closed outline in the frame's own plane: X across, Y up
        /// from the running surface. Walked in this order the outward faces come out facing outward,
        /// so nothing has to be flipped afterwards.
        /// </summary>
        static readonly Vector2[] k_Profile =
        {
            new(-k_HalfWidth, k_Underside),
            new(-k_HalfWidth, k_RailTop),
            new(-k_RailInner, k_RailTop),
            new(-k_RailInner, k_BedTop),
            new(k_RailInner, k_BedTop),
            new(k_RailInner, k_RailTop),
            new(k_HalfWidth, k_RailTop),
            new(k_HalfWidth, k_Underside),
        };

        /// <summary>Which profile edge is the running surface, and therefore gets the darker material.</summary>
        const int k_BedEdge = 3;

        /// <summary>
        /// Extrudes the section along the baked centre line. Rings are only joined to the ring before
        /// them when both carry track, which is what opens the jump and the mouth of the loop without
        /// the mesh needing to know what either of those is.
        /// </summary>
        static void BuildRibbon(Transform root, SlotCarTrack track)
        {
            var frames = track.Frames;
            if (frames.Count < 2)
                return;

            var vertices = new List<Vector3>(8192);
            var uvs = new List<Vector2>(8192);
            var body = new List<int>(16384);
            var bed = new List<int>(4096);

            int edges = k_Profile.Length;
            int[] previous = null;
            float step = track.Length / (frames.Count - 1);
            int stride = Mathf.Max(1, Mathf.RoundToInt(k_MeshStride / Mathf.Max(1e-4f, step)));

            for (int i = 0; i < frames.Count; i += stride)
            {
                // The last frame is where the lap meets itself, so it has to be laid down whether or
                // not the stride happens to land on it — otherwise the circuit is drawn with a
                // stride-wide notch missing at the start line, of all places.
                if (i + stride >= frames.Count)
                    i = frames.Count - 1;

                var frame = frames[i];
                if (frame.Bare)
                {
                    previous = null;
                    continue;
                }

                // Two vertices per profile edge rather than one per profile point, so the rails keep
                // their creases instead of being smoothed into a gutter by shared normals.
                var ring = new int[edges * 2];
                for (int e = 0; e < edges; e++)
                {
                    var a = k_Profile[e];
                    var b = k_Profile[(e + 1) % edges];

                    ring[e * 2] = vertices.Count;
                    vertices.Add(Section(frame, a));
                    uvs.Add(new Vector2(0f, frame.Distance));

                    ring[e * 2 + 1] = vertices.Count;
                    vertices.Add(Section(frame, b));
                    uvs.Add(new Vector2(1f, frame.Distance));
                }

                if (previous != null)
                {
                    for (int e = 0; e < edges; e++)
                    {
                        var into = e == k_BedEdge ? bed : body;
                        int a0 = previous[e * 2], b0 = previous[e * 2 + 1];
                        int a1 = ring[e * 2], b1 = ring[e * 2 + 1];

                        into.Add(a0); into.Add(a1); into.Add(b1);
                        into.Add(a0); into.Add(b1); into.Add(b0);
                    }
                }

                previous = ring;
            }

            var mesh = new Mesh
            {
                name = "HotWheelsTrack",
                indexFormat = vertices.Count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
            };

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(body, 0);
            mesh.SetTriangles(bed, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Ribbon", typeof(MeshFilter), typeof(MeshRenderer));
            Undo.RegisterCreatedObjectUndo(go, "Create track ribbon");
            go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = Save(mesh);
            go.GetComponent<MeshRenderer>().sharedMaterials = new[]
            {
                Material(k_BodyMaterialPath, new Color(1f, 0.42f, 0.05f), 0.35f),
                Material(k_BedMaterialPath, new Color(0.16f, 0.17f, 0.20f), 0.15f),
            };
        }

        static Vector3 Section(in TrackFrame frame, Vector2 point) =>
            frame.Position + frame.Right * point.x + frame.Up * point.y;

        /// <summary>
        /// Writes the ribbon to disk and hands back <em>the asset</em>, which on a rebuild is not the
        /// mesh that was passed in. Getting that the wrong way round leaves the renderer pointing at
        /// a loose Mesh that was never saved — it survives the session, gets baked into the scene
        /// file, and comes back as a second copy of the track that no later rebuild can reach.
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

            // Rewritten in place so every scene and prefab already pointing at this mesh follows the
            // rebuild instead of being left holding the old shape.
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            return existing;
        }

        // ---------------------------------------------------------------- supports

        /// <summary>
        /// Stands the track up. The two towers are columns on the axis each turn is centred on —
        /// which is the one line inside a spiral that no part of the spiral passes through — with
        /// arms out to the track. The front straight gets plain posts, and the outermost point of
        /// each turn gets a bracket to the wall it is nearly touching, which is what makes the
        /// circuit read as bolted into the room rather than floating in it.
        /// </summary>
        static void BuildSupports(Transform root, SlotCarTrack track, in Plan plan, in Walls walls)
        {
            var supports = NewChild(root, "Supports");
            var frames = track.Frames;

            Column(supports, "Spiral_Column", plan.SpiralAxis, k_HighHeight);
            Column(supports, "Plunge_Column", plan.PlungeAxis, k_HighHeight);

            float step = track.Length / Mathf.Max(1, frames.Count - 1);

            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                if (frame.Bare)
                    continue;

                float along = i * step;
                var under = frame.Position - frame.Up * 0.02f;

                // Arms out to the towers, but only from track that is actually going round one of
                // them. Curvature is the test rather than position, because a point can be out at
                // the tower's radius while belonging to the straight that passes it — and an arm
                // struck from there would be a chord across the spiral rather than a spoke of it.
                if (under.y > 0.25f && Mathf.Abs(frame.LateralCurvature) > 0.5f && Regular(along, 0.42f, step))
                {
                    var axis = under.x > 0f ? plan.SpiralAxis : plan.PlungeAxis;
                    Strut(supports, $"Arm_{i:0000}", new Vector3(axis.x, under.y, axis.z), under, 0.022f);
                }

                // Wall brackets where the track all but touches a side wall.
                if (Mathf.Abs(under.x) > plan.ReachX - 0.03f && Regular(along, 0.9f, step))
                {
                    float wallX = under.x > 0f ? walls.RightX : walls.LeftX;
                    Strut(supports, $"Bracket_{i:0000}", new Vector3(wallX, under.y, under.z), under, 0.026f);
                }

                // Posts under the high front straight, in the middle of the room where neither
                // turn's circle can reach them. Level track only, in both senses: the height window
                // keeps posts off the loop climbing away above it, and the surface-normal test keeps
                // them off the barrel roll, where a post would appear to hold the track up by a
                // point that is at that moment upside down.
                if (Mathf.Abs(under.y - k_HighHeight) < 0.05f && frame.Up.y > 0.9f &&
                    under.z > plan.FrontZ - 0.05f &&
                    Mathf.Abs(under.x) < plan.TurnX - k_TurnRadius - 0.05f && Regular(along, 0.5f, step))
                {
                    Strut(supports, $"Post_{i:0000}", new Vector3(under.x, 0f, under.z), under, 0.026f);
                }
            }
        }

        /// <summary>True once every <paramref name="spacing"/> metres, whatever the sample step is.</summary>
        static bool Regular(float along, float spacing, float step) =>
            along % spacing < step;

        static void Column(Transform parent, string name, Vector3 baseAt, float height)
        {
            Box(parent, name, new Vector3(baseAt.x, height * 0.5f, baseAt.z), Quaternion.identity,
                new Vector3(0.05f, height, 0.05f), k_SupportMaterialPath);
        }

        static void Strut(Transform parent, string name, Vector3 from, Vector3 to, float thickness)
        {
            var delta = to - from;
            float length = delta.magnitude;
            if (length < 0.02f)
                return;

            var along = delta / length;

            // A post straight down is exactly the case LookRotation cannot resolve against a world-up
            // hint, and it is also the commonest strut here — every post under the high straight is
            // one. Hand it a hint that cannot be parallel to the run instead of finding out at
            // build time that the whole support frame came out unrotated.
            var hint = Mathf.Abs(along.y) > 0.99f ? Vector3.forward : Vector3.up;

            Box(parent, name, (from + to) * 0.5f, Quaternion.LookRotation(along, hint),
                new Vector3(thickness, thickness, length), k_SupportMaterialPath);
        }

        // ---------------------------------------------------------------- start line

        static void BuildStartLine(Transform root, SlotCarTrack track)
        {
            var frame = track.Sample(0f);
            Box(root, "Start_Line", frame.Position + frame.Up * (k_BedTop + 0.001f), frame.Rotation,
                new Vector3(k_HalfWidth * 2f, 0.002f, 0.03f), k_SupportMaterialPath);
        }

        // ---------------------------------------------------------------- cars

        /// <summary>
        /// Two cars, parented straight onto the track object. That parentage is not incidental:
        /// <see cref="SlotCar"/> works in the track's own space throughout — including while a car is
        /// in mid-air over the jump — so putting them under an intermediate transform would quietly
        /// offset every flight.
        /// </summary>
        static List<SlotCar> BuildCars(Transform root, SlotCarTrack track)
        {
            var cars = new List<SlotCar>(2);

            for (int i = 0; i < k_CarNames.Length; i++)
            {
                var car = NewChild(root, $"Car_{i:00}_{k_CarNames[i]}");
                var paint = Material(string.Format(k_CarMaterialPath, i + 1), k_CarColors[i], 0.75f);
                var tyre = Material(k_TyreMaterialPath, new Color(0.09f, 0.09f, 0.11f), 0.1f);

                Shape(car, "Body", PrimitiveType.Cube, new Vector3(0f, 0.014f, 0f), Quaternion.identity,
                    new Vector3(0.055f, 0.020f, k_CarLength), paint);

                Shape(car, "Cabin", PrimitiveType.Cube, new Vector3(0f, 0.030f, -0.012f), Quaternion.identity,
                    new Vector3(0.042f, 0.018f, 0.048f), paint);

                Shape(car, "Wing", PrimitiveType.Cube, new Vector3(0f, 0.036f, -0.048f), Quaternion.identity,
                    new Vector3(0.052f, 0.005f, 0.016f), paint);

                // A Unity cylinder stands on its Y axis and is two units tall, so a wheel is a
                // quarter-turn about Z and half its width in scale.
                var lie = Quaternion.Euler(0f, 0f, 90f);
                for (int w = 0; w < 4; w++)
                {
                    float x = (w & 1) == 0 ? -0.030f : 0.030f;
                    float z = w < 2 ? 0.038f : -0.038f;
                    Shape(car, $"Wheel_{w}", PrimitiveType.Cylinder, new Vector3(x, 0.014f, z), lie,
                        new Vector3(0.028f, 0.006f, 0.028f), tyre);
                }

                var component = Undo.AddComponent<SlotCar>(car.gameObject);
                var throttle = Undo.AddComponent<SlotCarThrottle>(car.gameObject);

                var so = new SerializedObject(throttle);
                SetEnum(so, "m_Device", i);
                SetInt(so, "m_Key", (int)(i == 0 ? UnityEngine.InputSystem.Key.LeftShift
                    : UnityEngine.InputSystem.Key.RightShift));
                so.ApplyModifiedProperties();

                so = new SerializedObject(component);
                SetRef(so, "m_Track", track);
                SetRef(so, "m_Throttle", throttle);
                SetInt(so, "m_Lane", i);
                SetString(so, "m_CarName", k_CarNames[i]);
                so.ApplyModifiedProperties();

                cars.Add(component);
            }

            return cars;
        }

        // ---------------------------------------------------------------- sign

        static TextMeshPro BuildSign(Transform root, in Walls walls, out List<TextMeshPro> carText)
        {
            var sign = NewChild(root, "Race_Sign");

            // Low on the front wall, under the high straight rather than behind it — a sign at loop
            // height would spend the race hidden behind the loop. Text reads correctly when its
            // forward points away from the viewer, and the viewer is on the -Z side, so the default
            // rotation is already right.
            sign.localPosition = new Vector3(0f, 0.62f, walls.FrontZ - 0.01f);

            Text(sign, "Title", new Vector3(0f, 0.17f, 0f), 0.62f, "HOT  WHEELS  SLOT  RACE",
                new Color(0.55f, 0.62f, 0.72f));

            var status = Text(sign, "Status", Vector3.zero, 1.15f, "PULL  TRIGGER  TO  GO",
                new Color(0.93f, 0.95f, 1f));

            carText = new List<TextMeshPro>(2)
            {
                Text(sign, "Car_0", new Vector3(0f, -0.17f, 0f), 0.7f, $"{k_CarNames[0]}  LAP 1/{k_Laps}",
                    k_CarColors[0]),
                Text(sign, "Car_1", new Vector3(0f, -0.29f, 0f), 0.7f, $"{k_CarNames[1]}  LAP 1/{k_Laps}",
                    k_CarColors[1]),
            };

            return status;
        }

        // ---------------------------------------------------------------- wiring

        static void Wire(Transform root, List<SlotCar> cars, TextMeshPro status, List<TextMeshPro> carText)
        {
            var race = root.GetComponent<SlotCarRace>();
            if (race == null)
                race = Undo.AddComponent<SlotCarRace>(root.gameObject);

            // Both cars on the line, not staggered. Sharing a distance costs nothing here because
            // the lanes are drawn 90 mm apart and a car is 55 mm wide, so the two never touch — and
            // a stagger is not a fair way to start a race that is timed on the centre line anyway,
            // since the car placed further up simply has less track to cover.
            //
            // Zero is also the honest place to start counting from: the line is built at distance 0,
            // so a lap is exactly a lap rather than a lap less whatever head start was handed out.
            var grid = new List<float> { 0f, 0f };

            var so = new SerializedObject(race);
            SetArray(so, "m_Cars", cars);
            SetFloats(so, "m_Grid", grid);
            SetInt(so, "m_Laps", k_Laps);
            SetRef(so, "m_StatusText", status);
            SetArray(so, "m_CarText", carText);
            so.ApplyModifiedProperties();

            // Placed now as well as at play time, so the two cars sit on their marks in the Scene
            // view instead of both stacked on the track object's origin until someone presses play.
            for (int i = 0; i < cars.Count; i++)
                cars[i].Place(grid[i]);
        }

        // ================================================================== scene template

        /// <summary>
        /// Takes the other demos back out of the copy. The tether rigs would hang through the track,
        /// the gun's projectiles would knock cars off it, and a marble run left in from an earlier
        /// experiment would share the walls this circuit needs.
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
                        child.name.StartsWith("Marble_Run"))
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

            Debug.Log($"[HotWheels] Added '{k_ScenePath}' to the build list, disabled. Tick it in " +
                      "File ▸ Build Settings to build the Hot Wheels track for the CAVE.");
        }

        // ================================================================== assets

        static readonly Dictionary<string, Material> s_Materials = new();

        /// <summary>
        /// A material the builder owns, re-authored on every build. Everything here is opaque, which
        /// is the one real difference from the marble run's palette: there the marbles spent the run
        /// behind a rail and the rails had to be see-through, whereas a slot car sits on top of its
        /// track and nothing is ever between it and the viewer.
        /// </summary>
        static Material Material(string path, Color color, float smoothness)
        {
            if (s_Materials.TryGetValue(path, out var cached) && cached != null)
                return cached;

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;

            if (created)
            {
                EnsureFolder(path);

                // Taken from a material already in the project rather than looked up by name, so the
                // track cannot end up on the wrong render pipeline's Lit shader.
                var reference = AssetDatabase.LoadAssetAtPath<Material>(k_ShaderReferencePath);
                var shader = reference != null ? reference.shader : Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    Debug.LogWarning($"[HotWheels] No shader to build '{path}' from; it will render pink.");
                    return null;
                }

                material = new Material(shader);
            }

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);

            if (created)
                AssetDatabase.CreateAsset(material, path);
            else
                EditorUtility.SetDirty(material);

            AssetDatabase.SaveAssets();
            s_Materials[path] = material;
            return material;
        }

        // ================================================================== helpers

        static Transform Box(Transform parent, string name, Vector3 localPosition, Quaternion localRotation,
            Vector3 size, string materialPath) =>
            Shape(parent, name, PrimitiveType.Cube, localPosition, localRotation, size,
                Material(materialPath, new Color(0.62f, 0.65f, 0.70f), 0.3f));

        static Transform Shape(Transform parent, string name, PrimitiveType type, Vector3 localPosition,
            Quaternion localRotation, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, "Create track piece");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = size;

            // Nothing here is ever collided with — the cars ride the centre line and a crashed one is
            // scenery for a second — so the colliders would only be something for the room's own
            // physics to trip over.
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);

            if (material != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = material;

            return go.transform;
        }

        static Transform NewChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create track object");
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

        static void SetArray<T>(SerializedObject so, string path, List<T> values) where T : Object
        {
            var property = Find(so, path);
            if (property == null)
                return;

            property.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        static void SetFloats(SerializedObject so, string path, List<float> values)
        {
            var property = Find(so, path);
            if (property == null)
                return;

            property.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
                property.GetArrayElementAtIndex(i).floatValue = values[i];
        }

        static void SetRef(SerializedObject so, string path, Object value)
        {
            var property = Find(so, path);
            if (property != null)
                property.objectReferenceValue = value;
        }

        static void SetInt(SerializedObject so, string path, int value)
        {
            var property = Find(so, path);
            if (property != null)
                property.intValue = value;
        }

        static void SetEnum(SerializedObject so, string path, int value)
        {
            var property = Find(so, path);
            if (property != null)
                property.enumValueIndex = value;
        }

        static void SetString(SerializedObject so, string path, string value)
        {
            var property = Find(so, path);
            if (property != null)
                property.stringValue = value;
        }

        static SerializedProperty Find(SerializedObject so, string path)
        {
            var property = so.FindProperty(path);
            if (property == null)
                Debug.LogWarning($"[HotWheels] {so.targetObject.GetType().Name} has no field '{path}'.");

            return property;
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
                Debug.LogWarning("[HotWheels] No TMP font asset found. Run Window ▸ TextMeshPro ▸ " +
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
