// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 정한 값으로 화면에 그릴 모양과 진행도만 계산한다. 어디가 맞는지는 서버가 판정하고, 이 클래스의 IsInside는
    // 표시한 범위가 서버 판정 규칙과 같은지 확인하는 용도다. 서버 패턴(BossFanBlastPattern, BossLineBlastPattern, BossCircleBlastPattern,
    // BossDashPattern)이 쓰는 경계 규칙을 그대로 따른다. 대상은 중심점이고 경계를 포함한다
    /// <summary> 보스 예고의 모양과 시간 진행 계산, 서버와 클라이언트가 같은 값을 보게 하는 순수 계산 </summary>
    public static class BossTelegraphRules
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DIRECTION_SQR = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const float ANGLE_EPSILON     = 0.001f;    // 도, 경계 각도의 부동소수 오차 흡수
        private const float EDGE_EPSILON      = 0.001f;    // m, 경계의 부동소수 오차 흡수
        private const float HALF              = 0.5f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 수평 단위 벡터, 방향을 정할 수 없으면 fallback </summary>
        public static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? fallback : direction.normalized;
        }

        /// <summary> 예고 진행도 0~1, 시작 전이면 0이고 길이가 0이면 시작과 함께 1이다 </summary>
        public static float GetTelegraphProgress(double startServerTime, float telegraphSeconds, double serverNow)
        {
            if (telegraphSeconds <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01((float)((serverNow - startServerTime) / telegraphSeconds));
        }

        /// <summary> 돌진이 시작점에서 지금까지 간 거리, 일정한 속도로 가다가 최대 거리에서 멈춘다 </summary>
        public static float GetDashTraveled(double executeServerTime, float speed, float maxDistance, double serverNow)
        {
            double age = serverNow - executeServerTime;
            if ((age <= 0.0) || (speed <= 0f))
            {
                return 0f;
            }

            return Mathf.Min(maxDistance, (float)(speed * age));
        }

        /// <summary> 직사각형 중심, 시작점에서 전방으로 길이의 절반이다 </summary>
        public static Vector3 GetRectangleCenter(Vector3 origin, Vector3 direction, float length)
        {
            return origin + (direction * (length * HALF));
        }

        /// <summary> 표시한 범위 안인가, 수평면에서 경계를 포함한다. 직사각형은 시작점 뒤쪽을 제외한다 </summary>
        public static bool IsInside(EBossTelegraphShape shape, Vector3 origin, Vector3 direction, float length, float width,
            float arcAngle, Vector3 point)
        {
            Vector3 offset = point - origin;
            offset.y = 0f;

            switch (shape)
            {
                case EBossTelegraphShape.Circle:
                    return offset.sqrMagnitude <= (length * length);
                case EBossTelegraphShape.Fan:
                    return IsInsideFan(direction, offset, length, arcAngle);
                case EBossTelegraphShape.Line:
                case EBossTelegraphShape.Dash:
                    return IsInsideRectangle(direction, offset, length, width);
                default:
                    return false;
            }
        }

        // 거리와 각도 경계를 모두 포함한다. 시작점과 같은 위치는 방향이 없어 안으로 본다
        private static bool IsInsideFan(Vector3 direction, Vector3 offset, float radius, float arcAngle)
        {
            float sqrDistance = offset.sqrMagnitude;
            if (sqrDistance > (radius * radius))
            {
                return false;
            }

            if (sqrDistance < MIN_DIRECTION_SQR)
            {
                return true;
            }

            return Vector3.Angle(direction, offset) <= ((arcAngle * HALF) + ANGLE_EPSILON);
        }

        private static bool IsInsideRectangle(Vector3 direction, Vector3 offset, float length, float width)
        {
            float forward = (offset.x * direction.x) + (offset.z * direction.z);
            float lateral = (offset.x * direction.z) - (offset.z * direction.x);

            return (forward >= -EDGE_EPSILON) && (forward <= (length + EDGE_EPSILON))
                && (Mathf.Abs(lateral) <= ((width * HALF) + EDGE_EPSILON));
        }
    }
}
