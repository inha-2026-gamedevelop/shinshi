#ifndef SHINSHI_TOON_VFX_INCLUDED
#define SHINSHI_TOON_VFX_INCLUDED

// 툰 VFX 공통 식. 마스크(0~1)를 계단으로 나누고 중심부와 가장자리에 다른 색을 준다.
// 재질 키워드 _TOON_VFX가 켜진 셰이더만 이 함수를 부른다. 꺼진 재질은 원래 식을 그대로 쓴다

#define SHINSHI_TOON_VFX_MIN_STEPS 1.0
#define SHINSHI_TOON_VFX_MAX_STEPS 8.0
#define SHINSHI_TOON_VFX_MIN_SOFT  0.0001

// 마스크를 steps 단계로 나눈다. 각 단계의 끝 soft 폭(마스크 단위)에서만 다음 단계로 부드럽게 넘어간다.
// 마스크 0과 1은 그대로 0과 1이 된다. 가장 낮은 단계 아래의 옅은 꼬리는 0이 되어 가장자리가 딱 떨어진다
float ShinshiToonVfxStepMask(float mask, float steps, float soft)
{
    float stepCount = clamp(round(steps), SHINSHI_TOON_VFX_MIN_STEPS, SHINSHI_TOON_VFX_MAX_STEPS);
    float scaled    = saturate(mask) * stepCount;
    float baseStep  = floor(scaled);
    float within    = scaled - baseStep;
    float blendFrom = 1.0 - saturate(max(soft, SHINSHI_TOON_VFX_MIN_SOFT) * stepCount);
    float rise      = smoothstep(blendFrom, 1.0, within);
    return saturate((baseStep + rise) / stepCount);
}

// 마스크가 threshold 위인 중심부 비율. soft 폭으로 경계를 부드럽게 한다
float ShinshiToonVfxCore(float mask, float threshold, float soft)
{
    float width = max(soft, SHINSHI_TOON_VFX_MIN_SOFT);
    return smoothstep(threshold - width, threshold + width, mask);
}

// 마스크가 width 아래인 가장자리 띠 비율. width가 0이면 띠가 없다
float ShinshiToonVfxEdge(float mask, float width, float soft)
{
    if (width <= 0.0)
    {
        return 0.0;
    }

    float blend = max(soft, SHINSHI_TOON_VFX_MIN_SOFT);
    return 1.0 - smoothstep(width - blend, width + blend, mask);
}

// 원래 색에 중심부 색과 가장자리 색을 입힌다. 두 색의 알파가 입히는 세기다
float3 ShinshiToonVfxColor(float3 baseColor, float mask, float coreThreshold, float4 coreColor, float edgeWidth, float4 edgeColor, float soft)
{
    float  core  = ShinshiToonVfxCore(mask, coreThreshold, soft) * coreColor.a;
    float  edge  = ShinshiToonVfxEdge(mask, edgeWidth, soft) * edgeColor.a;
    float3 color = lerp(baseColor, coreColor.rgb, core);
    return lerp(color, edgeColor.rgb, edge);
}

#endif
