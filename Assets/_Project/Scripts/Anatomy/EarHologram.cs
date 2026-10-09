using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using VRShootingGallery.TrackingDemo;

namespace VRShootingGallery.AnatomyDemo
{
    /// <summary>
    /// Runs the ear hologram: turns and zooms it, explodes it, selects a part and frames it, brings the
    /// magnified cochlear section on stage when it has something to show, and writes the info panel.
    /// </summary>
    /// <remarks>
    /// <para><b>Controls.</b> Wand trigger picks the part under the pointer (pointing at nothing goes
    /// back to the overview); button 1 steps to the next part; 2 explodes; 3 cycles the hearing tones;
    /// 4 cycles the labels. The stick turns the model and zooms. The number row does the same as the
    /// buttons from either keyboard, the arrow keys stand in for the stick, and Backspace resets.</para>
    ///
    /// <para><b>Framing.</b> The model hangs off a pivot at a fixed spot in the room, and the anatomy
    /// under it is shifted so the point being looked at sits on the pivot. Selecting a part eases that
    /// point to the part's centre and the scale up until the part is about <see cref="m_FocusSize"/>
    /// across — so the model turns about whatever you are looking at, and everything else fades away
    /// outside a sphere round it rather than filling the room at five times its size.</para>
    ///
    /// <para><b>Idle.</b> Left alone the model sways gently either side of a three-quarter view, which
    /// reads as a hologram being displayed rather than a frozen one; touching the stick stops it until
    /// the demo has been left alone again for <see cref="m_IdleSeconds"/>.</para>
    /// </remarks>
    [DefaultExecutionOrder(10)]
    public class EarHologram : MonoBehaviour
    {
        static readonly int s_HoloTime = Shader.PropertyToID("_HoloTime");
        static readonly int s_HoloSweep = Shader.PropertyToID("_HoloSweep");
        static readonly int s_HoloFocus = Shader.PropertyToID("_HoloFocus");
        static readonly int s_Colour = Shader.PropertyToID("_Color");

        [Header("Stage")]
        [SerializeField] Transform m_Pivot;
        [SerializeField] Transform m_Anatomy;
        [SerializeField] Transform m_SectionPivot;
        [SerializeField] Renderer m_SectionMarker;
        [SerializeField] LineRenderer m_Callout;
        [SerializeField] TextMeshPro m_SectionTitle;
        [SerializeField] Transform[] m_Spinners = System.Array.Empty<Transform>();

        [SerializeField, Tooltip("Every part, in the order the Next button walks them.")]
        HologramPart[] m_Parts = System.Array.Empty<HologramPart>();

        [SerializeField] HologramPart m_Cochlea;
        [SerializeField] HologramLabels m_Labels;
        [SerializeField] HologramPointer m_Pointer;
        [SerializeField] HearingSimulation m_Hearing;
        [SerializeField] BalanceSimulation m_Balance;

        [Header("Info panel")]
        [SerializeField] Transform m_Panel;
        [SerializeField] TextMeshPro m_PanelTitle;
        [SerializeField] TextMeshPro m_PanelRegion;
        [SerializeField] TextMeshPro m_PanelBody;
        [SerializeField] TextMeshPro m_PanelStatus;
        [SerializeField] TextMeshPro m_PanelControls;

        [Header("Layout, in the room's space")]
        [SerializeField] Vector3 m_ModelHome = new(0.12f, 1.32f, 0.42f);
        [SerializeField, Tooltip("Where the model stands aside to while the section is up.")]
        Vector3 m_ModelBeside = new(-0.32f, 1.32f, 0.42f);
        [SerializeField] Vector3 m_SectionHome = new(0.9f, 1.45f, 0.5f);
        [SerializeField] Vector3 m_PanelHome = new(-1.5f, 1.45f, 0.15f);

        [SerializeField, Tooltip("Where to assume the viewer is when nothing is tracked.")]
        Vector3 m_ViewerFallback = new(0f, 1.65f, -0.5f);

