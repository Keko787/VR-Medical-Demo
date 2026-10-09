// Bubble — URP particle shader that draws a soap bubble from the billboard's UVs alone, so the
// effect needs no texture asset: a soft-edged disc, a thin rim brightest where the room's light
// would glance off it, a faint glassy body, and a highlight dot up and to the left.
//
// Additive, weighted by alpha: bubbles over the dark floor glow rather than occlude, and additive
// particles need no sorting, which matters when four CAVE cameras each draw the same cloud.
// Vertex colour is the particle colour, so the particle system's colour-over-lifetime does the
// fade-in and the pop.
Shader "VR Demo/Bubble"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.55, 0.90, 1.00, 1)
        _RimWidth ("Rim width", Range(0.02, 0.5)) = 0.12
        _Body ("Body opacity", Range(0, 1)) = 0.10
        _Highlight ("Highlight", Range(0, 2)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }
        LOD 100

        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Bubble"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _RimWidth;
                half _Body;
                half _Highlight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // Centre the quad on the origin, radius 1 at the edge of the billboard.
                float2 p = IN.uv * 2.0 - 1.0;
                float r = length(p);

                // Soft outer edge, then a rim just inside it.
                half disc = 1.0h - smoothstep(0.90, 1.0, r);
                half rim = smoothstep(1.0 - _RimWidth * 2.0, 1.0 - _RimWidth * 0.5, r) * disc;

                // The rim is brighter down and to the right, opposite the highlight, the way a real
                // bubble carries the light around its far edge.
                float2 toward = normalize(float2(0.6, -0.8));
                half shade = 0.45h + 0.55h * saturate(dot(p / max(r, 1e-4), toward));

                // Highlight dot.
                float2 offset = p - float2(-0.35, 0.40);
                half spot = exp(-dot(offset, offset) * 28.0) * _Highlight;

                half body = _Body * disc * (1.0h - rim);
                half alpha = saturate(rim * shade + body + spot) * IN.color.a * _Tint.a;

                half3 rgb = lerp(_Tint.rgb * IN.color.rgb, half3(1.0h, 1.0h, 1.0h), saturate(spot));
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Particles/Unlit"
}
