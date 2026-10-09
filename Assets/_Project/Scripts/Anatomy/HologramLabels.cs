using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using VRShootingGallery.TrackingDemo;

namespace VRShootingGallery.AnatomyDemo
{
    /// <summary>How many of the diagram's labels are up.</summary>
    public enum LabelMode
    {
        /// <summary>The main structures — every part not marked minor.</summary>
        Main,

        /// <summary>Everything, down to the utricle and the chorda tympani.</summary>
        All,

        /// <summary>None, bar the part under the pointer and the one selected.</summary>
        Off,
    }

    /// <summary>What a label names.</summary>
    public enum LabelKind
    {
        /// <summary>A part, by its display name.</summary>
        Part,

        /// <summary>One of a part's landmarks — shown while that part is selected.</summary>
        Landmark,

        /// <summary>
        /// Several parts under one name ("Ossicles"), anchored on one of them and shown only in the
        /// overview, standing in for their individual labels until the view is close enough to fit them.
        /// </summary>
        Group,
    }

    /// <summary>One label: a part, one of its landmarks, or a group, and the objects that draw it.</summary>
    [Serializable]
    public class HologramLabel
    {
        public HologramPart Part;
        public LabelKind Kind;

        [Tooltip("Which of the part's landmarks this names, for a landmark label.")]
        public int Landmark = -1;

        public TextMeshPro Text;
        public LineRenderer Leader;
        public Transform Dot;

        [NonSerialized] public Vector2 Position;
        [NonSerialized] public float Alpha;
        [NonSerialized] public bool Placed;
        [NonSerialized] public int Side = 1;
        [NonSerialized] public int Style = -1;

        public bool IsLandmark => Kind == LabelKind.Landmark;

        public Vector3 AnchorWorld => IsLandmark ? Part.LandmarkWorld(Landmark) : Part.AnchorWorld;
    }

    /// <summary>A diagram whose labels are laid out together round its own centre.</summary>
    [Serializable]
    public class LabelGroup
    {
        public HologramGroup Group;
        public Transform Centre;

        [Tooltip("Half-width and half-height, in metres, of the ellipse the labels stand on.")]
        public Vector2 Ring = new(0.75f, 0.55f);

        [Tooltip("0 puts each label on the side its structure is on; 1 or -1 puts them all right or left — for " +
            "a diagram with something else standing beside it.")]
        public int Side;

        [NonSerialized] public float Visibility = 1f;

        /// <summary>Above 0.5 the view is close enough (zoomed or exploded) for the detail labels.</summary>
        [NonSerialized] public float Detail;
    }

    /// <summary>
    /// Lays out the hologram's labels the way a medical illustrator would: in two columns either side
    /// of the model, stacked so they never overlap, each joined to its structure by a leader line.
    /// </summary>
    /// <remarks>
    /// <para>The layout is redone every frame in a plane through the diagram's centre facing the viewer's
    /// head, so the columns stay left and right of the model and the text stays square on however the
    /// model turns and wherever the viewer walks. Each column stands just clear of what is actually on
    /// show — the bounds of the visible parts, projected into that plane — so it hugs a zoomed-in
    /// cochlea as closely as it clears the whole auricle. A label goes to the side its structure is on,
    /// at the height its structure is at; each column is then sorted by height and pushed apart to at
    /// least <see cref="m_LineSpacing"/>. Positions ease toward their targets, so a label that has to
    /// change its place slides there.</para>
    ///
    /// <para>Labels face the tracked head, not a camera: the four CAVE cameras share the head's position
    /// but not a view plane, and text turned to one camera's plane would kink at a wall seam — the same
    /// reason the head-tracking demo's bubbles use Facing billboards.</para>
    /// </remarks>
    [DefaultExecutionOrder(100)]
    public class HologramLabels : MonoBehaviour
    {
        [SerializeField] List<HologramLabel> m_Labels = new();
        [SerializeField] LabelGroup[] m_Groups = Array.Empty<LabelGroup>();

        [SerializeField, Tooltip("Least vertical distance between two labels in a column, metres.")]
        float m_LineSpacing = 0.062f;

        [SerializeField, Tooltip("Space between the end of a leader line and its text, metres.")]
        float m_TextGap = 0.015f;

        [SerializeField, Tooltip("How far a column stands clear of the model's visible extent, metres.")]
        float m_ColumnMargin = 0.07f;