        [SerializeField, Tooltip("Metres per millimetre of anatomy at zoom 1.")]
        float m_Scale = 0.016f;

        [SerializeField, Tooltip("The model-space point the overview frames, millimetres.")]
        Vector3 m_OverviewFocus;

        [SerializeField] float m_SectionHalfAngle = 6f;

        [SerializeField, Tooltip("Half-width and half-height, metres, of the ellipse the overview's labels stand on.")]
        Vector2 m_OverviewRing = new(0.66f, 0.55f);

        [Header("Motion")]
        [SerializeField] float m_HomeYaw = -25f;
        [SerializeField] float m_SwayDegrees = 25f;
        [SerializeField] float m_SwayPeriod = 45f;
        [SerializeField] float m_IdleSeconds = 20f;
        [SerializeField, Tooltip("Degrees per second at full stick.")]
        float m_TurnSpeed = 70f;
        [SerializeField] Vector2 m_ZoomRange = new(0.6f, 2.5f);

        [SerializeField, Tooltip("Metres a selected part's radius is framed to.")]
        float m_FocusSize = 0.22f;

        [SerializeField] Vector2 m_FocusZoomRange = new(1f, 5f);

        [SerializeField, Tooltip("While zoomed in, parts further than this from the focus (metres) fade out.")]
        float m_FocusSphere = 0.5f;

        [Header("Wand buttons")]
        [SerializeField] int m_SelectButton = 0;
        [SerializeField] int m_NextButton = 1;
        [SerializeField] int m_ExplodeButton = 2;
        [SerializeField] int m_SoundButton = 3;
        [SerializeField] int m_LabelsButton = 4;

        const string k_OverviewTitle = "The human ear";
        const string k_OverviewRegion = "OUTER  ·  MIDDLE  ·  INNER EAR";
        const string k_OverviewBody =
            "Three stages, each handing on to the next. The outer ear gathers sound; the middle ear's " +
            "three tiny bones turn air-borne vibration into force; the fluid-filled inner ear turns that " +
            "into nerve signals — while the loops beside it sense every turn of your head.";

        HologramPart m_Selected;
        bool m_Exploded;
        float m_Explode;
        float m_Yaw;
        float? m_TargetYaw;
        float m_LastInput = -1000f;
        float m_UserZoom = 1f;
        Vector3 m_Focus;
        float m_Zoom = 1f;
        float m_SectionShown;
        float m_Beside;
        Quaternion m_PanelRotation = Quaternion.identity;
        bool m_Initialised;
        string m_ShownPanel;

        ButtonLatch m_SelectLatch, m_NextLatch, m_ExplodeLatch, m_SoundLatch, m_LabelsLatch, m_ResetLatch;
        MaterialPropertyBlock m_Block;

        public HologramPart Selected => m_Selected;

        void Awake()
        {
            m_SelectLatch = new ButtonLatch(m_SelectButton);
            m_NextLatch = new ButtonLatch(m_NextButton);
            m_ExplodeLatch = new ButtonLatch(m_ExplodeButton);
            m_SoundLatch = new ButtonLatch(m_SoundButton);
            m_LabelsLatch = new ButtonLatch(m_LabelsButton);
            m_ResetLatch = new ButtonLatch(-1, Key.Backspace);
            Initialise();
        }

        void Initialise()
        {
            if (m_Initialised)
                return;

            m_Initialised = true;
            m_Yaw = m_HomeYaw;
            m_Focus = m_OverviewFocus;
        }

        void Update()
        {
            ReadInput(Time.time, Time.deltaTime);
            Tick(Viewer(), Time.time, Time.deltaTime);
        }

        Vector3 Viewer() => TrackedHead.TryGetPose(out var head, out _) ? head : m_ViewerFallback;

        // ================================================================== input

