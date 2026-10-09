using System;
using UnityEngine;

namespace VRShootingGallery.AnatomyDemo
{
    /// <summary>
    /// How hearing works, played on the hologram: sound funnels down the canal, the eardrum and ossicles
    /// rock, the stapes pumps the oval window, a travelling wave runs up the cochlea and peaks at the place
    /// for the tone's pitch, and the cochlear nerve fires. <see cref="CycleTone"/> steps through a few
    /// tones and off again, and a real sine tone plays at each.
    /// </summary>
    /// <remarks>
    /// <para><b>Slow motion.</b> Nothing here moves at the tone's real frequency — a 2 kHz eardrum is a
    /// blur. Every mechanical motion runs at a <i>visual</i> rate of 1–4.5 Hz that rises with the tone,
    /// and the amplitudes are exaggerated about a thousandfold. What is kept true is the causal chain,
    /// the phase relationships (the round window bulges out as the stapes pushes in) and, above all,
    /// <i>where</i> each tone peaks.</para>
    ///
    /// <para><b>The ossicular chain is a lever, not three animations.</b> The malleus and incus rotate
    /// together about the axis from the malleus' anterior process to the incus' short process, by just
    /// enough that the umbo tracks the eardrum's centre. The stapes is then pushed by however far that
    /// rotation carries the tip of the incus, projected onto its own axis — so the piston's size and
    /// phase fall out of the geometry, lever ratio included.</para>
    ///
    /// <para><b>Place coding.</b> The travelling wave's peak is put where the Greenwood function says
    /// that frequency lives: x = log₁₀(f / 165.4 + 0.88) / 2.1, the fraction of the way from the apex.
    /// 125 Hz peaks near the apex and 8 kHz near the base. Its envelope builds slowly and dies within a
    /// few percent past the peak, as a real one does — which is why the magnified section, cut at the
    /// 2 kHz place, shakes hard at 2 kHz, a little at the lower tones whose waves pass through it on the
    /// way up, and not at all at 8 kHz, whose wave has died before it gets there.</para>
    /// </remarks>
    [DefaultExecutionOrder(20)]
    public class HearingSimulation : MonoBehaviour
    {
        /// <summary>One step of the walkthrough the info panel reads out, and the parts it lights.</summary>
        [Serializable]
        public struct Step
        {
            public string Text;
            public HologramPart[] Parts;
        }

        [Header("Ear")]
        [SerializeField] HologramPart m_Canal;
        [SerializeField] HologramPart m_Drum;
        [SerializeField] HologramPart m_Malleus;
        [SerializeField] HologramPart m_Incus;
        [SerializeField] HologramPart m_Stapes;
        [SerializeField] HologramPart m_OvalWindow;
        [SerializeField] HologramPart m_RoundWindow;
        [SerializeField] HologramPart m_Cochlea;
        [SerializeField] HologramPart m_CochlearNerve;
        [SerializeField] HologramPart m_Trunk;

        [Header("Section")]
        [SerializeField] HologramPart m_BasilarMembrane;
        [SerializeField] HologramPart m_OrganOfCorti;
        [SerializeField] HologramPart m_InnerHairCells;
        [SerializeField] HologramPart m_OuterHairCells;
        [SerializeField] HologramPart m_Tectorial;
        [SerializeField] HologramPart m_Ganglion;

        [SerializeField, Tooltip("Rings that travel in from outside the auricle to the canal.")]
        Renderer[] m_Wavefronts = Array.Empty<Renderer>();

        [Header("Geometry, in the model's millimetres")]
        [SerializeField] Vector3 m_AxisA;
        [SerializeField] Vector3 m_AxisB;
        [SerializeField] Vector3 m_Umbo;
        [SerializeField] Vector3 m_Lenticular;
        [SerializeField] Vector3 m_StapesAxis = Vector3.right;
        [SerializeField] Vector3 m_DrumNormal = Vector3.left;
        [SerializeField] Vector3 m_RoundWindowNormal = Vector3.left;
        [SerializeField] Vector3 m_SoundOrigin;
        [SerializeField] Vector3 m_CanalEntrance;

        [SerializeField, Tooltip("Where along the cochlea (0 base → 1 apex) the magnified section is cut.")]
        float m_SectionU = 0.47f;

        [Header("Tones")]
        [SerializeField] float[] m_Tones = { 125f, 500f, 2000f, 8000f };
        [SerializeField] bool m_PlayTone = true;
        [SerializeField, Range(0f, 1f)] float m_Volume = 0.12f;

