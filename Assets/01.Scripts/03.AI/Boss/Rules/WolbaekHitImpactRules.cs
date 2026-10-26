// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;

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

        // 임팩트 방향은 표시용이라 플레이어 방향 복제와 같은 yaw 하나로 줄인다. 방향 없는 공격은 yaw로 나타낼 수 없어 따로 표시한다
        /// <summary> 복제할 수평 방향을 yaw로 줄인다, 수평 방향이 없으면 거짓이고 yaw는 0이다 </summary>
        public static bool TryEncodeDirection(Vector3 direction, out ushort yaw)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < MIN_DIRECTION_SQR)
            {
                yaw = 0;
                return false;
            }

            yaw = FacingRules.EncodeYaw(direction.normalized);
            return true;
        }

        /// <summary> 받은 yaw를 수평 단위 방향으로 되돌린다, 방향이 없던 피격이면 0이다 </summary>
        public static Vector3 DecodeDirection(bool hasDirection, ushort yaw)
        {
            return hasDirection ? FacingRules.DecodeYaw(yaw) : Vector3.zero;
        }
    }
}
