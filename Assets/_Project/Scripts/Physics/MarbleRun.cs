using System.Collections.Generic;
using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace VRShootingGallery.PhysicsDemo
{
    /// <summary>How the rack lets go of the marbles.</summary>
    public enum MarbleReleaseMode
    {
        /// <summary>All of them on the same frame — a race, with a winner.</summary>
        Race,

        /// <summary>One at a time, so the run reads as a demonstration of the track rather than a contest.</summary>
        Stagger,
    }

    /// <summary>
    /// Drives the marble run. The track itself is plain static geometry — beds, rails and corner
    /// catchers bolted to the CAVE room's walls — so the only things this component owns are the
    /// ones the track cannot do for itself: holding the marbles at the top until someone asks for a
    /// race, calling the winner, and putting a marble back on the rack when it leaves the run.
    /// </summary>
    /// <remarks>
    /// A marble waits as a kinematic body sitting on the loading ramp, which is why release order
    /// still matters even in a race: index 0 is parked furthest down the ramp, so if a stagger is
    /// ever used a released marble never has to roll through one that is still parked (a kinematic
    /// body is solid). Pressing the start button again re-racks everything and runs from the top, so
    /// a demo never needs the Editor to reset it.
    ///
    /// The race is fairer than the staggered start it replaced, because every corner is a drop onto
    /// a catcher that stops a marble more or less dead. A lead built on one leg does not carry into
    /// the next, so the pack re-forms five times on the way down and the order really does change.
    /// </remarks>
    public class MarbleRun : MonoBehaviour
    {
        [SerializeField, Tooltip("Marbles in release order. Index 0 leaves first, so it has to be " +
            "the one parked furthest down the loading ramp.")]
        Rigidbody[] m_Marbles;

        [SerializeField, Tooltip("Where each marble waits — one per marble, in the same order.")]
        Transform[] m_Holds;

        [SerializeField, Tooltip("Name per marble for the sign to call the winner by — the colour " +
            "reads better than the index. Falls back to MARBLE n when empty.")]
        string[] m_MarbleNames;

        [SerializeField, Tooltip("Wand button that starts the race, and restarts it from the top if " +
            "one is already going. Negative disables it.")]
        int m_StartButton = 1;

        [SerializeField, Tooltip("All at once, or one at a time. Runs authored before this field " +
            "existed deserialize to Race.")]
        MarbleReleaseMode m_ReleaseMode = MarbleReleaseMode.Race;

        [SerializeField, Tooltip("Seconds between one marble leaving the rack and the next. Stagger mode only.")]
        float m_ReleaseInterval = 1.4f;

        [SerializeField, Tooltip("A marble inside this box — in this object's own space — has " +
            "finished. The builder fills it in from the catch basin; leave it zero-sized and no " +
            "winner is ever called.")]
        Bounds m_FinishZone;

        [SerializeField, Tooltip("A marble below this height has left the room, not just the track — " +
            "it goes straight back on the rack. One that only jumped a rail lands on the floor and " +
            "stays there in plain sight until the next press collects it.")]
        float m_RecycleBelowY = -0.3f;

        [SerializeField, Tooltip("Optional sign reporting what the run is waiting for.")]
        TextMeshPro m_StatusText;

        /// <summary>Index of the next marble to let go, or -1 when the rack is empty.</summary>
        int m_NextToRelease = -1;

        float m_NextReleaseTime;

        /// <summary>Marble indices in the order they reached the basin. First one is the winner.</summary>
        readonly List<int> m_Finished = new();

        // Wand edge state. See StartPressed for why the edge is latched here rather than taken
        // from MiddleVR's own "toggled" flag.
        bool m_WasHeld;
        bool m_Primed;

        string m_ShownStatus;

        int Count => m_Marbles == null || m_Holds == null ? 0 : Mathf.Min(m_Marbles.Length, m_Holds.Length);

        void Awake() => Rack();

        void Update()
        {
            if (StartPressed())
                StartRun();

            Release();
            Recycle();
            CheckFinish();
            ShowStatus();
        }

        // ------------------------------------------------------------------ run

        /// <summary>Puts every marble back on the rack and drops the flag.</summary>
        public void StartRun()
        {
            Rack();

            if (Count == 0)
            {
                Debug.LogWarning("[MarbleRun] Nothing to release — no marbles are wired up.", this);
                return;
            }

            m_NextToRelease = 0;
            m_NextReleaseTime = Time.time;
        }

        /// <summary>Stops the run, returns every marble to its hold, and clears the result.</summary>
        public void Rack()
        {
            m_NextToRelease = -1;
            m_Finished.Clear();

            for (int i = 0; i < Count; i++)
                Park(i);
        }

        /// <summary>
        /// Zero in a race, which makes <see cref="Release"/>'s loop run straight through the rack
        /// and let every marble go on the same frame.
        /// </summary>
        float Interval => m_ReleaseMode == MarbleReleaseMode.Race ? 0f : Mathf.Max(0.05f, m_ReleaseInterval);

        /// <summary>Lets go of every marble whose turn has come round — all of them, in a race.</summary>
        void Release()
        {
            while (m_NextToRelease >= 0 && Time.time >= m_NextReleaseTime)
            {
                var marble = m_Marbles[m_NextToRelease];
                if (marble != null)
                {
                    marble.isKinematic = false;

                    // A body that was parked long enough to fall asleep would otherwise sit there:
                    // it starts at rest, so nothing in the solver would wake it.
                    marble.WakeUp();
                }

                m_NextToRelease++;
                m_NextReleaseTime += Interval;

                if (m_NextToRelease >= Count)
                    m_NextToRelease = -1;
            }
        }

        /// <summary>
        /// Polls the basin rather than putting a trigger down there. Four marbles is nothing to test
        /// against a box, and it keeps the finish line out of the physics layer matrix — one less
        /// thing to get wrong when the run is dropped into a scene that already uses triggers.
        /// </summary>
        void CheckFinish()
        {
            if (m_FinishZone.size.sqrMagnitude < 1e-6f)
                return;

            for (int i = 0; i < Count; i++)
            {
                var marble = m_Marbles[i];
                if (marble == null || marble.isKinematic || m_Finished.Contains(i))
                    continue;

                if (!m_FinishZone.Contains(transform.InverseTransformPoint(marble.position)))
                    continue;

                m_Finished.Add(i);

                if (m_Finished.Count == 1)
                    Debug.Log($"[MarbleRun] {Name(i)} wins.", this);
            }
        }

        /// <summary>
        /// Catches the marble that jumped a rail or found a gap. It goes back on the rack rather
        /// than being dropped back onto the track mid-run — landing a marble in the middle of a
        /// track someone is watching reads as a glitch, whereas a full rack is what the next press
        /// expects anyway.
        /// </summary>
        void Recycle()
        {
            for (int i = 0; i < Count; i++)
            {
                var marble = m_Marbles[i];
                if (marble == null || marble.isKinematic || marble.position.y >= m_RecycleBelowY)
                    continue;

                Park(i);
            }
        }

        void Park(int index)
        {
            var marble = m_Marbles[index];
            var hold = m_Holds[index];
            if (marble == null || hold == null)
                return;

            // Kinematic first: moving a dynamic body by its transform leaves the solver holding the
            // velocity it had on the way out, which fires it off the rack the moment it is released.
            marble.isKinematic = true;
            marble.velocity = Vector3.zero;
            marble.angularVelocity = Vector3.zero;
            marble.transform.SetPositionAndRotation(hold.position, hold.rotation);
        }

        // ------------------------------------------------------------------ input

        /// <summary>
        /// Rising edge of the wand's start button. The edge is latched here instead of using
        /// MiddleVR's own "toggled" flag because the wand is sampled on the cluster's cadence, not
        /// Unity's, so that flag can stay set across more than one frame — which would restart the
        /// run twice on a single press. <c>m_Primed</c> swallows the first sample so a button
        /// already held when the scene loads does not count as a press.
        /// </summary>
        bool StartPressed()
        {
            if (m_StartButton < 0)
                return false;

#if UNITY_EDITOR
            // Desk testing: the wand only exists once MiddleVR's kernel is up, and on a dev PC it
            // usually is not. The number row key printed with the same digit stands in for it.
            var keyboard = Keyboard.current;
            if (keyboard != null && m_StartButton <= 9 && keyboard[NumberRowKey(m_StartButton)].wasPressedThisFrame)
                return true;
#endif

            bool held = MiddleVRWand.Held(m_StartButton);
            bool pressed = m_Primed && held && !m_WasHeld;

            m_WasHeld = held;
            m_Primed = true;
            return pressed;
        }

#if UNITY_EDITOR
        /// <summary>
        /// The number-row key printed with <paramref name="digit"/>. Not arithmetic on the enum:
        /// the row runs 1..9 and then 0, so <c>Digit1 + 9</c> would not be the zero key.
        /// </summary>
        static Key NumberRowKey(int digit) => digit == 0 ? Key.Digit0 : Key.Digit1 + (digit - 1);
#endif

        // ------------------------------------------------------------------ sign

        void ShowStatus()
        {
            if (m_StatusText == null)
                return;

            string status = Status();
            if (status == m_ShownStatus)
                return;

            m_ShownStatus = status;
            m_StatusText.text = status;
        }

        string Status()
        {
            // The result stands until the next start, so a race that finishes while nobody is
            // looking at the sign has still been called when they turn round.
            if (m_Finished.Count > 0)
                return $"{Name(m_Finished[0])}  WINS";

            return Racing() ? "RACING" : $"PRESS  {m_StartButton}  TO START";
        }

        /// <summary>
        /// Sleeping counts as stopped, which is what puts the sign back to READY once the last
        /// marble has settled — including on a run with no finish line configured, where nothing
        /// else would ever end the race.
        /// </summary>
        bool Racing()
        {
            if (m_NextToRelease >= 0)
                return true;

            for (int i = 0; i < Count; i++)
            {
                var marble = m_Marbles[i];
                if (marble != null && !marble.isKinematic && !marble.IsSleeping())
                    return true;
            }

            return false;
        }

        string Name(int index) =>
            m_MarbleNames != null && index < m_MarbleNames.Length && !string.IsNullOrEmpty(m_MarbleNames[index])
                ? m_MarbleNames[index]
                : $"MARBLE {index + 1}";
    }
}