        [SerializeField] float m_FontSize = 0.36f;
        [SerializeField] float m_LandmarkFontSize = 0.30f;
        [SerializeField] float m_SelectedFontSize = 0.48f;

        [SerializeField] Color m_TextColour = new(0.78f, 0.95f, 1f, 0.95f);
        [SerializeField] Color m_SelectedColour = new(1f, 1f, 1f, 1f);
        [SerializeField] Color m_LandmarkColour = new(1f, 0.86f, 0.55f, 0.95f);
        [SerializeField] Color m_LineColour = new(0.55f, 0.85f, 1f, 0.55f);

        [SerializeField, Tooltip("Width of a leader line, metres.")]
        float m_LineWidth = 0.0022f;

        [SerializeField, Tooltip("Diameter of the dot where a leader line lands, metres.")]
        float m_DotSize = 0.011f;

        readonly List<HologramLabel> m_Showing = new();
        readonly List<HologramLabel> m_Column = new();
        readonly HashSet<HologramPart> m_Measured = new();
        Vector2 m_ExtentX, m_ExtentY;

        public LabelMode Mode { get; set; } = LabelMode.Main;
        public HologramPart Selected { get; set; }
        public HologramPart Hovered { get; set; }

        /// <summary>
        /// While set, and nothing is selected, the ear's labels are only these parts — the hearing
        /// walkthrough's current step, so the labels follow the story rather than crowd it.
        /// </summary>
        public HologramPart[] Spotlight { get; set; }

        public IReadOnlyList<HologramLabel> Labels => m_Labels;

        public void SetRing(HologramGroup group, Vector2 ring)
        {
            foreach (var g in m_Groups)
                if (g.Group == group)
                    g.Ring = ring;
        }

        public void SetGroupVisibility(HologramGroup group, float visibility)
        {
            foreach (var g in m_Groups)
                if (g.Group == group)
                    g.Visibility = visibility;
        }

        public void SetDetail(HologramGroup group, float detail)
        {
            foreach (var g in m_Groups)
                if (g.Group == group)
                    g.Detail = detail;
        }

        /// <summary>Wired by the builder.</summary>
        public void Configure(List<HologramLabel> labels, LabelGroup[] groups)
        {
            m_Labels = labels;
            m_Groups = groups;
        }

        void LateUpdate()
        {
            if (TrackedHead.TryGetPose(out var head, out _))
                Layout(head, Time.deltaTime);
        }

        // ================================================================== visibility

        bool Wanted(HologramLabel label, LabelGroup group)
        {
            var part = label.Part;
            if (part == null || group.Visibility < 0.5f || part.Visibility < 0.45f || !part.InFocus)
                return false;

            if (part == Hovered && label.Kind == LabelKind.Part)
                return true;

            // Isolation applies inside the selected part's own diagram; the other diagram carries on
            // under the labels mode, which is what puts the section's labels up beside a selected cochlea.
            if (Selected != null && Selected.Group == part.Group)
                return part == Selected && label.Kind != LabelKind.Group;

            if (label.Kind == LabelKind.Landmark)
                return false;

            if (Spotlight != null && group.Group == HologramGroup.Ear && Mode != LabelMode.Off)
                return label.Kind == LabelKind.Part && Array.IndexOf(Spotlight, part) >= 0;

            bool detail = group.Detail > 0.5f;
            return Mode switch
            {
                LabelMode.All => label.Kind == LabelKind.Part,
                LabelMode.Main => label.Kind == LabelKind.Group
                    ? !detail
                    : part.Tier == 0 || (detail && part.Tier == 1),
                _ => false,
            };
        }

        // ================================================================== layout

