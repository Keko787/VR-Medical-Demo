using UnityEngine;

namespace VRShootingGallery.TrackingDemo
{
    /// <summary>
    /// Turns the viewer's position into splashes on a <see cref="RippleFloor"/>. The head is dropped
    /// straight down onto the sheet, so the rings come from under the feet: a big one when the
    /// viewer first steps over the water, a small one every stride while they walk, and a slow
    /// pulse while they stand still so the floor never quite goes quiet.
    /// </summary>
    /// <remarks>
    /// Rings are spaced by distance, not time, because that is what makes a wake. Walk and the
    /// rings fall behind you one stride apart; stop and they stop. A timer would either spray rings
    /// while standing or leave a fast walk sparse. The idle pulse is the one timed thing here, and it
    /// is there because a still viewer is the common case in a CAVE — people plant themselves and
    /// look — and a floor that only answers movement reads as broken to them.
    /// </remarks>
    [DefaultExecutionOrder(10)]   // reads the head after the MVRManager (−100) has written it
    public class HeadRippleSource : MonoBehaviour
    {
        [SerializeField, Tooltip("The water to splash. Found on this object or its children if left empty.")]
        RippleFloor m_Floor;

        [Header("Walking")]
        [SerializeField, Tooltip("Metres the viewer travels between one ring and the next.")]
        float m_StrideSpacing = 0.12f;

        [SerializeField, Tooltip("Depth of each stride's dish, metres.")]
        float m_StrideAmplitude = 0.025f;

        [SerializeField, Tooltip("Radius of each stride's dish, metres.")]
        float m_StrideRadius = 0.09f;

        [Header("Standing")]
        [SerializeField, Tooltip("Seconds between pulses while the viewer stands still.")]
        float m_IdlePeriod = 1.5f;

        [SerializeField, Tooltip("Depth of the idle pulse, metres.")]
        float m_IdleAmplitude = 0.015f;

        [SerializeField, Tooltip("Radius of the idle pulse, metres.")]
        float m_IdleRadius = 0.07f;

        [Header("Arrival")]
        [SerializeField, Tooltip("Depth of the splash when the viewer first appears over the water, or " +
            "steps back onto it, metres.")]
        float m_ArrivalAmplitude = 0.04f;

        [SerializeField, Tooltip("Radius of the arrival splash, metres.")]
        float m_ArrivalRadius = 0.14f;

        [SerializeField, Tooltip("A move longer than this in one frame is a tracking jump, not a step, and " +
            "is treated as arriving somewhere new.")]
        float m_JumpDistance = 1f;

        bool m_OverWater;
        Vector3 m_LastRing;
        float m_IdleTimer;

        void Awake()
        {
            if (m_Floor == null)
                m_Floor = GetComponentInChildren<RippleFloor>();
        }

        void Update()
        {
            if (m_Floor == null || !TrackedHead.TryGetPose(out var head, out _))
                return;

            if (!m_Floor.Contains(head))
            {
                // Off the sheet. Coming back counts as arriving again.
                m_OverWater = false;
                return;
            }

            var travel = head - m_LastRing;
            travel -= Vector3.Project(travel, m_Floor.transform.up);

            if (!m_OverWater || travel.magnitude > m_JumpDistance)
            {
                Ring(head, m_ArrivalAmplitude, m_ArrivalRadius);
                m_OverWater = true;
                return;
            }

            if (travel.magnitude >= m_StrideSpacing)
            {
                Ring(head, m_StrideAmplitude, m_StrideRadius);
                return;
            }

            m_IdleTimer += Time.deltaTime;
            if (m_IdleTimer >= m_IdlePeriod)
            {
                // The idle pulse is not a stride: it keeps the last ring where it was, so a viewer
                // swaying on the spot does not creep the stride reference along with them.
                m_Floor.Splash(head, m_IdleAmplitude, m_IdleRadius);
                m_IdleTimer = 0f;
            }
        }

        void Ring(Vector3 head, float amplitude, float radius)
        {
            m_Floor.Splash(head, amplitude, radius);
            m_LastRing = head;
            m_IdleTimer = 0f;
        }
    }
}
