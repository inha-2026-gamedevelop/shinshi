// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> HUD 파티 칸에 서버 상태를 넣는다. 마을은 파티 방 명단, 던전은 같은 출정의 다른 플레이어를 보여 준다 </summary>
    // 체력은 복제된 PlayerCombatState에서 읽는다. 마을에서 파티원이 시야 밖이면 체력은 '-'로 둔다.
    // 스폰 목록은 Mirror 사전을 직접 돌아 Find 계열 탐색을 쓰지 않는다
    public sealed class PartyHudBinder : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float REFRESH_INTERVAL_SECONDS = 0.25f;

        [SerializeField] private HudPartyView _view = null;

        private readonly List<PlayerCombatState> _remotes = new List<PlayerCombatState>(Constants.Session.MAX_PARTY_SIZE);

        private float _nextRefreshAt = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if ((_view == null) || (Time.unscaledTime < _nextRefreshAt))
            {
                return;
            }

            _nextRefreshAt = Time.unscaledTime + REFRESH_INTERVAL_SECONDS;
            Refresh();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Refresh()
        {
            int slot = 0;
            if (NetworkClient.isConnected)
            {
                CollectRemotePlayers();
                slot = (ClientRoles.IsDungeon()) ? FillFromDungeon() : FillFromTownRoom();
            }

            for (; slot < _view.SlotCount; ++slot)
            {
                _view.ClearMember(slot);
            }
        }

        private int FillFromDungeon()
        {
            int slot = 0;
            for (int i = 0; (i < _remotes.Count) && (slot < _view.SlotCount); ++i)
            {
                PlayerCombatState remote = _remotes[i];
                SetMember(slot++, remote.DisplayName, remote.PlayerClass, remote);
            }

            return slot;
        }

        private int FillFromTownRoom()
        {
            PartyRoomStateMessage room = TownPartyClient.Room;
            if (!room.InRoom)
            {
                return 0;
            }

            string            localName = ClientRoles.LocalCharacterName();
            PartyMemberView[] members   = room.Members;
            int slot = 0;
            for (int i = 0; (i < members.Length) && (slot < _view.SlotCount); ++i)
            {
                PartyMemberView member = members[i];
                if (string.Equals(member.Name, localName, StringComparison.Ordinal))
                {
                    continue;
                }

                SetMember(slot++, member.Name, member.PlayerClass, FindRemote(member.Name));
            }

            return slot;
        }

        private void SetMember(int slot, string memberName, EPlayerClass playerClass, PlayerCombatState combat)
        {
            bool hasHp = (combat != null) && (combat.MaxHealth > 0f);
            int  hp    = hasHp ? LocalPlayerHudRules.ToDisplayInt(combat.Health, combat.MaxHealth) : 0;
            int  max   = hasHp ? LocalPlayerHudRules.ToDisplayInt(combat.MaxHealth, combat.MaxHealth) : 0;
            _view.SetMember(slot, memberName, playerClass, hasHp, hp, max);
        }

        private PlayerCombatState FindRemote(string characterName)
        {
            for (int i = 0; i < _remotes.Count; ++i)
            {
                if (string.Equals(_remotes[i].DisplayName, characterName, StringComparison.Ordinal))
                {
                    return _remotes[i];
                }
            }

            return null;
        }

        // 소유하지 않은 플레이어만 모은다. 적과 보스도 스폰 목록에 있으므로 구성 요소로 거른다
        private void CollectRemotePlayers()
        {
            _remotes.Clear();
            foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            {
                if ((identity == null) || (identity.isOwned) || (!identity.TryGetComponent(out PlayerCombatState combat)))
                {
                    continue;
                }

                _remotes.Add(combat);
            }
        }
    }
}
