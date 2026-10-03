// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버와 클라이언트가 함께 쓰는 순수 기하 계산이다. 클라이언트는 표현 메시와 회전각을 만드는 데만 쓰고 전투 판정은 서버만 한다
    // 빔 한 줄은 두 꼭짓점(여우불) 사이 직사각형이다. 길이 방향은 두 꼭짓점 위치에서 끝나고 폭 방향 양쪽 변과 끝 변 모두 포함한다(월백 다른 범위 판정과 같은 규칙)
    // 구조 전체가 전장 중심을 축으로 같이 돌므로 별의 모양은 유지된다. 회전각은 서버 활성 시각에서 흐른 시간으로 직접 계산해 오차가 쌓이지 않는다
    /// <summary> 별형 회전 빔의 배치, 회전각, 포함 판정 </summary>
    public static class BossStarGeometry
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float EDGE_EPSILON      = 0.001f;  // m, 빔 변의 부동소수 오차 흡수
        private const float ARENA_EPSILON     = 0.0001f; // m^2 비율, 전장 원 경계의 부동소수 오차 흡수
        private const float HALF              = 0.5f;
        private const float FULL_CIRCLE       = 360f;
        private const float MIN_DIRECTION_SQR = 0.000001f;
        private const float MIN_LENGTH_SQR    = 0.000001f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 기준 방향(수평)과 회전 방향으로 배치를 만든다. 방향이 0에 가까우면 월드 정면을 쓴다 </summary>
        public static BossStarShape Place(Vector3 arenaCenter, float arenaRadius, Vector3 baseDirection, float ringRadius,
            int vertexCount, int connectStep, float beamWidth, float degreesPerSecond, bool rotatesRight)
        {
            baseDirection.y = 0f;
            Vector3 direction = (baseDirection.sqrMagnitude < MIN_DIRECTION_SQR) ? Vector3.forward : baseDirection.normalized;
            float signed = rotatesRight ? Mathf.Abs(degreesPerSecond) : -Mathf.Abs(degreesPerSecond);
            return new BossStarShape(arenaCenter, arenaRadius, direction, ringRadius, vertexCount, connectStep, beamWidth, signed);
        }

        /// <summary> 활성 뒤 흐른 시간의 회전각(도), 부호가 방향이고 0보다 작은 시간은 0, 유지 시간을 넘는 시간은 유지 시간으로 본다 </summary>
        public static float RotationAt(in BossStarShape shape, double elapsedSeconds, float durationSeconds)
        {
            double clamped = System.Math.Max(0.0, System.Math.Min(elapsedSeconds, durationSeconds));
            return (float)(shape.SignedDegreesPerSecond * clamped);
        }

        /// <summary> 꼭짓점(여우불)의 월드 위치, 회전각은 도이고 양수는 오른쪽이다 </summary>
        public static Vector3 GetVertex(in BossStarShape shape, int index, float rotationDegrees)
        {
            float angle = (index * (FULL_CIRCLE / shape.VertexCount)) + rotationDegrees;
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * shape.BaseDirection;
            return shape.ArenaCenter + (direction * shape.RingRadius);
        }

        /// <summary> 빔 하나의 양 끝 꼭짓점 위치 </summary>
        public static void GetBeam(in BossStarShape shape, int beamIndex, float rotationDegrees, out Vector3 start, out Vector3 end)
        {
            start = GetVertex(in shape, beamIndex, rotationDegrees);
            end   = GetVertex(in shape, (beamIndex + shape.ConnectStep) % shape.VertexCount, rotationDegrees);
        }

        /// <summary> 점이 별형 빔 중 하나라도 안인가, 전장 원 안(경계 포함)이면서 직사각형(길이와 폭 모두 경계 포함)에 든다. 여러 빔이 겹쳐도 참 거짓 하나다 </summary>
        public static bool Contains(in BossStarShape shape, Vector3 point, float rotationDegrees)
        {
            Vector3 fromCenter = point - shape.ArenaCenter;
            fromCenter.y = 0f;

            float limit = shape.ArenaRadius * shape.ArenaRadius;
            if (fromCenter.sqrMagnitude > (limit + (limit * ARENA_EPSILON)))
            {
                return false;
            }

            for (int i = 0; i < shape.BeamCount; ++i)
            {
                GetBeam(in shape, i, rotationDegrees, out Vector3 start, out Vector3 end);
                if (IsInsideBeam(start, end, shape.BeamWidth, point))
                {
                    return true;
                }
            }

            return false;
        }

        // 두 꼭짓점을 잇는 직사각형 안인지, 길이 방향은 두 끝점 사이이고 폭 방향은 중심선에서 폭의 절반 이내이다
        private static bool IsInsideBeam(Vector3 start, Vector3 end, float width, Vector3 point)
        {
            Vector3 axis = end - start;
            axis.y = 0f;
            float lengthSqr = axis.sqrMagnitude;
            if (lengthSqr < MIN_LENGTH_SQR)
            {
                return false;
            }

            float length = Mathf.Sqrt(lengthSqr);
            Vector3 unit = axis / length;
            Vector3 offset = point - start;
            offset.y = 0f;

            float along = (offset.x * unit.x) + (offset.z * unit.z);
            float side  = Mathf.Abs((offset.x * unit.z) - (offset.z * unit.x));
            return (along >= -EDGE_EPSILON) && (along <= (length + EDGE_EPSILON)) && (side <= ((width * HALF) + EDGE_EPSILON));
        }
    }
}