        void ReadInput(float time, float dt)
        {
            if (m_SelectLatch.Pressed())
            {
                Select(m_Pointer != null ? m_Pointer.Hovered : null);
                m_LastInput = time;
            }

            if (m_NextLatch.Pressed())
            {
                SelectNext();
                m_LastInput = time;
            }

            if (m_ExplodeLatch.Pressed())
                m_Exploded = !m_Exploded;

            if (m_SoundLatch.Pressed() && m_Hearing != null)
                m_Hearing.CycleTone();

            if (m_LabelsLatch.Pressed() && m_Labels != null)
                m_Labels.Mode = m_Labels.Mode switch
                {
                    LabelMode.Main => LabelMode.All,
                    LabelMode.All => LabelMode.Off,
                    _ => LabelMode.Main,
                };

            if (m_ResetLatch.Pressed())
                ResetView();

            float turn = MiddleVRWand.HorizontalAxis
                         + (ButtonLatch.Held(Key.RightArrow) ? 1f : 0f) - (ButtonLatch.Held(Key.LeftArrow) ? 1f : 0f);
            float zoom = MiddleVRWand.VerticalAxis
                         + (ButtonLatch.Held(Key.UpArrow) ? 1f : 0f) - (ButtonLatch.Held(Key.DownArrow) ? 1f : 0f);

            if (Mathf.Abs(turn) > 0.05f)
            {
                m_Yaw += Mathf.Clamp(turn, -1f, 1f) * m_TurnSpeed * dt;
                m_TargetYaw = null;
                m_LastInput = time;
            }

            if (Mathf.Abs(zoom) > 0.05f)
            {
                m_UserZoom = Mathf.Clamp(m_UserZoom * Mathf.Exp(Mathf.Clamp(zoom, -1f, 1f) * dt * 0.9f),
                    m_ZoomRange.x, m_ZoomRange.y);
                m_LastInput = time;
            }
        }

        /// <summary>
        /// Selects <paramref name="part"/>, or goes back to the overview for null, and turns the model to
        /// the angle the part is best seen from — the auricle's outer face, the drum down the canal, the
        /// cochlea along its axis — until the stick takes over.
        /// </summary>
        public void Select(HologramPart part)
        {
            m_Selected = part;
            if (part == null)
                m_TargetYaw = m_HomeYaw;
            else if (part.ViewYaw.HasValue)
                m_TargetYaw = part.ViewYaw.Value;
            else if (part.Group == HologramGroup.Section && m_Cochlea != null)
                m_TargetYaw = m_Cochlea.ViewYaw;
        }

        void SelectNext()
        {
            int index = System.Array.IndexOf(m_Parts, m_Selected);
            Select(index + 1 < m_Parts.Length ? m_Parts[index + 1] : null);
        }

        public void ResetView()
        {
            m_Selected = null;
            m_Exploded = false;
            m_UserZoom = 1f;
            m_Yaw = m_HomeYaw;
            m_TargetYaw = null;
            m_LastInput = -1000f;
            if (m_Hearing != null)
                m_Hearing.Stop();
            if (m_Labels != null)
                m_Labels.Mode = LabelMode.Main;
        }

        // ================================================================== frame

