// 지면 강타 레이어용. GroundRushParticle에 부채꼴 마스크(_HalfAngle)를 더했다. 레이어 UV 위쪽(+Z)이 정면이다.
// 무철 직선 지면 분출용 파티클/지면 표현. 텍스처 RGB x 정점 색 x HDR 색이 최종 색이고 알파는 텍스처 A x 정점 A x 색 A이다
// 블렌드는 재질에서 고른다(덮기 SrcAlpha OneMinusSrcAlpha, 가산 SrcAlpha One)
// _Reveal은 UV.x 방향으로 앞에서부터 드러내는 값(균열 스트립용), 파티클 재질은 1로 둔다
Shader "GiwoongVFX/GroundSlamLayer"
{
    Properties
    {
        _BaseMap ("Texture (RGBA)", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _SoftFade ("Soft Fade Distance (m)", Float) = 0.3
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Toggle] _ZWrite ("Z Write", Float) = 0
        _Reveal ("Reveal (UV.x)", Range(0, 1)) = 1
        _RevealSoft ("Reveal Softness", Range(0.001, 0.2)) = 0.03
        _Fade ("Fade", Range(0, 1)) = 1
        [Toggle] _UseVertexColor ("Use Vertex Color", Float) = 1
        _HalfAngle ("Fan Half Angle (deg, 180=circle)", Range(1, 180)) = 180
        _FanSoft ("Fan Edge Softness (deg)", Range(0.1, 30)) = 4
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "GroundRushParticle"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
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
                float  _Reveal;
                float  _RevealSoft;
                float  _Fade;
                float  _UseVertexColor;
                float  _HalfAngle;
                float  _FanSoft;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color      = input.color;
                output.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                output.screenPos  = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                // 균열·링처럼 정점 색이 없는 메시는 색이 0으로 들어오므로 끈다
                half4 vcol = lerp(half4(1, 1, 1, 1), input.color, _UseVertexColor);
                half4 col = tex * vcol * _Color;

                // 앞에서부터 드러냄: UV.x가 _Reveal보다 작은 부분만 보인다
                half reveal = saturate((_Reveal - input.uv.x) / _RevealSoft);
                col.a *= reveal * _Fade;

                // 부채꼴: 중심(0.5,0.5)에서 정면(+V)과의 각도가 반각 밖이면 지운다
                float2 d = input.uv - 0.5;
                float ang = degrees(abs(atan2(d.x, d.y)));
                col.a *= lerp(saturate((_HalfAngle - ang) / _FanSoft), 1.0, step(179.5, _HalfAngle));

                // 바닥과 겹치는 가장자리를 부드럽게
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
