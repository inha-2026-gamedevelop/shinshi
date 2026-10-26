Shader "Shinshi/Dev/CritRadialBlur"
{
    // 치명타 순간 화면 중심에서 바깥으로 번지는 방사형 줌 블러. 풀스크린 패스 렌더러 기능이 쓴다.
    // 카메라 움직임에 기대지 않고 _Strength만큼 화면을 중심 쪽으로 끌어 샘플해 번지게 한다. 가장자리일수록 더 번진다.
    // _Impact가 켜진 몇 프레임은 임팩트 프레임으로 그린다. 기본(_ImpactSoft 1)은 배경을 회색으로 어둡게 하고
    // _ImpactHotThreshold보다 밝은 곳(번쩍인 몸, 불똥)만 원래 색을 남긴다. 화면 밝기가 크게 뒤집히지 않아 눈이 덜 아프다.
    // 강한 방식(_ImpactSoft 0)은 배경을 _ImpactThreshold로 흑백을 가르고 밝은 곳을 강조색으로 칠하는 만화식 컷이다.
    // 같은 패스를 써서 공용 렌더러에 기능을 더하지 않는다
    Properties
    {
        _Strength("Strength", Range(0, 0.5)) = 0
        _Center("Center", Vector) = (0.5, 0.5, 0, 0)
        _ClearRadius("Clear Radius", Range(0, 1)) = 0.15
        _Impact("Impact", Range(0, 1)) = 0
        _ImpactThreshold("Impact Threshold", Range(0, 1)) = 0.12
        _ImpactHotThreshold("Impact Hot Threshold", Range(0, 2)) = 0.5
        _ImpactHotColor("Impact Hot Color", Color) = (1, 0.08, 0.05, 1)
        _ImpactInvert("Impact Invert", Range(0, 1)) = 1
        _ImpactSoft("Impact Soft", Range(0, 1)) = 1
        _ImpactDim("Impact Dim", Range(0, 1)) = 0.6
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
            #define POP_SOFTNESS 0.15

            float  _Strength;
            float4 _Center;
            float  _ClearRadius;
            float  _Impact;
            float  _ImpactThreshold;
            float  _ImpactHotThreshold;
            float4 _ImpactHotColor;
            float  _ImpactInvert;
            float  _ImpactSoft;
            float  _ImpactDim;

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

                color /= SAMPLE_COUNT;

                half luma = dot(color, half3(0.299, 0.587, 0.114));

                // 강한 방식: 배경은 흑백 두 색, 가장 밝은 곳은 강조색으로만 그려 한 프레임을 끊는다
                half split = (luma >= _ImpactThreshold) ? 1 : 0; // 위의 지역 변수 step이 같은 이름의 함수를 가린다
                split = lerp(split, 1 - split, _ImpactInvert);
                half3 hard = (luma >= _ImpactHotThreshold) ? (half3)_ImpactHotColor.rgb : split.xxx;

                // 부드러운 방식: 배경은 회색으로 어둡게 하고 밝은 곳만 원래 색을 남긴다
                half  pop  = smoothstep(_ImpactHotThreshold - POP_SOFTNESS, _ImpactHotThreshold + POP_SOFTNESS, luma);
                half3 soft = lerp(luma.xxx * _ImpactDim, color, pop);

                color = lerp(color, lerp(hard, soft, _ImpactSoft), _Impact);

                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
