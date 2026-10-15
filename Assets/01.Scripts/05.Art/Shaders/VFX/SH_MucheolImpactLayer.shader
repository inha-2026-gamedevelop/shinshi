// 무철 타격 VFX 레이어 한 장. 스트레이트 알파 텍스처를 받아 프리멀티플라이로 블렌드한다.
// _EmissiveBoost는 블룸이 붙는 지면 외 레이어(균열, 충격파, 섬광, 뿌리)에 재질 단계에서 곱해 두는 고정 배율이고,
// _Opacity는 MucheolImpactVfxPlayer가 매 프레임 MaterialPropertyBlock으로 넣는 값이다
Shader "Shinshi/VFX/MucheolImpactLayer"
{
    Properties
    {
        _MainTex ("Layer (RGB color, A straight alpha)", 2D) = "white" {}
        [HDR] _Tint ("Tint (HDR, 블룸용 강도 포함)", Color) = (1,1,1,1)
        _EmissiveBoost ("Emissive Boost", Float) = 1
        _Opacity ("Opacity", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "ImpactLayer"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tint;
                float  _EmissiveBoost;
                float  _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv         = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half3 color = tex.rgb * _Tint.rgb * _EmissiveBoost;
                half alpha  = tex.a * _Tint.a * _Opacity;
                return half4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
