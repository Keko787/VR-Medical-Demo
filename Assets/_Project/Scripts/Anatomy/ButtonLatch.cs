using UnityEngine.InputSystem;

namespace VRShootingGallery.AnatomyDemo
{
    /// <summary>
    /// Rising edge of one action, from whichever road reaches it first: the wand button, the number-row
    /// key printed with the same digit on Unity's keyboard, or that key on MiddleVR's keyboard. A
    /// presenter can drive the demo from the wand or from the operator's keyboard interchangeably, and
    /// at a desk the keys stand in for a wand that does not exist.
    /// </summary>
    /// <remarks>
    /// The wand edge is latched here rather than read from MiddleVR's "toggled" flag because the wand
    /// is sampled on the cluster's cadence, not Unity's, so that flag can stay set across more than one
    /// frame — see <see cref="PhysicsDemo.MarbleRun"/>. The first sample is swallowed so a button that
    /// is already held when the scene loads does not count as a press. MiddleVR's keyboard only reports
    /// held, so it is latched the same way.
    /// </remarks>
    sealed class ButtonLatch
    {
        readonly int m_Button;
        readonly Key m_Key;
        bool m_WandWasHeld;
        bool m_KeyWasHeld;
        bool m_Primed;

        /// <param name="button">Wand button, or −1 for a keyboard-only action.</param>
        /// <param name="key">Key for it; defaults to the number-row key printed with the button's digit.</param>
        public ButtonLatch(int button, Key key = Key.None)
        {
            m_Button = button;
            m_Key = key != Key.None ? key : button is >= 0 and <= 9 ? NumberRowKey(button) : Key.None;
        }

        /// <summary>True on the one frame the action is pressed. Call once per frame.</summary>
        public bool Pressed()
        {
            if (m_Button < 0 && m_Key == Key.None)
                return false;

            bool wand = m_Button >= 0 && MiddleVRWand.Held(m_Button);
            bool key = m_Key != Key.None && MiddleVRKeyboard.Held(m_Key);

            bool pressed = m_Primed && ((wand && !m_WandWasHeld) || (key && !m_KeyWasHeld));
            m_WandWasHeld = wand;
            m_KeyWasHeld = key;
            m_Primed = true;

            var keyboard = Keyboard.current;
            if (m_Key != Key.None && keyboard != null && keyboard[m_Key].wasPressedThisFrame)
                pressed = true;

            return pressed;
        }

        /// <summary>
        /// The number-row key printed with <paramref name="digit"/>. Not arithmetic on the enum: the row
        /// runs 1..9 and then 0, so <c>Digit1 + 9</c> would not be the zero key.
        /// </summary>
        static Key NumberRowKey(int digit) => digit == 0 ? Key.Digit0 : Key.Digit1 + (digit - 1);

        /// <summary>True while <paramref name="key"/> is held on either keyboard.</summary>
        public static bool Held(Key key)
        {
            var keyboard = Keyboard.current;
            return (keyboard != null && keyboard[key].isPressed) || MiddleVRKeyboard.Held(key);
        }
    }
}
