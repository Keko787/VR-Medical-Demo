// Hologram — URP shader for the ear's anatomy parts.
//
// Additive, two-sided and unlit: each surface is a faint tinted body with a bright Fresnel rim, so a
// part reads by its silhouette and you see straight through it to what is behind. That is the point
// of a hologram diagram — the ossicles show through the eardrum, the modiolus through the cochlea —
// and additive blending needs no sorting, which matters when four CAVE cameras each draw the model.
//
// Everything that moves is in world space or driven by the part's own UVs, never by the screen:
// scanlines are horizontal bands in world Y and the scan sweep rises through the room, so the four
// walls of the CAVE agree at their seams and both eyes see the same bands.
//
// Per-part state comes in through a MaterialPropertyBlock (HologramPart does this):
//   _State      x highlight (selected / hovered), y glow (activity), z visibility, w 1 if the zoom's
//               focus sphere applies to it (the ear does; the magnified section beside it does not)
//   _Signal     x strength, y wavelength in u, z speed in cycles/s (sign = direction), w sharpness
//   _SignalEnv  x peak u, y rise width, z fall width, w displacement along the normal
//   _Vibrate    x amplitude (object units), y frequency (Hz), z phase (radians)
//   _VibrateAxis  object-space direction of the vibration; profile is sin(pi * u)
//   _EndFade    fades the part in over u = x..y and out over u = z..w — a nerve that runs on out of the
//               diagram at one end or both
//
// Globals set by EarHologram every frame:
//   _HoloTime   the animation clock (seconds), shared with the C# side so transforms and shader agree
//   _HoloSweep  x bottom Y, y top Y, z period (s), w band width (m) of the rising scan
//   _HoloFocus  xyz world centre, w radius: parts fade out beyond it when zoomed in
Shader "VR Demo/Hologram"
{
    Properties
    {
        _Color ("Tint", Color) = (0.3, 0.85, 1, 1)
        _Body ("Body", Range(0, 0.5)) = 0.06
        _RimStrength ("Rim strength", Range(0, 3)) = 0.9
        _RimPower ("Rim power", Range(0.5, 8)) = 2.6
        _BackFace ("Back faces", Range(0, 1)) = 0.45
        _ScanDensity ("Scanlines per metre", Range(0, 400)) = 160
        _ScanStrength ("Scanline strength", Range(0, 1)) = 0.22
        _Intensity ("Intensity", Range(0, 4)) = 1

        _SignalColor ("Signal colour", Color) = (1, 1, 1, 1)
        _State ("State (highlight, glow, visibility, -)", Vector) = (0, 0, 1, 0)
        _Signal ("Signal (strength, wavelength, speed, sharpness)", Vector) = (0, 0.1, 1, 1)
        _SignalEnv ("Signal envelope (peak, rise, fall, displace)", Vector) = (0.5, 1000, 1000, 0)
        _Vibrate ("Vibrate (amplitude, Hz, phase, -)", Vector) = (0, 1, 0, 0)
        _VibrateAxis ("Vibrate axis", Vector) = (0, 1, 0, 0)
        _EndFade ("End fade (in from, in to, out from, out to)", Vector) = (-2, -1, 2, 3)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "Hologram"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Body;
                half _RimStrength;
                half _RimPower;
                half _BackFace;
                half _ScanDensity;
                half _ScanStrength;
                half _Intensity;
                half4 _SignalColor;
                float4 _State;
                float4 _Signal;
                float4 _SignalEnv;
                float4 _Vibrate;
                float4 _VibrateAxis;
                float4 _EndFade;
            CBUFFER_END

            float _HoloTime;
            float4 _HoloSweep;
            float4 _HoloFocus;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  signal     : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // The travelling pattern along u: a wave (sharpness 1) or a train of pulses (sharpness > 1),
            // shaped by an asymmetric envelope — long rise toward the peak, quick fall after it, the
            // way the cochlea's travelling wave builds and then dies just past its place.
            float SignalAt(float u)
            {
                if (_Signal.x <= 0.0)
                    return 0.0;

                float d = u - _SignalEnv.x;
                float width = d < 0.0 ? _SignalEnv.y : _SignalEnv.z;
                float envelope = exp(-(d * d) / max(width * width, 1e-6));

                float phase = u / max(_Signal.y, 1e-4) - _HoloTime * _Signal.z;
                float wave = pow(0.5 + 0.5 * sin(phase * 6.2831853), max(_Signal.w, 1.0));
                return _Signal.x * envelope * wave;
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 position = IN.positionOS.xyz;

                // Membrane vibration: largest mid-span, pinned at both ends (u = 0.5 is the middle of a
                // drum, whose rim is u = 1).
                float profile = sin(saturate(IN.uv.x) * 3.14159265);
                position += _VibrateAxis.xyz * (_Vibrate.x * profile * sin(_HoloTime * _Vibrate.y * 6.2831853 + _Vibrate.z));

                float signal = SignalAt(IN.uv.x);
                position += IN.normalOS * (signal * _SignalEnv.w);

                VertexPositionInputs positions = GetVertexPositionInputs(position);
                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv;
                OUT.signal = signal;
                return OUT;
            }

            half4 Frag(Varyings IN, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 normal = normalize(IN.normalWS) * (front ? 1.0 : -1.0);
                float3 view = normalize(GetCameraPositionWS() - IN.positionWS);
                float facing = saturate(dot(normal, view));
                float rim = pow(1.0 - facing, _RimPower);

                // A soft key from above and in front, so round forms read as round and not as outlines.
                float key = 0.55 + 0.45 * saturate(dot(normal, normalize(float3(0.25, 0.85, -0.45))));

                float scan = 1.0 - _ScanStrength * (0.5 + 0.5 * sin((IN.positionWS.y * _ScanDensity - _HoloTime * 0.6) * 6.2831853));

                float sweepY = lerp(_HoloSweep.x, _HoloSweep.y, frac(_HoloTime / max(_HoloSweep.z, 0.01)));
                float sweepD = (IN.positionWS.y - sweepY) / max(_HoloSweep.w, 1e-3);
                float sweep = exp(-sweepD * sweepD) * step(0.001, _HoloSweep.w);

                float highlight = _State.x;
                float glow = _State.y;

                float intensity = (_Body * key + rim * _RimStrength) * scan;
                intensity *= 1.0 + highlight * 1.0 + glow * 0.6;
                intensity += sweep * (0.12 + rim * 0.5);
                intensity += highlight * 0.18 * (0.6 + 0.4 * sin(_HoloTime * 5.0)) * (0.3 + rim);
                intensity *= front ? 1.0 : _BackFace;

                // Edges run a little whiter than the body: the look of light, not paint.
                float3 color = lerp(_Color.rgb, float3(1.0, 1.0, 1.0), saturate(rim * 0.35 + highlight * 0.15)) * intensity;
                color += _SignalColor.rgb * IN.signal * (0.35 + rim * 0.8) * (front ? 1.0 : _BackFace);

                // Visibility: the part's own fade, the tail fade along u, and the focus sphere.
                float visibility = _State.z;
                visibility *= smoothstep(_EndFade.x, _EndFade.y, IN.uv.x) * (1.0 - smoothstep(_EndFade.z, _EndFade.w, IN.uv.x));
                // A global nobody has set reads as zero; a zero radius means no focus, not "hide all".
                if (_HoloFocus.w > 0.0 && _State.w > 0.5)
                {
                    float focus = length(IN.positionWS - _HoloFocus.xyz);
                    visibility *= 1.0 - smoothstep(_HoloFocus.w, _HoloFocus.w * 1.4 + 0.02, focus);
                }

                return half4(color * visibility * _Intensity, 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
