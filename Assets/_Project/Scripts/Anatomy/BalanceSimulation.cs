using UnityEngine;
using VRShootingGallery.TrackingDemo;

namespace VRShootingGallery.AnatomyDemo
{
    /// <summary>
    /// The semicircular canals, driven by the viewer's own head. Shake your head "no" and the lateral
    /// canal lights; nod and the vertical canals do; tilt an ear to your shoulder and the posterior
    /// canal takes most of it. Fluid runs round each active canal the way the real endolymph does —
    /// against the turn — and the vestibular nerve fires.
    /// </summary>
    /// <remarks>
    /// <para>The head's angular velocity is taken from the change in the tracked pose between frames,
    /// expressed in the head's own frame (so it is the same whichever way the viewer faces the room),
    /// then carried into the model's anatomical frame: the model is a right ear, so the head's right is
    /// the model's lateral (−X), its forward is the model's anterior (−Z), and up is up — a half turn
    /// about Y, a proper rotation, so angular velocity comes across like any other vector.</para>
    ///
    /// <para>Each canal responds to the part of that rotation about its own plane's normal, which is
    /// the physics of the thing: the fluid in a ring only lags behind a rotation about the ring's axis.
    /// The builder works out, from the geometry, which way along each canal's u a positive rotation
    /// carries the wall; the fluid, lagging, goes the other way.</para>
    ///
    /// <para>In the CAVE the pose is the ART body tracker. At a desk it is MiddleVR's head simulator or
    /// the main camera, and nothing lights unless that turns.</para>
    /// </remarks>
    [DefaultExecutionOrder(20)]
    public class BalanceSimulation : MonoBehaviour
    {
        [SerializeField] HologramPart m_AnteriorCanal;
        [SerializeField] HologramPart m_PosteriorCanal;
        [SerializeField] HologramPart m_LateralCanal;
        [SerializeField] HologramPart m_VestibularNerve;

        [Header("Canal planes, in the model's anatomical frame")]
        [SerializeField] Vector3 m_AnteriorNormal = new(0.7f, 0.35f, -0.6f);
        [SerializeField] Vector3 m_PosteriorNormal = new(-0.7f, 0.35f, -0.6f);
        [SerializeField] Vector3 m_LateralNormal = new(0f, 0.87f, 0.5f);

        [SerializeField, Tooltip("Which way along the canal's u a positive rotation about its normal carries the wall.")]
        Vector3 m_FlowSigns = Vector3.one;

        [Header("Response")]
        [SerializeField, Tooltip("Head rotation, degrees per second, below which nothing lights. Tracker jitter lives under it.")]
        float m_Threshold = 15f;

        [SerializeField, Tooltip("Head rotation, degrees per second, that lights a canal fully.")]
        float m_FullScale = 140f;

        [SerializeField, Tooltip("Seconds the response takes to settle — the cupula's sluggishness, and a filter on noise.")]
        float m_Settle = 0.12f;

        [SerializeField] Color m_FlowColour = new(0.85f, 0.95f, 1f);
        [SerializeField] Color m_NerveColour = new(1f, 1f, 0.7f);

        Quaternion m_Previous;
        bool m_HasPrevious;
        Vector3 m_Omega;
        readonly float[] m_Activity = new float[3];
        readonly float[] m_Rate = new float[3];

        static readonly string[] k_Names = { "anterior canal", "posterior canal", "lateral canal" };
        static readonly string[] k_Motions = { "nodding", "tilting toward a shoulder", "turning side to side" };

        /// <summary>Head angular velocity in the model's frame, degrees per second, smoothed.</summary>
        public Vector3 AngularVelocity => m_Omega;

        /// <summary>The most active canal (0 anterior, 1 posterior, 2 lateral) and how active, 0–1.</summary>
        public int Dominant(out float activity)
        {
            int best = 0;
            for (int i = 1; i < 3; i++)
                if (m_Activity[i] > m_Activity[best])
                    best = i;

            activity = m_Activity[best];
            return best;
        }

        public static string CanalName(int index) => k_Names[index];
        public static string CanalMotion(int index) => k_Motions[index];

        void Update()
        {
            if (TrackedHead.TryGetPose(out _, out var rotation))
                Sense(rotation, Time.deltaTime);

            Apply(Time.deltaTime);
        }

        /// <summary>Feeds this frame's head orientation in.</summary>
        public void Sense(Quaternion head, float dt)
        {
            if (dt <= 1e-5f)
                return;

            if (!m_HasPrevious)
            {
                m_Previous = head;
                m_HasPrevious = true;
                return;
            }

            (head * Quaternion.Inverse(m_Previous)).ToAngleAxis(out float angle, out var axis);
            m_Previous = head;
            if (angle > 180f)
                angle -= 360f;

            var world = axis * (angle / dt);
            if (float.IsNaN(world.x) || float.IsInfinity(world.x))
                world = Vector3.zero;

            var local = Quaternion.Inverse(head) * world;
            var model = new Vector3(-local.x, local.y, -local.z);

            float settle = 1f - Mathf.Exp(-dt / Mathf.Max(m_Settle, 1e-3f));
            m_Omega = Vector3.Lerp(m_Omega, model, settle);
        }

        /// <summary>Sets the head's angular velocity (model frame, °/s) directly — for the still renderer.</summary>
        public void Inject(Vector3 modelAngularVelocity)
        {
            m_Omega = modelAngularVelocity;
            for (int i = 0; i < 3; i++)
                m_Activity[i] = Response(Rate(i));
        }

        float Rate(int canal) => Vector3.Dot(m_Omega, canal switch
        {
            0 => m_AnteriorNormal,
            1 => m_PosteriorNormal,
            _ => m_LateralNormal,
        });

        float Response(float rate) =>
            Mathf.Clamp01((Mathf.Abs(rate) - m_Threshold) / Mathf.Max(m_FullScale - m_Threshold, 1f));

        public void Apply(float dt)
        {
            float settle = 1f - Mathf.Exp(-dt / Mathf.Max(m_Settle * 2f, 1e-3f));
            float strongest = 0f;

            for (int i = 0; i < 3; i++)
            {
                m_Rate[i] = Rate(i);
                m_Activity[i] = Mathf.Lerp(m_Activity[i], Response(m_Rate[i]), settle);
                strongest = Mathf.Max(strongest, m_Activity[i]);

                var canal = i switch
                {
                    0 => m_AnteriorCanal,
                    1 => m_PosteriorCanal,
                    _ => m_LateralCanal,
                };
                if (canal == null)
                    continue;

                float sign = i switch
                {
                    0 => m_FlowSigns.x,
                    1 => m_FlowSigns.y,
                    _ => m_FlowSigns.z,
                };

                // The endolymph lags the turn, so relative to the canal it flows the other way.
                float activity = m_Activity[i];
                float speed = -sign * Mathf.Sign(m_Rate[i]) * (0.5f + 1.5f * activity);
                canal.SignalColor = m_FlowColour;
                canal.Signal = new Vector4(1.0f * activity, 0.1f, speed, 3f);
                canal.SignalEnvelope = new Vector4(0.5f, 1000f, 1000f, 0.08f * activity);
                canal.Activity = 0.5f * activity;
            }

            if (m_VestibularNerve != null)
            {
                m_VestibularNerve.SignalColor = m_NerveColour;
                m_VestibularNerve.Signal = new Vector4(strongest, 0.07f, 1.6f, 6f);
                m_VestibularNerve.Activity = 0.5f * strongest;
            }
        }
    }
}
