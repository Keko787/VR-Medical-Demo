using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using VRShootingGallery.PhysicsDemo;

namespace VRShootingGallery
{
    /// <summary>
    /// Controller sit rep: what is plugged in, which road it arrived by, and what each car's
    /// throttle is actually reading. Logs a full report at startup and whenever you ask for one,
    /// then keeps a running log of anything that moves.
    /// </summary>
    /// <remarks>
    /// A gamepad can reach this project two ways and they are mutually exclusive — Unity's Input
    /// System claims it, or MiddleVR's DirectInput driver does — so "the pad does not work" has at
    /// least four distinct causes that look identical from the outside: no device at all, a device
    /// Unity sees but does not recognise as a gamepad, a device MiddleVR has taken, or a device
    /// everyone can see that is wired to the wrong axis. This tells them apart in one screen of log.
    ///
    /// The live change log is the half that finds indices. Pull a trigger and the line that appears
    /// names the control and the number to put in the Inspector, the same way
    /// <see cref="MiddleVRWandButtonProbe"/> does for the wand — which is where the trick comes
    /// from. Unlike that probe this one is meant to stay in the scene: it is silent unless something
    /// moves, and the startup report is worth having in a demo log whether or not anything is wrong.
    /// </remarks>
    public class ControllerSitRep : MonoBehaviour
    {
        [SerializeField, Tooltip("Key that prints a fresh report.")]
        Key m_ReportKey = Key.F1;

        [SerializeField, Tooltip("Wand button that also prints one, for when there is no keyboard " +
            "in the CAVE. Negative disables it.")]
        int m_ReportWandButton = -1;

        [SerializeField, Tooltip("Log controls as they move, which is how you discover which axis " +
            "or button index a pad's trigger really is.")]
        bool m_LogChanges = true;

        [SerializeField, Tooltip("How far an axis must move before it is worth a line, so a noisy " +
            "stick resting against its own deadzone does not fill the console.")]
        float m_AxisChange = 0.15f;

        [SerializeField, Tooltip("How many MiddleVR joystick axes and buttons to scan.")]
        int m_ScanAxes = 8;

        [SerializeField, Tooltip("Highest MiddleVR joystick button index to scan.")]
        int m_ScanButtons = 15;

        [SerializeField, Tooltip("Highest wand button index to scan.")]
        int m_ScanWandButtons = 15;

        const string k_Tag = "[SitRep]";

        SlotCar[] m_Cars;

        float[] m_LastAxis;
        bool[] m_LastButton;
        bool[] m_LastWand;
        float m_LastLeftTrigger, m_LastRightTrigger;
        int m_LastDeviceCount = -1;
        bool m_WandWasAvailable;
        bool m_ReportHeld, m_ReportPrimed;

        void Start()
        {
            m_Cars = FindObjectsOfType<SlotCar>();
            m_LastAxis = new float[Mathf.Max(1, m_ScanAxes)];
            m_LastButton = new bool[Mathf.Max(1, m_ScanButtons + 1)];
            m_LastWand = new bool[Mathf.Max(1, m_ScanWandButtons + 1)];

            Report("startup");
        }

        void Update()
        {
            if (ReportRequested())
                Report("requested");

            // The kernel comes up some frames after the scene does, and a pad can be plugged in
            // half way through a demo. Either is worth a fresh report without being asked.
            if (MiddleVRWand.Available != m_WandWasAvailable)
            {
                m_WandWasAvailable = MiddleVRWand.Available;
                Report(m_WandWasAvailable ? "MiddleVR kernel came up" : "MiddleVR kernel went away");
            }

            if (InputSystem.devices.Count != m_LastDeviceCount)
            {
                m_LastDeviceCount = InputSystem.devices.Count;
                Report("device list changed");
            }

            if (m_LogChanges)
                LogChanges();
        }

        bool ReportRequested()
        {
            var keyboard = Keyboard.current;
            if (m_ReportKey != Key.None && keyboard != null && keyboard[m_ReportKey].wasPressedThisFrame)
                return true;

            if (m_ReportWandButton < 0)
                return false;

            bool held = MiddleVRWand.Held(m_ReportWandButton);
            bool pressed = m_ReportPrimed && held && !m_ReportHeld;
            m_ReportHeld = held;
            m_ReportPrimed = true;
            return pressed;
        }

        // ------------------------------------------------------------------ the report

        void Report(string why)
        {
            var log = new StringBuilder();
            log.AppendLine($"{k_Tag} ===== controller sit rep ({why}) =====");

            UnitySection(log);
            MiddleVRSection(log);
            CarSection(log);

            log.Append($"{k_Tag} press {m_ReportKey} for another");
            Debug.Log(log.ToString(), this);
        }

        void UnitySection(StringBuilder log)
        {
            log.AppendLine($"{k_Tag} Unity Input System — {InputSystem.devices.Count} device(s):");
            foreach (var device in InputSystem.devices)
                log.AppendLine($"{k_Tag}    {device.displayName}  [{device.layout}]" +
                               $"{(device.enabled ? string.Empty : "  DISABLED")}");

            var pad = Gamepad.current;
            log.AppendLine(pad == null
                ? $"{k_Tag} Gamepad.current — NONE. An Xbox pad should appear here; if it does not, " +
                  "either Windows is not reporting it as XInput (look for a Joystick above) or " +
                  "MiddleVR's DirectInput driver has claimed it."
                : $"{k_Tag} Gamepad.current — {pad.displayName}: " +
                  $"LT {pad.leftTrigger.ReadValue():0.00}  RT {pad.rightTrigger.ReadValue():0.00}  " +
                  $"A {(pad.buttonSouth.isPressed ? "DOWN" : "up")}");

            var stick = Joystick.current;
            if (stick != null)
                log.AppendLine($"{k_Tag} Joystick.current — {stick.displayName}: " +
                               $"trigger {(stick.trigger.isPressed ? "DOWN" : "up")}  " +
                               "(a pad seen as a plain HID stick; its trigger still drives a car)");
        }

