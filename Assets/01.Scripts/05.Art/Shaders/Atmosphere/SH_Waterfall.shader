// 섬 가장자리에서 떨어지는 폭포 띠. UV U는 폭, V는 위(0)에서 아래(1)로 떨어지는 방향이다.
Shader "Shinshi/Environment/Waterfall"
{
    Properties
    {
        _NoiseTex ("Streak Noise", 2D) = "gray" {}
        _WaterColor ("Water Color", Color) = (0.72, 0.85, 0.92, 1)
        _FoamColor ("Foam Color", Color) = (0.97, 0.99, 1, 1)
        _FallSpeed ("Fall Speed", Float) = 0.55
        _StreakStretch ("Streak Stretch (U, V)", Vector) = (3, 0.18, 0, 0)
        _EdgeSoftness ("Side Edge Softness", Range(0.01, 0.5)) = 0.18
        _BottomFade ("Bottom Fade Start", Range(0, 1)) = 0.55
        _Opacity ("Opacity", Range(0, 1)) = 0.9
        _IntersectionFade ("Intersection Fade (m)", Float) = 0.6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-40" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Waterfall"
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
                half4 _WaterColor;
                half4 _FoamColor;
                float _FallSpeed;
                float4 _StreakStretch;
                float _EdgeSoftness;
                float _BottomFade;
                float _Opacity;
                float _IntersectionFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float time = _Time.y;
                float2 uv = input.uv;
                float2 streakUV = float2(uv.x * _StreakStretch.x, uv.y * _StreakStretch.y - time * _FallSpeed);
                float2 streakUV2 = float2(uv.x * _StreakStretch.x * 1.9 + 0.37, uv.y * _StreakStretch.y * 1.4 - time * _FallSpeed * 1.35);
                float streak = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, streakUV).g * 0.6
                             + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, streakUV2).a * 0.4;

                float side = smoothstep(0.0, _EdgeSoftness, uv.x) * smoothstep(0.0, _EdgeSoftness, 1.0 - uv.x);
                float bottom = 1.0 - smoothstep(_BottomFade, 1.0, uv.y);
                float lip = 1.0 - smoothstep(0.0, 0.06, uv.y);

                // 떨어질수록 공기가 섞여 하얗게 부서진다.
                float foamAmount = saturate(streak * 1.3 - 0.35 + uv.y * 0.6 + lip * 0.5);
                Light mainLight = GetMainLight();
                half3 color = lerp(_WaterColor.rgb, _FoamColor.rgb, foamAmount);
                color *= lerp(0.85, 1.05, saturate(dot(mainLight.direction, float3(0, 1, 0))));

                float softFade = ShinshiSoftIntersection(input.positionCS, input.positionWS, _IntersectionFade);
                float alpha = saturate(streak * 0.7 + 0.45) * side * bottom * softFade * _Opacity;
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
