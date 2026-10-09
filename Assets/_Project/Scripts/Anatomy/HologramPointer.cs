using UnityEngine;
using VRShootingGallery.TrackingDemo;

namespace VRShootingGallery.AnatomyDemo
{
    /// <summary>
    /// A beam from the wand that finds the part it is pointing at. The controller reads
    /// <see cref="Hovered"/> to light the part and its label, and selects it on the trigger.
    /// </summary>
    /// <remarks>
    /// The hologram is see-through, so the nearest thing on the ray is not always the thing being
    /// looked at: parts faded out by a selection or by the zoom's focus sphere are passed over as if
    /// they were not there, and so are parts that are not pickable. The beam still stops at the room's
    /// walls — a beam that ran on through the screen would read as a glitch.
    /// </remarks>
    [DefaultExecutionOrder(5)]
    public class HologramPointer : MonoBehaviour
    {
        [SerializeField] LineRenderer m_Beam;
        [SerializeField, Tooltip("Marks where the beam meets a part.")]
        Transform m_Dot;

        [SerializeField] float m_Length = 4f;

        readonly RaycastHit[] m_Hits = new RaycastHit[32];

        /// <summary>The part under the beam this frame, or null.</summary>
        public HologramPart Hovered { get; private set; }

        /// <summary>True while there is a wand to point with.</summary>
        public bool Active { get; private set; }

        public void Configure(LineRenderer beam, Transform dot)
        {
            m_Beam = beam;
            m_Dot = dot;
        }

        void Update()
        {
            Hovered = null;
            Active = TrackedHand.TryGetRay(out var ray);
            if (m_Beam != null)
                m_Beam.enabled = Active;

            if (!Active)
            {
                if (m_Dot != null)
                    m_Dot.gameObject.SetActive(false);
                return;
            }

            float wall = m_Length;
            float nearest = m_Length;
            int count = Physics.RaycastNonAlloc(ray, m_Hits, m_Length, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = m_Hits[i];
                if (!hit.collider.TryGetComponent<HologramPart>(out var part))
                {
                    wall = Mathf.Min(wall, hit.distance);
                    continue;
                }

                if (!part.Pickable || part.Visibility < 0.35f || hit.distance >= nearest)
                    continue;

                nearest = hit.distance;
                Hovered = part;
            }

            if (Hovered != null && nearest > wall)
                Hovered = null;

            float end = Hovered != null ? nearest : wall;
            if (m_Beam != null)
            {
                m_Beam.SetPosition(0, ray.origin);
                m_Beam.SetPosition(1, ray.GetPoint(end));
            }

            if (m_Dot != null)
            {
                m_Dot.gameObject.SetActive(Hovered != null);
                m_Dot.position = ray.GetPoint(end);
            }
        }
    }
}
