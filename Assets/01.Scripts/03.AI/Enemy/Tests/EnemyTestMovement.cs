// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    /// <summary> 돌진 시험용 이동 창구, 벽은 z = WallZ 평면이고 몸체 반경만큼 앞에서 멈춘다 </summary>
    public sealed class EnemyTestMovement : IBossMovement
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EnemyServer _body;

        /****************************************
        *              Properties
        ****************************************/

        // 예고도 아는 벽
        public float WallZ       { get; set; } = float.PositiveInfinity;

        // 이동할 때만 막는 벽, 길이를 잰 뒤에 생긴 장애물 모사
        public float HiddenWallZ { get; set; } = float.PositiveInfinity;

        /****************************************
        *                Methods
        ****************************************/

        public EnemyTestMovement(EnemyServer body)
        {
            _body = body;
        }

        public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
        {
            return Mathf.Clamp(LimitAlong(origin, direction, WallZ, bodyRadius, maxDistance), 0f, maxDistance);
        }

        public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
        {
            Vector3 delta    = to - from;
            float   distance = delta.magnitude;
            if (distance <= 0f)
            {
                return from;
            }

            Vector3 direction = delta / distance;
            float   allowed   = Mathf.Min(
                LimitAlong(from, direction, WallZ, bodyRadius, distance),
                LimitAlong(from, direction, HiddenWallZ, bodyRadius, distance));

            Vector3 reached = from + (direction * Mathf.Max(0f, allowed));
            _body.SetPosition(reached);
            return reached;
        }

        private static float LimitAlong(Vector3 origin, Vector3 direction, float wallZ, float bodyRadius, float max)
        {
            if ((float.IsPositiveInfinity(wallZ)) || (direction.z <= 0f))
            {
                return max;
            }

            return Mathf.Min(max, (wallZ - bodyRadius - origin.z) / direction.z);
        }
    }
}
