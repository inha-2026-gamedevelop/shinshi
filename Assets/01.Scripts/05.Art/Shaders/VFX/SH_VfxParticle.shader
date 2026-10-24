// 근접 연출용 파티클. 텍스처 A = 모양, 정점 색 x HDR 색이 최종 색이다.
// SlashArc와 같이 미리 곱한 알파를 내보내 블렌드 One One(가산)과 One OneMinusSrcAlpha(덮기)를 재질에서 고른다.
// URP Particles/Unlit 대신 직접 쓴 이유: 그 재질은 블렌드 모드를 에디터 GUI가 키워드로 설정하므로
// 스크립트로 만든 재질이 GUI를 거치지 않으면 블렌드가 맞지 않는다. ASE를 쓰지 않은 이유는 SlashArc와 같다
// 툰 표현(_TOON_VFX)은 재질에서 켤 때만 쓴다. 꺼진 재질은 예전과 같은 식으로 그린다.
// 툰 분기를 ASE로 만들지 않은 이유: ASE는 자신이 만든 그래프 정보가 든 셰이더만 열 수 있어 이 셰이더를 가져오지 못한다.
// ASE로 하려면 미리 곱한 알파, 블렌드 선택, 소프트 페이드를 그래프로 다시 만들고 기존 재질 결과가 같은지 모두 확인해야 해서 분기만 더했다
Shader "Shinshi/VFX/Particle"
{
    Properties
    {
        _BaseMap ("Mask (A)", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _SoftFade ("Soft Fade Distance (m)", Float) = 0.25
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1

        [Header(Toon)]
        [Toggle(_TOON_VFX)] _ToonVfx ("Toon", Float) = 0
        [IntRange] _ToonAlphaSteps ("Alpha Steps", Range(2, 4)) = 3
        _ToonAlphaSoft ("Step Softness", Range(0.005, 0.2)) = 0.03
        _ToonCoreThreshold ("Core Threshold", Range(0, 1)) = 0.7
        [HDR] _ToonCoreColor ("Core Color (A = Strength)", Color) = (1, 1, 1, 1)
        _ToonEdgeWidth ("Edge Width", Range(0, 0.5)) = 0
        _ToonEdgeColor ("Edge Color (A = Strength)", Color) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "VfxParticle"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ _TOON_VFX

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "ShinshiToonVfx.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Color;
                float  _SoftFade;
                float  _ToonAlphaSteps;
                float  _ToonAlphaSoft;
                float  _ToonCoreThreshold;
                float4 _ToonCoreColor;
                float  _ToonEdgeWidth;
                float4 _ToonEdgeColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float  eyeDepth   : TEXCOORD2;
                float4 color      : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.screenPos  = ComputeScreenPos(positions.positionCS);
                output.eyeDepth   = -positions.positionVS.z;
                output.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color      = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float  mask  = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
                float3 color = input.color.rgb * _Color.rgb;

            #if defined(_TOON_VFX)
                // 계단은 텍스처 모양에만 건다. 정점 알파(수명 페이드)는 그 뒤에 곱해 사라지는 흐름을 부드럽게 둔다
                color = ShinshiToonVfxColor(color, mask, _ToonCoreThreshold, _ToonCoreColor, _ToonEdgeWidth, _ToonEdgeColor, _ToonAlphaSoft);
                mask  = ShinshiToonVfxStepMask(mask, _ToonAlphaSteps, _ToonAlphaSoft);
            #endif

                float alpha = mask * input.color.a * _Color.a;

                float sceneDepth = LinearEyeDepth(SampleSceneDepth(input.screenPos.xy / input.screenPos.w), _ZBufferParams);
                alpha *= saturate((sceneDepth - input.eyeDepth) / max(_SoftFade, 0.0001));

                return half4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
