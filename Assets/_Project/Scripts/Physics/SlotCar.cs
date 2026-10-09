using UnityEngine;

namespace VRShootingGallery.PhysicsDemo
{
    /// <summary>What a car is doing right now.</summary>
    public enum SlotCarState
    {
        /// <summary>In the slot, driven along the centre line.</summary>
        Slotted,

        /// <summary>Off the end of a ramp with nothing under it, on a ballistic arc.</summary>
        Flying,

        /// <summary>Off the track and tumbling, waiting to be put back.</summary>
        Crashed,
    }

    /// <summary>
    /// One car. It rides the track's centre line as a distance and a speed rather than as a
    /// rigidbody, which is what makes a vertical loop and a barrel roll survivable at all — a free
    /// body would have to be flung round them at exactly the right speed, and a demo cannot be one
    /// bad frame away from a car in the audience.
    /// </summary>
    /// <remarks>
    /// Everything that makes it feel physical is still real, because it is all longitudinal:
    /// gravity is the tangent's own rise, so climbing the spiral genuinely costs speed and the
    /// plunge genuinely gives it back, and the car slows over the top of the loop and accelerates
    /// out of it without any of that being scripted.
    ///
    /// There are three ways to lose it, and all three are about carrying the right speed:
    ///
    /// <list type="bullet">
    /// <item>Too slow at the ramp and the jump is not cleared — the classic Hot Wheels one.</item>
    /// <item>Too slow for the loop and the car cannot get over the top of it, which
    /// <see cref="Stuck"/> eventually calls.</item>
    /// <item>Too fast through a corner and the slot loses its grip — the classic slot-car mistake,
    /// switched off at the shipped <see cref="m_SlotGrip"/> and a tuning knob away.</item>
    /// </list>
    ///
    /// Two of those three are on the too-slow side, and that is deliberate rather than an oversight.
    /// The one corner fast enough to matter is the foot of the plunge, and its speed comes from the
    /// 1.05 m drop above it, not from the throttle — so a player cannot drive around a de-slot there
    /// however carefully they lift, and it would read as the track being broken rather than as their
    /// mistake. The circuit peaks at about 5.7 g, the slot is specified to hold 60 m/s², and the
    /// failures a player can actually control are the ones left in. Dropping
    /// <see cref="m_SlotGrip"/> to around 45 puts the too-fast failure back for anyone who wants a
    /// harder demo.
    ///
    /// Being upside down is <em>not</em> one of them. A car hanging from the top of the loop or half
    /// way through the barrel roll is held by the slot, because that is what a slot is: a pin in a
    /// groove, not a hope. That is not a concession either — a barrel roll is straight, and a
    /// straight piece generates no centripetal force to hold anything to it at any speed, so an
    /// unattached car could not survive one however fast it went.
    ///
    /// Holding an inverted car does mean the loop cannot simply drop a car that is too slow for it.
    /// What it does instead is worse to watch and so has to be caught another way: the car rolls
    /// back down the near side, climbs again, and rocks there indefinitely. <see cref="Stuck"/>
    /// notices that no progress is being made and lets go, which lands in the same place a player
    /// expects — too slow for the loop, try again — by a route that cannot hang.
    /// </remarks>
    public class SlotCar : MonoBehaviour
    {
        [SerializeField, Tooltip("The circuit this car runs on.")]
        SlotCarTrack m_Track;

        [SerializeField, Tooltip("This player's throttle.")]
        SlotCarThrottle m_Throttle;

        [SerializeField, Tooltip("Which lane, 0 or 1. Only decides which side of the centre line " +
            "the car is drawn — both cars are timed on the centre line, so neither lane is quicker.")]
        int m_Lane;

        [SerializeField, Tooltip("Name the scoreboard calls this car by.")]
        string m_CarName = "CAR";

        // ---------------------------------------------------------------- drive

        [SerializeField, Tooltip("Acceleration at full throttle from rest, m/s². Falls away toward " +
            "the top speed, so this is the launch off the line rather than a constant shove.")]
        float m_Power = 13f;

