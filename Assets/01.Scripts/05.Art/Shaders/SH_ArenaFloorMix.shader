// 바닥 한 벌의 반복 무늬를 깨기 위해 기본 타일 위에 때(Dirt)와 풍화(Weathering) 텍스처를 큰 얼룩 마스크로 섞는다.
// 모두 월드 XZ 평면 매핑이라 서로 다른 메시(안쪽 원판과 바깥 링)도 무늬가 이어진다. 평평한 바닥 전용이다
Shader "Shinshi/ArenaFloorMix"
{
    Properties
    {
        [Header(Base)]
        _BaseColorMap("Base Color", 2D) = "white" {}
        _NormalMap("Normal", 2D) = "bump" {}
        _RoughnessMap("Roughness", 2D) = "white" {}
        _AOMap("Ambient Occlusion", 2D) = "white" {}
        _BaseTint("Base Tint", Color) = (1, 1, 1, 1)
        _TileSize("Base Tile Size (m)", Float) = 5

        [Header(Per Tile Variation)]
        _TilesPerRepeat("Tiles Per Texture Repeat", Float) = 4
        _TileGridOffset("Grout Offset (uv)", Vector) = (0.135, 0.135, 0, 0)
        _TileVariation("Per Tile Variation", Range(0, 1)) = 0.7

        [Header(Dirt)]
        _DirtColorMap("Dirt Color", 2D) = "gray" {}
        _DirtNormalMap("Dirt Normal", 2D) = "bump" {}
        _DirtRoughnessMap("Dirt Roughness", 2D) = "white" {}
        _DirtTint("Dirt Tint", Color) = (1, 1, 1, 1)
        _DirtSize("Dirt Tile Size (m)", Float) = 4

        [Header(Weathering)]
        _DamageColorMap("Weathering Color", 2D) = "gray" {}
        _DamageNormalMap("Weathering Normal", 2D) = "bump" {}
        _DamageRoughnessMap("Weathering Roughness", 2D) = "white" {}
        _DamageTint("Weathering Tint", Color) = (1, 1, 1, 1)
        _DamageSize("Weathering Tile Size (m)", Float) = 4

        [Header(Mesh UV Mode)]
        [Toggle] _UseMeshUV("Use Mesh UV (falling pieces)", Float) = 0
        _MeshUVScale("Mesh UV To Metres", Float) = 44
        _MeshUVOffset("Mesh UV Offset (m)", Float) = -22

        [Header(Emission)]
        _EmissionMap("Emission Map", 2D) = "black" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)

        [Header(Mix)]
        _MaskMap("Blotch Mask", 2D) = "black" {}
        _MacroSize("Blotch Size (m)", Float) = 16
        _DirtThreshold("Dirt Threshold", Range(0, 1)) = 0.2
        _DirtStrength("Dirt Strength", Range(0, 1)) = 0.8
        _DamageThreshold("Weathering Threshold", Range(0, 1)) = 0.3
        _DamageStrength("Weathering Strength", Range(0, 1)) = 0.8
        _Softness("Edge Softness", Range(0.01, 0.5)) = 0.12
        _NormalStrength("Normal Strength", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseTint;
            float4 _DirtTint;
            float4 _DamageTint;
            float  _TileSize;
            float  _TilesPerRepeat;
            float4 _TileGridOffset;
            float  _TileVariation;
            float  _DirtSize;
            float  _DamageSize;
            float  _MacroSize;
            float  _DirtThreshold;
            float  _DirtStrength;
            float  _DamageThreshold;
            float  _DamageStrength;
            float  _Softness;
            float  _NormalStrength;
            float  _UseMeshUV;
            float  _MeshUVScale;
            float  _MeshUVOffset;
            float4 _EmissionColor;
        CBUFFER_END

        TEXTURE2D(_BaseColorMap);      TEXTURE2D(_NormalMap);       TEXTURE2D(_RoughnessMap);       TEXTURE2D(_AOMap);
        TEXTURE2D(_DirtColorMap);      TEXTURE2D(_DirtNormalMap);   TEXTURE2D(_DirtRoughnessMap);
        TEXTURE2D(_DamageColorMap);    TEXTURE2D(_DamageNormalMap); TEXTURE2D(_DamageRoughnessMap);
        TEXTURE2D(_MaskMap);
        TEXTURE2D(_EmissionMap);
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
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor  = ComputeFogFactor(positions.positionCS.z);
                output.uv         = input.uv;
                return output;
            }

            // 원본 KT 텍스처는 노멀, 거칠기, 마스크도 sRGB로 임포트돼 있어 샘플이 감마 해제된다. 원래 값으로 되돌려 쓴다
            float3 Raw(float3 sampled)
            {
                return LinearToSRGB(sampled);
            }

            float3 SampleNormalTS(TEXTURE2D_PARAM(tex, smp), float2 uv)
            {
                float3 n = Raw(SAMPLE_TEXTURE2D(tex, smp, uv).rgb) * 2.0 - 1.0;
                n.xy *= _NormalStrength;
                return n;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float2 Rotate(float2 p, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                return float2(c * p.x - s * p.y, s * p.x + c * p.y);
            }

            float Blotch(float2 p, float2 offset, float angle, float threshold)
            {
                // 큰 얼룩 위에 2.7배 촘촘한 얼룩을 섞어 가장자리가 자연스럽게 흐트러지게 한다
                float2 uv = Rotate(p / _MacroSize, angle) + offset;
                float macro  = Raw(SAMPLE_TEXTURE2D(_MaskMap, sampler_LinearRepeat, uv).rrr).r;
                float detail = Raw(SAMPLE_TEXTURE2D(_MaskMap, sampler_LinearRepeat, uv * 2.7 + offset.yx).rrr).r;
                float n = macro * 0.7 + detail * 0.3;
                return smoothstep(threshold - _Softness, threshold + _Softness, n);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // 떨어지는 조각은 월드 좌표를 쓰면 무늬가 미끄러지므로 메시 UV(쉬는 자리의 월드 XZ)로 샘플한다
                bool useMeshUV = _UseMeshUV > 0.5;
                float2 p = useMeshUV ? input.uv * _MeshUVScale + _MeshUVOffset : input.positionWS.xz;

                float dirtW   = Blotch(p, float2(0.13, 0.71), 0.0,  _DirtThreshold)   * _DirtStrength;
                float damageW = Blotch(p, float2(0.57, 0.29), 0.65, _DamageThreshold) * _DamageStrength;

                float2 baseUV   = p / _TileSize;
                float2 dirtUV   = p / _DirtSize;
                float2 damageUV = p / _DamageSize;

                float3 baseColor   = SAMPLE_TEXTURE2D(_BaseColorMap,   sampler_LinearRepeat, baseUV).rgb * _BaseTint.rgb;

                // 줄눈이 타일 사이에 오도록 격자를 맞춰 타일마다 밝기와 색, 거칠기를 조금씩 다르게 한다
                float2 tileId = floor((baseUV - _TileGridOffset.xy) * _TilesPerRepeat);
                float randA = Hash21(tileId);
                float randB = Hash21(tileId + 17.31);
                float tileBrightness = 1.0 + (randA - 0.5) * 0.7 * _TileVariation;
                float3 tileTint = float3(1.0 + (randB - 0.5) * 0.25 * _TileVariation, 1.0, 1.0 - (randB - 0.5) * 0.25 * _TileVariation);
                baseColor *= tileBrightness * tileTint;
                float3 dirtColor   = SAMPLE_TEXTURE2D(_DirtColorMap,   sampler_LinearRepeat, dirtUV).rgb * _DirtTint.rgb;
                float3 damageColor = SAMPLE_TEXTURE2D(_DamageColorMap, sampler_LinearRepeat, damageUV).rgb * _DamageTint.rgb;

                float3 albedo = lerp(baseColor, dirtColor, dirtW);
                albedo = lerp(albedo, damageColor, damageW);

                float3 normalTS = lerp(SampleNormalTS(TEXTURE2D_ARGS(_NormalMap, sampler_LinearRepeat), baseUV),
                                       SampleNormalTS(TEXTURE2D_ARGS(_DirtNormalMap, sampler_LinearRepeat), dirtUV), dirtW);
                normalTS = lerp(normalTS, SampleNormalTS(TEXTURE2D_ARGS(_DamageNormalMap, sampler_LinearRepeat), damageUV), damageW);
                normalTS = normalize(normalTS);

                float roughness = lerp(Raw(SAMPLE_TEXTURE2D(_RoughnessMap, sampler_LinearRepeat, baseUV).rrr).r,
                                       Raw(SAMPLE_TEXTURE2D(_DirtRoughnessMap, sampler_LinearRepeat, dirtUV).rrr).r, dirtW);
                roughness = lerp(roughness, Raw(SAMPLE_TEXTURE2D(_DamageRoughnessMap, sampler_LinearRepeat, damageUV).rrr).r, damageW);
                roughness += (randA - 0.5) * 0.25 * _TileVariation;

                float occlusion = Raw(SAMPLE_TEXTURE2D(_AOMap, sampler_LinearRepeat, baseUV).rrr).r;

                // 바닥은 위를 보는 면이라 월드 X, Z를 탄젠트, 바이탄젠트로 쓴다 (uv가 월드 xz이므로 u는 +X, v는 +Z)
                float3 normalWS = normalize(input.normalWS);
                float3 tangentWS   = useMeshUV ? normalize(TransformObjectToWorldDir(float3(1, 0, 0))) : float3(1, 0, 0);
                float3 bitangentWS = useMeshUV ? normalize(TransformObjectToWorldDir(float3(0, 0, 1))) : float3(0, 0, 1);
                float3 worldNormal = normalize(tangentWS * normalTS.x + bitangentWS * normalTS.y + normalWS * normalTS.z);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = worldNormal;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SampleSH(worldNormal);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = 0;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.smoothness = 1.0 - saturate(roughness);
                surfaceData.occlusion = occlusion;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_LinearRepeat, input.uv).rgb * _EmissionColor.rgb;
                surfaceData.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }

    FallBack Off
}
