using UnityEngine;

namespace VRShootingGallery.AnatomyDemo
{
    /// <summary>Which diagram a part belongs to: the whole ear, or the magnified cochlear section.</summary>
    public enum HologramGroup
    {
        Ear,
        Section,
    }

    /// <summary>
    /// One labelled, pickable piece of an anatomy hologram, and the owner of how it looks this frame.
    /// </summary>
    /// <remarks>
    /// <para>The mesh is authored in the diagram's own space and the part sits at the origin of its
    /// parent with no rotation, so the anchor, the landmarks and the pivots the animation rocks about are
    /// all in one frame. Explode and motion are then just transforms layered on top:
    /// <c>localPosition = (P − R·P) + explode + offset</c>, <c>localRotation = R</c> — a rotation R about
    /// the pivot P, then carried out to the exploded position.</para>
    ///
    /// <para>Several systems want a say in how a part looks — selection, the pointer, the hearing and
    /// balance simulations — so each writes its own channel here and this component composes them into
    /// a MaterialPropertyBlock once, in LateUpdate, after they have all run. Highlight and dimming ease
    /// in and out; signals and vibration are taken as given, since the simulations already animate them.</para>
    /// </remarks>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(Renderer))]
    public class HologramPart : MonoBehaviour
    {
        static readonly int s_State = Shader.PropertyToID("_State");
        static readonly int s_Signal = Shader.PropertyToID("_Signal");
        static readonly int s_SignalEnv = Shader.PropertyToID("_SignalEnv");
        static readonly int s_SignalColor = Shader.PropertyToID("_SignalColor");
        static readonly int s_Vibrate = Shader.PropertyToID("_Vibrate");
        static readonly int s_VibrateAxis = Shader.PropertyToID("_VibrateAxis");
        static readonly int s_EndFade = Shader.PropertyToID("_EndFade");

        [SerializeField] string m_DisplayName;
        [SerializeField] string m_Region;
        [SerializeField, TextArea(3, 8)] string m_Description;
        [SerializeField] HologramGroup m_Group;

        [SerializeField, Tooltip("How important its label is. 0: labelled in the overview. 1: labelled once the " +
            "view is zoomed in or exploded. 2: only in the All labels mode, or when selected.")]
        int m_Tier;

        [SerializeField, Tooltip("Turn the model to this yaw when the part is selected — the angle it is best seen from.")]
        bool m_HasViewYaw;

        [SerializeField] float m_ViewYaw;

        [SerializeField, Tooltip("Can the wand's pointer pick it? Structures inside others (the utricle inside " +
            "the vestibule) are reached with the Next button instead.")]
        bool m_Pickable = true;

        [SerializeField, Tooltip("Where the label's leader line lands, in this part's space.")]
        Vector3 m_Anchor;

        [SerializeField, Tooltip("How far the part moves out in the exploded view, in its parent's space.")]
        Vector3 m_ExplodeOffset;

        [SerializeField, Tooltip("Centre and radius of the part, in its own space — what the view frames when " +
            "it is selected.")]
        Vector3 m_FocusCentre;

        [SerializeField] float m_FocusRadius = 1f;

        [SerializeField, Tooltip("Fade in over u = x..y and out over u = z..w along the part's length — for a " +
            "nerve or tube that carries on out of the diagram.")]
        Vector4 m_EndFade = new(-2f, -1f, 2f, 3f);

        [SerializeField] string[] m_LandmarkNames = System.Array.Empty<string>();
        [SerializeField] Vector3[] m_LandmarkPoints = System.Array.Empty<Vector3>();

        Renderer m_Renderer;
        MaterialPropertyBlock m_Block;

        float m_Highlight;
        float m_Visibility = 1f;

        // ------------------------------------------------------------------ channels, written each frame

        /// <summary>1 while selected. Set by the controller.</summary>
        public bool Selected { get; set; }

        /// <summary>1 while the pointer is on it. Set by the controller.</summary>
        public bool Hovered { get; set; }

        /// <summary>Target visibility, 0–1 — dimmed when another part is selected.</summary>
        public float TargetVisibility { get; set; } = 1f;

        /// <summary>Glow for being the subject of the hearing walkthrough's current step.</summary>
        public float Emphasis { get; set; }

        /// <summary>Glow for activity — a canal sensing rotation.</summary>
        public float Activity { get; set; }

        /// <summary>Exploded-view amount, 0–1.</summary>
        public float Explode { get; set; }

        public Color SignalColor { get; set; } = Color.white;

        /// <summary>Strength, wavelength (in u), speed (cycles/s along u), sharpness. Strength 0 is off.</summary>
        public Vector4 Signal { get; set; }

        /// <summary>Envelope peak u, rise width, fall width, and how far the surface swells with the signal.</summary>
        public Vector4 SignalEnvelope { get; set; } = new(0.5f, 1000f, 1000f, 0f);

        /// <summary>Amplitude (in this part's units), frequency (Hz) and phase of the membrane vibration.</summary>
        public Vector3 Vibrate { get; set; }

        /// <summary>Direction of the vibration, in this part's space.</summary>
        public Vector3 VibrateAxis { get; set; } = Vector3.up;

        Quaternion m_MotionRotation = Quaternion.identity;
        Vector3 m_MotionPivot;
        Vector3 m_MotionOffset;

        // ------------------------------------------------------------------ read-only