        /// <summary>
        /// Lays every label out for a viewer at <paramref name="viewer"/>. <paramref name="dt"/> drives the
        /// easing; pass a large value to snap.
        /// </summary>
        public void Layout(Vector3 viewer, float dt)
        {
            float ease = 1f - Mathf.Exp(-dt * 7f);
            float fade = 1f - Mathf.Exp(-dt * 6f);

            foreach (var group in m_Groups)
            {
                if (group.Centre == null)
                    continue;

                var centre = group.Centre.position;
                var forward = centre - viewer;
                if (forward.sqrMagnitude < 1e-6f)
                    forward = Vector3.forward;
                forward.Normalize();

                var right = Vector3.Cross(Vector3.up, forward);
                if (right.sqrMagnitude < 1e-6f)
                    right = Vector3.right;
                right.Normalize();
                var up = Vector3.Cross(forward, right);

                Measure(group, centre, right, up);

                m_Showing.Clear();
                foreach (var label in m_Labels)
                {
                    if (label.Part == null || label.Part.Group != group.Group)
                        continue;

                    bool wanted = Wanted(label, group);
                    label.Alpha = Mathf.Lerp(label.Alpha, wanted ? 1f : 0f, fade);
                    if (!wanted && label.Alpha < 0.01f)
                    {
                        Hide(label);
                        continue;
                    }

                    // A little hysteresis, so a structure right on the centre line does not send its
                    // label flicking from column to column as the model turns.
                    float ax = Vector3.Dot(label.AnchorWorld - centre, right);
                    if (group.Side != 0)
                        label.Side = group.Side;
                    else if (!label.Placed || Mathf.Abs(ax) > 0.04f)
                        label.Side = ax >= 0f ? 1 : -1;

                    m_Showing.Add(label);
                }

                for (int side = -1; side <= 1; side += 2)
                {
                    m_Column.Clear();
                    foreach (var label in m_Showing)
                        if (label.Side == side)
                            m_Column.Add(label);

                    PlaceColumn(group, side, centre, right, up, ease);
                }
            }

            foreach (var label in m_Labels)
                if (label.Part != null && label.Text != null && label.Text.gameObject.activeSelf)
                    Draw(label, viewer);
        }

        void PlaceColumn(LabelGroup group, int side, Vector3 centre, Vector3 right, Vector3 up, float ease)
        {
            if (m_Column.Count == 0)
                return;

            var ring = group.Ring;
            float limit = Mathf.Max(ring.y, Mathf.Max(-m_ExtentY.x, m_ExtentY.y)) * 1.1f;

            // Straight columns, just clear of the model on this side — never tighter than the ring, so a
            // small part still spreads its labels out.
            float column = side > 0
                ? Mathf.Max(m_ExtentX.y, ring.x * 0.5f) + m_ColumnMargin
                : Mathf.Min(m_ExtentX.x, -ring.x * 0.5f) - m_ColumnMargin;

            // Where each label would like to be: level with its structure, stretched a little so the
            // leaders fan out rather than run flat.
            var desired = new float[m_Column.Count];
            for (int i = 0; i < m_Column.Count; i++)
            {
                float ay = Vector3.Dot(m_Column[i].AnchorWorld - centre, up);
                desired[i] = Mathf.Clamp(ay * 1.15f, -limit, limit);
            }

            var order = new int[m_Column.Count];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            Array.Sort(order, (a, b) => desired[a].CompareTo(desired[b]));

            var y = new float[m_Column.Count];
            for (int k = 0; k < order.Length; k++)
            {
                float want = desired[order[k]];
                y[k] = k == 0 ? want : Mathf.Max(want, y[k - 1] + m_LineSpacing);
            }

            // Pushed off the top: slide the column down and settle it from the top.
            float over = y[^1] - limit;
            if (over > 0f)
            {
                for (int k = 0; k < y.Length; k++)
                    y[k] -= over;
                for (int k = y.Length - 2; k >= 0; k--)
                    y[k] = Mathf.Min(y[k], y[k + 1] - m_LineSpacing);
            }

            for (int k = 0; k < order.Length; k++)
            {
                var label = m_Column[order[k]];
                float ax = Vector3.Dot(label.AnchorWorld - centre, right);
                float x = side > 0 ? Mathf.Max(column, ax + 0.05f) : Mathf.Min(column, ax - 0.05f);

                var target = new Vector2(x, y[k]);
                label.Position = label.Placed ? Vector2.Lerp(label.Position, target, ease) : target;
                label.Placed = true;

                var world = centre + right * label.Position.x + up * label.Position.y;
                Place(label, world, right * side);
            }
        }

