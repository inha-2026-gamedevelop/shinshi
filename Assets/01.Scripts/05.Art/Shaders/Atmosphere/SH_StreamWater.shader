// 마을 개울·연못 수면. 메시 UV의 V를 흐름 방향으로 보고 물결을 흘린다. 연못은 흐름 속도를 0에 가깝게 둔다.
Shader "Shinshi/Environment/StreamWater"
{
    Properties
    {
        _NoiseTex ("Ripple Noise", 2D) = "gray" {}
        _ShallowColor ("Shallow Color", Color) = (0.42, 0.72, 0.74, 1)
        _DeepColor ("Deep Color", Color) = (0.12, 0.36, 0.5, 1)
        _SkyColor ("Sky Reflection Color", Color) = (0.78, 0.86, 0.96, 1)
        _FoamColor ("Foam Color", Color) = (0.95, 0.97, 1, 1)
        _DepthRange ("Depth Range (m)", Float) = 1.2
        _FoamDistance ("Edge Foam Distance (m)", Float) = 0.35
        _RippleScale ("Ripple World Scale", Float) = 0.35
        _FlowSpeed ("Flow Speed", Float) = 0.6
        _RippleStrength ("Ripple Normal Strength", Range(0, 2)) = 0.7
        _Smoothness ("Specular Sharpness", Range(8, 512)) = 160
        _SpecularStrength ("Specular Strength", Range(0, 4)) = 1.4
        _Opacity ("Opacity", Range(0, 1)) = 0.88
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-50" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "StreamWater"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "ShinshiAtmosphereCommon.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _SkyColor;
                half4 _FoamColor;
                float _DepthRange;
                float _FoamDistance;
                float _RippleScale;
                float _FlowSpeed;
                float _RippleStrength;
                float _Smoothness;
                float _SpecularStrength;
                float _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            float SampleRipple(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv).a;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float time = _Time.y;
                // UV(가로, 흐름 방향) 기준으로 두 겹의 물결을 흘린다.
                float2 flowUV = input.uv * _RippleScale + float2(0, -time * _FlowSpeed * _RippleScale);
                float2 crossUV = input.uv * _RippleScale * 1.7 + float2(time * 0.02, -time * _FlowSpeed * _RippleScale * 0.6);
                float2 texel = float2(0.004, 0.004);
                float h0 = SampleRipple(flowUV) + SampleRipple(crossUV);
                float hx = SampleRipple(flowUV + float2(texel.x, 0)) + SampleRipple(crossUV + float2(texel.x, 0));
                float hy = SampleRipple(flowUV + float2(0, texel.y)) + SampleRipple(crossUV + float2(0, texel.y));
                float3 normalWS = normalize(float3((h0 - hx) * _RippleStrength * 8.0, 1.0, (h0 - hy) * _RippleStrength * 8.0));

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneDepth = ShinshiSceneEyeDepth(screenUV);
                float selfDepth = -TransformWorldToView(input.positionWS).z;
                float waterDepth = max(sceneDepth - selfDepth, 0.0);

                half3 color = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(waterDepth / _DepthRange));

                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), 4.0);
                color = lerp(color, _SkyColor.rgb, saturate(fresnel * 0.8 + 0.15));

                Light mainLight = GetMainLight();
                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                float specular = pow(saturate(dot(normalWS, halfDir)), _Smoothness) * _SpecularStrength;
                color += mainLight.color * specular;

                float foam = 1.0 - saturate(waterDepth / max(_FoamDistance, 0.001));
                foam *= step(0.45, SampleRipple(flowUV * 2.3) + foam * 0.5);
                color = lerp(color, _FoamColor.rgb, saturate(foam));

                float alpha = saturate(_Opacity * saturate(waterDepth / 0.15) + foam * 0.5 + specular);
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
