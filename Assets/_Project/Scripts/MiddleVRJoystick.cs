#if UNITY_STANDALONE_WIN || UNITY_EDITOR
using MiddleVR;
#endif
using UnityEngine;

namespace VRShootingGallery
{
    /// <summary>
    /// Thin wrapper over MiddleVR's joysticks, the twin of <see cref="MiddleVRWand"/>. A gamepad
    /// reaches Unity by one of two roads and they are mutually exclusive: either Unity's Input
    /// System claims it, or MiddleVR's DirectInput driver does. This is the second road.
    /// </summary>
    /// <remarks>
    /// Which road a given pad takes is not something a script can decide, so the components that
    /// want a gamepad read Unity first and fall through to here — see
    /// <see cref="PhysicsDemo.SlotCarThrottle"/>, and <see cref="ControllerSitRep"/> for finding out
    /// which road a pad actually took. Nothing here is reachable until MiddleVR's kernel is up, and
    /// on a non-MiddleVR platform every call is a compile-time zero.
    /// </remarks>
    public static class MiddleVRJoystick
    {
        /// <summary>How many joysticks the kernel has, or 0 before it is up.</summary>
        public static int Count
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            get
            {
                var devices = MVR.DeviceMgr;
                return devices == null ? 0 : (int)devices.GetJoysticksNb();
            }
#else
            get => 0;
#endif
        }

        /// <summary>Name the <c>.vrx</c> knows this joystick by, or empty if there is no such device.</summary>
        public static string Name(int joystick)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            var pad = Pad(joystick);
            return pad == null ? string.Empty : pad.GetName();
#else
            return string.Empty;
#endif
        }

        public static int AxisCount(int joystick)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            var pad = Pad(joystick);
            return pad == null ? 0 : (int)pad.GetAxisNb();
#else
            return 0;
#endif
        }

        public static int ButtonCount(int joystick)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            var pad = Pad(joystick);
            return pad == null ? 0 : (int)pad.GetButtonsNb();
#else
            return 0;
#endif
        }

        /// <summary>Raw axis value, however this device happens to report it.</summary>
        public static float Axis(int joystick, int axis)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            var pad = Pad(joystick);
            return pad == null || axis < 0 || axis >= pad.GetAxisNb()
                ? 0f
                : pad.GetAxisValue((uint)axis);
#else
            return 0f;
#endif
        }

        /// <summary>True while <paramref name="button"/> on joystick <paramref name="joystick"/> is held.</summary>
        public static bool Held(int joystick, int button)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            var pad = Pad(joystick);
            return pad != null && button >= 0 && button < pad.GetButtonsNb() &&
                   pad.IsButtonPressed((uint)button);
#else
            return false;
#endif
        }

        /// <summary>
        /// An axis read as a throttle: 0 when released, 1 when fully pulled.
        /// </summary>
        /// <remarks>
        /// Simply clamped, and deliberately not rescaled from -1..1. Both conventions exist —
        /// triggers that rest at -1 and triggers that rest at 0 — and mapping -1..1 onto 0..1 to
        /// support the first breaks the second badly: an axis resting at 0 would come back as
        /// <em>half throttle</em>, and the car it drives would pull away on its own and never stop.
        /// Clamping is right for a resting-at-0 trigger and merely wastes the unused half of a
        /// resting-at--1 one, which costs a little travel and cannot run away.
        /// </remarks>
        public static float Trigger(int joystick, int axis) => Mathf.Clamp01(Axis(joystick, axis));

#if UNITY_STANDALONE_WIN || UNITY_EDITOR
        static vrJoystick Pad(int joystick)
        {
            var devices = MVR.DeviceMgr;
            if (devices == null || joystick < 0 || joystick >= devices.GetJoysticksNb())
                return null;

            return devices.GetJoystick((uint)joystick);
        }
#endif
    }
}
