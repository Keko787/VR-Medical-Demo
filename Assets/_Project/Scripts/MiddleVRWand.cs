#if UNITY_STANDALONE_WIN || UNITY_EDITOR
using MiddleVR;
#endif
using UnityEngine;

namespace VRShootingGallery
{
    /// <summary>
    /// Thin wrapper over MiddleVR's wand buttons so the input components don't each repeat the
    /// kernel null-guard and uint cast, and so the rising-edge semantics live in one place.
    /// Mirrors the Unity Input System reads the Quest build used: <see cref="Held"/> ≈
    /// <c>ReadValue&gt;=press</c>, <see cref="PressedThisFrame"/> ≈ <c>WasPressedThisFrame()</c>.
    /// Returns <c>false</c> before the MiddleVR kernel is up, or on non-MiddleVR platforms.
    /// </summary>
    public static class MiddleVRWand
    {
        /// <summary>
        /// True once MiddleVR's kernel is up and there is a device manager to ask. False at a desk,
        /// which is the difference between "the wand says no" and "there is no wand".
        /// </summary>
        public static bool Available
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            get => MVR.DeviceMgr != null;
#else
            get => false;
#endif
        }

        /// <summary>True while the wand button is held (MiddleVR maps the analog trigger to a button).</summary>
        public static bool Held(int button)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            var devices = MVR.DeviceMgr;
            return devices != null && devices.IsWandButtonPressed((uint)Mathf.Max(0, button));
#else
            return false;
#endif
        }

        /// <summary>
        /// The wand joystick's sideways deflection, -1 (left) to +1 (right). MiddleVR has already
        /// applied the <c>.vrx</c> dead zone, so a resting stick reads exactly 0.
        /// </summary>
        public static float HorizontalAxis
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            get
            {
                var devices = MVR.DeviceMgr;
                return devices == null ? 0f : devices.GetWandHorizontalAxisValue();
            }
#else
            get => 0f;
#endif
        }

        /// <summary>The wand joystick's forward deflection, -1 (pulled back) to +1 (pushed away).</summary>
        public static float VerticalAxis
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            get
            {
                var devices = MVR.DeviceMgr;
                return devices == null ? 0f : devices.GetWandVerticalAxisValue();
            }
#else
            get => 0f;
#endif
        }

        /// <summary>True only on the frame the wand button transitions to pressed (rising edge).</summary>
        public static bool PressedThisFrame(int button)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            var devices = MVR.DeviceMgr;
            if (devices == null)
                return false;

            uint b = (uint)Mathf.Max(0, button);
            // IsWandButtonToggled flags a state change this frame; AND-ing with Pressed isolates
            // the press (rising) edge and ignores the release edge.
            return devices.IsWandButtonToggled(b) && devices.IsWandButtonPressed(b);
#else
            return false;
#endif
        }
    }
}