        void MiddleVRSection(StringBuilder log)
        {
            if (!MiddleVRWand.Available)
            {
                log.AppendLine($"{k_Tag} MiddleVR — kernel not up. Expected at a desk; in the CAVE it " +
                               "means MVRManager has not initialised, and neither the wand nor any " +
                               "MiddleVR joystick can be read.");
                return;
            }

            log.AppendLine($"{k_Tag} MiddleVR — kernel up. Wand buttons down: {WandButtonsDown()}");
            log.AppendLine($"{k_Tag} MiddleVR joysticks — {MiddleVRJoystick.Count}:");

            for (int j = 0; j < MiddleVRJoystick.Count; j++)
            {
                log.AppendLine($"{k_Tag}    [{j}] \"{MiddleVRJoystick.Name(j)}\"  " +
                               $"{MiddleVRJoystick.AxisCount(j)} axes  " +
                               $"{MiddleVRJoystick.ButtonCount(j)} buttons");

                var axes = new StringBuilder();
                for (int a = 0; a < Mathf.Min(m_ScanAxes, MiddleVRJoystick.AxisCount(j)); a++)
                    axes.Append($"  {a}:{MiddleVRJoystick.Axis(j, a):+0.00;-0.00}");

                if (axes.Length > 0)
                    log.AppendLine($"{k_Tag}        axes{axes}");
            }
        }

        string WandButtonsDown()
        {
            var down = new StringBuilder();
            for (int b = 0; b <= m_ScanWandButtons; b++)
                if (MiddleVRWand.Held(b))
                    down.Append($" {b}");

            return down.Length == 0 ? "none" : down.ToString().Trim();
        }

        void CarSection(StringBuilder log)
        {
            if (m_Cars == null || m_Cars.Length == 0)
            {
                log.AppendLine($"{k_Tag} Cars — none found.");
                return;
            }

            log.AppendLine($"{k_Tag} Cars:");
            foreach (var car in m_Cars)
            {
                if (car == null)
                    continue;

                var throttle = car.Throttle;
                log.AppendLine(throttle == null
                    ? $"{k_Tag}    {car.CarName}: NO THROTTLE COMPONENT WIRED"
                    : $"{k_Tag}    {car.CarName} ({throttle.Describe()}) — reading {throttle.Value:0.00}");
            }
        }

        // ------------------------------------------------------------------ live changes

        /// <summary>
        /// One line per thing that moves. This is the half that answers "which index is my trigger":
        /// pull it, and the answer is the number in the line that appears.
        /// </summary>
        void LogChanges()
        {
            var pad = Gamepad.current;
            if (pad != null)
            {
                Moved("Gamepad leftTrigger", pad.leftTrigger.ReadValue(), ref m_LastLeftTrigger);
                Moved("Gamepad rightTrigger", pad.rightTrigger.ReadValue(), ref m_LastRightTrigger);

                foreach (var control in pad.allControls)
                    if (control is ButtonControl button && button.wasPressedThisFrame)
                        Debug.Log($"{k_Tag} Gamepad button \"{control.name}\" PRESSED");
            }

            for (int b = 0; b <= m_ScanWandButtons && b < m_LastWand.Length; b++)
            {
                bool held = MiddleVRWand.Held(b);
                if (held != m_LastWand[b])
                    Debug.Log($"{k_Tag} wand button {b} {(held ? "PRESSED" : "released")}");

                m_LastWand[b] = held;
            }

            if (MiddleVRJoystick.Count == 0)
                return;

            // Joystick 0 only. A second MiddleVR pad is rare enough that scanning it every frame
            // would cost more than it ever found.
            for (int a = 0; a < m_LastAxis.Length && a < MiddleVRJoystick.AxisCount(0); a++)
            {
                float value = MiddleVRJoystick.Axis(0, a);
                if (Mathf.Abs(value - m_LastAxis[a]) < m_AxisChange)
                    continue;

                Debug.Log($"{k_Tag} MVR joystick 0 axis {a}: {m_LastAxis[a]:+0.00;-0.00} -> " +
                          $"{value:+0.00;-0.00}   (set m_JoystickAxis to {a} to drive a car with this)");
                m_LastAxis[a] = value;
            }

            for (int b = 0; b < m_LastButton.Length && b < MiddleVRJoystick.ButtonCount(0); b++)
            {
                bool held = MiddleVRJoystick.Held(0, b);
                if (held != m_LastButton[b])
                    Debug.Log($"{k_Tag} MVR joystick 0 button {b} {(held ? "PRESSED" : "released")}" +
                              $"{(held ? $"   (set m_JoystickButton to {b} to drive a car with this)" : string.Empty)}");

                m_LastButton[b] = held;
            }
        }

        void Moved(string what, float value, ref float last)
        {
            if (Mathf.Abs(value - last) < m_AxisChange)
                return;

            Debug.Log($"{k_Tag} {what}: {last:0.00} -> {value:0.00}");
            last = value;
        }
    }
}
