using UnityEngine;

namespace VRShootingGallery.TrackingDemo
{
    /// <summary>
    /// Where the viewer's head is. MiddleVR mirrors every node of the <c>.vrx</c> into a GameObject
    /// hierarchy under the MVRManager and writes the tracker's pose into those transforms each frame,
    /// so the head is simply the one called <c>HeadNode</c>. This finds it the way MiddleVR's own
    /// <c>MVRAttachToNode</c> and <c>MVRFollowNode</c> do — by name, retried until it exists — and
    /// falls back to the main camera where there is no MiddleVR to ask (an Android build, or Unity XR
    /// left on at a desk).
    /// </summary>
    /// <remarks>
    /// The MVRManager updates its nodes at script execution order −100, so anything reading this from
    /// a plain <c>Update</c> sees the current frame's pose. In the CAVE the pose is the ART body
    /// tracker; at a desk it is MiddleVR's head simulator, which stands a good default height and
    /// moves on the kernel's own keys — either way <see cref="IsTracked"/> is true and the demos
    /// behave the same.
    /// </remarks>
    public static class TrackedHead
    {
        /// <summary>Name MiddleVR gives the head's GameObject — the <c>Node3D</c> name in the <c>.vrx</c>.</summary>
        public const string NodeName = "HeadNode";

        // Find is a scene walk. Nothing appears between frames that would make a faster retry find
        // it sooner, so a miss is only re-checked twice a second.
        const float k_RetryInterval = 0.5f;

        static Transform s_Node;
        static float s_NextSearch;

        /// <summary>
        /// True once MiddleVR's head node has been found — the pose is tracking (or the simulator at a
        /// desk), not the fallback camera.
        /// </summary>
        public static bool IsTracked => Node != null;

        /// <summary>MiddleVR's head node, or null before the kernel has created it or where there is none.</summary>
        public static Transform Node
        {
            get
            {
                if (s_Node != null)
                    return s_Node;

                if (Time.unscaledTime < s_NextSearch)
                    return null;

                s_NextSearch = Time.unscaledTime + k_RetryInterval;

                var go = GameObject.Find(NodeName);
                s_Node = go != null ? go.transform : null;
                return s_Node;
            }
        }

        /// <summary>
        /// The transform to follow: the head node, else the main camera. Null only when the scene has
        /// neither, which no CAVE scene of this project does — MiddleVR needs a camera to clone.
        /// </summary>
        public static Transform Current
        {
            get
            {
                var node = Node;
                if (node != null)
                    return node;

                var camera = Camera.main;
                return camera != null ? camera.transform : null;
            }
        }

        /// <summary>World pose of the head this frame. False when there is nothing to read it from.</summary>
        public static bool TryGetPose(out Vector3 position, out Quaternion rotation)
        {
            var current = Current;
            if (current == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return false;
            }

            position = current.position;
            rotation = current.rotation;
            return true;
        }
    }
}
