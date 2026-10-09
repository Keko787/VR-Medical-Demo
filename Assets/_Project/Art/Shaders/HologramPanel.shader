// Hologram Panel — the backing card for the ear demo's info panel and section title. Drawn from the
// quad's UVs alone: a faint wash that is brightest at the top, a thin border, brighter corner brackets,
// and the same world-space scanlines as the hologram so the panel reads as part of the same projection.
//
// _Size is the quad's size in metres, so the border and brackets stay a fixed physical width however
// the panel is proportioned.
Shader "VR Demo/Hologram Panel"
{
    Properties
    {
        _Color ("Colour", Color) = (0.35, 0.8, 1, 1)
        _Fill ("Fill", Range(0, 0.5)) = 0.07
        _Border ("Border", Range(0, 2)) = 0.45
        _Size ("Size (m)", Vector) = (0.8, 0.9, 0, 0)
        _BorderWidth ("Border width (m)", Float) = 0.003
        _Bracket ("Corner bracket length (m)", Float) = 0.06
        _ScanDensity ("Scanlines per metre", Float) = 160
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
        Cull Off

        Pass
        {
            Name "Panel"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Fill;
                half _Border;
                float4 _Size;
                float _BorderWidth;
                float _Bracket;
                float _ScanDensity;
            CBUFFER_END

            float _HoloTime;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Distance from each edge in metres.
                float2 metres = IN.uv * _Size.xy;
                float2 edge = min(metres, _Size.xy - metres);

                float border = step(min(edge.x, edge.y), _BorderWidth);

                // Brackets: thicker lines hugging each corner.
                float2 corner = step(edge, _Bracket);
                float bracket = corner.x * corner.y * step(min(edge.x, edge.y), _BorderWidth * 2.5);

                float wash = _Fill * lerp(0.55, 1.0, IN.uv.y);
                float scan = 0.85 + 0.15 * sin((IN.positionWS.y * _ScanDensity - _HoloTime * 0.6) * 6.2831853);

                float intensity = wash * scan + border * _Border * 0.5 + bracket * _Border;
                return half4(_Color.rgb * intensity, 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
