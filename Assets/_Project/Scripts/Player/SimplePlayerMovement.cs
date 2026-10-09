using UnityEngine;
using UnityEngine.InputSystem;

namespace VRShootingGallery
{
    /// <summary>
    /// Drives the Player capsule in the HolographicPlayer scene from whichever control is live:
    /// the CAVE wand's joystick, the keyboard as MiddleVR sees it, or the keyboard as Unity sees it.
    /// </summary>
    /// <remarks>
    /// <b>Controls.</b> Wand joystick / WASD / arrows move along the ground; the wand button in
    /// <see cref="m_RiseButton"/> (the trigger, by default) or Space lifts the player, and gravity
    /// brings it back down on release. Movement is in world axes — forward is +Z, the front
    /// screen — whichever way the wand or the viewer happens to face, which is the natural reading
    /// for a figure you are looking down on rather than standing inside.
    ///
    /// <b>Why three input roads.</b> Unity's Input System only delivers keys while the Unity window
    /// has focus, which in the CAVE it seldom does (see <see cref="MiddleVRKeyboard"/>). All three
    /// sources are read every frame and summed, so the same scene works at a desk, in the Editor with
    /// the CAVE up, and in a built player, with nothing to configure.
    ///
    /// <b>Why velocity, not force.</b> The capsule rests on the ground at Unity's default friction
    /// (0.6), which needs ~5.9 m/s² of push before it moves at all; a force tuned just under that is
    /// indistinguishable from broken input. Steering the velocity sidesteps friction entirely and
    /// makes the top speed a number you can read straight off the Inspector.
    /// </remarks>
    [RequireComponent(typeof(Rigidbody))]
    public class SimplePlayerMovement : MonoBehaviour
    {
        [SerializeField, Tooltip("Top speed along the ground, in metres per second.")]
        float m_MoveSpeed = 1f;

        [SerializeField, Tooltip("How quickly the body reaches, and sheds, that speed, in m/s².")]
        float m_Acceleration = 10f;

        [SerializeField, Tooltip("Climb rate while the rise control is held, in m/s. Gravity brings " +
            "the body back down on release.")]
        float m_RiseSpeed = 1f;

        [SerializeField, Tooltip("MiddleVR wand button that lifts the player, the twin of Space. On " +
            "this project's .vrx the trigger is 0 — run MiddleVRWandButtonProbe if yours differs. " +
            "Negative to disable.")]
        int m_RiseButton = 0;

        Rigidbody m_Body;

        /// <summary>Planar direction for this frame, in world axes, magnitude at most 1.</summary>
        Vector3 m_Move;
        bool m_Rise;

        void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
        }

        void Update()
        {
            // MiddleVR has already applied the .vrx dead zone to the stick, so a resting wand adds
            // nothing here and the keys are not fighting a drift. Clamping rather than normalising
            // keeps the stick's partial deflections as partial speed.
            var stick = new Vector2(MiddleVRWand.HorizontalAxis, MiddleVRWand.VerticalAxis) + Keys();
            stick = Vector2.ClampMagnitude(stick, 1f);
            m_Move = new Vector3(stick.x, 0f, stick.y);

            m_Rise = KeyHeld(Key.Space) || (m_RiseButton >= 0 && MiddleVRWand.Held(m_RiseButton));
        }

        void FixedUpdate()
        {
            float step = m_Acceleration * Time.fixedDeltaTime;
            Vector3 velocity = m_Body.velocity;

            var planar = new Vector3(velocity.x, 0f, velocity.z);
            planar = Vector3.MoveTowards(planar, m_Move * m_MoveSpeed, step);

            // Only the rise control touches the vertical axis; the rest of the time it is gravity's,
            // so a released player falls rather than hanging where it was let go.
            float vertical = m_Rise ? Mathf.MoveTowards(velocity.y, m_RiseSpeed, step) : velocity.y;

            m_Body.velocity = new Vector3(planar.x, vertical, planar.z);
        }

        static Vector2 Keys()
        {
            Vector2 direction = Vector2.zero;
            if (KeyHeld(Key.W) || KeyHeld(Key.UpArrow))
                direction.y += 1f;
            if (KeyHeld(Key.S) || KeyHeld(Key.DownArrow))
                direction.y -= 1f;
            if (KeyHeld(Key.A) || KeyHeld(Key.LeftArrow))
                direction.x -= 1f;
            if (KeyHeld(Key.D) || KeyHeld(Key.RightArrow))
                direction.x += 1f;
            return direction;
        }

        /// <summary>The key on either road: Unity's Input System, or MiddleVR's DirectInput driver.</summary>
        static bool KeyHeld(Key key)
        {
            var keyboard = Keyboard.current;
            return (keyboard != null && keyboard[key].isPressed) || MiddleVRKeyboard.Held(key);
        }
    }
}
