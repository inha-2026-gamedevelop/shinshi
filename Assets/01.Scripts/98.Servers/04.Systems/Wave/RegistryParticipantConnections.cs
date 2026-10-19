// System
using System;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Systems
{
    // 명단은 던전 호스트가 출정 중에 만든다. 팩토리 생성 시점에 없을 수 있어 호출 때마다 공급자에서 얻는다.
    // 레지스트리의 대상이 PlayerServer가 아니거나 명단에 없으면 접속 중으로 보지 않는다
    /// <summary> 레지스트리와 참가자 명단으로 접속 여부를 답하는 어댑터, 도착 판정이 접속 중 생존자만 세는 데 쓴다 </summary>
    public sealed class RegistryParticipantConnections : IBossParticipantConnection
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ICombatTargetQuery      _targets;
        private readonly Func<ParticipantRoster> _roster;

        /****************************************
        *                Methods
        ****************************************/

        public RegistryParticipantConnections(ICombatTargetQuery targets, Func<ParticipantRoster> roster)
        {
            _targets = targets;
            _roster  = roster;
        }

        public bool IsConnected(int combatId)
        {
            ParticipantRoster roster = _roster?.Invoke();
            if ((_targets == null) || (roster == null))
            {
                return false;
            }

            if ((!_targets.TryGet(combatId, out ICombatTarget target)) || (!(target is PlayerServer player)))
            {
                return false;
            }

            return (roster.TryGet(player.ParticipantId, out ParticipantSnapshot snapshot))
                && (snapshot.ConnectionState == EParticipantConnectionState.Connected);
        }
    }
}
