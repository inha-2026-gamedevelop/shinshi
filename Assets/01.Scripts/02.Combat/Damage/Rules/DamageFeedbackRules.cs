// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 숫자 표시의 유효 값과 수명, 화면 투영 검사를 서버와 클라이언트가 함께 쓴다 </summary>
    public static class DamageFeedbackRules
    {
        /****************************************
        *                Fields
        ****************************************/

        // 전송 대기 중 오래된 타격을 재생하지 않는 표시 전용 수명이다
        public const double MAX_AGE_SECONDS = 1.25;
        public const double FUTURE_TOLERANCE_SECONDS = 0.25;

        /****************************************
        *                Methods
        ****************************************/

        public static bool IsFinite(float value) => (!float.IsNaN(value)) && (!float.IsInfinity(value));
        public static bool IsFinite(double value) => (!double.IsNaN(value)) && (!double.IsInfinity(value));

        public static bool IsFinite(Vector3 value)
        {
            return (IsFinite(value.x)) && (IsFinite(value.y)) && (IsFinite(value.z));
        }

        public static bool IsValid(in DamageFeedback feedback)
        {
            return (feedback.SessionEpoch > 0) && (feedback.Sequence != 0) && (feedback.TargetCombatId > 0)
                && (IsFinite(feedback.Amount)) && (feedback.Amount > 0f) && (IsFinite(feedback.WorldPosition))
                && (IsFinite(feedback.ServerTime));
        }

        public static bool IsFresh(double serverTime, double now)
        {
            return (IsFinite(serverTime)) && (IsFinite(now)) && ((now - serverTime) <= MAX_AGE_SECONDS)
                && ((serverTime - now) <= FUTURE_TOLERANCE_SECONDS);
        }

        public static double ReceptionTime(double interpolatedTime, double bufferTime)
        {
            // Mirror의 클라이언트 시각은 표시 보간 버퍼만큼 뒤에 있다. 서버 타격 시각은 현재 축으로 비교한다
            if ((!IsFinite(interpolatedTime)) || (!IsFinite(bufferTime)) || (bufferTime < 0.0))
            {
                return double.NaN;
            }
            double current = interpolatedTime + bufferTime;
            return IsFinite(current) ? current : double.NaN;
        }

        public static bool IsVisibleProjection(Vector3 viewport)
        {
            return (IsFinite(viewport)) && (viewport.z > 0f) && (viewport.x >= 0f) && (viewport.x <= 1f)
                && (viewport.y >= 0f) && (viewport.y <= 1f);
        }
    }
}
