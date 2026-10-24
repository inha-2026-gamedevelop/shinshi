Shader "Shinshi/Dev/CritRadialBlur"
{
    // 치명타 순간 화면 중심에서 바깥으로 번지는 방사형 줌 블러. 풀스크린 패스 렌더러 기능이 쓴다.
    // 카메라 움직임에 기대지 않고 _Strength만큼 화면을 중심 쪽으로 끌어 샘플해 번지게 한다. 가장자리일수록 더 번진다
    Properties
    {
        _Strength("Strength", Range(0, 0.5)) = 0
        _Center("Center", Vector) = (0.5, 0.5, 0, 0)
        _ClearRadius("Clear Radius", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "CritRadialBlur"
            ZTest Always ZWrite Off Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define SAMPLE_COUNT 12

            float  _Strength;
            float4 _Center;
            float  _ClearRadius;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv    = input.texcoord;
                float2 toEdge = uv - _Center.xy;

                // 중심 근처는 또렷하게 두고 바깥으로 갈수록 번짐 폭을 키운다
                float  edge  = saturate((length(toEdge) - _ClearRadius) / max(0.0001, 0.7 - _ClearRadius));
                float2 step  = toEdge * (_Strength * edge) / SAMPLE_COUNT;

                half3 color = 0;
                for (int i = 0; i < SAMPLE_COUNT; ++i)
                {
                    color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - (step * i)).rgb;
                }

                return half4(color / SAMPLE_COUNT, 1);
            }
            ENDHLSL
        }
    }
}
