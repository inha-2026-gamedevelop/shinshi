Shader "Shinshi/Dev/AshDissolve"
{
    // 몸이 재처럼 타들어 가며 사라지는 시험용 셰이더. _Dissolve가 오를수록 UV 잡음이 낮은 곳부터 사라진다.
    // 사라지는 경계에서는 불씨(Ember)가 빛나고, 그 앞쪽은 재(Ash) 색으로 까맣게 식는다.
    // 프로퍼티 이름은 URP Lit과 맞춰 두어 MaterialPropertyBlock의 _BaseColor가 그대로 먹는다
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Dissolve("Dissolve", Range(-1, 1.5)) = -1
        _NoiseScale("Noise Scale", Float) = 14
        _EdgeWidth("Ember Width", Range(0.001, 0.5)) = 0.1
        _AshWidth("Ash Width", Range(0.001, 0.8)) = 0.35
        [HDR] _EmberColor("Ember Color", Color) = (5, 1.4, 0.25, 1)
        _AshColor("Ash Color", Color) = (0.05, 0.045, 0.04, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half4  _EmberColor;
                half4  _AshColor;
                half   _Dissolve;
                half   _NoiseScale;
                half   _EdgeWidth;
                half   _AshWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash(i);
                float b = Hash(i + float2(1.0, 0.0));
                float c = Hash(i + float2(0.0, 1.0));
                float d = Hash(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float noise = ValueNoise(input.uv * _NoiseScale) * 0.65 + ValueNoise(input.uv * _NoiseScale * 2.3) * 0.35;
                float t     = noise - _Dissolve;
                clip(t);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;

                // 타는 경계에 가까울수록 재 색으로 식고, 경계 바로 옆은 불씨로 빛난다
                float ash   = 1.0 - saturate(t / _AshWidth);
                float ember = 1.0 - saturate(t / _EdgeWidth);
                albedo = lerp(albedo, _AshColor.rgb, ash);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light  mainLight   = GetMainLight(shadowCoord);
                half3  normalWS    = normalize(input.normalWS);
                half3  lighting    = mainLight.color * (saturate(dot(normalWS, mainLight.direction)) * mainLight.shadowAttenuation)
                                   + SampleSH(normalWS);

                half3 color = albedo * lighting + _EmberColor.rgb * (ember * ember);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