        [Header("Walkthrough")]
        [SerializeField] Step[] m_Steps = Array.Empty<Step>();
        [SerializeField] float m_StepSeconds = 3.6f;

        [Header("Look")]
        [SerializeField, Tooltip("Travel of the umbo, millimetres. Exaggerated: a real eardrum moves nanometres.")]
        float m_DrumTravel = 0.45f;

        [SerializeField, Tooltip("Travel of the basilar membrane in the section, in canal radii.")]
        float m_MembraneTravel = 0.07f;

        [SerializeField] Color m_SoundColour = new(0.8f, 1f, 1f);
        [SerializeField] Color m_WaveColour = new(1f, 0.75f, 1f);
        [SerializeField] Color m_NerveColour = new(1f, 1f, 0.7f);

        static readonly int s_Colour = Shader.PropertyToID("_Color");

        int m_Tone = -1;
        float m_Frequency = 2000f;
        float m_Level;
        float m_StepClock;
        // False so the first idle frame clears: a scene saved mid-tone, or fresh from the builder, must
        // not leave a wavefront hanging at the eardrum.
        bool m_Cleared;
        AudioSource m_Audio;
        AudioClip[] m_Clips;
        MaterialPropertyBlock m_Block;
        HologramPart[] m_Riders;

        /// <summary>True while a tone is selected (it may still be fading in).</summary>
        public bool On => m_Tone >= 0;

        /// <summary>0–1: how far the simulation has faded in.</summary>
        public float Level => m_Level;

        /// <summary>The current tone, or the last one while it fades out.</summary>
        public float Frequency => m_Frequency;

        public int StepIndex { get; private set; }
        public int StepCount => m_Steps.Length;
        public string StepText(int index) => m_Steps[index].Text;

        /// <summary>The parts the current step is about, while a tone is on; otherwise null.</summary>
        public HologramPart[] StepParts => On && m_Steps.Length > 0 ? m_Steps[StepIndex].Parts : null;

        /// <summary>Where along the cochlea (0 base → 1 apex) the current tone peaks.</summary>
        public float Peak => PeakPlace(m_Frequency);

        /// <summary>How hard the magnified section is shaking, 0–1.</summary>
        public float SectionResponse => Envelope(m_SectionU, Peak);

        public void CycleTone()
        {
            m_Tone = m_Tone + 1 >= m_Tones.Length ? -1 : m_Tone + 1;
            if (m_Tone >= 0)
            {
                m_Frequency = m_Tones[m_Tone];
                if (m_Level < 0.05f)
                    m_StepClock = 0f;
            }
        }

        public void Stop() => m_Tone = -1;

        /// <summary>Selects tone <paramref name="index"/> (or none, for −1) straight away. For the still renderer.</summary>
        public void SetTone(int index, float level)
        {
            m_Tone = index;
            if (index >= 0)
                m_Frequency = m_Tones[index];
            m_Level = level;
        }

        /// <summary>
        /// Greenwood's place–frequency map for the human cochlea, turned round to run from the base:
        /// 0 at the base (20 kHz), 1 at the apex (20 Hz).
        /// </summary>
        public static float PeakPlace(float hz) => 1f - Mathf.Log10(hz / 165.4f + 0.88f) / 2.1f;

        /// <summary>The travelling wave's envelope at <paramref name="u"/>: a long rise, a sharp fall past the peak.</summary>
        static float Envelope(float u, float peak)
        {
            float d = u - peak;
            float width = d < 0f ? 0.32f : 0.04f;
            return Mathf.Exp(-(d * d) / (width * width));
        }

        void Update()
        {
            Simulate(Time.time, Time.deltaTime);
            UpdateAudio();
        }