        [SerializeField, Tooltip("Speed the motor alone can reach on the flat, m/s. Set by the loop " +
            "rather than by taste: a car needs sqrt(4·g·loopRadius) — about 3.8 m/s — merely to " +
            "reach the top of it, and half throttle has to be comfortably above that or a player " +
            "feathering an analog trigger can never get round at all.")]
        float m_TopSpeed = 6.2f;

        [SerializeField, Tooltip("Rolling resistance, as a fraction of speed lost per second. This " +
            "is what makes a released trigger coast rather than brake.")]
        float m_Rolling = 0.55f;

        [SerializeField, Tooltip("Ride height of the car body above the running surface, m.")]
        float m_RideHeight = 0.018f;

        // ---------------------------------------------------------------- grip

        [SerializeField, Tooltip("Cornering load the slot can hold, m/s². This is the difficulty " +
            "dial: 60 clears the circuit's own peak of about 5.7 g, so nobody is ever thrown out " +
            "of a corner; around 45 makes the entry to the spiral a corner you have to lift for.")]
        float m_SlotGrip = 60f;

        [SerializeField, Tooltip("How far ahead and behind to measure the track's bend, m. Wide " +
            "enough to ignore the baked line's stair-stepping, short enough to see a real corner.")]
        float m_CurvatureSpan = 0.06f;

        // ---------------------------------------------------------------- flight

        [SerializeField, Tooltip("How far past the gap to keep looking for a landing, m. Long " +
            "enough for the longest jump anyone can set up — a car off the plunge clears three " +
            "times the ground one leaving the grid does.")]
        float m_LandingWindow = 2f;

        [SerializeField, Tooltip("How far off the line a car may be and still be landed, once it is " +
            "below the running surface, m. Generous on purpose: at 5 m/s a frame carries the car " +
            "eight centimetres, so a tight tolerance would let it step clean over the ramp.")]
        float m_CatchReach = 0.3f;

        [SerializeField, Tooltip("Below this height a flying car has missed everything and is lost, m. " +
            "The room's floor, so a car that drops into the jump crashes on it rather than sinking " +
            "through it first.")]
        float m_LostBelowY;

        // ---------------------------------------------------------------- recovery

        [SerializeField, Tooltip("How long a lost car tumbles before it is put back, s.")]
        float m_RespawnDelay = 1.5f;

        [SerializeField, Tooltip("How far back up the track a car is put after losing it, m. Far " +
            "enough that a missed jump gets a fresh run at the ramp.")]
        float m_RespawnBackup = 0.9f;

        [SerializeField, Tooltip("How long a car may fail to get any further before it is counted " +
            "as lost, s. This is what ends a car that has run out of speed half way up the loop: " +
            "the slot will hold it there, but it can only roll back and try again, and a car " +
            "rocking in the bottom of a loop for the rest of the demo is worse than one that falls " +
            "out of it and gets another run at it.")]
        float m_StallGrace = 2.5f;

        SlotCarState m_State = SlotCarState.Slotted;

        float m_Distance;
        float m_Speed;
        int m_Lap;

        // Flight and crash are both "not in the slot", and both need the same three things. The
        // velocity is in the track's own space, like everything else here — the car is built as a
        // direct child of the track object precisely so that the two never have to be converted.
        Vector3 m_Velocity;
        float m_Timer;
        float m_GapExit;
        Vector3 m_Tumble;
        Vector3 m_FlightUp = Vector3.up;

        // Furthest the car has got, so that "stuck" can be measured as failing to beat it rather
        // than as standing still — a car oscillating in a loop is never actually stationary.
        float m_Furthest;
        float m_Stalled;

        /// <summary>Gravity in the track's own space, so a rotated track still falls downward.</summary>
        Vector3 Gravity => transform.parent != null
            ? transform.parent.InverseTransformDirection(Physics.gravity)
            : Physics.gravity;

        public string CarName => m_CarName;
        public SlotCarState State => m_State;
        public float Speed => m_Speed;
        public int Lap => m_Lap;
        public SlotCarThrottle Throttle => m_Throttle;

        /// <summary>Distance covered since the flag, used to order the field.</summary>
        public float Progress => m_Track == null ? 0f : m_Lap * m_Track.Length + m_Distance;

