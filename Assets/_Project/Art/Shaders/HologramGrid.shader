// Hologram Grid — the dark lab the ear demo stands in. An opaque, unlit lining for the CAVE's walls,
// floor and ceiling: near-black blue with a faint blueprint grid, and a glow pooled on the floor round
// the projector.
//
// Lines are drawn in world space from whichever two axes the surface lies along, so one material
// serves every wall and the grid runs continuously round the corners — which it has to, in a CAVE,
// where the corners are screen seams the eye is already hunting for. They are anti-aliased with
// screen-space derivatives so they stay one crisp pixel-ish line at any distance instead of shimmering.
//
// Opaque on purpose: it sits a few millimetres inside the template's walls and hides them, so the
// hologram's additive light lands on black rather than on the room's grey.
Shader "VR Demo/Hologram Grid"
{
    Properties
    {
        _Base ("Base", Color) = (0.006, 0.012, 0.024, 1)
        _Line ("Minor lines", Color) = (0.02, 0.06, 0.09, 1)
        _Major ("Major lines", Color) = (0.04, 0.13, 0.19, 1)
        _Cell ("Cell size (m)", Float) = 0.25
        _MajorEvery ("Major line every n cells", Float) = 4
        _LineWidth ("Line width (px)", Range(0.5, 4)) = 1.2
        _Glow ("Floor glow colour", Color) = (0.05, 0.22, 0.32, 1)
        _GlowCentre ("Floor glow centre (xz) and radius (w)", Vector) = (0, 0, 0.3, 1.1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "Grid"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Base;
                half4 _Line;
                half4 _Major;
                float _Cell;
                float _MajorEvery;
                float _LineWidth;
                half4 _Glow;
                float4 _GlowCentre;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
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
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            // Distance to the nearest grid line in pixels, per axis, from the derivative of the cell
            // coordinate — the standard anti-aliased grid.
            float GridLine(float2 coord, float width)
            {
                float2 grid = abs(frac(coord - 0.5) - 0.5) / max(fwidth(coord), 1e-5);
                return 1.0 - saturate(min(grid.x, grid.y) - width * 0.5 + 0.5);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 n = abs(normalize(IN.normalWS));
                float3 p = IN.positionWS;

                // The plane's two axes: floor and ceiling use xz, the side walls zy, front and back xy.
                float2 plane = n.y > 0.5 ? p.xz : (n.x > 0.5 ? p.zy : p.xy);

                float minor = GridLine(plane / _Cell, _LineWidth);
                float major = GridLine(plane / (_Cell * _MajorEvery), _LineWidth * 1.4);

                half3 colour = _Base.rgb;
                colour = lerp(colour, _Line.rgb, minor);
                colour = lerp(colour, _Major.rgb, major);

                // The pool of light round the projector, on the floor only.
                if (n.y > 0.5)
                {
                    float d = length(p.xz - _GlowCentre.xy);
                    float glow = exp(-(d * d) / max(_GlowCentre.w * _GlowCentre.w, 1e-4));
                    colour += _Glow.rgb * glow * (0.6 + 0.4 * minor);
                }
                else
                {
                    // Walls fall off toward the ceiling, which keeps the eye low, on the model.
                    colour *= lerp(1.0, 0.55, saturate(p.y / 2.2));
                }

                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