        /// <summary>Advances and applies the simulation at <paramref name="time"/> — the same clock the shader reads.</summary>
        public void Simulate(float time, float dt)
        {
            m_Level = Mathf.MoveTowards(m_Level, On ? 1f : 0f, dt / 0.6f);
            float level = Mathf.SmoothStep(0f, 1f, m_Level);

            if (level <= 0.001f)
            {
                if (!m_Cleared)
                    Clear();
                return;
            }

            m_Cleared = false;

            // Faster tones, faster motion — within what the eye can follow.
            float visual = 1.1f + 0.55f * Mathf.Log(m_Frequency / 125f, 2f);
            float phase = time * visual * Mathf.PI * 2f;
            float peak = Peak;

            // ------------------------------------------------ walkthrough

            m_StepClock += dt;
            StepIndex = m_Steps.Length == 0 ? 0 : Mathf.FloorToInt(m_StepClock / m_StepSeconds) % m_Steps.Length;
            for (int s = 0; s < m_Steps.Length; s++)
                foreach (var part in m_Steps[s].Parts)
                    if (part != null)
                        part.Emphasis = 0f;
            if (m_Steps.Length > 0)
                foreach (var part in m_Steps[StepIndex].Parts)
                    if (part != null)
                        part.Emphasis = 0.55f * level;

            // ------------------------------------------------ outer ear

            Wave(m_Canal, m_SoundColour, new Vector4(0.9f * level, 0.11f, visual * 0.9f, 2f), Flat);

            for (int i = 0; i < m_Wavefronts.Length; i++)
            {
                var ring = m_Wavefronts[i];
                if (ring == null)
                    continue;

                float s = Mathf.Repeat(time * 0.45f + i / (float)m_Wavefronts.Length, 1f);
                var t = ring.transform;
                t.localPosition = Vector3.Lerp(m_SoundOrigin, m_CanalEntrance, s);
                t.localRotation = Quaternion.LookRotation(m_CanalEntrance - m_SoundOrigin);
                t.localScale = Vector3.one * Mathf.Lerp(24f, 4.5f, s);
                Tint(ring, m_SoundColour * (Mathf.Sin(s * Mathf.PI) * 0.8f * level));
            }

            // ------------------------------------------------ middle ear

            m_Drum.VibrateAxis = -m_DrumNormal;
            m_Drum.Vibrate = new Vector3(m_DrumTravel * level, visual, 0f);
            Wave(m_Drum, m_SoundColour, new Vector4(0.5f * level, 0.12f, visual, 2f), Flat);

            // The malleus and incus rock as one about the ossicular axis, just far enough that the umbo
            // follows the drum.
            var axis = (m_AxisB - m_AxisA).normalized;
            float perRadian = Vector3.Dot(Vector3.Cross(axis, m_Umbo - m_AxisA), -m_DrumNormal);
            float swing = Mathf.Abs(perRadian) > 1e-3f ? m_DrumTravel / perRadian : 0f;
            float angle = swing * level * Mathf.Sin(phase);
            var rotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, axis);
            m_Malleus.SetMotion(rotation, m_AxisA, Vector3.zero);
            m_Incus.SetMotion(rotation, m_AxisA, Vector3.zero);

            // The stapes goes wherever the tip of the incus pushes it, along its own axis.
            float pistonPerRadian = Vector3.Dot(Vector3.Cross(axis, m_Lenticular - m_AxisA), m_StapesAxis);
            float piston = swing * pistonPerRadian;
            m_Stapes.SetMotion(Quaternion.identity, Vector3.zero, m_StapesAxis * (piston * level * Mathf.Sin(phase)));

            // Oval window moves with the footplate; the round window bulges out as it goes in.
            float pistonPhase = piston < 0f ? Mathf.PI : 0f;
            m_OvalWindow.VibrateAxis = m_StapesAxis;
            m_OvalWindow.Vibrate = new Vector3(Mathf.Abs(piston) * level, visual, pistonPhase);
            m_RoundWindow.VibrateAxis = m_RoundWindowNormal;
            m_RoundWindow.Vibrate = new Vector3(Mathf.Abs(piston) * 1.5f * level, visual, pistonPhase);

            // ------------------------------------------------ inner ear

            m_Cochlea.SignalColor = m_WaveColour;
            m_Cochlea.Signal = new Vector4(1.3f * level, 0.045f, visual * 0.9f, 1.5f);
            m_Cochlea.SignalEnvelope = new Vector4(peak, 0.32f, 0.04f, 0.22f);

            float response = Envelope(m_SectionU, peak) * level;
            // Everything standing on the basilar membrane rides it.
            m_Riders ??= new[] { m_BasilarMembrane, m_OrganOfCorti, m_InnerHairCells, m_OuterHairCells };
            foreach (var part in m_Riders)
            {
                if (part == null)
                    continue;

                part.VibrateAxis = Vector3.up;
                part.Vibrate = new Vector3(m_MembraneTravel * response, visual, 0f);
            }

            // Hinged at the limbus rather than the lamina, the tectorial membrane travels less than the
            // organ beneath it — the shear that bends the stereocilia.
            if (m_Tectorial != null)
            {
                m_Tectorial.VibrateAxis = Vector3.up;
                m_Tectorial.Vibrate = new Vector3(m_MembraneTravel * 0.5f * response, visual, 0f);
            }