        /// <summary>True once the car has completed <paramref name="laps"/> laps.</summary>
        public bool Finished(int laps) => m_Lap >= laps;

        void Update()
        {
            if (m_Track == null || m_Track.Length <= 0f)
                return;

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            switch (m_State)
            {
                case SlotCarState.Slotted:
                    Drive(dt);
                    break;
                case SlotCarState.Flying:
                    Fly(dt);
                    break;
                case SlotCarState.Crashed:
                    Tumble(dt);
                    break;
            }
        }

        // ------------------------------------------------------------------ control

        /// <summary>Puts the car on the grid at <paramref name="distance"/>, stopped.</summary>
        public void Place(float distance)
        {
            m_State = SlotCarState.Slotted;
            m_Speed = 0f;
            m_Lap = 0;
            m_Velocity = Vector3.zero;
            m_Timer = 0f;

            if (m_Track == null)
                return;

            m_Distance = m_Track.Wrap(distance);
            m_Furthest = Progress;
            m_Stalled = 0f;
            Show(m_Track.Sample(m_Distance));
        }

        // ------------------------------------------------------------------ slotted

        void Drive(float dt)
        {
            var frame = m_Track.Sample(m_Distance);
            float throttle = m_Throttle != null ? m_Throttle.Value : 0f;

            // Motor, falling away as the car approaches what it can pull on the flat, so the top
            // speed is a property of the car rather than a clamp applied after the fact.
            float drive = m_Power * throttle * Mathf.Clamp01(1f - m_Speed / Mathf.Max(0.1f, m_TopSpeed));

            // The only gravity term there is: the rise of the tangent itself. Nothing else is
            // needed for the spiral to cost speed and the plunge to give it back.
            float gravity = -Physics.gravity.magnitude * frame.Forward.y;

            m_Speed += (drive + gravity - m_Rolling * m_Speed) * dt;

            // A car that has stalled on the climb rolls back down, which is the honest outcome and
            // reads better than freezing half way up the spiral.
            if (m_Speed < -2f)
                m_Speed = -2f;

            m_Distance += m_Speed * dt;
            Advance();

            frame = m_Track.Sample(m_Distance);

            if (frame.Gap)
            {
                Launch();
                return;
            }

            if (CorneringLoad(frame) > m_SlotGrip || Stuck(dt, throttle))
            {
                Crash(frame);
                return;
            }

            Show(frame);
        }

        /// <summary>
        /// True once the car has spent long enough <em>trying</em> to get somewhere and failing.
        /// Progress rather than speed is the test, because the case this exists for — a car that
        /// could not carry enough speed over the top of the loop — spends its time rolling back down
        /// and climbing again, which is a lot of movement and no progress at all.
        /// </summary>
        /// <remarks>
        /// The throttle is half the question and leaving it out was a real bug: a car sitting on the
        /// grid waiting for someone to pick up a controller is making no progress either, and
        /// without this it was thrown off the track a few seconds into every scene, put back, and
        /// thrown off again. Standing still because nobody has asked for anything is not being
        /// stuck, so a closed throttle clears the count rather than building it.
        /// </remarks>
        bool Stuck(float dt, float throttle)
        {
            if (Progress > m_Furthest)
            {
                m_Furthest = Progress;
                m_Stalled = 0f;
                return false;
            }

            if (throttle <= 0f)
            {
                m_Stalled = 0f;
                return false;
            }

            m_Stalled += dt;
            return m_Stalled > m_StallGrace;
        }

        /// <summary>
        /// Wraps the lap over, forwards or back. Backwards matters because a car can roll back down
        /// the spiral across the line it came in by.
        /// </summary>
        void Advance()
        {
            float length = m_Track.Length;

            while (m_Distance >= length)
            {
                m_Distance -= length;
                m_Lap++;
            }

            while (m_Distance < 0f)
            {
                m_Distance += length;
                m_Lap = Mathf.Max(0, m_Lap - 1);
            }
        }

