// System
using System;

namespace Shinshi.AI
{
    // 원본 VFX 재생기는 자기 프로필의 예고 길이로 그려지도록 만들어져 있다. 프리뷰는 패턴 시각을 그 프로필 시각으로 바꿔 그렸고,
    // 여기서는 같은 변환을 서버 시각에 적용한다. 변환한 시각을 재생기의 외부 시계 입력(BeginTelegraph, ConfirmImpact, Tick)에 넘긴다
    //   예고: 시작부터 예고 끝까지를 프로필 예고 길이로 늘이거나 줄인다
    //   돌진: 예고 끝(돌진 시작)부터 예정 타격까지를 돌진 머리 이동 길이로 바꾼다
    //   타격 뒤: 서버 타격 시각부터 실제 초 그대로 흐른다
    // 서버 타격 확정이 오기 전에는 예고 끝 자세에서 멈춰 기다린다. 스스로 타격을 만들지 않는다
    /// <summary> 서버 시각을 무철 VFX 재생기의 프로필 시각으로 바꾼다 </summary>
    public static class MucheolVfxTimeline
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double MIN_SPAN = 0.0001;

        /****************************************
        *                Methods
        ****************************************/

        // telegraphEnd는 단발이면 예정 타격 시각, 돌진이면 돌진 시작 시각(모르면 예상 시각)이다.
        // impact와 hitTime은 모르면 NaN이다
        /// <summary> 프로필 시각, 0에서 예고가 시작하고 telegraph + charge에서 타격한다 </summary>
        public static float ToProfileTime(double now, double start, double telegraphEnd, double impact, double hitTime,
            float telegraph, float charge)
        {
            if (!double.IsNaN(hitTime))
            {
                return telegraph + charge + (float)Math.Max(0d, now - hitTime);
            }

            bool hasCharge = (charge > MIN_SPAN) && (!double.IsNaN(impact)) && (impact > telegraphEnd + MIN_SPAN);
            if ((!hasCharge) || (now <= telegraphEnd))
            {
                return telegraph * InverseLerp(start, telegraphEnd, now);
            }

            return telegraph + (charge * InverseLerp(telegraphEnd, impact, now));
        }

        /// <summary> 프로필 시각의 타격 순간 </summary>
        public static float ProfileImpact(float telegraph, float charge)
        {
            return telegraph + charge;
        }

        /// <summary> 늦게 받은 타격이 이미 꼬리까지 끝났는가, 끝났으면 다시 터뜨리지 않는다 </summary>
        public static bool IsImpactOver(double now, double hitTime, float tailSeconds)
        {
            return (!double.IsNaN(hitTime)) && (now - hitTime >= tailSeconds);
        }

        private static float InverseLerp(double from, double to, double value)
        {
            if (to - from <= MIN_SPAN)
            {
                return (value >= to) ? 1f : 0f;
            }

            double t = (value - from) / (to - from);
            return (float)Math.Min(1d, Math.Max(0d, t));
        }
    }
}
