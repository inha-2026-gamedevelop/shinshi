// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Combat
{
    // 보스 기믹이 전투 번호로 묻는 참가자 명단, 최대 체력, 마지막 부활 시각과 서버 사망 확정, 승리 보호를 전투 런타임의 참가자 목록으로 답한다.
    // 참가자인지는 출정 명단(ParticipantRoster)의 연결 상태로 정한다: 접속 중(Connected)과 재접속 유예(ReconnectGrace)는 참가자이고
    // 이탈(Departed)과 명단에 없는 번호는 참가자가 아니다. 유예 중에도 서버에 캐릭터가 남은 참가자만 목록에 있으므로 그 사람만 답한다.
    // 사망 확정 계약(IWolbaekFinalKiller)에는 시각 인자가 없어 서버 시각은 serverClock으로 읽는다. 보스 틱 안에서 불리므로 그 틱의 시각을 주는 시계를 넘긴다.
    // 틱마다 불려도 할당하지 않는다: 참가자 목록은 파티 상한 이하라 순차 탐색하고 명단 조회는 값 구조체를 돌려준다
    /// <summary> 보스 기믹의 참가자 창구, 명단과 최대 체력과 부활 시각 조회, 서버 사망 확정, 승리 보호를 맡는다 </summary>
    public sealed class BossParticipantSource : IBossParticipantRoster, IBossParticipantHealth, IBossReviveSource,
        IWolbaekFinalKiller, IWolbaekFinalGuard
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly List<PlayerServer> _players;
        private readonly ParticipantRoster  _roster;
        private readonly Func<int>          _remainingRevives;
        private readonly Func<double>       _serverClock;

        /****************************************
        *                Methods
        ****************************************/

        // players는 전투 런타임이 가진 목록 그대로다. 복사하지 않아 입장, 퇴장이 바로 반영된다.
        // remainingRevives는 남은 공용 부활 횟수, serverClock은 이번 틱의 서버 시각을 돌려준다
        public BossParticipantSource(List<PlayerServer> players, ParticipantRoster roster, Func<int> remainingRevives,
            Func<double> serverClock)
        {
            _players          = players ?? throw new ArgumentNullException(nameof(players));
            _roster           = roster ?? throw new ArgumentNullException(nameof(roster));
            _remainingRevives = remainingRevives ?? throw new ArgumentNullException(nameof(remainingRevives));
            _serverClock      = serverClock ?? throw new ArgumentNullException(nameof(serverClock));
        }

        // 부르는 쪽이 비운 목록에 덧붙인다
        public int CollectParticipants(List<int> combatIds)
        {
            for (int i = 0; i < _players.Count; ++i)
            {
                PlayerServer player = _players[i];
                if (IsParticipant(player, out _))
                {
                    combatIds.Add(player.CombatId);
                }
            }

            return combatIds.Count;
        }

        public bool TryGetParticipant(int combatId, out BossParticipantInfo info)
        {
            if (!TryFind(combatId, out PlayerServer player, out bool isConnected))
            {
                info = default;
                return false;
            }

            info = new BossParticipantInfo(player.LifeState, isConnected, _remainingRevives() > 0, player.Position);
            return true;
        }

        public bool TryGetMaxHealth(int combatId, out float maxHealth)
        {
            if ((!TryFind(combatId, out PlayerServer player, out _)) || (!(player.MaxHealth > 0f)))
            {
                maxHealth = 0f;
                return false;
            }

            maxHealth = player.MaxHealth;
            return true;
        }

        public bool TryGetLastReviveServerTime(int combatId, out double serverTime)
        {
            if ((!TryFind(combatId, out PlayerServer player, out _)) || (player.LastReviveTime == PlayerServer.NO_REVIVE_TIME))
            {
                serverTime = 0.0;
                return false;
            }

            serverTime = player.LastReviveTime;
            return true;
        }

        // 서버 보호 중인 참가자도 죽인다. 보호가 막을지는 부르는 기믹이 정한다
        public bool TryKill(int combatId, int castInstanceId, EWolbaekFinalDeath cause)
        {
            return (TryFind(combatId, out PlayerServer player, out _)) && (player.KillByServer(_serverClock()));
        }

        // 쓰러진 사람과 유예 중인 사람까지 목록의 모두에게 건다. 풀지 않는다: 출정이 끝나면 참가자 개체가 사라진다
        public void GrantVictoryProtection(int castInstanceId, double serverTime)
        {
            for (int i = 0; i < _players.Count; ++i)
            {
                _players[i].SetServerProtection(true);
            }
        }

        private bool TryFind(int combatId, out PlayerServer player, out bool isConnected)
        {
            player      = null;
            isConnected = false;
            if (combatId == CombatIds.NONE)
            {
                return false;
            }

            for (int i = 0; i < _players.Count; ++i)
            {
                if (_players[i].CombatId != combatId)
                {
                    continue;
                }

                if (!IsParticipant(_players[i], out isConnected))
                {
                    return false;
                }

                player = _players[i];
                return true;
            }

            return false;
        }

        private bool IsParticipant(PlayerServer player, out bool isConnected)
        {
            isConnected = false;
            if ((!_roster.TryGet(player.ParticipantId, out ParticipantSnapshot snapshot))
                || (snapshot.ConnectionState == EParticipantConnectionState.Departed))
            {
                return false;
            }

            isConnected = snapshot.ConnectionState == EParticipantConnectionState.Connected;
            return true;
        }
    }
}
