// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 봉인 표식 한 개가 한 순간에 보이는 모습, 셰이더 값과 크기 배율 </summary>
    public readonly struct MucheolSealLook
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 확정 타수 비율 0~1, 고리 조각이 채워지고 색이 청록에서 호박색으로 바뀐다 </summary>
        public float Progress { get; }

        /// <summary> 유효타를 센 직후 1에서 0으로 줄어드는 반응 </summary>
        public float Pulse { get; }

        /// <summary> 투명도 0~1, 나올 때 차오르고 사라질 때 줄어든다 </summary>
        public float Opacity { get; }

        /// <summary> 기본 크기에 곱하는 배율, 유효타에 부풀고 사라질 때 크게 퍼진다 </summary>
        public float ScaleFactor { get; }

        /****************************************
        *                Methods
        ****************************************/

        public MucheolSealLook(float progress, float pulse, float opacity, float scaleFactor)
        {
            Progress    = progress;
            Pulse       = pulse;
            Opacity     = opacity;
            ScaleFactor = scaleFactor;
        }
    }

    // 프리뷰(Dev_MucheolMotionVfxPreview)에서 확정한 봉인 표식 연출을 시간과 타수만으로 계산한다. 게임 오브젝트나 시계를 읽지 않는다.
    // 타수가 오를 때마다 진행도가 한 칸 차오르고 그 순간 표식이 잠깐 번쩍이며 부푼다. 방향이 채워지면 표식이 크게 퍼지며 사라진다
    /// <summary> 무철 방향 봉인 표식의 타수별 연출 계산 </summary>
    public static class MucheolSealVisual
    {
        /****************************************
        *              Constants
        ****************************************/

        /// <summary> 표식이 나올 때 투명도가 차오르는 시간 </summary>
        public const float REVEAL_FADE_SECONDS = 0.2f;

        /// <summary> 유효타 반응이 사라지는 시간 </summary>
        public const float PULSE_SECONDS = 0.2f;

        /// <summary> 방향이 채워진 표식이 사라지는 시간 </summary>
        public const float DISSOLVE_SECONDS = 0.4f;

        private const float PULSE_SCALE    = 0.12f;
        private const float DISSOLVE_SCALE = 0.7f;

        /// <summary> 사라지는 중이 아님을 뜻하는 경과 시간 </summary>
        public const float NOT_DISSOLVING = -1f;

        /****************************************
        *                Methods
        ****************************************/

        // age는 표식이 나온 뒤 흐른 시간, sinceHit은 마지막 유효타 뒤 흐른 시간, dissolveAge는 사라지기 시작한 뒤 흐른 시간(아니면 NOT_DISSOLVING)이다
        /// <summary> 표식의 지금 모습을 계산한다 </summary>
        public static MucheolSealLook Evaluate(float age, int hits, int requiredHits, float sinceHit, float dissolveAge)
        {
            float dissolve = (dissolveAge < 0f) ? 0f : Mathf.Clamp01(dissolveAge / DISSOLVE_SECONDS);
            float progress = (requiredHits > 0) ? Mathf.Clamp01((float)hits / requiredHits) : 0f;
            float pulse    = (hits > 0) ? Mathf.Clamp01(1f - (sinceHit / PULSE_SECONDS)) : 0f;
            float opacity  = Mathf.Clamp01(age / REVEAL_FADE_SECONDS) * (1f - dissolve);
            float scale    = 1f + (pulse * PULSE_SCALE) + (dissolve * DISSOLVE_SCALE);

            return new MucheolSealLook(progress, pulse, opacity, scale);
        }
    }
}
