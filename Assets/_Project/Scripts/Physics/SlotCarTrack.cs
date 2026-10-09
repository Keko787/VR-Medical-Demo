using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRShootingGallery.PhysicsDemo
{
    /// <summary>
    /// One piece of the circuit, described by what it does to the frame carrying the track rather
    /// than by where it ends up. Everything the track needs — straights, banked turns, climbing
    /// spirals, vertical loops, barrel rolls and the ramps either side of the jump — is some
    /// combination of these four channels, which is why there is only one segment type.
    /// </summary>
    /// <remarks>
    /// <see cref="TurnDeg"/> is swept about the <em>world</em> vertical, not the frame's own up.
    /// That is the whole reason a spiral comes out as a true helix: yawing about the frame's up
    /// while it is pitched into a climb would trace a tilted circle instead of a rising one.
    ///
    /// <see cref="LoopDeg"/> is swept about the frame's <em>local right</em>, which is what carries
    /// a loop past the vertical, where a heading-and-grade description would gimbal-lock.
    ///
    /// A segment is one or the other, and the choice also decides what happens to the grade: a
    /// segment with <see cref="LoopDeg"/> set carries whatever pitch it inherits (that pitch is what
    /// it is sweeping), while a segment without one is <em>held</em> at <see cref="GradeDeg"/>.
    /// Holding rather than accumulating is what stops a long spiral from drifting off its intended
    /// climb, and it makes the ramps either side of the jump the only pieces that change the grade.
    /// </remarks>
    [Serializable]
    public struct TrackSegment
    {
        public string Name;

        [Tooltip("Arc length of the centre line through this piece, in metres — the real 3D length, " +
            "not its shadow on the floor.")]
        public float Length;

        [Tooltip("Total yaw swept about the world vertical across the piece. Negative turns toward +Z.")]
        public float TurnDeg;

        [Tooltip("Total pitch swept about the frame's own right axis — 360 for a vertical loop, a " +
            "few degrees for a jump ramp. Non-zero makes this a pitch piece, which carries its " +
            "incoming grade instead of holding one.")]
        public float LoopDeg;

        [Tooltip("Climb angle held throughout, in degrees. Ignored on pitch pieces.")]
        public float GradeDeg;

        [Tooltip("Roll of the running surface at the start and end, in degrees. The path ignores " +
            "these — only the surface leans — so a 0 to 360 sweep is a barrel roll and an equal " +
            "pair is a banked corner.")]
        public float BankInDeg;

        public float BankOutDeg;

        [Tooltip("No track is built here. The car leaves the slot at the near lip and flies.")]
        public bool Gap;

        [Tooltip("No track is built here either, but the car stays in the slot. This is what opens " +
            "the mouth of the loop, where the circle closes back onto the straight it rises out of.")]
        public bool Hidden;
    }

    /// <summary>One baked sample of the centre line.</summary>
    public readonly struct TrackFrame
    {
        /// <summary>Centre of the running surface.</summary>
        public readonly Vector3 Position;

        /// <summary>Forward is the direction of travel; up is the surface normal, including bank.</summary>
        public readonly Quaternion Rotation;

        public readonly float Distance;

        /// <summary>
        /// Signed 1/m about the surface normal. This is the cornering load a car feels, and it is
        /// taken from the yaw channel alone — the pitch channel is the loop, and a loop presses a
        /// car <em>into</em> its slot rather than sideways out of it.
        /// </summary>
        public readonly float LateralCurvature;

        public readonly bool Gap;

        /// <summary>True where no track is drawn, whether or not the car is still in the slot.</summary>
        public readonly bool Bare;

        public TrackFrame(Vector3 position, Quaternion rotation, float distance, float lateralCurvature,
            bool gap, bool hidden = false)
        {
            Position = position;
            Rotation = rotation;
            Distance = distance;
            LateralCurvature = lateralCurvature;
            Gap = gap;
            Bare = gap || hidden;
        }

        public Vector3 Forward => Rotation * Vector3.forward;
        public Vector3 Up => Rotation * Vector3.up;
        public Vector3 Right => Rotation * Vector3.right;
    }

    /// <summary>
    /// The circuit: a list of segments, and the baked centre line they integrate to.
    /// </summary>
    /// <remarks>
    /// Only the segments are serialized. The frames are rebuilt in <c>Awake</c> from the same code
    /// the editor builder walks to lay out the mesh, so the track a car drives and the track a
    /// player sees cannot drift apart — and the scene file stays a few dozen lines of description
    /// rather than a thousand baked transforms.
    /// </remarks>
    public class SlotCarTrack : MonoBehaviour
    {
        [SerializeField, Tooltip("Where the lap begins, in this object's own space.")]
        Vector3 m_Start;

        [SerializeField, Tooltip("Heading at the start of the lap, in this object's own space.")]
        Vector3 m_StartForward = Vector3.right;

        [SerializeField, Tooltip("The circuit, in order. The last piece is expected to arrive back " +
            "at the first; baking warns if it does not.")]
        TrackSegment[] m_Segments;

        [SerializeField, Tooltip("Spacing of the baked centre-line samples, in metres. Finer costs " +
            "memory and nothing else — the car interpolates between samples either way.")]
        float m_Resolution = 0.01f;

        [SerializeField, Tooltip("Half the distance between the two lanes, in metres.")]
        float m_LaneOffset = 0.05f;

        TrackFrame[] m_Frames;

        /// <summary>Length of one lap along the centre line, in metres.</summary>
        public float Length => m_Frames == null || m_Frames.Length == 0
            ? 0f
            : m_Frames[m_Frames.Length - 1].Distance;

        public IReadOnlyList<TrackFrame> Frames
        {
            get
            {
                EnsureBaked();
                return m_Frames;
            }
        }

        void Awake() => EnsureBaked();

        public void EnsureBaked()
        {
            if (m_Frames == null || m_Frames.Length == 0)
                Bake();
        }

        /// <summary>Throws the baked line away so the next read re-integrates it.</summary>
        public void Rebake()
        {
            m_Frames = null;
            EnsureBaked();
        }

        /// <summary>Used by the builder, which authors the circuit and then bakes it immediately.</summary>
        public void Define(Vector3 start, Vector3 startForward, TrackSegment[] segments, float laneOffset)
        {
            m_Start = start;
            m_StartForward = startForward;
            m_Segments = segments;
            m_LaneOffset = laneOffset;
            Rebake();
        }

        // ------------------------------------------------------------------ baking

        void Bake()
        {
            if (m_Segments == null || m_Segments.Length == 0)
            {
                m_Frames = Array.Empty<TrackFrame>();
                return;
            }

            float step = Mathf.Max(0.002f, m_Resolution);
            var frames = new List<TrackFrame>(1024);

            var position = m_Start;
            var forward = m_StartForward.sqrMagnitude < 1e-6f ? Vector3.forward : m_StartForward.normalized;
            var up = Orthogonal(forward);
            float distance = 0f;

            frames.Add(new TrackFrame(position, Banked(forward, up, m_Segments[0].BankInDeg), 0f,
                Curvature(m_Segments[0]), m_Segments[0].Gap, m_Segments[0].Hidden));

            foreach (var segment in m_Segments)
            {
                float length = Mathf.Max(1e-4f, segment.Length);
                bool pitching = Mathf.Abs(segment.LoopDeg) > 1e-4f;

                // A pitch piece carries the pitch it arrives with, because that pitch is exactly
                // what it is about to sweep. Everything else is forced to its own grade, so a
                // spiral climbs at the angle it was authored with however long it runs.
                if (!pitching)
                {
                    var heading = new Vector3(forward.x, 0f, forward.z);
                    if (heading.sqrMagnitude < 1e-8f)
                        heading = Vector3.ProjectOnPlane(up, Vector3.up);

                    heading.Normalize();
                    float grade = segment.GradeDeg * Mathf.Deg2Rad;
                    forward = (heading * Mathf.Cos(grade) + Vector3.up * Mathf.Sin(grade)).normalized;
                    up = Orthogonal(forward);
                }

                int steps = Mathf.Max(1, Mathf.RoundToInt(length / step));
                float ds = length / steps;
                float curvature = Curvature(segment);

                for (int i = 0; i < steps; i++)
                {
                    if (Mathf.Abs(segment.TurnDeg) > 1e-4f)
                    {
                        var yaw = Quaternion.AngleAxis(segment.TurnDeg * ds / length, Vector3.up);
                        forward = yaw * forward;
                        up = yaw * up;
                    }

                    if (pitching)
                    {
                        // Cross(forward, up), not Cross(up, forward). Unity turns a positive angle
                        // about the frame's own right axis into a nose-down pitch, so the axis has
                        // to be the left one for a positive LoopDeg to climb — which is what a
                        // take-off ramp and the near side of a loop both need it to mean.
                        var pitch = Quaternion.AngleAxis(segment.LoopDeg * ds / length,
                            Vector3.Cross(forward, up));
                        forward = pitch * forward;
                        up = pitch * up;
                    }

                    position += forward * ds;
                    distance += ds;

                    float bank = Mathf.Lerp(segment.BankInDeg, segment.BankOutDeg, (i + 1f) / steps);
                    frames.Add(new TrackFrame(position, Banked(forward, up, bank), distance,
                        curvature, segment.Gap, segment.Hidden));
                }
            }

            m_Frames = frames.ToArray();

            float error = Vector3.Distance(position, m_Start);
            if (error > 0.02f)
                Debug.LogWarning($"[SlotCarTrack] The lap misses its own start by {error:0.###} m. " +
                                 "Cars will jump that gap crossing the line.", this);
        }

        /// <summary>
        /// Yaw rate turned into 1/m. The sign follows the yaw, so cornering load comes out signed
        /// the same way round the whole circuit.
        /// </summary>
        static float Curvature(TrackSegment segment) =>
            Mathf.Abs(segment.Length) < 1e-4f
                ? 0f
                : segment.TurnDeg * Mathf.Deg2Rad / segment.Length;

        /// <summary>The surface normal for a frame pointing <paramref name="forward"/>, kept upright.</summary>
        static Vector3 Orthogonal(Vector3 forward)
        {
            var right = Vector3.Cross(forward, Vector3.up);
            if (right.sqrMagnitude < 1e-8f)
                right = Vector3.Cross(forward, Vector3.forward);

            return Vector3.Cross(right, forward).normalized;
        }

        /// <summary>
        /// Leans the running surface without touching the path. Keeping the two apart is what lets a
        /// barrel roll be a straight piece whose surface happens to turn over, rather than a
        /// corkscrew the car would have to be flung around.
        /// </summary>
        static Quaternion Banked(Vector3 forward, Vector3 up, float bankDegrees) =>
            Quaternion.LookRotation(forward, up) * Quaternion.AngleAxis(bankDegrees, Vector3.forward);

        // ------------------------------------------------------------------ sampling

        /// <summary>Wraps a distance into [0, Length).</summary>
        public float Wrap(float distance)
        {
            float length = Length;
            if (length <= 0f)
                return 0f;

            distance %= length;
            return distance < 0f ? distance + length : distance;
        }

        /// <summary>The centre line at <paramref name="distance"/> metres round the lap.</summary>
        public TrackFrame Sample(float distance)
        {
            EnsureBaked();

            // One frame means an empty circuit, and the interpolation below would divide by the
            // zero-length lap it implies.
            if (m_Frames.Length < 2)
                return m_Frames.Length == 1
                    ? m_Frames[0]
                    : new TrackFrame(m_Start, Quaternion.identity, 0f, 0f, false);

            float wrapped = Wrap(distance);
            float step = Length / (m_Frames.Length - 1);
            int index = Mathf.Clamp(Mathf.FloorToInt(wrapped / step), 0, m_Frames.Length - 2);

            var a = m_Frames[index];
            var b = m_Frames[index + 1];
            float t = Mathf.InverseLerp(a.Distance, b.Distance, wrapped);

            return new TrackFrame(
                Vector3.Lerp(a.Position, b.Position, t),
                Quaternion.Slerp(a.Rotation, b.Rotation, t),
                wrapped,
                Mathf.Lerp(a.LateralCurvature, b.LateralCurvature, t),
                a.Gap, a.Bare);
        }

        /// <summary>Where a car in <paramref name="lane"/> sits, given a centre line frame.</summary>
        public Vector3 LanePosition(in TrackFrame frame, int lane, float height) =>
            frame.Position
            + frame.Right * ((lane == 0 ? -1f : 1f) * m_LaneOffset)
            + frame.Up * height;
    }
}
