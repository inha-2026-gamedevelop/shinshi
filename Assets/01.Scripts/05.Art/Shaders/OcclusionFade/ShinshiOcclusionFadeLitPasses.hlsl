#ifndef SHINSHI_OCCLUSION_FADE_LIT_PASSES_INCLUDED
#define SHINSHI_OCCLUSION_FADE_LIT_PASSES_INCLUDED

// URP Lit 패스의 프래그먼트 앞에서 가림 픽셀을 버리는 감싸개. 각 패스의 URP 포함 파일 뒤에 포함한다.
// 색과 깊이, 노멀 패스가 같은 픽셀을 버려야 한다. 깊이 프리패스만 남기면 비운 자리에 하늘이 보이고
// 노멀 패스만 남기면 SSAO가 사라진 면의 그늘을 그린다

#include "ShinshiOcclusionFade.hlsl"

// 깊이 패스의 Varyings에는 월드 위치가 없어 화면 좌표와 깊이로 되살린다. URP 디버그 셰이더와 같은 변환이다
float3 ShinshiOcclusionPositionFromClip(float4 positionCS)
{
    float2 positionNDC = positionCS.xy / _ScaledScreenParams.xy;
    return ComputeWorldSpacePosition(positionNDC, positionCS.z, UNITY_MATRIX_I_VP);
}

#if defined(UNIVERSAL_FORWARD_LIT_PASS_INCLUDED)
void ShinshiLitPassFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    clip(ShinshiOcclusionKeep(input.positionWS, input.positionCS.xy));

    LitPassFragment(input, outColor
#ifdef _WRITE_RENDERING_LAYERS
        , outRenderingLayers
#endif
    );
}
#endif

#if defined(UNIVERSAL_LIT_GBUFFER_PASS_INCLUDED)
GBufferFragOutput ShinshiLitGBufferPassFragment(Varyings input)
{
    clip(ShinshiOcclusionKeep(input.positionWS, input.positionCS.xy));
    return LitGBufferPassFragment(input);
}
#endif

#if defined(UNIVERSAL_DEPTH_ONLY_PASS_INCLUDED)
half ShinshiDepthOnlyFragment(Varyings input) : SV_TARGET
{
    clip(ShinshiOcclusionKeep(ShinshiOcclusionPositionFromClip(input.positionCS), input.positionCS.xy));
    return DepthOnlyFragment(input);
}
#endif

#if defined(UNIVERSAL_FORWARD_LIT_DEPTH_NORMALS_PASS_INCLUDED)
void ShinshiDepthNormalsFragment(
    Varyings input
    , out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    clip(ShinshiOcclusionKeep(ShinshiOcclusionPositionFromClip(input.positionCS), input.positionCS.xy));

    DepthNormalsFragment(input, outNormalWS
#ifdef _WRITE_RENDERING_LAYERS
        , outRenderingLayers
#endif
    );
}
#endif

#endif
