// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 월백 피격 임팩트의 방향과 유효 시간 규칙, 화면 없이 검증한다 </summary>
    public static class WolbaekHitImpactRules
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DIRECTION_SQR = 0.0001f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 임팩트가 공격 쪽을 보게 한다. 수평 방향이 없으면 회전하지 않는다 </summary>
        public static Quaternion ResolveRotation(Vector3 attackDirection)
        {
            attackDirection.y = 0f;
            if (attackDirection.sqrMagnitude < MIN_DIRECTION_SQR)
            {
                return Quaternion.identity;
            }

            return Quaternion.LookRotation(-attackDirection.normalized, Vector3.up);
        }

        /// <summary> 서버 시각이 너무 오래됐으면 늦게 도착한 피격이라 터뜨리지 않는다 </summary>
        public static bool IsFresh(double serverTime, double now, float maxAgeSeconds)
        {
            return (now - serverTime) <= maxAgeSeconds;
        }
    }
}