        /// <summary>
        /// What the slot has to hold, m/s². The track's own bend supplies the acceleration the car
        /// needs; gravity supplies part of it for free wherever the surface is banked, and the
        /// remainder is the sideways load on the pin.
        /// </summary>
        /// <remarks>
        /// The bend is measured off the baked line rather than read from the segment, so a loop and
        /// a banked corner are handled by the same three lines. Only the component along the
        /// surface's own right axis counts: what is left over presses the car into the track, and
        /// the track can take any amount of that.
        /// </remarks>
        float CorneringLoad(in TrackFrame frame)
        {
            var ahead = m_Track.Sample(frame.Distance + m_CurvatureSpan).Forward;
            var behind = m_Track.Sample(frame.Distance - m_CurvatureSpan).Forward;
            var bend = (ahead - behind) / (2f * m_CurvatureSpan);

            // Newton along the surface's right axis: what the corner demands, less what gravity is
            // already providing down the bank. The two are added rather than subtracted because on
            // a corner banked into the turn both the demand and gravity's share point the same way
            // (inward), so gravity's share is already carrying part of the load and what is left for
            // the slot is the smaller number.
            var right = frame.Right;
            float needed = Vector3.Dot(bend, right) * m_Speed * m_Speed;
            float fromGravity = Physics.gravity.magnitude * right.y;

            return Mathf.Abs(needed + fromGravity);
        }

        // ------------------------------------------------------------------ flying

        /// <summary>
        /// Leaves the slot, from the lip and along the lip — <em>not</em> along the centre line
        /// inside the gap.
        /// </summary>
        /// <remarks>
        /// The line through a gap is bookkeeping. It carries arc length across the hole so the lap
        /// still measures, and it is deliberately flat, which is exactly why it must not be used
        /// here: taking the launch direction from it would fire the car horizontally off a ramp that
        /// was pointing upward. The car leaves along the last track it was actually standing on.
        ///
        /// The car is also moved to that lip rather than left where the frame ended. A frame at
        /// 5 m/s is eight centimetres, and a jump that starts eight centimetres short of its own
        /// take-off is a jump whose difficulty depends on the frame rate.
        /// </remarks>
        void Launch()
        {
            float lipDistance = FindGapEdge(-1f);
            var lip = m_Track.Sample(lipDistance);

            transform.localPosition = m_Track.LanePosition(lip, m_Lane, m_RideHeight);
            m_State = SlotCarState.Flying;
            m_Velocity = lip.Forward * m_Speed;
            m_FlightUp = lip.Up;

            // The far edge is found now rather than each frame, because it is the one thing that
            // says where a landing is allowed to start — without it the car would be caught straight
            // back onto the ramp it has just left.
            m_GapExit = FindGapEdge(1f);
            m_Timer = 0f;
        }

        /// <summary>
        /// Walks off the gap in <paramref name="direction"/> and returns the first distance that has
        /// track on it — the take-off lip going back, the landing lip going forward.
        /// </summary>
        float FindGapEdge(float direction)
        {
            for (float step = 0f; step < m_LandingWindow; step += 0.01f)
            {
                float s = m_Distance + direction * step;
                if (!m_Track.Sample(s).Gap)
                    return s;
            }

            return m_Distance;
        }

        void Fly(float dt)
        {
            m_Velocity += Gravity * dt;
            transform.localPosition += m_Velocity * dt;

            // Nose follows the arc, which is what sells a jump: the car pitches up off the ramp and
            // noses over at the top all by itself. The roll it left the lip with is carried through
            // rather than being re-derived, so a car launched off a banked lip stays banked instead
            // of snapping level in mid-air.
            if (m_Velocity.sqrMagnitude > 1e-6f)
            {
                var heading = m_Velocity.normalized;
                if (Mathf.Abs(Vector3.Dot(heading, m_FlightUp)) < 0.999f)
                    transform.localRotation = Quaternion.LookRotation(heading, m_FlightUp);
            }

            m_Timer += dt;

            if (Catch())
                return;

            if (transform.localPosition.y < m_LostBelowY || m_Timer > 4f)
                Crash(m_Track.Sample(m_GapExit));
        }

