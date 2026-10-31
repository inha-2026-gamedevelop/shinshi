// 쿼터뷰 직교 카메라용 하늘. 직교 카메라는 모든 픽셀의 시선 방향이 같아 일반 스카이박스가 단색이 되므로
// 화면 높이 기준 그라디언트로 하늘을 그린다.
Shader "Shinshi/Environment/ScreenGradientSky"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.56, 0.72, 0.95, 1)
        _MiddleColor ("Middle Color", Color) = (0.84, 0.88, 0.98, 1)
        _BottomColor ("Bottom Color", Color) = (0.99, 0.93, 0.94, 1)
        _MiddleHeight ("Middle Height", Range(0.05, 0.95)) = 0.55
        _GlowColor ("Sun Glow Color", Color) = (1, 0.95, 0.85, 1)
        _GlowPosition ("Sun Glow Screen Position (XY)", Vector) = (0.72, 0.9, 0, 0)
        _GlowSize ("Sun Glow Size", Range(0.01, 2)) = 0.55
        _GlowStrength ("Sun Glow Strength", Range(0, 2)) = 0.45
        _DiscColor ("Disc Color", Color) = (0.9, 0.95, 1, 1)
        _DiscSize ("Disc Size (0 = Off)", Range(0, 0.2)) = 0
        _DiscSoftness ("Disc Edge Softness", Range(0.001, 0.05)) = 0.004
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _MiddleColor;
                half4 _BottomColor;
                float _MiddleHeight;
                half4 _GlowColor;
                float4 _GlowPosition;
                float _GlowSize;
                float _GlowStrength;
                half4 _DiscColor;
                float _DiscSize;
                float _DiscSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float height = screenUV.y;
                half3 lower = lerp(_BottomColor.rgb, _MiddleColor.rgb, smoothstep(0.0, _MiddleHeight, height));
                half3 color = lerp(lower, _TopColor.rgb, smoothstep(_MiddleHeight, 1.0, height));

                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 glowOffset = (screenUV - _GlowPosition.xy) * float2(aspect, 1.0);
                float glow = exp(-dot(glowOffset, glowOffset) / (_GlowSize * _GlowSize));
                color += _GlowColor.rgb * glow * _GlowStrength;

                // 해나 달 원반. 크기가 0이면 그리지 않는다
                float discDistance = length(glowOffset);
                float disc = 1.0 - smoothstep(_DiscSize - _DiscSoftness, _DiscSize, discDistance);
                disc *= step(0.0001, _DiscSize);
                color = lerp(color, _DiscColor.rgb, disc);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
