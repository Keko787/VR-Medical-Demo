using UnityEngine;

namespace VRShootingGallery.TrackingDemo
{
    /// <summary>
    /// Keeps this object on the viewer's head. Position always follows; rotation only if asked, since
    /// most things worn on the head — a cloud of bubbles, a halo — should not roll when the viewer
    /// tilts theirs.
    /// </summary>
    /// <remarks>
    /// This runs in <c>Update</c>, not <c>LateUpdate</c>, on purpose. Particle systems sample their
    /// transform for rate-over-distance emission in <c>PreLateUpdate</c>, before any LateUpdate runs,
    /// so a follower that moved in LateUpdate would feed the emitter last frame's position and the
    /// bubbles would trail a frame behind the head. The MVRManager has already written this frame's
    /// tracker pose by then — it updates at execution order −100.
    /// </remarks>
    public class FollowTrackedHead : MonoBehaviour
    {
        [SerializeField, Tooltip("Offset from the head. In the head's own frame when Follow Rotation is on, " +
            "world space otherwise.")]
        Vector3 m_Offset = Vector3.zero;

        [SerializeField, Tooltip("Also copy the head's rotation.")]
        bool m_FollowRotation;

        [SerializeField, Tooltip("Seconds to close most of the gap to the head. Zero snaps, which is right " +
            "for anything that should read as the tracker itself.")]
        float m_SmoothTime;

        Vector3 m_Velocity;

        void Update()
        {
            if (!TrackedHead.TryGetPose(out var position, out var rotation))
                return;

            var target = position + (m_FollowRotation ? rotation * m_Offset : m_Offset);

            transform.position = m_SmoothTime > 0f
                ? Vector3.SmoothDamp(transform.position, target, ref m_Velocity, m_SmoothTime)
                : target;

            if (m_FollowRotation)
                transform.rotation = rotation;
        }
    }
}
