// 서드파티
using Mirror;

namespace Shinshi.Network
{
    /// <summary> 채팅 요청을 서버가 거절했다는 안내, 보낸 사람에게만 온다 </summary>
    public readonly struct ChatRejectMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EChatReject _reason;

        /****************************************
        *              Properties
        ****************************************/

        public EChatReject Reason => _reason;

        /****************************************
        *                Methods
        ****************************************/

        public ChatRejectMessage(EChatReject reason)
        {
            _reason = reason;
        }
    }
}
