using UnityEngine;

namespace VRShootingGallery.TrackingDemo
{
    /// <summary>
    /// Where the wand is — the twin of <see cref="TrackedHead"/> for MiddleVR's <c>HandNode</c>, the
    /// node the ART Flystick's tracker drives. The wand points along the node's +Z; the gun prefab's
    /// muzzle is set up the same way.
    /// </summary>
    /// <remarks>
    /// Unlike the head there is no camera to fall back on: without MiddleVR there is no wand, and a
    /// pointer that pretended otherwise would draw a beam out of the viewer's eyes. Callers get
    /// <c>false</c> and are expected to offer another way in — the number keys, in the ear demo.
    /// </remarks>
    public static class TrackedHand
    {
        /// <summary>Name MiddleVR gives the wand's GameObject — the <c>Node3D</c> name in the <c>.vrx</c>.</summary>
        public const string NodeName = "HandNode";

        const float k_RetryInterval = 0.5f;

        static Transform s_Node;
        static float s_NextSearch;

        /// <summary>MiddleVR's hand node, or null before the kernel has created it or where there is none.</summary>
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

        /// <summary>World position of the wand and the way it points. False when there is no wand.</summary>
        public static bool TryGetRay(out Ray ray)
        {
            var node = Node;
            if (node == null)
            {
                ray = default;
                return false;
            }

            ray = new Ray(node.position, node.forward);
            return true;
        }
    }
}
