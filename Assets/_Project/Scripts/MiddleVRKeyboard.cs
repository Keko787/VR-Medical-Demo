#if UNITY_STANDALONE_WIN || UNITY_EDITOR
using MiddleVR;
#endif
using UnityEngine.InputSystem;

namespace VRShootingGallery
{
    /// <summary>
    /// Thin wrapper over MiddleVR's keyboard, the twin of <see cref="MiddleVRWand"/>. Keys are named
    /// by Unity's <see cref="Key"/> enum so callers never touch MiddleVR types; the mapping to
    /// MiddleVR's <c>VRK_</c> codes lives here, behind the same platform guard as the other helpers.
    /// </summary>
    /// <remarks>
    /// The keyboard reaches a script by two roads. Unity's Input System only delivers keys while the
    /// Unity window — in the Editor, the Game view itself — has focus, and in the CAVE it rarely does:
    /// MiddleVR's always-on-top compositor sits on the mosaic and the operator's clicks land there or
    /// on the Editor's other panels. MiddleVR's DirectInput driver is the second road; it is what the
    /// kernel's own Shift+D / Shift+W shortcuts ride on, so wherever those work, so does this.
    /// Components that want a key read Unity first and OR in this, the way
    /// <see cref="SimplePlayerMovement"/> does. Returns <c>false</c> before the kernel is up, for a
    /// key with no MiddleVR equivalent, and on non-MiddleVR platforms.
    /// </remarks>
    public static class MiddleVRKeyboard
    {
        /// <summary>True while <paramref name="key"/> is held on MiddleVR's keyboard.</summary>
        public static bool Held(Key key)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            var devices = MVR.DeviceMgr;
            if (devices == null)
                return false;

            int code = Code(key);
            return code >= 0 && devices.IsKeyPressed((uint)code);
#else
            return false;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR
        /// <summary>
        /// MiddleVR's code for a Unity key, or -1 for one it has no name for. The codes are DirectInput
        /// scancodes, laid out by keyboard position rather than alphabet, so this has to be a table.
        /// </summary>
        static int Code(Key key)
        {
            switch (key)
            {
                case Key.A: return (int)MVR.VRK_A;
                case Key.B: return (int)MVR.VRK_B;
                case Key.C: return (int)MVR.VRK_C;
                case Key.D: return (int)MVR.VRK_D;
                case Key.E: return (int)MVR.VRK_E;
                case Key.F: return (int)MVR.VRK_F;
                case Key.G: return (int)MVR.VRK_G;
                case Key.H: return (int)MVR.VRK_H;
                case Key.I: return (int)MVR.VRK_I;
                case Key.J: return (int)MVR.VRK_J;
                case Key.K: return (int)MVR.VRK_K;
                case Key.L: return (int)MVR.VRK_L;
                case Key.M: return (int)MVR.VRK_M;
                case Key.N: return (int)MVR.VRK_N;
                case Key.O: return (int)MVR.VRK_O;
                case Key.P: return (int)MVR.VRK_P;
                case Key.Q: return (int)MVR.VRK_Q;
                case Key.R: return (int)MVR.VRK_R;
                case Key.S: return (int)MVR.VRK_S;
                case Key.T: return (int)MVR.VRK_T;
                case Key.U: return (int)MVR.VRK_U;
                case Key.V: return (int)MVR.VRK_V;
                case Key.W: return (int)MVR.VRK_W;
                case Key.X: return (int)MVR.VRK_X;
                case Key.Y: return (int)MVR.VRK_Y;
                case Key.Z: return (int)MVR.VRK_Z;

                case Key.Digit0: return (int)MVR.VRK_0;
                case Key.Digit1: return (int)MVR.VRK_1;
                case Key.Digit2: return (int)MVR.VRK_2;
                case Key.Digit3: return (int)MVR.VRK_3;
                case Key.Digit4: return (int)MVR.VRK_4;
                case Key.Digit5: return (int)MVR.VRK_5;
                case Key.Digit6: return (int)MVR.VRK_6;
                case Key.Digit7: return (int)MVR.VRK_7;
                case Key.Digit8: return (int)MVR.VRK_8;
                case Key.Digit9: return (int)MVR.VRK_9;

                case Key.UpArrow: return (int)MVR.VRK_UP;
                case Key.DownArrow: return (int)MVR.VRK_DOWN;
                case Key.LeftArrow: return (int)MVR.VRK_LEFT;
                case Key.RightArrow: return (int)MVR.VRK_RIGHT;

                case Key.Space: return (int)MVR.VRK_SPACE;
                case Key.Enter: return (int)MVR.VRK_RETURN;
                case Key.Tab: return (int)MVR.VRK_TAB;
                case Key.Escape: return (int)MVR.VRK_ESCAPE;
                case Key.Backspace: return (int)MVR.VRK_BACK;
                case Key.Delete: return (int)MVR.VRK_DELETE;
                case Key.Insert: return (int)MVR.VRK_INSERT;
                case Key.Home: return (int)MVR.VRK_HOME;
                case Key.End: return (int)MVR.VRK_END;
                case Key.PageUp: return (int)MVR.VRK_PRIOR;
                case Key.PageDown: return (int)MVR.VRK_NEXT;

                case Key.LeftShift: return (int)MVR.VRK_LSHIFT;
                case Key.RightShift: return (int)MVR.VRK_RSHIFT;
                case Key.LeftCtrl: return (int)MVR.VRK_LCONTROL;
                case Key.RightCtrl: return (int)MVR.VRK_RCONTROL;
                case Key.LeftAlt: return (int)MVR.VRK_LMENU;
                case Key.RightAlt: return (int)MVR.VRK_RMENU;

                case Key.F1: return (int)MVR.VRK_F1;
                case Key.F2: return (int)MVR.VRK_F2;
                case Key.F3: return (int)MVR.VRK_F3;
                case Key.F4: return (int)MVR.VRK_F4;
                case Key.F5: return (int)MVR.VRK_F5;
                case Key.F6: return (int)MVR.VRK_F6;
                case Key.F7: return (int)MVR.VRK_F7;
                case Key.F8: return (int)MVR.VRK_F8;
                case Key.F9: return (int)MVR.VRK_F9;
                case Key.F10: return (int)MVR.VRK_F10;
                case Key.F11: return (int)MVR.VRK_F11;
                case Key.F12: return (int)MVR.VRK_F12;

                default: return -1;
            }
        }
#endif
    }
}
