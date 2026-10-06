// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    /// <summary> 마을의 공개 방 목록, 요청한 클라이언트에게만 온다. 필터는 클라이언트가 한다 </summary>
    public readonly struct PartyRoomListMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PartyRoomSummary[] _rooms;

        /****************************************
        *              Properties
        ****************************************/

        public PartyRoomSummary[] Rooms => _rooms ?? Array.Empty<PartyRoomSummary>();

        /****************************************
        *                Methods
        ****************************************/

        public PartyRoomListMessage(PartyRoomSummary[] rooms)
        {
            _rooms = rooms;
        }
    }
}