        /// <summary>
        /// Looks for track under the car. The search starts at the far lip so the ramp the car has
        /// just left cannot catch it back, and runs on past the landing so a car that clears the
        /// whole thing is still picked up rather than being thrown away for being quick.
        /// </summary>
        /// <remarks>
        /// Landed means the car has come <em>down to</em> the track, not that it is near it.
        /// Testing proximity instead put the car back in the slot the moment it drew level with the
        /// landing ramp, which on a shallow ramp is almost immediately — the jump existed in the
        /// geometry and never once in the air. Height above the running surface is the honest test,
        /// and it costs nothing extra since the nearest point has already been found.
        ///
        /// It is also the robust one. At 5 m/s a frame carries the car eight centimetres, so any
        /// tolerance tight enough to stop an early snap would be loose enough to let a fast car step
        /// clean over the ramp between two frames; asking which side of the surface it is on has no
        /// such gap to fall through.
        ///
        /// The test is held back until the car is past the landing lip. A car that is too slow drops
        /// into the hole while still alongside that lip, and without the guard the track it failed
        /// to reach would reach out and rescue it.
        /// </remarks>
        bool Catch()
        {
            var here = transform.localPosition;
            float best = float.MaxValue;
            float at = 0f;

            for (float s = m_GapExit; s < m_GapExit + m_LandingWindow; s += 0.02f)
            {
                var frame = m_Track.Sample(s);
                float d = Vector3.SqrMagnitude(m_Track.LanePosition(frame, m_Lane, m_RideHeight) - here);
                if (d >= best)
                    continue;

                best = d;
                at = s;
            }

            var landing = m_Track.Sample(at);
            float above = Vector3.Dot(here - m_Track.LanePosition(landing, m_Lane, m_RideHeight), landing.Up);

            if (above > 0f || at <= m_GapExit + 0.05f || best > m_CatchReach * m_CatchReach)
                return false;

            // Only the speed that was going the track's way survives the landing. Dropping the rest
            // is why a car that lands nose-first out of a high arc comes out slower than one that
            // skims the ramp, without any of that being special-cased.
            m_Speed = Mathf.Max(0f, Vector3.Dot(m_Velocity, landing.Forward));
            m_Distance = at;
            Advance();
            m_Furthest = Progress;
            m_Stalled = 0f;
            m_State = SlotCarState.Slotted;
            Show(m_Track.Sample(m_Distance));
            return true;
        }

        // ------------------------------------------------------------------ crashing

        void Crash(in TrackFrame where)
        {
            if (m_State == SlotCarState.Crashed)
                return;

            if (m_State == SlotCarState.Slotted)
                m_Velocity = where.Forward * m_Speed + where.Right * (m_Speed * 0.4f);

            m_State = SlotCarState.Crashed;
            m_Timer = 0f;
            m_Speed = 0f;
            m_Tumble = new Vector3(Random.Range(-540f, 540f), Random.Range(-360f, 360f),
                Random.Range(-720f, 720f));
        }

        void Tumble(float dt)
        {
            m_Velocity += Gravity * dt;
            transform.localPosition += m_Velocity * dt;
            transform.Rotate(m_Tumble * dt, Space.Self);

            // The room's floor, not a collider. A crashed car is scenery for a second and a half and
            // giving it a rigidbody would only invite it to find a way through the track.
            if (transform.localPosition.y < 0.03f)
            {
                var stopped = transform.localPosition;
                stopped.y = 0.03f;
                transform.localPosition = stopped;
                m_Velocity = Vector3.zero;
                m_Tumble = Vector3.zero;
            }

            m_Timer += dt;
            if (m_Timer < m_RespawnDelay)
                return;

            m_Distance -= m_RespawnBackup;
            Advance();
            m_Speed = 0f;
            m_Velocity = Vector3.zero;

            // Backing up moves the car behind its own high-water mark, which would read as stuck
            // the instant it is put down again.
            m_Furthest = Progress;
            m_Stalled = 0f;
            m_State = SlotCarState.Slotted;
            Show(m_Track.Sample(m_Distance));
        }

        // ------------------------------------------------------------------ drawing

        void Show(in TrackFrame frame)
        {
            transform.localPosition = m_Track.LanePosition(frame, m_Lane, m_RideHeight);
            transform.localRotation = frame.Rotation;
        }
    }
}
