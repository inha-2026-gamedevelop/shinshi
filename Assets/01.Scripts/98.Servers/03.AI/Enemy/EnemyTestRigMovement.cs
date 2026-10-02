// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.AI
{
    // 장애물 레이어가 프로젝트에 없으면 CombatLineOfSight가 검사하지 않으므로 벽 없이 직선으로 간다.
    // 실제 서버 이동 창구가 생기기 전까지 시험 구동기가 쓰는 대역이다
    /// <summary> 시험 구동기용 이동 창구, 정적 지형 레이어의 벽 앞에서 몸체 반경만큼 떨어져 멈춘다 </summary>
    public sealed class EnemyTestRigMovement : IBossMovement
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EnemyServer       _body;
        private readonly CombatLineOfSight _lineOfSight;

        /****************************************
        *                Methods
        ****************************************/

        public EnemyTestRigMovement(EnemyServer body, CombatLineOfSight lineOfSight)
        {
            _body        = body;
            _lineOfSight = lineOfSight;
        }

        public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
        {
            float clear = _lineOfSight.GetClearDistance(origin, direction, maxDistance + bodyRadius) - bodyRadius;
            return Mathf.Clamp(clear, 0f, maxDistance);
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
            Vector3 reached   = from + (direction * GetClearDistance(from, direction, distance, bodyRadius));
            _body.SetPosition(reached);
            return reached;
        }
    }
}
