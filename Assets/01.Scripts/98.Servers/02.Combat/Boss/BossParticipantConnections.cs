// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Combat
{
    // 보스 패턴이 전투 번호로 묻는 접속 상태를 출정 명단(ParticipantRoster)의 연결 상태로 답한다. 생사(LifeState)로 대신하지 않는다:
    // 연결이 끊겨 유예 중인 캐릭터는 서버에 살아 남아 있어도 접속 상태가 아니고, 유예가 끝나 이탈한 참가자도 아니다.
    // 접속 중(Connected)만 참이고 재접속 유예(ReconnectGrace)와 이탈(Departed)은 거짓이다. 전투 번호가 참가자 목록에 없으면 거짓이다.
    // 틱마다 불려도 할당하지 않는다: 참가자 목록은 파티 상한 이하라 순차 탐색하고 명단 조회는 값 구조체를 돌려준다
    /// <summary> 전투 번호의 현재 접속 상태를 출정 명단에서 읽어 보스 패턴에 답한다 </summary>
    public sealed class BossParticipantConnections : IBossParticipantConnection
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly List<PlayerServer> _players;
        private readonly ParticipantRoster  _roster;

        /****************************************
        *                Methods
        ****************************************/

        // players는 전투 런타임이 가진 목록 그대로다. 복사하지 않아 입장, 퇴장이 바로 반영된다
        public BossParticipantConnections(List<PlayerServer> players, ParticipantRoster roster)
        {
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _roster  = roster ?? throw new ArgumentNullException(nameof(roster));
        }

        public bool IsConnected(int combatId)
        {
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

                return (_roster.TryGet(player.ParticipantId, out ParticipantSnapshot snapshot))
                    && (snapshot.ConnectionState == EParticipantConnectionState.Connected);
            }

            return false;
        }
    }
}
