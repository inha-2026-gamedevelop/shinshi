// 서드파티
using Mirror;

namespace Shinshi.Network
{
    /// <summary> 받은 직접 초대, 남은 초 안에 AcceptInvite로 roomId를 보내면 비밀번호 없이 들어간다 </summary>
    public readonly struct PartyInviteMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int        _roomId;
        private readonly string     _roomName;
        private readonly string     _inviterName;
        private readonly EPartyMode _mode;
        private readonly string     _contentId;
        private readonly float      _expiresIn;

        /****************************************
        *              Properties
        ****************************************/

        public int        RoomId      => _roomId;
        public string     RoomName    => _roomName;
        public string     InviterName => _inviterName;
        public EPartyMode Mode        => _mode;
        public string     ContentId   => _contentId;
        public float      ExpiresIn   => _expiresIn;

        /****************************************
        *                Methods
        ****************************************/

        public PartyInviteMessage(int roomId, string roomName, string inviterName, EPartyMode mode, string contentId,
            float expiresIn)
        {
            _roomId      = roomId;
            _roomName    = roomName;
            _inviterName = inviterName;
            _mode        = mode;
            _contentId   = contentId;
            _expiresIn   = expiresIn;
        }
    }
}
