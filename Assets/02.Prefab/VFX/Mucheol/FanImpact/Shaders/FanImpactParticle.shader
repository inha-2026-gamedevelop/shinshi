// 무철 부채꼴 착탄용 파티클/바닥 표현. GroundRushParticle에서 갈라져 나왔고 아래 둘이 다르다
// _ZTest: 큰 글로우가 바닥에 잘리지 않게 Always로 둘 수 있다
// _RevealUV1: 켜면 UV1.x(중심에서의 거리)로 바깥으로 드러낸다. UV0는 무늬 텍스처 좌표로 따로 쓴다
Shader "GiwoongVFX/FanImpactParticle"
{
    Properties
    {
        _BaseMap ("Texture (RGBA)", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _SoftFade ("Soft Fade Distance (m)", Float) = 0.3
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Z Test", Float) = 4
        [Toggle] _ZWrite ("Z Write", Float) = 0
        [Toggle] _RevealUV1 ("Reveal By UV1.x", Float) = 0
        _Reveal ("Reveal", Range(0, 1)) = 1
        _RevealSoft ("Reveal Softness", Range(0.001, 0.2)) = 0.03
        _Fade ("Fade", Range(0, 1)) = 1
        [Toggle] _UseVertexColor ("Use Vertex Color", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "FanImpactParticle"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Color;
                float  _SoftFade;
                float  _RevealUV1;
                float  _Reveal;
                float  _RevealSoft;
                float  _Fade;
                float  _UseVertexColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float  revealCoord : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color      = input.color;
                output.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                output.screenPos  = ComputeScreenPos(output.positionCS);
                output.revealCoord = lerp(input.uv.x, input.uv1.x, _RevealUV1);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex  = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 vcol = lerp(half4(1, 1, 1, 1), input.color, _UseVertexColor);
                half4 col  = tex * vcol * _Color;

                half reveal = saturate((_Reveal - input.revealCoord) / _RevealSoft);
                col.a *= reveal * _Fade;

                float2 suv   = input.screenPos.xy / input.screenPos.w;
                float  scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float  diff  = scene - input.screenPos.w;
                col.a *= saturate(diff / max(_SoftFade, 0.0001));

                return col;
            }
            ENDHLSL
        }
    }
}
