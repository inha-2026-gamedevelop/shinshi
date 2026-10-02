#ifndef SHINSHI_ATMOSPHERE_COMMON_INCLUDED
#define SHINSHI_ATMOSPHERE_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

// 원근·직교 카메라 모두에서 장면 깊이를 카메라 기준 거리로 바꾼다.
float ShinshiSceneEyeDepth(float2 screenUV)
{
    float rawDepth = SampleSceneDepth(screenUV);
    if (unity_OrthoParams.w > 0.5)
    {
    #if UNITY_REVERSED_Z
        rawDepth = 1.0 - rawDepth;
    #endif
        return lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
    }
    return LinearEyeDepth(rawDepth, _ZBufferParams);
}

// 불투명 물체와 맞닿는 곳을 부드럽게 지운다. 구름이 절벽에 잘린 선으로 보이지 않게 한다.
float ShinshiSoftIntersection(float4 positionCS, float3 positionWS, float fadeDistance)
{
    float2 screenUV = GetNormalizedScreenSpaceUV(positionCS);
    float sceneDepth = ShinshiSceneEyeDepth(screenUV);
    float selfDepth = -TransformWorldToView(positionWS).z;
    return saturate((sceneDepth - selfDepth) / max(fadeDistance, 0.001));
}

#endif
