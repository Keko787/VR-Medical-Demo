using UnityEngine;
using UnityEngine.InputSystem;

namespace VRShootingGallery.PhysicsDemo
{
    /// <summary>Which physical control drives a car.</summary>
    public enum ThrottleDevice
    {
        /// <summary>The CAVE wand's trigger — player one, standing in the room.</summary>
        Wand,

        /// <summary>An Xbox pad's right trigger — player two, standing next to them.</summary>
        Gamepad,
    }

    /// <summary>
    /// One player's throttle, read as 0..1 so the car does not care what is squeezing it.
    /// </summary>
    /// <remarks>
    /// The wand's trigger is a button — MiddleVR's <c>.vrx</c> turns the analog pull into a digital
    /// press — so player one gets a hard on/off. The pad's trigger really is analog, so player two
    /// gets fine control. That asymmetry is left in rather than levelled out: squeezing an Xbox
    /// trigger half way is the natural thing to try, and taking it away to be fair to the wand would
    /// make the better controller feel broken. It also happens to be the older, truer slot-car
    /// arrangement, where the two hand controllers were never identical either.
    ///
    /// A pad reaches Unity by one of two roads, and which one is not up to this script: Unity's
    /// Input System claims it, or MiddleVR's DirectInput driver does. Both are read and the larger
    /// pull wins, so the same build works whichever way the machine is set up, with no
    /// configuration to get wrong.
    ///
    /// The keyboard fallback is deliberately live in built players, not just in the Editor. If a pad
    /// goes missing five minutes before a demo, the operator can still run two cars.
    /// </remarks>
    public class SlotCarThrottle : MonoBehaviour
    {
        [SerializeField, Tooltip("Which control drives this car.")]
        ThrottleDevice m_Device = ThrottleDevice.Wand;

        [SerializeField, Tooltip("Wand button that acts as the throttle. On this project's .vrx the " +
            "trigger is 0 — run MiddleVRWandButtonProbe if yours differs.")]
        int m_WandButton;

        [SerializeField, Tooltip("MiddleVR joystick to fall back to when Unity has not claimed the pad.")]
        int m_JoystickIndex;

        [SerializeField, Tooltip("MiddleVR axis carrying the pad's trigger. Negative to ignore the axis.")]
        int m_JoystickAxis = 2;

        [SerializeField, Tooltip("MiddleVR button to accept as full throttle, for a pad whose trigger " +
            "only reports as a button. Negative to ignore.")]
        int m_JoystickButton = 5;

        [SerializeField, Tooltip("Key that stands in for this player's control, at a desk or when " +
            "their controller goes missing mid-demo.")]
        Key m_Key = Key.None;

        [SerializeField, Tooltip("Below this the throttle reads as closed, so a pad whose trigger " +
            "does not quite rest at zero cannot creep the car forward on the grid.")]
        float m_DeadZone = 0.08f;

        /// <summary>How hard this player is asking, 0..1.</summary>
        public float Value
        {
            get
            {
                float pull = m_Device == ThrottleDevice.Wand ? Wand() : Pad();

                var keyboard = Keyboard.current;
                if (m_Key != Key.None && keyboard != null && keyboard[m_Key].isPressed)
                    pull = 1f;

                return pull < m_DeadZone ? 0f : Mathf.Clamp01(pull);
            }
        }

        /// <summary>What this throttle is wired to, for <see cref="ControllerSitRep"/>.</summary>
        public string Describe() => m_Device == ThrottleDevice.Wand
            ? $"wand button {m_WandButton}, key {m_Key}"
            : $"pad triggers, MVR joystick {m_JoystickIndex} axis {m_JoystickAxis} / button " +
              $"{m_JoystickButton}, key {m_Key}";

        float Wand() => MiddleVRWand.Held(m_WandButton) ? 1f : 0f;

        /// <summary>
        /// Both roads a pad can take, whichever is pulling hardest. Unity reports an untouched
        /// trigger as 0 and MiddleVR reports a missing device as 0, so an absent road contributes
        /// nothing rather than having to be detected.
        /// </summary>
        float Pad()
        {
            float pull = 0f;

            var pad = Gamepad.current;
            if (pad != null)
            {
                // Either trigger. Which one is under a given player's finger is not worth being
                // opinionated about, and taking the larger costs nothing.
                pull = Mathf.Max(pad.rightTrigger.ReadValue(), pad.leftTrigger.ReadValue());

                // The face button is not a second throttle so much as an escape hatch: a pad whose
                // triggers arrive as an axis Unity does not recognise still has a working A button.
                if (pad.buttonSouth.isPressed)
                    pull = 1f;
            }

            // A pad Windows does not report as XInput arrives as a plain HID stick instead, and
            // Gamepad.current stays null however hard its trigger is pulled. Its primary trigger is
            // still a button, which is enough to drive a car.
            var stick = Joystick.current;
            if (stick != null && stick.trigger.isPressed)
                pull = 1f;

            if (m_JoystickAxis >= 0)
                pull = Mathf.Max(pull, MiddleVRJoystick.Trigger(m_JoystickIndex, m_JoystickAxis));

            if (m_JoystickButton >= 0 && MiddleVRJoystick.Held(m_JoystickIndex, m_JoystickButton))
                pull = 1f;

            return pull;
        }
    }
}
