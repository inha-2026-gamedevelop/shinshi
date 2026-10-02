// 섬 아래로 깔리는 구름 바다 한 층. 여러 높이에 겹쳐 두께를 만든다.
Shader "Shinshi/Environment/CloudLayer"
{
    Properties
    {
        _NoiseTex ("Cloud Noise (RGBA)", 2D) = "gray" {}
        _LargeScale ("Large Scale", Float) = 0.006
        _DetailScale ("Detail Scale", Float) = 0.021
        _LargeScroll ("Large Scroll (XY)", Vector) = (0.004, 0.0015, 0, 0)
        _DetailScroll ("Detail Scroll (XY)", Vector) = (-0.006, 0.003, 0, 0)
        _Coverage ("Coverage", Range(0, 1)) = 0.62
        _Softness ("Edge Softness", Range(0.01, 1)) = 0.28
        _LitColor ("Lit Color", Color) = (1, 0.98, 0.96, 1)
        _ShadowColor ("Shadow Color", Color) = (0.78, 0.76, 0.9, 1)
        _SunInfluence ("Sun Color Influence", Range(0, 1)) = 0.35
        _Opacity ("Opacity", Range(0, 1)) = 0.92
        _IntersectionFade ("Intersection Fade (m)", Float) = 6
        _FadeCenter ("Edge Fade Center (XZ)", Vector) = (0, 0, 0, 0)
        _FadeRadius ("Edge Fade Radius (Inner, Outer)", Vector) = (160, 260, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "CloudLayer"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

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
                float _LargeScale;
                float _DetailScale;
                float4 _LargeScroll;
                float4 _DetailScroll;
                float _Coverage;
                float _Softness;
                half4 _LitColor;
                half4 _ShadowColor;
                float _SunInfluence;
                float _Opacity;
                float _IntersectionFade;
                float4 _FadeCenter;
                float4 _FadeRadius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 worldXZ = input.positionWS.xz;
                float time = _Time.y;

                half4 large = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, worldXZ * _LargeScale + _LargeScroll.xy * time);
                half4 detail = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, worldXZ * _DetailScale + _DetailScroll.xy * time);

                // 큰 덩어리 위에 몽글한 billow를 얹어 적운처럼 보이게 한다.
                float shape = large.r * 0.55 + large.g * 0.2 + detail.b * 0.25;
                shape -= (1.0 - detail.a) * 0.08;
                float density = saturate((shape - (1.0 - _Coverage)) / _Softness);

                // 두꺼운 곳일수록 위로 솟아 햇빛을 더 받는다고 본다.
                float lit = saturate((shape - (1.0 - _Coverage)) / (_Softness * 2.2) + 0.15);
                Light mainLight = GetMainLight();
                half maxChannel = max(max(mainLight.color.r, mainLight.color.g), max(mainLight.color.b, 0.001));
                half3 sunTint = lerp(half3(1, 1, 1), saturate(mainLight.color / maxChannel), _SunInfluence);
                half3 color = lerp(_ShadowColor.rgb, _LitColor.rgb * sunTint, lit);

                float edgeDistance = distance(worldXZ, _FadeCenter.xy);
                float edgeFade = 1.0 - smoothstep(_FadeRadius.x, _FadeRadius.y, edgeDistance);
                float softFade = ShinshiSoftIntersection(input.positionCS, input.positionWS, _IntersectionFade);

                float alpha = density * edgeFade * softFade * _Opacity;
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
