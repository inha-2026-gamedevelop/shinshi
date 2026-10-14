// System
using System;
using System.Collections.Generic;

namespace Shinshi.Combat
{
    // 보스 밀어내기가 전투 번호로 묻는 대상을 참가자 목록에서 찾는다. 지금은 플레이어만 답한다: 동료와 소환물은 전투 번호가 목록에 없어
    // 거짓이 되고 밀리지 않는다. 틱마다 불려도 할당하지 않는다: 참가자 목록은 파티 상한 이하라 순차 탐색한다
    /// <summary> 전투 번호의 플레이어를 밀어낼 대상으로 보스에 답한다 </summary>
    public sealed class BossPlayerDisplaceLookup : IBossDisplaceLookup
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly List<PlayerServer> _players;

        /****************************************
        *                Methods
        ****************************************/

        // players는 전투 런타임이 가진 목록 그대로다. 복사하지 않아 입장, 퇴장이 바로 반영된다
        public BossPlayerDisplaceLookup(List<PlayerServer> players)
        {
            _players = players ?? throw new ArgumentNullException(nameof(players));
        }

        public bool TryGetDisplaceTarget(int combatId, out IBossDisplaceTarget target)
        {
            target = null;
            if (combatId == CombatIds.NONE)
            {
                return false;
            }

            for (int i = 0; i < _players.Count; ++i)
            {
                PlayerServer player = _players[i];
                if (player.CombatId != combatId)
                {
                    continue;
                }

                target = player;
                return true;
            }

            return false;
        }
    }
}
