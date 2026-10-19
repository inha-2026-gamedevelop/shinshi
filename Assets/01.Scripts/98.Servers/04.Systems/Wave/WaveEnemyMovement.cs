// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Systems
{
    // 몸체 위치는 돌진 두뇌가 돌려받은 값으로 직접 넣으므로 이 창구는 위치를 바꾸지 않는다.
    // walls가 null이면 벽 없이 직선으로 간다(장애물 레이어가 없는 개발 씬)
    /// <summary> 돌진 두뇌가 쓰는 서버 이동 창구, 정적 지형 벽 검사 앞에서 멈춘 위치를 돌려준다 </summary>
    public sealed class WaveEnemyMovement : IBossMovement
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IBossWallQuery _walls;

        /****************************************
        *                Methods
        ****************************************/

        public WaveEnemyMovement(IBossWallQuery walls)
        {
            _walls = walls;
        }

        public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
        {
            float limit = Mathf.Max(0f, maxDistance);
            if (_walls == null)
            {
                return limit;
            }

            return Mathf.Clamp(_walls.GetClearDistance(origin, direction, limit, bodyRadius), 0f, limit);
        }

        public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
        {
            Vector3 delta    = to - from;
            float   distance = delta.magnitude;
            if (!(distance > 0f))
            {
                return from;
            }

            Vector3 direction = delta / distance;
            return from + (direction * GetClearDistance(from, direction, distance, bodyRadius));
        }
    }
}
