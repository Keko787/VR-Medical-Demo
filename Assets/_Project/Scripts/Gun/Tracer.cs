using UnityEngine;

namespace VRShootingGallery.Gun
{
    /// <summary>How the tracer beam behaves while <see cref="Tracer.LineEnabled"/> is on.</summary>
    public enum TracerLineMode
    {
        /// <summary>A laser sight, held from the muzzle to the aim point for as long as the line is on.</summary>
        Always,

        /// <summary>A brief beam on each shot — a muzzle flash rather than a sight.</summary>
        OnShot,
    }

    /// <summary>
    /// Optional aiming aids: a persistent dot at the aim point and a beam from the muzzle,
    /// either held as a laser sight or flashed on each shot. Both independently toggleable
    /// (the cave preset turns both on and hides the gun model — that coupling lives in
    /// SettingsService later, not here).
    /// </summary>
    public class Tracer : MonoBehaviour
    {
        [SerializeField] GunController m_Gun;
        [SerializeField] Transform m_Muzzle;
        [SerializeField] LineRenderer m_Line;
        [SerializeField] Transform m_Dot;

        [SerializeField, Tooltip("Held as a laser sight, or flashed on each shot. Guns authored " +
            "before this field existed deserialize to Always, which is what a tracer line switched " +
            "on as an aiming aid was always trying to be.")]
        TracerLineMode m_LineMode = TracerLineMode.Always;

        [SerializeField, Tooltip("Seconds the tracer beam stays visible after a shot. OnShot mode only.")]
        float m_LineLifetime = 0.08f;

        [SerializeField] bool m_LineEnabled;
        [SerializeField] bool m_DotEnabled;

        Camera m_Camera;
        float m_LineHideTime;

        public bool LineEnabled
        {
            get => m_LineEnabled;
            set
            {
                m_LineEnabled = value;

                // OnShot owns the renderer's enabled flag through the shot timer, so switching the
                // beam off there has to take effect now — there is no next shot to do it.
                if (!value && m_LineMode == TracerLineMode.OnShot && m_Line != null)
                    m_Line.enabled = false;
            }
        }

        public TracerLineMode LineMode
        {
            get => m_LineMode;
            set => m_LineMode = value;
        }

        public bool DotEnabled
        {
            get => m_DotEnabled;
            set
            {
                m_DotEnabled = value;
                if (m_Dot != null)
                    m_Dot.gameObject.SetActive(value);
            }
        }

        void Awake() => m_Camera = Camera.main;

        void OnEnable()
        {
            if (m_Gun != null)
                m_Gun.Fired += OnFired;
        }

        void OnDisable()
        {
            if (m_Gun != null)
                m_Gun.Fired -= OnFired;
        }

        void Start()
        {
            if (m_Line != null)
                m_Line.enabled = false;
            if (m_Dot != null)
                m_Dot.gameObject.SetActive(m_DotEnabled);
        }

        void Update()
        {
            if (m_DotEnabled && m_Dot != null)
            {
                m_Dot.position = m_Gun.AimPoint;
                if (m_Camera != null)
                    m_Dot.rotation = Quaternion.LookRotation(m_Dot.position - m_Camera.transform.position);
            }

            if (m_LineMode == TracerLineMode.OnShot &&
                m_Line != null && m_Line.enabled && Time.time >= m_LineHideTime)
                m_Line.enabled = false;
        }

        /// <summary>
        /// Redraws the held beam after every Update has run, so it uses this frame's muzzle pose and
        /// this frame's aim point. Both ends move every frame — the near one with the wand, the far
        /// one with whatever the ray lands on — and drawn from Update the beam would trail a frame
        /// behind whichever of the gun and the tracer happened to run second, which on something
        /// anchored to the barrel reads as the laser coming unstuck from the gun.
        /// </summary>
        void LateUpdate()
        {
            if (m_LineMode != TracerLineMode.Always || m_Line == null)
                return;

            bool show = m_LineEnabled && m_Gun != null && m_Muzzle != null;
            if (show)
                Draw();

            if (m_Line.enabled != show)
                m_Line.enabled = show;
        }

        void OnFired()
        {
            // Nothing to do for a beam that is already being held open.
            if (m_LineMode != TracerLineMode.OnShot || !m_LineEnabled || m_Line == null || m_Muzzle == null)
                return;

            Draw();
            m_Line.enabled = true;
            m_LineHideTime = Time.time + m_LineLifetime;
        }

        /// <summary>
        /// Muzzle to aim point, in world space — which is what the LineRenderer on the gun prefabs
        /// is set up for. A local-space line would have to undo the gun's own transform.
        /// </summary>
        void Draw()
        {
            m_Line.positionCount = 2;
            m_Line.SetPosition(0, m_Muzzle.position);
            m_Line.SetPosition(1, m_Gun.AimPoint);
        }
    }
}
