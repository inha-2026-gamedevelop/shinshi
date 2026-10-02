// 섬 둘레를 떠다니는 구름 덩어리 파티클. 텍스처 R=밀도, G=윗면 빛, B=가장자리 투과.
Shader "Shinshi/Environment/CloudPuff"
{
    Properties
    {
        _BaseMap ("Cloud Puff Atlas", 2D) = "black" {}
        _LitColor ("Lit Color", Color) = (1, 0.98, 0.95, 1)
        _ShadowColor ("Shadow Color", Color) = (0.74, 0.72, 0.88, 1)
        _RimColor ("Rim Color", Color) = (1, 0.93, 0.9, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.35
        _SunInfluence ("Sun Color Influence", Range(0, 1)) = 0.4
        _Opacity ("Opacity", Range(0, 1)) = 1
        _IntersectionFade ("Intersection Fade (m)", Float) = 8
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "CloudPuff"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "ShinshiAtmosphereCommon.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _LitColor;
                half4 _ShadowColor;
                half4 _RimColor;
                float _RimStrength;
                float _SunInfluence;
                float _Opacity;
                float _IntersectionFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : COLOR;
                float fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 puff = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

                Light mainLight = GetMainLight();
                half maxChannel = max(max(mainLight.color.r, mainLight.color.g), max(mainLight.color.b, 0.001));
                half3 sunTint = lerp(half3(1, 1, 1), saturate(mainLight.color / maxChannel), _SunInfluence);

                half3 color = lerp(_ShadowColor.rgb, _LitColor.rgb * sunTint, puff.g);
                color = lerp(color, _RimColor.rgb * sunTint, puff.b * _RimStrength);
                color *= input.color.rgb;

                float softFade = ShinshiSoftIntersection(input.positionCS, input.positionWS, _IntersectionFade);
                float alpha = puff.r * input.color.a * softFade * _Opacity;
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
