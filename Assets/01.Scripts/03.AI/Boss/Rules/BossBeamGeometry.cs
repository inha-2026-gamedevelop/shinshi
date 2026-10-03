// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버와 클라이언트가 함께 쓰는 순수 기하 계산이다. 클라이언트는 표현 메시와 방향을 만드는 데만 쓰고 전투 판정은 서버만 한다
    // 경계는 전장 원과 띠의 양쪽 변 모두 포함한다(월백 다른 범위 판정과 같은 규칙)
    /// <summary> 고정 평행 빔의 배치와 포함 판정 </summary>
    public static class BossBeamGeometry
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float EDGE_EPSILON   = 0.001f;  // m, 띠 변의 부동소수 오차 흡수
        private const float ARENA_EPSILON  = 0.0001f; // m^2 비율, 전장 원 경계의 부동소수 오차 흡수
        private const float HALF           = 0.5f;
        private const float RIGHT_DEGREES  = 90f;
        private const float MIN_DIRECTION_SQR = 0.000001f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 방향의 오른쪽(위에서 볼 때 시계 방향 90도), 방향이 0에 가까우면 앞쪽의 오른쪽이다 </summary>
        public static Vector3 RightOf(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < MIN_DIRECTION_SQR)
            {
                direction = Vector3.forward;
            }

            return Quaternion.AngleAxis(RIGHT_DEGREES, Vector3.up) * direction.normalized;
        }

        // 빔은 전장 중심을 기준으로 방향의 좌우에 대칭으로 놓는다. 줄 사이 간격은 중심선 사이 거리이며 offset의 두 배다
        // 두 줄이면 중심에서 좌우 offset씩, 세 줄이면 가운데 한 줄과 좌우 2 x offset씩이다
        /// <summary> index번째(0부터, 왼쪽부터) 빔의 기하 값 </summary>
        public static BossBeamShape Place(Vector3 arenaCenter, float arenaRadius, Vector3 baseDirection, int index, int count,
            float offset, float width)
        {
            baseDirection.y = 0f;
            Vector3 direction = (baseDirection.sqrMagnitude < MIN_DIRECTION_SQR) ? Vector3.forward : baseDirection.normalized;
            float lateral = (index - ((count - 1) * HALF)) * (offset * 2f);
            return new BossBeamShape(arenaCenter, arenaRadius, direction, lateral, width);
        }

        /// <summary> 점이 빔 안인가, 폭 띠와 원형 전장의 교집합이며 수평 거리와 띠의 양쪽 변을 모두 포함한다 </summary>
        public static bool Contains(in BossBeamShape shape, Vector3 point)
        {
            Vector3 offset = point - shape.ArenaCenter;
            offset.y = 0f;

            float limit = shape.ArenaRadius * shape.ArenaRadius;
            if (offset.sqrMagnitude > (limit + (limit * ARENA_EPSILON)))
            {
                return false;
            }

            float lateral = Vector3.Dot(offset, shape.Right);
            return Mathf.Abs(lateral - shape.LateralOffset) <= ((shape.Width * HALF) + EDGE_EPSILON);
        }

        // 중심선이 전장 원과 만나는 두 점이다. 띠 모서리가 아니라 중심선의 길이이므로 표시용 참고이고 피해 영역을 정하는 값이 아니다
        /// <summary> 중심선과 전장 원의 교점, 중심선이 전장 밖이면 거짓이다 </summary>
        public static bool TryGetCenterLineChord(in BossBeamShape shape, out Vector3 start, out Vector3 end)
        {
            float squared = (shape.ArenaRadius * shape.ArenaRadius) - (shape.LateralOffset * shape.LateralOffset);
            if (squared < 0f)
            {
                start = shape.LineCenter;
                end   = shape.LineCenter;
                return false;
            }

            float half = Mathf.Sqrt(squared);
            start = shape.LineCenter - (shape.Direction * half);
            end   = shape.LineCenter + (shape.Direction * half);
            return true;
        }
    }
}
