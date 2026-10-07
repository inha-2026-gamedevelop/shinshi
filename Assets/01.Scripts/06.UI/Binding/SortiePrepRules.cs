// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 파티 방 상태를 출정 준비 창 값으로 옮기는 규칙 </summary>
    public static class SortiePrepRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 방 명단을 창의 칸 순서대로 옮긴다. 레벨과 전투력은 방 상태에 없어 0이다 </summary>
        public static void BuildMembers(in PartyRoomStateMessage room, List<SortieMemberData> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            if (!room.InRoom)
            {
                return;
            }

            PartyMemberView[] members = room.Members;
            for (int i = 0; i < members.Length; ++i)
            {
                PartyMemberView member = members[i];
                into.Add(new SortieMemberData(member.Name, member.PlayerClass, member.ParticipantId == room.LeaderParticipantId,
                    member.Ready == EPartyReadyState.Ready, true, true, 0, 0));
            }
        }

        /// <summary> 이름으로 자기 칸을 찾는다. 방 상태에 받는 사람의 참가자 번호가 없어서다 </summary>
        public static bool TryFindLocal(in PartyRoomStateMessage room, string localName, out PartyMemberView member)
        {
            PartyMemberView[] members = room.Members;
            for (int i = 0; i < members.Length; ++i)
            {
                if (string.Equals(members[i].Name, localName, StringComparison.Ordinal))
                {
                    member = members[i];
                    return true;
                }
            }

            member = default;
            return false;
        }

        /// <summary> 방장을 뺀 모두가 준비했는지, 혼자면 참 </summary>
        public static bool AreGuestsReady(in PartyRoomStateMessage room)
        {
            PartyMemberView[] members = room.Members;
            for (int i = 0; i < members.Length; ++i)
            {
                if ((members[i].ParticipantId != room.LeaderParticipantId) && (members[i].Ready != EPartyReadyState.Ready))
                {
                    return false;
                }
            }

            return members.Length > 0;
        }

        public static bool AreAllReady(in PartyRoomStateMessage room)
        {
            PartyMemberView[] members = room.Members;
            for (int i = 0; i < members.Length; ++i)
            {
                if (members[i].Ready != EPartyReadyState.Ready)
                {
                    return false;
                }
            }

            return members.Length > 0;
        }

        /// <summary> 처음 방을 만들 보스전, 체험판에 있는 것을 카탈로그 순서로 고른다 </summary>
        public static bool TrySelectBattle(ProgressionCatalogSO catalog, EBossDifficulty difficulty,
            out ProgressionCatalogSO.BossBattleDefinition battle)
        {
            battle = null;
            if ((catalog == null) || (catalog.BossBattles == null))
            {
                return false;
            }

            IReadOnlyList<ProgressionCatalogSO.BossBattleDefinition> battles = catalog.BossBattles;
            for (int i = 0; i < battles.Count; ++i)
            {
                if ((battles[i] != null) && (battles[i].AvailableInTrial) && (battles[i].Difficulty == difficulty))
                {
                    battle = battles[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary> 같은 보스의 다른 난이도 보스전을 찾는다 </summary>
        public static bool TryFindSibling(ProgressionCatalogSO catalog, string battleId, EBossDifficulty difficulty,
            out ProgressionCatalogSO.BossBattleDefinition sibling)
        {
            sibling = null;
            if ((catalog == null) || (!catalog.TryGetBattle(battleId, out ProgressionCatalogSO.BossBattleDefinition current)))
            {
                return false;
            }

            return catalog.TryGetBattle(current.BossId, difficulty, out sibling);
        }

        /// <summary> 서버 난이도 값을 창 난이도로 바꾼다, 정의에 없으면 Easy </summary>
        public static EBossDifficulty ToDifficulty(sbyte difficulty)
        {
            return Enum.IsDefined(typeof(EBossDifficulty), (int)difficulty) ? (EBossDifficulty)difficulty : EBossDifficulty.Easy;
        }
    }
}
