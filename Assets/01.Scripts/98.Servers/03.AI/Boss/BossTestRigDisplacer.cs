// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 운영 서버의 플레이어, 동료, 소환물 이동에는 이 창구의 구현이 아직 없다. 이 클래스는 시험 구동기 안에서만 위치를 직접 옮기는 로컬 대역이며
    // 서버 이동과 네트워크 동기화를 대신하지 않는다. 전장 경계와 벽의 막힘은 보스 몸체의 검사 창구(IBossWallQuery)를 그대로 빌려 쓴다.
    // 그 창구는 대상 몸체 반경만큼 안쪽에서 멈추고, 이미 경계 밖에 선 대상은 0m 밀린다
    /// <summary> 시험 구동기용 로컬 밀림 창구, 등록한 시험 대상의 위치를 전장 경계와 벽 안에서만 옮긴다 </summary>
    public sealed class BossTestRigDisplacer : IBossHitDisplacer
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IBossWallQuery                         _walls;
        private readonly Dictionary<int, BossTestRigCombatant> _targets = new Dictionary<int, BossTestRigCombatant>();

        /****************************************
        *                Methods
        ****************************************/

        public BossTestRigDisplacer(IBossWallQuery walls)
        {
            _walls = walls;
        }

        public void Register(BossTestRigCombatant target)
        {
            _targets[target.CombatId] = target;
        }

        public float TryPush(int combatId, Vector3 direction, float maxDistance)
        {
            if ((!_targets.TryGetValue(combatId, out BossTestRigCombatant target)) || (maxDistance <= 0f))
            {
                return 0f;
            }

            Vector3 origin   = target.Position;
            float   distance = Mathf.Clamp(_walls.GetClearDistance(origin, direction, maxDistance, target.BodyRadius), 0f, maxDistance);

            target.Position = origin + (direction * distance);
            return distance;
        }
    }
}
