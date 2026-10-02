#ifndef SHINSHI_OCCLUSION_FADE_INCLUDED
#define SHINSHI_OCCLUSION_FADE_INCLUDED

// 카메라와 로컬 플레이어 사이에서 플레이어를 가리는 면을 디더로 비운다.
// 값은 OcclusionFadeDriver가 그리기 직전에 전역으로 넣는다. 넣는 쪽이 없으면 모두 0이라 아무것도 비우지 않는다
// xyz 카메라 위치, w 켜짐(0 또는 1)
float4 _ShinshiOcclusionCamera;
// xyz 초점 위치, w 초점 깊이에서 비우는 원의 반지름(m)
float4 _ShinshiOcclusionFocus;
// x 가장자리 흐림 비율, y 초점 앞 제외 거리(m), z 바닥 높이(월드 y), w 원 한가운데에서 남기는 비율
float4 _ShinshiOcclusionParams;

#define SHINSHI_OCCLUSION_MIN_FOCUS_DISTANCE 0.0001
#define SHINSHI_OCCLUSION_MIN_EDGE_WIDTH     0.0001
#define SHINSHI_OCCLUSION_DITHER_SIZE        4
#define SHINSHI_OCCLUSION_DITHER_CELLS       16.0

// OcclusionFadeShape.GetVisibility와 같은 식이다. 한쪽만 고치면 화면에서 비어 보이는 곳과 클릭이 통과하는 곳이 어긋난다
float ShinshiOcclusionVisibility(float3 positionWS)
{
    if (_ShinshiOcclusionCamera.w <= 0.0)
    {
        return 1.0;
    }

    if (positionWS.y < _ShinshiOcclusionParams.z)
    {
        return 1.0;
    }

    float3 toFocus       = _ShinshiOcclusionFocus.xyz - _ShinshiOcclusionCamera.xyz;
    float  focusDistance = length(toFocus);
    if (focusDistance <= SHINSHI_OCCLUSION_MIN_FOCUS_DISTANCE)
    {
        return 1.0;
    }

    float3 direction = toFocus / focusDistance;
    float3 toPoint   = positionWS - _ShinshiOcclusionCamera.xyz;
    float  along     = dot(toPoint, direction);
    if ((along <= 0.0) || (along >= (focusDistance - _ShinshiOcclusionParams.y)))
    {
        return 1.0;
    }

    float coneRadius  = _ShinshiOcclusionFocus.w * (along / focusDistance);
    float innerRadius = min(coneRadius * (1.0 - _ShinshiOcclusionParams.x), coneRadius - SHINSHI_OCCLUSION_MIN_EDGE_WIDTH);
    float offAxis     = length(toPoint - (direction * along));
    float edge        = smoothstep(innerRadius, coneRadius, offAxis);

    return lerp(_ShinshiOcclusionParams.w, 1.0, edge);
}

// 4x4 Bayer 순서. 반투명 정렬 없이 불투명 패스 그대로 비울 수 있어 디더를 쓴다
float ShinshiOcclusionDitherThreshold(float2 pixelPosition)
{
    static const float bayer[16] =
    {
         0.0,  8.0,  2.0, 10.0,
        12.0,  4.0, 14.0,  6.0,
         3.0, 11.0,  1.0,  9.0,
        15.0,  7.0, 13.0,  5.0
    };

    uint2 cell = (uint2)floor(pixelPosition) % SHINSHI_OCCLUSION_DITHER_SIZE;
    return (bayer[(cell.y * SHINSHI_OCCLUSION_DITHER_SIZE) + cell.x] + 0.5) / SHINSHI_OCCLUSION_DITHER_CELLS;
}

// 0 이하면 이 픽셀을 버린다
float ShinshiOcclusionKeep(float3 positionWS, float2 pixelPosition)
{
    float visibility = ShinshiOcclusionVisibility(positionWS);
    if (visibility >= 1.0)
    {
        return 1.0;
    }

    return visibility - ShinshiOcclusionDitherThreshold(pixelPosition);
}

// Shader Graph Custom Function 노드용. 버릴 픽셀은 알파를 음수로 내려 그래프의 알파 클립이 버리게 한다.
// 그림자는 비우지 않는다. 비워진 지붕 아래에 그림자까지 사라지면 실내가 갑자기 밝아진다
void ShinshiOcclusionFade_float(float Alpha, float3 PositionWS, float2 PixelPosition, out float Out)
{
    Out = Alpha;

#if defined(SHADERPASS) && defined(SHADERPASS_SHADOWCASTER)
    #if (SHADERPASS == SHADERPASS_SHADOWCASTER)
    return;
    #endif
#endif

    if (ShinshiOcclusionKeep(PositionWS, PixelPosition) < 0.0)
    {
        Out = -1.0;
    }
}

void ShinshiOcclusionFade_half(half Alpha, float3 PositionWS, float2 PixelPosition, out half Out)
{
    float result;
    ShinshiOcclusionFade_float(Alpha, PositionWS, PixelPosition, result);
    Out = (half)result;
}

#endif