        /// <summary>
        /// Runs one frame of everything but input, for a viewer at <paramref name="viewer"/>. The still
        /// renderer calls it directly with a large <paramref name="dt"/> to snap every ease.
        /// </summary>
        public void Tick(Vector3 viewer, float time, float dt)
        {
            Initialise();
            var hovered = m_Pointer != null ? m_Pointer.Hovered : null;

            bool sectionWanted = m_Selected != null &&
                                 (m_Selected == m_Cochlea || m_Selected.Group == HologramGroup.Section)
                                 || m_Hearing != null && m_Hearing.On;
            m_SectionShown = Ease(m_SectionShown, sectionWanted ? 1f : 0f, 3f, dt);
            m_Beside = Ease(m_Beside, sectionWanted ? 1f : 0f, 2.2f, dt);
            m_Explode = Mathf.MoveTowards(m_Explode, m_Exploded ? 1f : 0f, dt / 1.2f);
            float explode = Mathf.SmoothStep(0f, 1f, m_Explode);

            foreach (var part in m_Parts)
                if (part != null)
                    part.Explode = explode;

            // ------------------------------------------------ framing

            Vector3 focus;
            float zoom;
            var framed = m_Selected != null && m_Selected.Group == HologramGroup.Section ? m_Cochlea : m_Selected;
            if (framed != null)
            {
                focus = framed.FocusInParent;
                zoom = Mathf.Clamp(m_FocusSize / (framed.FocusRadius * m_Scale), m_FocusZoomRange.x, m_FocusZoomRange.y);
            }
            else
            {
                // Standing aside for the section, the whole ear shrinks a little to keep clear of the panel;
                // exploded, it pulls back so the auricle, flung outward, stays out of the viewer's face.
                focus = m_OverviewFocus;
                zoom = Mathf.Lerp(1f, 0.78f, m_Beside) * Mathf.Lerp(1f, 0.82f, explode);
            }

            float frame = 1f - Mathf.Exp(-dt * 2.4f);
            m_Focus = Vector3.Lerp(m_Focus, focus, frame);
            m_Zoom = Mathf.Exp(Mathf.Lerp(Mathf.Log(m_Zoom), Mathf.Log(zoom), frame));

            if (m_TargetYaw.HasValue)
            {
                m_Yaw = Mathf.LerpAngle(m_Yaw, m_TargetYaw.Value, 1f - Mathf.Exp(-dt * 1.8f));
            }
            else if (time - m_LastInput > m_IdleSeconds)
            {
                float sway = m_HomeYaw + m_SwayDegrees * Mathf.Sin(time * Mathf.PI * 2f / m_SwayPeriod);
                m_Yaw = Mathf.LerpAngle(m_Yaw, sway, 1f - Mathf.Exp(-dt * 0.5f));
            }

            // A selection holds its angle; the overview's turn to home hands back to the sway once there.
            if (m_Selected == null && m_TargetYaw.HasValue && Mathf.Abs(Mathf.DeltaAngle(m_Yaw, m_TargetYaw.Value)) < 1f)
                m_TargetYaw = null;

            float scale = m_Scale * m_Zoom * m_UserZoom;
            m_Pivot.localPosition = Vector3.Lerp(m_ModelHome, m_ModelBeside, Mathf.SmoothStep(0f, 1f, m_Beside));
            m_Pivot.localRotation = Quaternion.Euler(0f, m_Yaw, 0f);
            m_Pivot.localScale = Vector3.one * scale;
            m_Anatomy.localPosition = -m_Focus;

            // ------------------------------------------------ section

            float shown = Mathf.SmoothStep(0f, 1f, m_SectionShown);
            if (m_SectionPivot != null)
            {
                bool visible = shown > 0.01f;
                if (m_SectionPivot.gameObject.activeSelf != visible)
                    m_SectionPivot.gameObject.SetActive(visible);

                var away = m_SectionPivot.position - viewer;
                away.y = 0f;
                if (away.sqrMagnitude > 1e-4f)
                {
                    // Turned so the cut face (at −half-angle) looks at the viewer, and rocked gently so
                    // the slice reads as a slice and not a picture of one.
                    float rock = 12f * Mathf.Sin(time * Mathf.PI * 2f / 16f);
                    m_SectionPivot.rotation = Quaternion.LookRotation(away) *
                                              Quaternion.AngleAxis(rock - m_SectionHalfAngle, Vector3.up);
                }

                m_SectionPivot.localPosition = m_SectionHome;
                m_SectionPivot.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, shown);
            }

            if (m_SectionMarker != null)
                Tint(m_SectionMarker, new Color(1f, 0.85f, 1f) * (0.9f * shown));

