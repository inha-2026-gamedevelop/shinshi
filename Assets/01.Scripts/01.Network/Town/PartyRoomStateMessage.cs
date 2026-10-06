// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 방에 바뀐 것이 있을 때 방 참가자 전원에게 보낸다. 방을 나가거나 방이 닫히면 InRoom이 거짓인 메시지가 온다.
    // 카운트다운은 서버가 보낸 시점의 남은 초라 클라이언트는 받은 시각부터 센다
    /// <summary> 내가 들어 있는 방의 현재 상태 </summary>
    public readonly struct PartyRoomStateMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly bool              _inRoom;
        private readonly int               _roomId;
        private readonly long              _revision;
        private readonly string            _name;
        private readonly EPartyMode        _mode;
        private readonly string            _contentId;
        private readonly sbyte             _difficulty;
        private readonly bool              _hasPassword;
        private readonly int               _leaderParticipantId;
        private readonly EPartyPhase       _phase;
        private readonly float             _countdownRemaining;
        private readonly PartyMemberView[] _members;

        /****************************************
        *              Properties
        ****************************************/

        public bool              InRoom              => _inRoom;
        public int               RoomId              => _roomId;
        public long              Revision            => _revision;
        public string            Name                => _name;
        public EPartyMode        Mode                => _mode;
        public string            ContentId           => _contentId;
        public sbyte             Difficulty          => _difficulty;
        public bool              HasPassword         => _hasPassword;
        public int               LeaderParticipantId => _leaderParticipantId;
        public EPartyPhase       Phase               => _phase;
        public float             CountdownRemaining  => _countdownRemaining;
        public PartyMemberView[] Members             => _members ?? Array.Empty<PartyMemberView>();

        /****************************************
        *                Methods
        ****************************************/

        public PartyRoomStateMessage(bool inRoom, int roomId, long revision, string name, EPartyMode mode, string contentId,
            sbyte difficulty, bool hasPassword, int leaderParticipantId, EPartyPhase phase, float countdownRemaining,
            PartyMemberView[] members)
        {
            _inRoom              = inRoom;
            _roomId              = roomId;
            _revision            = revision;
            _name                = name;
            _mode                = mode;
            _contentId           = contentId;
            _difficulty          = difficulty;
            _hasPassword         = hasPassword;
            _leaderParticipantId = leaderParticipantId;
            _phase               = phase;
            _countdownRemaining  = countdownRemaining;
            _members             = members;
        }

        /// <summary> 방에 들어 있지 않다는 알림 </summary>
        public static PartyRoomStateMessage Left()
        {
            return new PartyRoomStateMessage(false, 0, 0, null, EPartyMode.Normal, null, 0, false, 0, EPartyPhase.Open, 0f, null);
        }
    }
}
