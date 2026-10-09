// Ripple floor — URP forward shader for the RippleFloor mesh.
//
// The mesh carries the water's state in vertex colour: R is height (0 trough, 0.5 rest, 1 crest)
// and G is slope. This tints crests, troughs and foam from those, lights the surface with the main
// light and a sharp Blinn-Phong highlight, and adds a Fresnel sheen at grazing angles. The
// highlight and the sheen are the view-dependent part — they slide over the water as the viewer's
// head moves, which is exactly what a floor at floor level otherwise cannot show.
//
// No additional lights: this project's URP asset has them switched off. No shadow receiving: a
// water surface with a shadow across it reads as a pattern, not a shadow.
Shader "VR Demo/Ripple Floor"
{
    Properties
    {
        _DeepColor ("Trough", Color) = (0.004, 0.030, 0.070, 1)
        _RestColor ("Still water", Color) = (0.020, 0.110, 0.190, 1)
        _CrestColor ("Crest", Color) = (0.250, 0.800, 1.000, 1)
        _FoamColor ("Foam", Color) = (0.850, 1.000, 1.000, 1)
        _FoamStrength ("Foam strength", Range(0, 2)) = 0.8
        _CrestGlow ("Crest glow", Range(0, 2)) = 0.6
        _SpecularPower ("Specular power", Range(4, 512)) = 120
        _SpecularStrength ("Specular strength", Range(0, 3)) = 0.8
        _FresnelStrength ("Fresnel strength", Range(0, 1)) = 0.3
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

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Shared by every pass so the SRP batcher sees one UnityPerMaterial layout.
        CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor;
            half4 _RestColor;
            half4 _CrestColor;
            half4 _FoamColor;
            half _FoamStrength;
            half _CrestGlow;
            half _SpecularPower;
            half _SpecularStrength;
            half _FresnelStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogFactor = ComputeFogFactor(positions.positionCS.z);
                OUT.color = IN.color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half3 normal = normalize(IN.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                Light light = GetMainLight();

                half height = IN.color.r;
                half foam = saturate(IN.color.g * _FoamStrength);
                half crest = saturate((height - 0.5h) * 2.0h);
                half trough = saturate((0.5h - height) * 2.0h);

                // Rest colour in the middle, sliding to the trough colour below and the crest
                // colour above; foam whitens the steep slopes on top of that.
                half3 albedo = lerp(_RestColor.rgb, _DeepColor.rgb, trough);
                albedo = lerp(albedo, _CrestColor.rgb, crest);
                albedo = lerp(albedo, _FoamColor.rgb, foam);

                half ndl = saturate(dot(normal, light.direction));
                half3 halfway = normalize(light.direction + view);
                half specular = pow(saturate(dot(normal, halfway)), _SpecularPower) * _SpecularStrength;
                half fresnel = pow(1.0h - saturate(dot(normal, view)), 4.0h) * _FresnelStrength;
                half3 ambient = SampleSH(normal);

                half3 color = albedo * (ambient + light.color * ndl)
                            + light.color * specular
                            + _CrestColor.rgb * (fresnel + crest * crest * _CrestGlow);

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // Only used if a camera asks for a depth texture; the project's URP asset does not, but a
        // floor that vanished from the depth buffer the day someone switched it on would be a
        // puzzling bug.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Back
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half DepthFrag(DepthVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                return IN.positionCS.z;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
