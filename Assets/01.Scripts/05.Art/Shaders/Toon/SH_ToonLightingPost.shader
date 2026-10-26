// 화면 공간 툰 조명과 경계선. ToonLightingRendererFeature가 불투명 렌더링 뒤 전체 화면에 한 번 그린다.
// 화면 노멀, 깊이, 메인 광원 그림자로 매끈한 조명 Ls와 계단 조명 Lc를 같은 식으로 계산하고 원래 색에 Lc / Ls를 곱한다.
// 원래 색에 비율을 곱하는 방식이라 완전한 셀 셰이딩이 아니라 제한된 조명 변형이다.
// ASE를 쓰지 않은 이유: 설치된 ASE에 URP 템플릿이 없고 전체 화면 블릿 템플릿도 없어 이 패스를 만들 수 없다
Shader "Hidden/Shinshi/ToonLightingPost"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "ToonLightingPost"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // x 조명 강도, y 램프 단계 수, z 단계 경계 폭, w 환경광 유지
            float4 _ToonLightingParams;
            // x 비율 하한, y 비율 상한, z 밝은 픽셀 보존 시작, w 보존 범위
            float4 _ToonRatioParams;
            // x 거리 페이드 시작(m), y 페이드 범위(m), z 노멀 경계 페이드 범위(m)
            float4 _ToonFadeParams;
            float4 _ToonShadowColor;
            float4 _ToonAmbientL0;
            // x 경계선 강도, y 노멀 경계, z 깊이 경계 임계, w 선 어둡기
            float4 _ToonEdgeParams;
            float4 _ToonLineTint;
            // x 안개 모드(0 없음, 1 선형, 2 지수, 3 지수 제곱), y 시작, z 끝, w 밀도
            float4 _ToonFogParams;

            #define TOON_MIN_LIGHT       0.001
            #define TOON_MIN_NORMAL_LEN  0.5
            #define TOON_MIN_EDGE_WIDTH  0.0001
            #define TOON_MIN_RANGE       0.0001

            bool IsSkyDepth(float rawDepth)
            {
            #if UNITY_REVERSED_Z
                return rawDepth <= 0.000001;
            #else
                return rawDepth >= 0.999999;
            #endif
            }

            // 0에서 1까지를 stepCount 단계로 나눈 계단. Ramp(0) = 0, Ramp(1) = 1
            float RampInteger(float value, float stepCount, float edgeWidth)
            {
                float width = max(edgeWidth, TOON_MIN_EDGE_WIDTH);
                float sum   = 0.0;
                for (int i = 1; i < 4; i++)
                {
                    if (i < stepCount)
                    {
                        float threshold = i / stepCount;
                        sum += smoothstep(threshold - width, threshold + width, value);
                    }
                }
                return sum / max(stepCount - 1.0, 1.0);
            }

            // 소수 단계는 아래 정수 단계와 위 정수 단계의 결과를 섞어 볼륨 전이 중 명암이 튀지 않게 한다
            float Ramp(float value, float steps, float edgeWidth)
            {
                float lower = floor(steps);
                float upper = min(lower + 1.0, 4.0);
                float blend = steps - lower;
                return lerp(RampInteger(value, lower, edgeWidth), RampInteger(value, upper, edgeWidth), blend);
            }

            float FogAmount(float eyeDepth)
            {
                float mode = _ToonFogParams.x;
                if (mode < 0.5)
                {
                    return 0.0;
                }
                if (mode < 1.5)
                {
                    return saturate((eyeDepth - _ToonFogParams.y) / max(_ToonFogParams.z - _ToonFogParams.y, TOON_MIN_RANGE));
                }
                if (mode < 2.5)
                {
                    return 1.0 - exp(-_ToonFogParams.w * eyeDepth);
                }
                float fog = _ToonFogParams.w * eyeDepth;
                return 1.0 - exp(-fog * fog);
            }

            float FadeByDistance(float eyeDepth, float start, float range)
            {
                return 1.0 - saturate((eyeDepth - start) / max(range, TOON_MIN_RANGE));
            }

            float3 ApplyToonLighting(float3 color, float2 uv, float rawDepth, float eyeDepth)
            {
                float intensity = _ToonLightingParams.x;
                if (intensity <= 0.0)
                {
                    return color;
                }

                float3 normal    = SampleSceneNormals(uv);
                float  normalLen = length(normal);
                if (normalLen < TOON_MIN_NORMAL_LEN)
                {
                    return color;
                }
                normal /= normalLen;

                float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                Light  mainLight  = GetMainLight(TransformWorldToShadowCoord(positionWS), positionWS, half4(1, 1, 1, 1));
                float3 lightColor = mainLight.color;
                if (Max3(lightColor.r, lightColor.g, lightColor.b) <= TOON_MIN_LIGHT)
                {
                    return color;
                }

                float  diffuse = saturate(dot(normal, mainLight.direction)) * mainLight.shadowAttenuation;
                float3 ambient = _ToonAmbientL0.rgb;
                float3 smooth  = ambient + lightColor * diffuse;
                float  ramp    = Ramp(diffuse, _ToonLightingParams.y, _ToonLightingParams.z);
                float3 stepped = (ambient * _ToonLightingParams.w + lightColor * ramp) * lerp(_ToonShadowColor.rgb, 1.0, ramp);

                float3 ratio = stepped / max(smooth, TOON_MIN_LIGHT);
                ratio = clamp(ratio, _ToonRatioParams.x, _ToonRatioParams.y);
                if (any(isnan(ratio)) || any(isinf(ratio)))
                {
                    ratio = 1.0;
                }

                float brightness = Max3(color.r, color.g, color.b);
                float keep       = 1.0 - saturate((brightness - _ToonRatioParams.z) / max(_ToonRatioParams.w, TOON_MIN_RANGE));
                float distance   = FadeByDistance(eyeDepth, _ToonFadeParams.x, _ToonFadeParams.y);
                float fog        = 1.0 - FogAmount(eyeDepth);
                float weight     = intensity * keep * distance * fog;

                return color * lerp(1.0, ratio, weight);
            }

            float EdgeAmount(float2 uv, float eyeDepth)
            {
                float intensity = _ToonEdgeParams.x;
                if (intensity <= 0.0)
                {
                    return 0.0;
                }

                float2 texel = _CameraDepthTexture_TexelSize.xy;
                float2 offsetX = float2(texel.x, 0.0);
                float2 offsetY = float2(0.0, texel.y);

                // 깊이 2차 차분을 깊이로 나눠 먼 곳의 바닥이 줄무늬가 되지 않게 한다
                float left   = LinearEyeDepth(SampleSceneDepth(uv - offsetX), _ZBufferParams);
                float right  = LinearEyeDepth(SampleSceneDepth(uv + offsetX), _ZBufferParams);
                float down   = LinearEyeDepth(SampleSceneDepth(uv - offsetY), _ZBufferParams);
                float up     = LinearEyeDepth(SampleSceneDepth(uv + offsetY), _ZBufferParams);
                float second = abs(left + right - 2.0 * eyeDepth) + abs(down + up - 2.0 * eyeDepth);
                float depthEdge = step(_ToonEdgeParams.z, second / max(eyeDepth, TOON_MIN_RANGE));

                // 빈 노멀끼리는 내적이 0이라 선으로 잘못 잡히므로 두 노멀이 모두 있을 때만 비교한다
                float3 center = SampleSceneNormals(uv);
                float3 nRight = SampleSceneNormals(uv + offsetX);
                float3 nUp    = SampleSceneNormals(uv + offsetY);
                float  normalEdge = 0.0;
                if (length(center) >= TOON_MIN_NORMAL_LEN && length(nRight) >= TOON_MIN_NORMAL_LEN && length(nUp) >= TOON_MIN_NORMAL_LEN)
                {
                    float similarity = min(dot(normalize(center), normalize(nRight)), dot(normalize(center), normalize(nUp)));
                    normalEdge = step(similarity, _ToonEdgeParams.y) * FadeByDistance(eyeDepth, 0.0, _ToonFadeParams.z);
                }

                float distance = FadeByDistance(eyeDepth, _ToonFadeParams.x, _ToonFadeParams.y);
                float fog      = 1.0 - FogAmount(eyeDepth);
                return max(depthEdge, normalEdge) * intensity * distance * fog;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv    = input.texcoord;
                float4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);

                float rawDepth = SampleSceneDepth(uv);
                if (IsSkyDepth(rawDepth))
                {
                    return color;
                }

                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                color.rgb = ApplyToonLighting(color.rgb, uv, rawDepth, eyeDepth);

                float  edge     = EdgeAmount(uv, eyeDepth);
                float3 lineMult = _ToonEdgeParams.w * _ToonLineTint.rgb;
                color.rgb *= lerp(float3(1.0, 1.0, 1.0), lineMult, edge);

                return color;
            }
            ENDHLSL
        }
    }
}