        /// <summary>
        /// The extent, in the layout plane, of the group's parts that are on show — their renderers'
        /// bounds projected onto the plane's axes. Falls back to the ring when nothing is visible.
        /// </summary>
        void Measure(LabelGroup group, Vector3 centre, Vector3 right, Vector3 up)
        {
            m_ExtentX = new Vector2(float.MaxValue, float.MinValue);
            m_ExtentY = new Vector2(float.MaxValue, float.MinValue);
            m_Measured.Clear();

            foreach (var label in m_Labels)
            {
                var part = label.Part;
                if (part == null || part.Group != group.Group || !m_Measured.Add(part))
                    continue;

                if (part.Visibility < 0.3f || !part.InFocus || part.Renderer == null || !part.Renderer.enabled)
                    continue;

                var b = part.Renderer.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? b.min.x : b.max.x,
                        (c & 2) == 0 ? b.min.y : b.max.y,
                        (c & 4) == 0 ? b.min.z : b.max.z) - centre;
                    float x = Vector3.Dot(corner, right);
                    float y = Vector3.Dot(corner, up);
                    m_ExtentX = new Vector2(Mathf.Min(m_ExtentX.x, x), Mathf.Max(m_ExtentX.y, x));
                    m_ExtentY = new Vector2(Mathf.Min(m_ExtentY.x, y), Mathf.Max(m_ExtentY.y, y));
                }
            }

            if (m_ExtentX.x > m_ExtentX.y)
            {
                m_ExtentX = new Vector2(-group.Ring.x, group.Ring.x);
                m_ExtentY = new Vector2(-group.Ring.y, group.Ring.y);
            }
        }

        void Place(HologramLabel label, Vector3 world, Vector3 outward)
        {
            if (!label.Text.gameObject.activeSelf)
                label.Text.gameObject.SetActive(true);

            label.Text.transform.position = world + outward * m_TextGap;

            if (label.Leader != null)
            {
                label.Leader.SetPosition(0, label.AnchorWorld);
                label.Leader.SetPosition(1, world);
            }

            if (label.Dot != null)
                label.Dot.position = label.AnchorWorld;
        }

        void Draw(HologramLabel label, Vector3 viewer)
        {
            var text = label.Text;
            var t = text.transform;

            // TMP text reads from its −Z side: point +Z away from the viewer.
            var away = t.position - viewer;
            if (away.sqrMagnitude > 1e-6f)
                t.rotation = Quaternion.LookRotation(away, Vector3.up);

            // Right-hand column reads left-to-right from the leader; the left one ends at it.
            bool leftAligned = Vector3.Dot(t.position - label.Leader.GetPosition(1), t.right) >= 0f;
            var alignment = leftAligned ? TextAlignmentOptions.Left : TextAlignmentOptions.Right;
            var pivot = new Vector2(leftAligned ? 0f : 1f, 0.5f);
            if (text.alignment != alignment)
                text.alignment = alignment;
            if (text.rectTransform.pivot != pivot)
                text.rectTransform.pivot = pivot;

            int style = label.Part == Selected && label.Kind == LabelKind.Part ? 2 : label.IsLandmark ? 1 : 0;
            if (label.Part == Hovered && style == 0)
                style = 3;

            if (label.Style != style)
            {
                label.Style = style;
                text.fontSize = style switch
                {
                    2 => m_SelectedFontSize,
                    1 => m_LandmarkFontSize,
                    _ => m_FontSize,
                };
                text.fontStyle = style == 2 ? FontStyles.Bold : FontStyles.Normal;
            }

            var colour = style switch
            {
                2 or 3 => m_SelectedColour,
                1 => m_LandmarkColour,
                _ => m_TextColour,
            };
            colour.a *= label.Alpha;
            text.color = colour;

            if (label.Leader != null)
            {
                var line = style == 1 ? m_LandmarkColour : m_LineColour;
                line.a *= label.Alpha * (style >= 2 ? 1.6f : 1f);
                label.Leader.startColor = line;
                label.Leader.endColor = line * new Color(1f, 1f, 1f, 0.5f);
                label.Leader.widthMultiplier = m_LineWidth * (style >= 2 ? 1.6f : 1f);
            }

            if (label.Dot != null)
                label.Dot.localScale = Vector3.one * (m_DotSize * label.Alpha * (style >= 2 ? 1.4f : 1f));
        }

        static void Hide(HologramLabel label)
        {
            label.Placed = false;
            if (label.Text != null && label.Text.gameObject.activeSelf)
                label.Text.gameObject.SetActive(false);
        }
    }
}
