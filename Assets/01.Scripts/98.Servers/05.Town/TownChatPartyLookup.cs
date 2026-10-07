// System
using System;
using System.Collections.Generic;

namespace Shinshi.Network
{
    /// <summary> 마을 파티 방의 참가자를 파티 채팅 수신 대상으로 준다 </summary>
    public sealed class TownChatPartyLookup : IChatPartyLookup
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly TownPartyDirector _party;

        /****************************************
        *                Methods
        ****************************************/

        public TownChatPartyLookup(TownPartyDirector party)
        {
            _party = party ?? throw new ArgumentNullException(nameof(party));
        }

        public bool TryGetPartyMembers(int participantId, List<int> into)
        {
            PartyRoom room = _party.RoomOf(participantId);
            if (room == null)
            {
                return false;
            }

            for (int i = 0; i < room.Count; ++i)
            {
                into.Add(room.MemberAt(i).ParticipantId);
            }

            return true;
        }
    }
}
