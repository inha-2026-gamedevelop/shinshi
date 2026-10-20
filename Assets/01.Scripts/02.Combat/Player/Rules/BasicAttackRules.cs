// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 기본 공격 판정 도형, 서버 판정과 클라이언트 사거리 표시가 같이 쓴다. 모두 수평면 기준이다 </summary>
    public static class BasicAttackRules
    {
        // TODO(민성): 근접 부채꼴과 즉발 직선도 대상 몸체 반경을 더할지 기획에서 정한다. 지금은 날아가는 화살만 몸체를 본다

        /****************************************
        *                Fields
        ****************************************/

        private const float HALF              = 0.5f;
        private const float HALF_TURN_DEGREES = 180f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 근접 부채꼴 안인지, 경계를 포함하고 360도 이상이면 원형이다 </summary>
        public static bool IsInArc(Vector3 origin, Vector3 forward, Vector3 target, float range, float arcDegrees)
        {
            float deltaX = target.x - origin.x;
            float deltaZ = target.z - origin.z;
            float distanceSqr = (deltaX * deltaX) + (deltaZ * deltaZ);

            if (distanceSqr > range * range)
            {
                return false;
            }

            // 겹쳐 선 대상은 방향을 정할 수 없으므로 맞은 것으로 본다
            if (distanceSqr <= Constants.Player.DIRECTION_EPSILON_SQR)
            {
                return true;
            }

            float halfAngle = arcDegrees * HALF;
            if (halfAngle >= HALF_TURN_DEGREES)
            {
                return true;
            }

            float cos = ((deltaX * forward.x) + (deltaZ * forward.z)) / Mathf.Sqrt(distanceSqr);
            return cos >= Mathf.Cos(halfAngle * Mathf.Deg2Rad);
        }

        /// <summary> 수평 도형과 별개로 공격자와 대상의 높이 차가 상한 안인지, 경계를 포함한다 </summary>
        public static bool IsWithinHeight(Vector3 origin, Vector3 target, float maxHeightDifference)
        {
            return Mathf.Abs(target.y - origin.y) <= maxHeightDifference;
        }

        /// <summary> 원거리 직선 판정 안인지, 시작점에서 진행 방향으로 잰 거리를 돌려준다. 첫 대상은 거리가 가장 짧은 것이다 </summary>
        public static bool TryProjectOnRay(Vector3 origin, Vector3 direction, Vector3 target, float range, float halfWidth,
            out float alongDistance)
        {
            float deltaX = target.x - origin.x;
            float deltaZ = target.z - origin.z;

            alongDistance = (deltaX * direction.x) + (deltaZ * direction.z);
            if ((alongDistance < 0f) || (alongDistance > range))
            {
                return false;
            }

            float distanceSqr = (deltaX * deltaX) + (deltaZ * deltaZ);
            float lateralSqr  = distanceSqr - (alongDistance * alongDistance);
            return lateralSqr <= halfWidth * halfWidth;
        }

        /// <summary> 폭이 있는 선분이 대상 몸체 원에 닿는지, 진행 방향으로 잰 처음 닿는 거리를 돌려준다. 경계를 포함한다 </summary>
        public static bool TryHitBodyOnSegment(Vector3 origin, Vector3 direction, Vector3 target, float start, float end,
            float halfWidth, float bodyRadius, out float entryDistance)
        {
            float deltaX = target.x - origin.x;
            float deltaZ = target.z - origin.z;
            float along  = (deltaX * direction.x) + (deltaZ * direction.z);
            float reach  = halfWidth + Mathf.Max(0f, bodyRadius);

            float distanceSqr = (deltaX * deltaX) + (deltaZ * deltaZ);
            float lateralSqr  = Mathf.Max(0f, distanceSqr - (along * along));
            float reachSqr    = reach * reach;
            entryDistance = start;
            if (lateralSqr > reachSqr)
            {
                return false;
            }

            // start는 음수일 수 있다. 몸체가 이미 시작점에 걸쳐 있으면 처음 닿는 거리는 start다
            float halfChord = Mathf.Sqrt(reachSqr - lateralSqr);
            float entry     = along - halfChord;
            float exit      = along + halfChord;
            if ((exit < start) || (entry > end))
            {
                return false;
            }

            entryDistance = Mathf.Max(entry, start);
            return true;
        }
    }
}
