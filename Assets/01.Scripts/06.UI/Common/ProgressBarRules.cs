// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> 진행 막대의 시간 채움, 남은 시간 글자, 무한 대기 띠 위치를 계산하는 순수 규칙 </summary>
    public static class ProgressBarRules
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float TENTHS_PER_SECOND = 10f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 0에서 1로 자른 비율, 숫자가 아닌 값은 0이다 </summary>
        public static float ClampRatio(float ratio)
        {
            if (float.IsNaN(ratio))
            {
                return 0f;
            }

            return Mathf.Clamp01(ratio);
        }

        /// <summary> 경과 시간에 맞는 비율, drain이면 1에서 0으로 줄어든다. 길이가 0 이하면 끝난 값이다 </summary>
        public static float TimedRatio(float elapsed, float duration, bool drain)
        {
            float progress = (duration <= 0f) ? 1f : ClampRatio(elapsed / duration);
            return drain ? (1f - progress) : progress;
        }

        public static float RemainingSeconds(float elapsed, float duration)
        {
            return Mathf.Max(0f, duration - elapsed);
        }

        /// <summary> 남은 시간을 0.1초 단위로 올린 값, 글자를 바뀔 때만 다시 만들 때 쓴다 </summary>
        public static int RemainingTenths(float remainingSeconds)
        {
            return Mathf.CeilToInt(Mathf.Max(0f, remainingSeconds) * TENTHS_PER_SECOND);
        }

        /// <summary> 무한 대기 띠의 왼쪽, 오른쪽 앵커. phase 0은 맨 왼쪽, 1은 맨 오른쪽에 붙는다 </summary>
        public static void SweepAnchors(float phase, float width, out float min, out float max)
        {
            float clampedWidth = Mathf.Clamp01(width);
            min = ClampRatio(phase) * (1f - clampedWidth);
            max = min + clampedWidth;
        }
    }
}