            if (m_Callout != null)
            {
                m_Callout.enabled = shown > 0.05f && m_SectionMarker != null;
                if (m_Callout.enabled)
                {
                    m_Callout.SetPosition(0, m_SectionMarker.transform.position);
                    m_Callout.SetPosition(1, m_SectionPivot.position + (viewer - m_SectionPivot.position).normalized * 0.05f
                                             - Vector3.Cross(Vector3.up, m_SectionPivot.position - viewer).normalized * 0.28f);
                    var c = new Color(1f, 0.8f, 1f, 0.5f * shown);
                    m_Callout.startColor = c;
                    m_Callout.endColor = c;
                }
            }

            if (m_SectionTitle != null)
                m_SectionTitle.alpha = shown;

            // ------------------------------------------------ parts

            float totalZoom = m_Zoom * m_UserZoom;
            bool focusing = totalZoom > 1.3f;
            var sphere = m_Pivot.position;

            foreach (var part in m_Parts)
            {
                if (part == null)
                    continue;

                part.Selected = part == m_Selected;
                part.Hovered = part == hovered;
                part.InFocus = !focusing || part.Group != HologramGroup.Ear ||
                               Vector3.Distance(part.FocusWorld, sphere) < m_FocusSphere * 1.1f;

                // Everything but the selection fades to a ghost; with a section part selected, the ear
                // keeps only the cochlea it was cut from.
                float visibility = part.Group == HologramGroup.Section ? shown : 1f;
                if (m_Selected != null && part != m_Selected &&
                    (part.Group == m_Selected.Group || part.Group == HologramGroup.Ear && part != m_Cochlea))
                    visibility *= 0.16f;

                part.TargetVisibility = visibility;
            }

            if (m_Labels != null)
            {
                m_Labels.Selected = m_Selected;
                m_Labels.Hovered = hovered;
                m_Labels.Spotlight = m_Hearing != null ? m_Hearing.StepParts : null;
                m_Labels.SetGroupVisibility(HologramGroup.Section, shown);
                m_Labels.SetDetail(HologramGroup.Ear, totalZoom > 1.6f || m_Explode > 0.5f ? 1f : 0f);

                float focused = Mathf.InverseLerp(1f, 2f, totalZoom);
                m_Labels.SetRing(HologramGroup.Ear, Vector2.Lerp(m_OverviewRing, new Vector2(0.4f, 0.36f), focused));
            }

            // The projector's rings turn slowly, alternate ones the other way.
            for (int i = 0; i < m_Spinners.Length; i++)
                if (m_Spinners[i] != null)
                    m_Spinners[i].localRotation = Quaternion.Euler(0f, time * (i % 2 == 0 ? 9f : -6f), 0f);

            // ------------------------------------------------ shader globals

            Shader.SetGlobalFloat(s_HoloTime, time);
            Shader.SetGlobalVector(s_HoloSweep, new Vector4(sphere.y - 0.6f, sphere.y + 0.6f, 7f, 0.018f));
            Shader.SetGlobalVector(s_HoloFocus, focusing
                ? new Vector4(sphere.x, sphere.y, sphere.z, m_FocusSphere)
                : Vector4.zero);

            UpdatePanel(viewer, dt);
        }

        static float Ease(float value, float target, float rate, float dt) =>
            Mathf.Lerp(value, target, 1f - Mathf.Exp(-dt * rate));

        void Tint(Renderer renderer, Color colour)
        {
            m_Block ??= new MaterialPropertyBlock();
            m_Block.SetColor(s_Colour, colour);
            renderer.SetPropertyBlock(m_Block);
            renderer.enabled = colour.maxColorComponent > 0.002f;
        }

        // ================================================================== panel

        void UpdatePanel(Vector3 viewer, float dt)
        {
            if (m_Panel == null)
                return;

            m_Panel.localPosition = m_PanelHome;
            var away = m_Panel.position - viewer;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-4f)
            {
                var target = Quaternion.LookRotation(away);
                m_PanelRotation = dt > 1f ? target : Quaternion.Slerp(m_PanelRotation, target, 1f - Mathf.Exp(-dt * 1.5f));
                m_Panel.rotation = m_PanelRotation;
            }

