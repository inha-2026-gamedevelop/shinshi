// 월백 P06 액체 손이 지나간 자리에 남는 웅덩이. 메시 로컬 Y(진행 방향)를 기준으로 앞쪽(_FrontY)까지만 드러나고 뒤쪽(_RearY)부터 마른다
// 앞뒤 경계는 로컬 XY 노이즈로 흔들어 직선으로 잘리지 않게 한다. 두 값은 WolbaekLiquidHandView가 매 프레임 넣는다
Shader "Shinshi/WolbaekLiquidPuddle"
{
    Properties
    {
        [Header(Color)]
        _ColorDeep("Deep Color", Color) = (0.001, 0.012, 0.07, 1)
        _ColorBody("Body Color", Color) = (0.003, 0.03, 0.14, 1)
        _ColorLight("Light Color", Color) = (0.007, 0.075, 0.32, 1)
        _ColorNoiseScale("Color Noise Scale", Float) = 0.9

        [Header(Surface)]
        _Smoothness("Smoothness", Range(0, 1)) = 0.9
        _BumpScale("Bump Strength", Range(0, 1)) = 0.12
        _BumpNoiseScale("Bump Noise Scale", Float) = 1.4

        [NoScaleOffset] _NoiseTex("Baked Noise (tile 8m)", 2D) = "gray" {}

        [Header(Reveal)]
        _FrontY("Front Y (local)", Float) = -2
        _RearY("Rear Y (local)", Float) = -2
        _EdgeWobble("Edge Wobble (m)", Float) = 2.6
        _EdgeNoiseScale("Edge Noise Scale", Float) = 1.1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+1" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _ColorDeep;
            float4 _ColorBody;
            float4 _ColorLight;
            float  _ColorNoiseScale;
            float  _Smoothness;
            float  _BumpScale;
            float  _BumpNoiseScale;
            float  _FrontY;
            float  _RearY;
            float  _EdgeWobble;
            float  _EdgeNoiseScale;
        CBUFFER_END

        // 픽셀마다 노이즈를 계산하지 않도록 미리 구운 타일형 노이즈 한 장을 읽는다, 한 타일이 가로세로 8m
        TEXTURE2D(_NoiseTex);
        SAMPLER(sampler_NoiseTex);

        #define NOISE_TILE_INVERSE 0.125

        float Fbm(float2 p)
        {
            return SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, p * NOISE_TILE_INVERSE, 0).r;
        }

        // 드러남 판정 값, 0보다 작으면 그리지 않는다
        float RevealValue(float2 localXY)
        {
            float y = localXY.y + (Fbm(localXY * _EdgeNoiseScale) - 0.5) * _EdgeWobble;
            return min(_FrontY - y, y - _RearY);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 localXY    : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.localXY    = input.positionOS.xy;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(RevealValue(input.localXY));

                float n = Fbm(input.localXY * _ColorNoiseScale);
                float3 albedo = lerp(_ColorDeep.rgb, _ColorBody.rgb, smoothstep(0.22, 0.5, n));
                albedo = lerp(albedo, _ColorLight.rgb, smoothstep(0.5, 0.7, n));

                // 큰 물결 높이를 노멀에 섞어 젖은 표면의 흐름을 만든다
                float2 e = float2(0.05, 0);
                float h0 = Fbm(input.localXY * _BumpNoiseScale);
                float hx = Fbm((input.localXY + e.xy) * _BumpNoiseScale);
                float hy = Fbm((input.localXY + e.yx) * _BumpNoiseScale);
                float3 nrm = normalize(input.normalWS);
                float3 up = abs(nrm.y) > 0.99 ? float3(1, 0, 0) : float3(0, 1, 0);
                float3 tx = normalize(cross(up, nrm));
                float3 ty = cross(nrm, tx);
                nrm = normalize(nrm - _BumpScale * ((hx - h0) / e.x * tx + (hy - h0) / e.x * ty) * 0.25);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = nrm;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.bakedGI = SampleSH(nrm);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = 0;
                surface.specular = half3(0, 0, 0);
                surface.smoothness = _Smoothness;
                surface.occlusion = 1;
                surface.alpha = 1;
                surface.normalTS = half3(0, 0, 1);

                return UniversalFragmentPBR(inputData, surface);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 localXY    : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.localXY = input.positionOS.xy;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(RevealValue(input.localXY));
                return 0;
            }
            ENDHLSL
        }
    }
}