        public string DisplayName => m_DisplayName;
        public string Region => m_Region;
        public string Description => m_Description;
        public HologramGroup Group => m_Group;
        public int Tier => m_Tier;
        public bool Minor => m_Tier >= 2;

        /// <summary>The model yaw this part is best seen from, if it has one.</summary>
        public float? ViewYaw => m_HasViewYaw ? m_ViewYaw : null;

        /// <summary>
        /// False while the zoom's focus sphere has faded the part out — set by the controller, since the
        /// fade itself happens in the shader and the labels need to know.
        /// </summary>
        public bool InFocus { get; set; } = true;
        public bool Pickable => m_Pickable;
        public int LandmarkCount => m_LandmarkNames.Length;
        public string LandmarkName(int index) => m_LandmarkNames[index];

        /// <summary>Current visibility after easing — the labels follow it.</summary>
        public float Visibility => m_Visibility;

        public float FocusRadius => m_FocusRadius;

        public Renderer Renderer => m_Renderer != null ? m_Renderer : m_Renderer = GetComponent<Renderer>();

        /// <summary>The label's landing point, in world space, wherever the part has moved to.</summary>
        public Vector3 AnchorWorld => transform.TransformPoint(m_Anchor);

        public Vector3 LandmarkWorld(int index) => transform.TransformPoint(m_LandmarkPoints[index]);

        /// <summary>
        /// The part's centre in its parent's space, including how far the exploded view has carried it.
        /// This is what the view frames when the part is selected.
        /// </summary>
        public Vector3 FocusInParent => m_FocusCentre + m_ExplodeOffset * Explode;

        public Vector3 FocusWorld => transform.parent != null ? transform.parent.TransformPoint(FocusInParent) : FocusInParent;

        // ------------------------------------------------------------------ set-up

        /// <summary>Everything the builder knows about the part, in one call.</summary>
        public void Configure(string displayName, string region, string description, HologramGroup group,
            int tier, float? viewYaw, bool pickable, Vector3 anchor, Vector3 explode, Bounds bounds, Vector4 endFade,
            string[] landmarkNames, Vector3[] landmarkPoints)
        {
            m_DisplayName = displayName;
            m_Region = region;
            m_Description = description;
            m_Group = group;
            m_Tier = tier;
            m_HasViewYaw = viewYaw.HasValue;
            m_ViewYaw = viewYaw ?? 0f;
            m_Pickable = pickable;
            m_Anchor = anchor;
            m_ExplodeOffset = explode;
            m_FocusCentre = bounds.center;
            m_FocusRadius = Mathf.Max(bounds.extents.magnitude, 0.1f);
            m_EndFade = endFade;
            m_LandmarkNames = landmarkNames;
            m_LandmarkPoints = landmarkPoints;
        }

        /// <summary>
        /// Rocks the part by <paramref name="rotation"/> about <paramref name="pivot"/> and moves it by
        /// <paramref name="offset"/>, both in the parent's space, on top of the exploded position.
        /// </summary>
        public void SetMotion(Quaternion rotation, Vector3 pivot, Vector3 offset)
        {
            m_MotionRotation = rotation;
            m_MotionPivot = pivot;
            m_MotionOffset = offset;
        }

        public void ClearMotion() => SetMotion(Quaternion.identity, Vector3.zero, Vector3.zero);

        public void ClearSignals()
        {
            Signal = Vector4.zero;
            Vibrate = Vector3.zero;
            Emphasis = 0f;
            Activity = 0f;
        }

        void Awake() => m_Renderer = GetComponent<Renderer>();

        void LateUpdate() => Apply(Time.deltaTime);

        /// <summary>
        /// Composes every channel into the transform and the property block. <paramref name="dt"/> drives
        /// the easing; pass a large value to snap, as the still renderer does.
        /// </summary>
        public void Apply(float dt)
        {
            if (m_Renderer == null)
                m_Renderer = GetComponent<Renderer>();

            float ease = 1f - Mathf.Exp(-dt * 8f);
            m_Highlight = Mathf.Lerp(m_Highlight, Selected ? 1f : Hovered ? 0.55f : 0f, ease);
            m_Visibility = Mathf.Lerp(m_Visibility, TargetVisibility, 1f - Mathf.Exp(-dt * 5f));

            transform.localRotation = m_MotionRotation;
            transform.localPosition = m_MotionPivot - m_MotionRotation * m_MotionPivot
                                      + m_ExplodeOffset * Explode + m_MotionOffset;

            m_Block ??= new MaterialPropertyBlock();
            m_Block.SetVector(s_State, new Vector4(m_Highlight, Emphasis + Activity, m_Visibility,
                m_Group == HologramGroup.Ear ? 1f : 0f));
            m_Block.SetVector(s_Signal, Signal);
            m_Block.SetVector(s_SignalEnv, SignalEnvelope);
            m_Block.SetColor(s_SignalColor, SignalColor);
            m_Block.SetVector(s_Vibrate, Vibrate);
            m_Block.SetVector(s_VibrateAxis, VibrateAxis);
            m_Block.SetVector(s_EndFade, m_EndFade);
            m_Renderer.SetPropertyBlock(m_Block);

            // Nothing to see, nothing to draw: four CAVE cameras skip it entirely.
            m_Renderer.enabled = m_Visibility > 0.01f;
        }
    }
}
