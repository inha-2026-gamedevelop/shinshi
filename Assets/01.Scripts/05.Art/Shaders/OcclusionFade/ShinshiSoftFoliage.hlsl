#ifndef SHINSHI_SOFT_FOLIAGE_INCLUDED
#define SHINSHI_SOFT_FOLIAGE_INCLUDED

// 잎 덩어리의 각진 면 노멀을 수관 중심에서 바깥으로 향하는 둥근 노멀 쪽으로 굽힌다.
// 면마다 밝기가 끊겨 보이는 로우폴리 느낌을 줄이고 수관 전체가 하나의 부드러운 덩어리로 빛을 받게 한다.
// 기존 메시와 재질 속성은 그대로 두고 꼭짓점 단계에서 노멀만 바꾼다.

float _FoliageCenterY;   // 수관 중심 높이(오브젝트 공간 m)
float _FoliageSquash;    // 세로 방향 노멀 비중. 작을수록 옆으로 퍼진 원통형 노멀
float _FoliageSoftness;  // 원래 노멀에서 둥근 노멀로 섞는 정도
float _FoliageTrunkY;    // 이 높이 아래(줄기)는 원래 노멀을 유지

float3 ShinshiSoftFoliageNormal(float3 positionOS, float3 normalOS)
{
    float3 radial = float3(positionOS.x, (positionOS.y - _FoliageCenterY) * _FoliageSquash, positionOS.z);
    radial = normalize(radial + float3(0.0, 0.35, 0.0) * length(radial));
    float weight = _FoliageSoftness * smoothstep(_FoliageTrunkY, _FoliageTrunkY + 0.6, positionOS.y);
    return normalize(lerp(normalOS, radial, weight));
}

#if defined(UNIVERSAL_FORWARD_LIT_PASS_INCLUDED)
Varyings ShinshiSoftFoliageLitPassVertex(Attributes input)
{
    input.normalOS = ShinshiSoftFoliageNormal(input.positionOS.xyz, input.normalOS);
    return LitPassVertex(input);
}
#endif

#if defined(UNIVERSAL_LIT_GBUFFER_PASS_INCLUDED)
Varyings ShinshiSoftFoliageGBufferVertex(Attributes input)
{
    input.normalOS = ShinshiSoftFoliageNormal(input.positionOS.xyz, input.normalOS);
    return LitGBufferPassVertex(input);
}
#endif

#if defined(UNIVERSAL_FORWARD_LIT_DEPTH_NORMALS_PASS_INCLUDED)
Varyings ShinshiSoftFoliageDepthNormalsVertex(Attributes input)
{
    input.normal = ShinshiSoftFoliageNormal(input.positionOS.xyz, input.normal);
    return DepthNormalsVertex(input);
}
#endif

#endif