            string title = m_Selected != null ? m_Selected.DisplayName : k_OverviewTitle;
            string region = m_Selected != null ? m_Selected.Region.ToUpperInvariant() : k_OverviewRegion;
            string body = m_Selected != null ? m_Selected.Description : k_OverviewBody;
            string status = Status();

            string key = title + region + body + status;
            if (key == m_ShownPanel)
                return;

            m_ShownPanel = key;
            Set(m_PanelTitle, title);
            Set(m_PanelRegion, region);
            Set(m_PanelBody, body);
            Set(m_PanelStatus, status);
            Set(m_PanelControls,
                $"<b>TRIGGER</b> select   <b>{m_NextButton}</b> next   <b>{m_ExplodeButton}</b> explode   " +
                $"<b>{m_SoundButton}</b> sound   <b>{m_LabelsButton}</b> labels   <b>STICK</b> turn · zoom");
        }

        static void Set(TextMeshPro text, string value)
        {
            if (text != null && text.text != value)
                text.text = value;
        }

        readonly StringBuilder m_Status = new();

        string Status()
        {
            m_Status.Clear();

            if (m_Hearing != null && m_Hearing.On)
            {
                float hz = m_Hearing.Frequency;
                float peak = m_Hearing.Peak;
                string where = peak < 0.3f ? "near the base, where high pitches peak"
                    : peak > 0.7f ? "near the apex, where low pitches peak"
                    : "mid-way round the spiral";
                string section = m_Hearing.SectionResponse > 0.6f ? " — right at section A"
                    : m_Hearing.SectionResponse < 0.05f ? " — it dies before section A" : "";

                m_Status.Append("<color=#ffd27a><b>TONE  ").Append(hz >= 1000f ? $"{hz / 1000f:0.#} kHz" : $"{hz:0} Hz")
                    .Append("</b></color>   peaks ").Append(where).Append(section).Append('\n');

                for (int i = 0; i < m_Hearing.StepCount; i++)
                {
                    bool now = i == m_Hearing.StepIndex;
                    m_Status.Append(now ? "<color=#ffffff><b>› " : "<color=#6fa9c4>   ")
                        .Append(i + 1).Append(". ").Append(m_Hearing.StepText(i))
                        .Append(now ? "</b></color>\n" : "</color>\n");
                }

                return m_Status.ToString();
            }

            if (m_Balance != null)
            {
                int canal = m_Balance.Dominant(out float activity);
                if (activity > 0.25f)
                {
                    m_Status.Append("<color=#9fd0ff><b>BALANCE</b></color>   your head is ")
                        .Append(BalanceSimulation.CanalMotion(canal)).Append(" — the ")
                        .Append(BalanceSimulation.CanalName(canal)).Append(" senses it");
                    return m_Status.ToString();
                }
            }

            if (m_Selected == null)
                m_Status.Append("<color=#6fa9c4>Press <b>").Append(m_SoundButton)
                    .Append("</b> to send a tone through the ear. Turn or nod your head and watch the canals.</color>");

            return m_Status.ToString();
        }

        // ================================================================== stills

        /// <summary>
        /// For the still renderer: puts the demo straight into a state, without input. A null
        /// <paramref name="yaw"/> takes the selected part's own best angle, as selecting it would.
        /// </summary>
        public void Pose(HologramPart selected, bool exploded, float? yaw, float userZoom)
        {
            Initialise();
            Select(selected);
            if (yaw.HasValue)
                m_TargetYaw = yaw;
            m_Yaw = m_TargetYaw ?? m_HomeYaw;
            m_Exploded = exploded;
            m_Explode = exploded ? 1f : 0f;
            m_UserZoom = userZoom;
            m_LastInput = float.MaxValue;
        }
    }
}
