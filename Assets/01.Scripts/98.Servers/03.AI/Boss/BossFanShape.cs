// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 부채꼴 판정의 단일 구현이다. 1페이즈 P02와 2페이즈 P03(구역 하나가 부채꼴 하나)이 같은 경계와 정밀도 규칙을 쓴다
    /// <summary> 부채꼴 안팎 판정, 상태가 없는 순수 계산이다 </summary>
    public static class BossFanShape
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DIRECTION_SQR = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const float ANGLE_EPSILON     = 0.001f;    // 도, 경계 각도의 부동소수 오차 흡수
        private const float HALF              = 0.5f;

        /****************************************
        *                Methods
        ****************************************/

        // 거리와 각도 경계를 모두 포함한다. 보스와 같은 위치의 대상은 방향이 없어 안으로 본다
        // 이웃한 부채꼴이 경계를 공유하면 경계 위의 대상은 둘 다 안이다. 중복 타격 방지는 호출하는 쪽의 시전 단위 피격 기록이 맡는다
        /// <summary> 대상이 origin에서 aim 방향으로 arcAngle(전체 각도), radius 안에 있는가, 수평 거리와 각도 경계를 포함한다 </summary>
        public static bool IsInsideSector(Vector3 origin, Vector3 aim, Vector3 targetPosition, float radius, float arcAngle)
        {
            Vector3 offset = targetPosition - origin;
            offset.y = 0f;

            float sqrDistance = offset.sqrMagnitude;
            if (sqrDistance > (radius * radius))
            {
                return false;
            }

            if (sqrDistance < MIN_DIRECTION_SQR)
            {
                return true;
            }

            return Vector3.Angle(aim, offset) <= ((arcAngle * HALF) + ANGLE_EPSILON);
        }
    }
}
