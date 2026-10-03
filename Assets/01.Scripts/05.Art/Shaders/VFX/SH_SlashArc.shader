// 근접 베기 호 메시. U = 호 진행(0 시작, 1 끝), V = 폭(0 칼끝 쪽, 1 몸 쪽).
// 단면 텍스처 R = 몸통, G = 핵, B = 먹, A = 가장자리 정리. _ChannelMask로 층마다 한 채널을 고른다.
// 출력은 미리 곱한 알파라서 블렌드 One One이면 가산(아이보리), One OneMinusSrcAlpha면 덮기(먹)가 된다.
// ASE가 아니라 직접 쓴 이유: 에디터 노드 편집을 할 수 없는 자동화 환경에서 제작했고,
// 진행, 침식 값을 스크립트가 MaterialPropertyBlock으로 매 프레임 넣는 단순한 구조라 그래프 이점이 작다
Shader "Shinshi/VFX/SlashArc"
{
    Properties
    {
        _ProfileTex ("Profile (R Body, G Core, B Ink)", 2D) = "black" {}
        _NoiseTex ("Erode Noise", 2D) = "gray" {}
        _ChannelMask ("Channel Mask", Vector) = (1, 0, 0, 0)
        [HDR] _HeadColor ("Head Color", Color) = (1, 1, 1, 1)
        [HDR] _TailColor ("Tail Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Head ("Head (0..1)", Float) = 1
        _TailLength ("Tail Length", Float) = 1
        _HeadSoftness ("Head Softness", Range(0.001, 0.5)) = 0.04
        _TailWidth ("Tail Width Ratio", Range(0.05, 1)) = 0.35
        _Erode ("Erode", Range(0, 1)) = 0
        _ErodeSoftness ("Erode Softness", Range(0.01, 1)) = 0.15
        _NoiseScale ("Noise Scale (U, V)", Vector) = (3, 1, 0, 0)
        _NoiseOffset ("Noise Offset", Vector) = (0, 0, 0, 0)
        _SoftFade ("Soft Fade Distance (m)", Float) = 0.3
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "SlashArc"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_ProfileTex); SAMPLER(sampler_ProfileTex);
            TEXTURE2D(_NoiseTex);   SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ProfileTex_ST;
                float4 _ChannelMask;
                float4 _HeadColor;
                float4 _TailColor;
                float  _Intensity;
                float  _Opacity;
                float  _Head;
                float  _TailLength;
                float  _HeadSoftness;
                float  _TailWidth;
                float  _Erode;
                float  _ErodeSoftness;
                float4 _NoiseScale;
                float4 _NoiseOffset;
                float  _SoftFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float  eyeDepth   : TEXCOORD2;
                float4 color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.screenPos  = ComputeScreenPos(positions.positionCS);
                output.eyeDepth   = -positions.positionVS.z;
                output.uv         = input.uv;
                output.color      = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = input.uv.x;

                // 머리 뒤로 _TailLength만큼만 보인다. tailNorm은 머리 바로 뒤가 0, 꼬리 끝이 1
                float behind    = _Head - t;
                float tailNorm  = saturate(behind / max(_TailLength, 0.0001));
                float headMask  = saturate(behind / _HeadSoftness + 1.0);
                float tailMask  = 1.0 - smoothstep(0.5, 1.0, tailNorm);
                float alongMask = headMask * tailMask;

                // 꼬리 쪽은 폭이 좁아진다. V를 늘려 칼끝 쪽 단면만 남긴다
                float widthRatio = lerp(1.0, _TailWidth, tailNorm);
                float v          = input.uv.y / widthRatio;
                float4 profile   = SAMPLE_TEXTURE2D(_ProfileTex, sampler_ProfileTex, float2(1.0 - tailNorm, saturate(v)));
                float shape      = dot(profile, _ChannelMask) * profile.a * step(v, 1.0);

                float2 noiseUV = float2(t * _NoiseScale.x, input.uv.y * _NoiseScale.y) + _NoiseOffset.xy;
                float noise    = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV).r;

                // 밀도가 낮은 곳부터 먹는다. 모양 값을 곱해 얇은 가장자리가 먼저 사라진다
                float density   = shape * (0.35 + 0.65 * noise);
                float erodeMask = saturate((density - _Erode) / _ErodeSoftness);

                float alpha = alongMask * erodeMask * shape * _Opacity * input.color.a;

                float sceneDepth = LinearEyeDepth(SampleSceneDepth(input.screenPos.xy / input.screenPos.w), _ZBufferParams);
                alpha *= saturate((sceneDepth - input.eyeDepth) / max(_SoftFade, 0.0001));

                float3 color = lerp(_HeadColor.rgb, _TailColor.rgb, tailNorm) * _Intensity * input.color.rgb;
                return half4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
