// Hologram Additive — unlit, additive light for everything in the ear demo that is not anatomy:
// leader lines, the wand's beam, the projector's rings and light cone, the sound wavefronts.
//
// Colour is _Color times the vertex colour (LineRenderer gradients come in that way), weighted by
// alpha. Two optional shapings along the mesh's UVs:
//   _FadeV     fades from full at uv.y = 0 to nothing at uv.y = 1 — the projector's cone, bright at the
//              emitter and gone by the time it reaches the model
//   _Streaks   x count round uv.x, y strength, z speed — the faint vertical streaks in the cone
// Additive and depth-tested without writing depth, like the hologram itself, so none of it needs
// sorting across the four CAVE cameras.
Shader "VR Demo/Hologram Additive"
{
    Properties
    {
        _Color ("Colour", Color) = (0.5, 0.9, 1, 1)
        _FadeV ("Fade along V", Range(0, 1)) = 0
        _FadePower ("Fade power", Range(0.2, 6)) = 1.5
        _Streaks ("Streaks (count, strength, speed, -)", Vector) = (0, 0, 0, 0)
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

        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Additive"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _FadeV;
                half _FadePower;
                float4 _Streaks;
            CBUFFER_END

            float _HoloTime;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half4 colour = _Color * IN.color;
                half fade = lerp(1.0h, pow(saturate(1.0 - IN.uv.y), _FadePower), _FadeV);
                half streak = 1.0h + _Streaks.y * (0.5h + 0.5h * sin((IN.uv.x * _Streaks.x + _HoloTime * _Streaks.z) * 6.2831853));
                return half4(colour.rgb, saturate(colour.a * fade * streak));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
