#ifndef SHINSHI_TOON_FOLIAGE_NORMAL_INCLUDED
#define SHINSHI_TOON_FOLIAGE_NORMAL_INCLUDED

// 툰 식생용 둥근 노멀. ShinshiSoftFoliage.hlsl과 같은 식이지만 높이 축을 오브젝트 Y로 고정하지 않고
// 월드 위쪽을 오브젝트 공간으로 바꾼 방향으로 잰다. 숲 나무처럼 Z가 위인 메시도 같은 속성 값으로 수관을 둥글게 만든다.
// 오브젝트 Y가 위인 메시에서는 ShinshiSoftFoliageNormal과 결과가 같다

float3 ShinshiToonFoliageUpOS()
{
    return normalize(mul((float3x3)GetWorldToObjectMatrix(), float3(0.0, 1.0, 0.0)));
}

float3 ShinshiToonFoliageNormal(float3 positionOS, float3 normalOS)
{
    float3 upOS    = ShinshiToonFoliageUpOS();
    float  height  = dot(positionOS, upOS);
    float3 lateral = positionOS - upOS * height;
    float3 radial  = lateral + upOS * ((height - _FoliageCenterY) * _FoliageSquash);
    radial = normalize(radial + upOS * 0.35 * length(radial));
    float weight = _FoliageSoftness * smoothstep(_FoliageTrunkY, _FoliageTrunkY + 0.6, height);
    return normalize(lerp(normalOS, radial, weight));
}

#if defined(UNIVERSAL_FORWARD_LIT_PASS_INCLUDED)
Varyings ShinshiToonFoliageLitPassVertex(Attributes input)
{
    input.normalOS = ShinshiToonFoliageNormal(input.positionOS.xyz, input.normalOS);
    return LitPassVertex(input);
}
#endif

#if defined(UNIVERSAL_LIT_GBUFFER_PASS_INCLUDED)
Varyings ShinshiToonFoliageGBufferVertex(Attributes input)
{
    input.normalOS = ShinshiToonFoliageNormal(input.positionOS.xyz, input.normalOS);
    return LitGBufferPassVertex(input);
}
#endif

#if defined(UNIVERSAL_FORWARD_LIT_DEPTH_NORMALS_PASS_INCLUDED)
Varyings ShinshiToonFoliageDepthNormalsVertex(Attributes input)
{
    input.normal = ShinshiToonFoliageNormal(input.positionOS.xyz, input.normal);
    return DepthNormalsVertex(input);
}
#endif

#endif
