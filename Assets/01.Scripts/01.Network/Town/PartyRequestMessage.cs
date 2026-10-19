// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 요청자는 서버가 인증된 연결로 정한다. 메시지에 참가자, 계정, 캐릭터를 싣지 않는다.
    // 종류별로 쓰는 필드만 채운다. 생성: mode, contentId, name(방 이름), password / 입장: roomId, password /
    // 준비: ready, 물약 수량 / 초대: name(대상 캐릭터 이름) / 초대 수락, 거절: roomId
    /// <summary> 클라이언트의 마을 파티 요청, 결과는 PartyResultMessage와 PartyRoomStateMessage로 온다 </summary>
    public readonly struct PartyRequestMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EPartyAction _action;
        private readonly int          _roomId;
        private readonly EPartyMode   _mode;
        private readonly string       _contentId;
        private readonly string       _name;
        private readonly string       _password;
        private readonly bool         _ready;
        private readonly int          _smallPotions;
        private readonly int          _largePotions;
        private readonly uint         _requestId;

        /****************************************
        *              Properties
        ****************************************/

        public EPartyAction Action       => _action;
        public int          RoomId       => _roomId;
        public EPartyMode   Mode         => _mode;

        // 일반전은 지역 ID, 보스전은 보스전 정의 ID
        public string       ContentId    => _contentId;
        public string       Name         => _name;
        public string       Password     => _password;
        public bool         Ready        => _ready;
        public int          SmallPotions => _smallPotions;
        public int          LargePotions => _largePotions;

        // 0이면 상관 ID가 없는 요청이다. 서버는 판정에 쓰지 않고 결과 메시지에 그대로 돌려준다
        public uint         RequestId    => _requestId;

        /****************************************
        *                Methods
        ****************************************/

        public PartyRequestMessage(EPartyAction action, int roomId, EPartyMode mode, string contentId, string name,
            string password, bool ready, int smallPotions, int largePotions, uint requestId = 0)
        {
            _requestId    = requestId;
            _action       = action;
            _roomId       = roomId;
            _mode         = mode;
            _contentId    = contentId;
            _name         = name;
            _password     = password;
            _ready        = ready;
            _smallPotions = smallPotions;
            _largePotions = largePotions;
        }
    }
}
