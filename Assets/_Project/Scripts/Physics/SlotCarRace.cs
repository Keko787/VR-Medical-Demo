using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VRShootingGallery.PhysicsDemo
{
    /// <summary>
    /// Runs the race: racks the cars on the grid, counts laps, calls the winner, and puts everything
    /// back when someone asks for another one.
    /// </summary>
    /// <remarks>
    /// There is no countdown and no start gate. Pulling your trigger drives your car, from the first
    /// frame the scene is up — which is what a visitor handed a controller in a CAVE actually tries,
    /// and being told to wait for a light is a worse first ten seconds than simply going. The clock
    /// starts on its own when a car first moves.
    ///
    /// Crossing the line for the last lap does not stop anybody either. The winner is called and
    /// held on the sign, and both cars stay drivable, because the demo is more often two people
    /// playing than a contest anyone is scoring. The restart button re-racks the grid.
    /// </remarks>
    public class SlotCarRace : MonoBehaviour
    {
        [SerializeField, Tooltip("The cars, in lane order.")]
        SlotCar[] m_Cars;

        [SerializeField, Tooltip("Where each car starts, in metres round the lap — one per car, " +
            "staggered so they do not begin the race inside one another.")]
        float[] m_Grid;

        [SerializeField, Tooltip("Laps that win it.")]
        int m_Laps = 5;

        [SerializeField, Tooltip("Wand button that re-racks the grid. Negative disables it.")]
        int m_RestartButton = 1;

        [SerializeField, Tooltip("Key that also re-racks the grid, for a desk or a stuck wand.")]
        Key m_RestartKey = Key.R;

        [SerializeField, Tooltip("Sign line reporting what the race is doing.")]
        TextMeshPro m_StatusText;

        [SerializeField, Tooltip("One sign line per car, in the same order.")]
        TextMeshPro[] m_CarText;

        [SerializeField, Tooltip("Colour for the car currently in front.")]
        Color m_LeaderColor = new(1f, 0.85f, 0.3f);

        [SerializeField, Tooltip("Colour for everyone else.")]
        Color m_CarColor = new(0.82f, 0.87f, 0.95f);

        bool m_Running;
        int m_Winner = -1;
        float m_Elapsed;

        // Wand edge state. Latched here for the same reason MarbleRun latches it: the wand is
        // sampled on the cluster's cadence, not Unity's, so MiddleVR's own toggled flag can stay set
        // over more than one frame and restart the race twice on a single press.
        bool m_WasHeld;
        bool m_Primed;

        string m_ShownStatus;

        int Count => m_Cars == null ? 0 : m_Cars.Length;

        void Start() => Rack();

        void Update()
        {
            if (RestartPressed())
                Rack();

            Track();
            ShowSign();
        }

        /// <summary>Puts both cars back on the grid and clears the result.</summary>
        public void Rack()
        {
            m_Running = false;
            m_Winner = -1;
            m_Elapsed = 0f;

            for (int i = 0; i < Count; i++)
            {
                if (m_Cars[i] == null)
                    continue;

                m_Cars[i].Place(m_Grid != null && i < m_Grid.Length ? m_Grid[i] : 0f);
            }
        }

        void Track()
        {
            if (m_Winner >= 0)
                return;

            for (int i = 0; i < Count; i++)
            {
                var car = m_Cars[i];
                if (car == null)
                    continue;

                // The clock starts itself. Nobody has to be told when the race began, and a demo
                // that was left idle for ten minutes still times the lap someone actually drove.
                if (!m_Running && car.Speed > 0.05f)
                    m_Running = true;

                if (car.Finished(m_Laps))
                {
                    m_Winner = i;
                    Debug.Log($"[SlotCarRace] {car.CarName} takes it in {m_Elapsed:0.0} s.", this);
                    return;
                }
            }

            if (m_Running)
                m_Elapsed += Time.deltaTime;
        }

        // ------------------------------------------------------------------ input

        bool RestartPressed()
        {
            var keyboard = Keyboard.current;
            if (m_RestartKey != Key.None && keyboard != null && keyboard[m_RestartKey].wasPressedThisFrame)
                return true;

            if (m_RestartButton < 0)
                return false;

            bool held = MiddleVRWand.Held(m_RestartButton);

            // m_Primed swallows the first sample, so a button already down when the scene loads is
            // not read as a press.
            bool pressed = m_Primed && held && !m_WasHeld;

            m_WasHeld = held;
            m_Primed = true;
            return pressed;
        }

        // ------------------------------------------------------------------ sign

        void ShowSign()
        {
            string status = Status();
            if (status != m_ShownStatus && m_StatusText != null)
            {
                m_ShownStatus = status;
                m_StatusText.text = status;
            }

            int leader = Leader();

            for (int i = 0; i < Count; i++)
            {
                if (m_CarText == null || i >= m_CarText.Length || m_CarText[i] == null || m_Cars[i] == null)
                    continue;

                var car = m_Cars[i];
                string lap = $"{Mathf.Min(car.Lap + 1, m_Laps)}/{m_Laps}";
                string note = car.State switch
                {
                    SlotCarState.Flying => "  AIR",
                    SlotCarState.Crashed => "  OFF",
                    _ => $"  {car.Speed:0.0}",
                };

                m_CarText[i].text = $"{car.CarName}  LAP {lap}{note}";
                m_CarText[i].color = i == leader && m_Winner < 0 ? m_LeaderColor : m_CarColor;
            }
        }

        string Status()
        {
            if (m_Winner >= 0)
                return $"{m_Cars[m_Winner].CarName}  WINS   {m_Elapsed:0.0}s";

            return m_Running ? $"{m_Elapsed:0.0}s" : "PULL  TRIGGER  TO  GO";
        }

        /// <summary>Whoever has covered the most ground, or -1 when nobody has moved.</summary>
        int Leader()
        {
            int best = -1;
            float furthest = 0.01f;

            for (int i = 0; i < Count; i++)
            {
                if (m_Cars[i] == null || m_Cars[i].Progress <= furthest)
                    continue;

                furthest = m_Cars[i].Progress;
                best = i;
            }

            return best;
        }
    }
}