            // Hair cells depolarise on the upswing, when the stereocilia bend toward the tallest row.
            float firing = response * Mathf.Max(0f, Mathf.Sin(phase));
            if (m_InnerHairCells != null) m_InnerHairCells.Activity = firing;
            if (m_OuterHairCells != null) m_OuterHairCells.Activity = firing * 0.7f;
            if (m_Ganglion != null) m_Ganglion.Activity = response * 0.6f;

            // ------------------------------------------------ nerve

            Wave(m_CochlearNerve, m_NerveColour, new Vector4(1.1f * level, 0.07f, 1.4f, 6f), Flat);
            Wave(m_Trunk, m_NerveColour, new Vector4(0.9f * level, 0.06f, 1.4f, 6f), Flat);
        }

        static readonly Vector4 Flat = new(0.5f, 1000f, 1000f, 0f);

        static void Wave(HologramPart part, Color colour, Vector4 signal, Vector4 envelope)
        {
            if (part == null)
                return;

            part.SignalColor = colour;
            part.Signal = signal;
            part.SignalEnvelope = envelope;
        }

        void Tint(Renderer renderer, Color colour)
        {
            m_Block ??= new MaterialPropertyBlock();
            m_Block.SetColor(s_Colour, colour);
            renderer.SetPropertyBlock(m_Block);
            renderer.enabled = colour.maxColorComponent > 0.002f;
        }

        void Clear()
        {
            m_Cleared = true;
            foreach (var part in new[]
                     {
                         m_Canal, m_Drum, m_Malleus, m_Incus, m_Stapes, m_OvalWindow, m_RoundWindow, m_Cochlea,
                         m_CochlearNerve, m_Trunk, m_BasilarMembrane, m_OrganOfCorti, m_InnerHairCells,
                         m_OuterHairCells, m_Tectorial, m_Ganglion,
                     })
            {
                if (part == null)
                    continue;

                part.ClearSignals();
                part.ClearMotion();
            }

            foreach (var step in m_Steps)
                foreach (var part in step.Parts)
                    if (part != null)
                        part.Emphasis = 0f;

            foreach (var ring in m_Wavefronts)
                if (ring != null)
                    ring.enabled = false;
        }

        // ================================================================== audio

        /// <summary>
        /// A pure tone at the real frequency, faded with the animation. Each clip is exactly one second
        /// of a whole number of cycles, so it loops without a click.
        /// </summary>
        void UpdateAudio()
        {
            if (!m_PlayTone)
                return;

            if (m_Level <= 0f)
            {
                if (m_Audio != null && m_Audio.isPlaying)
                    m_Audio.Stop();
                return;
            }

            EnsureAudio();
            int index = Array.IndexOf(m_Tones, m_Frequency);
            if (index < 0)
                return;

            if (m_Audio.clip != m_Clips[index])
            {
                m_Audio.clip = m_Clips[index];
                m_Audio.Play();
            }
            else if (!m_Audio.isPlaying)
            {
                m_Audio.Play();
            }

            // Equal loudness is not equal amplitude: the ear is least sensitive at the extremes, and a
            // high tone at full amplitude is just unpleasant.
            float weight = m_Frequency < 300f ? 1.4f : m_Frequency > 4000f ? 0.45f : 1f;
            m_Audio.volume = m_Volume * weight * Mathf.SmoothStep(0f, 1f, m_Level);
        }

        void EnsureAudio()
        {
            if (m_Audio != null)
                return;

            m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Audio.loop = true;
            m_Audio.spatialBlend = 0f;
            m_Audio.volume = 0f;

            int rate = AudioSettings.outputSampleRate;
            m_Clips = new AudioClip[m_Tones.Length];
            for (int i = 0; i < m_Tones.Length; i++)
            {
                float hz = Mathf.Round(m_Tones[i]);
                var data = new float[rate];
                for (int n = 0; n < rate; n++)
                    data[n] = Mathf.Sin(2f * Mathf.PI * hz * n / rate);

                m_Clips[i] = AudioClip.Create($"Tone {hz} Hz", rate, 1, rate, false);
                m_Clips[i].SetData(data, 0);
            }

            // MiddleVR renders through cameras of its own and the template's listener may not survive
            // that; a tone nobody can hear is worse than a second listener warning.
            var listener = FindObjectOfType<AudioListener>();
            if (listener == null || !listener.enabled)
            {
                gameObject.AddComponent<AudioListener>();
                Debug.Log("[EarHologram] No active AudioListener in the scene — added one for the tones.", this);
            }
        }
    }
}
