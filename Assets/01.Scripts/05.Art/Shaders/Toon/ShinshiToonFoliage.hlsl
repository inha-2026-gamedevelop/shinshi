#ifndef SHINSHI_TOON_FOLIAGE_INCLUDED
#define SHINSHI_TOON_FOLIAGE_INCLUDED

// 잎 덩어리를 원신풍으로 그리는 툰 조명. 둥근 노멀(ShinshiSoftFoliage.hlsl)로 수관 전체를 한 덩어리로 보고
// 밝은 면과 그늘 두 단계로만 나눈다. 잎 텍스처의 잔무늬는 높은 밉 단계의 평균 색과 섞어 넓은 색 면으로 만든다.
// 추가 속성은 Soft Foliage와 같이 UnityPerMaterial 밖에 있다

float  _ToonFlatten;      // 잎 텍스처 잔무늬를 평균 색으로 바꾸는 정도
float  _ToonFlatMip;      // 평균 색을 읽을 밉 단계. 클수록 넓은 범위의 평균
float  _ToonRampEdge;     // 밝은 면과 그늘 경계의 부드러운 폭
float  _ToonAmbientKeep;  // 환경광을 얼마나 남길지
float  _ToonOcclusionKeep;// 수관 안쪽 AO를 얼마나 남길지
float4 _ToonShadowColor;  // 그늘 쪽에 곱하는 색
float4 _ToonLitTint;      // 밝은 면에 곱하는 색

#define SHINSHI_TOON_FOLIAGE_MIN_EDGE 0.001
#define SHINSHI_TOON_FOLIAGE_THRESHOLD 0.5

#if defined(UNIVERSAL_FORWARD_LIT_PASS_INCLUDED)
void ShinshiToonFoliageFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    clip(ShinshiOcclusionKeep(input.positionWS, input.positionCS.xy));

    SurfaceData surfaceData;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);

#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    InitializeBakedGIData(input, inputData);

    half3 flatColor = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, input.uv, _ToonFlatMip).rgb * _BaseColor.rgb;
    half3 albedo    = lerp(surfaceData.albedo, flatColor, _ToonFlatten);

    Light mainLight   = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
    half  halfLambert = dot(inputData.normalWS, mainLight.direction) * 0.5 + 0.5;
    half  edge        = max(_ToonRampEdge, SHINSHI_TOON_FOLIAGE_MIN_EDGE);
    half  lit         = smoothstep(SHINSHI_TOON_FOLIAGE_THRESHOLD - edge, SHINSHI_TOON_FOLIAGE_THRESHOLD + edge, halfLambert * mainLight.shadowAttenuation);

    half3 ambient     = inputData.bakedGI * _ToonAmbientKeep;
    half3 shadowLight = ambient * _ToonShadowColor.rgb;
    half3 litLight    = ambient + mainLight.color * _ToonLitTint.rgb;
    half  occlusion   = lerp(1.0, surfaceData.occlusion, _ToonOcclusionKeep);

    half3 color = albedo * lerp(shadowLight, litLight, lit) * occlusion + surfaceData.emission;
    color = MixFog(color, inputData.fogCoord);

    outColor = half4(color, OutputAlpha(surfaceData.alpha, IsSurfaceTypeTransparent(_Surface)));

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}
#endif

#endif
