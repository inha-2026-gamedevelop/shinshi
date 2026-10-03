// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 원형 보스 전장의 경계 계산, 몸체 중심이 갈 수 있는 원은 전장 반경에서 몸체 반경만큼 안쪽이다 </summary>
    public static class BossArenaRules
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DIRECTION_SQR = 0.000001f;

        /****************************************
        *                Methods
        ****************************************/

        // 경계 위나 밖에서 시작해도 안쪽을 향하면 반대편 경계까지 갈 수 있다. 돌진이 경계에 닿아 멈춘 뒤 부동소수 오차로
        // 원 밖으로 판정돼도 다시 안쪽으로 돌진할 수 있게 하려는 것이다. 바깥을 향하거나 원을 지나지 않으면 0이다
        /// <summary> origin에서 수평 방향으로 경계까지 몸체 중심이 갈 수 있는 거리(m) </summary>
        public static float DistanceToBoundary(Vector3 center, float arenaRadius, Vector3 origin, Vector3 direction,
            float bodyRadius)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            float   sqr  = flat.sqrMagnitude;
            if (sqr <= MIN_DIRECTION_SQR)
            {
                return 0f;
            }

            flat /= Mathf.Sqrt(sqr);

            float   limit  = Mathf.Max(0f, arenaRadius - bodyRadius);
            Vector3 offset = new Vector3(origin.x - center.x, 0f, origin.z - center.z);

            float b = Vector3.Dot(offset, flat);
            float c = offset.sqrMagnitude - (limit * limit);
            if ((c > 0f) && (b >= 0f))
            {
                return 0f;
            }

            float discriminant = (b * b) - c;
            if (discriminant < 0f)
            {
                return 0f;
            }

            return Mathf.Max(0f, -b + Mathf.Sqrt(discriminant));
        }
    }
}
