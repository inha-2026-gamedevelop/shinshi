#ifndef SHINSHI_WIND_CLOTH_INCLUDED
#define SHINSHI_WIND_CLOTH_INCLUDED

// 위쪽 끝이 걸린 천을 바람에 흔든다. 꼭짓점 높이로 매단 끝에서 멀수록 크게 움직이게 해
// 걸린 부분은 고정되고 아래 자락이 휘날린다. 물리 시뮬레이션이 아닌 시간 함수라 비용이 거의 없고
// 클라이언트마다 따로 계산해도 판정과 무관하다. 같은 재질을 쓰는 천은 위치로 위상을 달리해 서로 어긋나게 흔들린다.

float4 _WindDir;        // xy: 바람이 미는 오브젝트 공간 수평 방향
float _WindAmplitude;   // 자락 끝의 최대 밀림 거리(m)
float _WindSpeed;       // 흔들림 속도
float _WindFlutter;     // 바람 직각 방향의 잔떨림 크기(m)
float _ClothTopY;       // 천이 걸린 높이(오브젝트 공간 m)
float _ClothLength;     // 걸린 곳에서 자락 끝까지의 길이(m)

#define SHINSHI_CLOTH_MIN_DIR 0.0001
#define SHINSHI_CLOTH_BASE_PUSH 0.35
#define SHINSHI_CLOTH_GUST_PUSH 0.65
#define SHINSHI_CLOTH_LENGTH_KEEP 0.4

float3 ShinshiWindClothSway(float3 positionOS)
{
    float hang = saturate((_ClothTopY - positionOS.y) / max(_ClothLength, SHINSHI_CLOTH_MIN_DIR));
    float weight = hang * hang;

    float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
    float time = _Time.y * _WindSpeed + origin.x * 0.7 + origin.z * 1.3;

    float gust = sin(time + positionOS.y * 1.6 + positionOS.x * 1.2) * 0.6
               + sin(time * 1.9 + positionOS.y * 2.7) * 0.4;
    float flutter = sin(time * 3.1 + positionOS.y * 5.0 + positionOS.z * 3.0) * _WindFlutter * weight;

    float2 dir = _WindDir.xy;
    dir = dot(dir, dir) > SHINSHI_CLOTH_MIN_DIR ? normalize(dir) : float2(1.0, 0.0);
    float2 side = float2(-dir.y, dir.x);

    float push = (SHINSHI_CLOTH_BASE_PUSH + SHINSHI_CLOTH_GUST_PUSH * gust) * _WindAmplitude * weight;

    positionOS.xz += dir * push + side * flutter;
    positionOS.y -= abs(push) * SHINSHI_CLOTH_LENGTH_KEEP;
    return positionOS;
}

#if defined(UNIVERSAL_FORWARD_LIT_PASS_INCLUDED)
Varyings ShinshiWindClothLitPassVertex(Attributes input)
{
    input.positionOS.xyz = ShinshiWindClothSway(input.positionOS.xyz);
    return LitPassVertex(input);
}
#endif

#if defined(UNIVERSAL_LIT_GBUFFER_PASS_INCLUDED)
Varyings ShinshiWindClothGBufferVertex(Attributes input)
{
    input.positionOS.xyz = ShinshiWindClothSway(input.positionOS.xyz);
    return LitGBufferPassVertex(input);
}
#endif

#if defined(UNIVERSAL_DEPTH_ONLY_PASS_INCLUDED)
Varyings ShinshiWindClothDepthOnlyVertex(Attributes input)
{
    input.position.xyz = ShinshiWindClothSway(input.position.xyz);
    return DepthOnlyVertex(input);
}
#endif

#if defined(UNIVERSAL_FORWARD_LIT_DEPTH_NORMALS_PASS_INCLUDED)
Varyings ShinshiWindClothDepthNormalsVertex(Attributes input)
{
    input.positionOS.xyz = ShinshiWindClothSway(input.positionOS.xyz);
    return DepthNormalsVertex(input);
}
#endif

#if defined(UNIVERSAL_SHADOW_CASTER_PASS_INCLUDED)
Varyings ShinshiWindClothShadowVertex(Attributes input)
{
    input.positionOS.xyz = ShinshiWindClothSway(input.positionOS.xyz);
    return ShadowPassVertex(input);
}
#endif

#endif
