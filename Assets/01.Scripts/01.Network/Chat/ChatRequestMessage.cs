// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 보낸 사람은 서버가 인증된 연결의 결합에서 정한다. 클라이언트는 채널과 본문만 보낸다
    /// <summary> 클라이언트가 채팅 한 줄을 보내는 요청, 거절되면 ChatRejectMessage가 온다 </summary>
    public readonly struct ChatRequestMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EChatChannel _channel;
        private readonly string       _text;

        /****************************************
        *              Properties
        ****************************************/

        public EChatChannel Channel => _channel;
        public string       Text    => _text;

        /****************************************
        *                Methods
        ****************************************/

        public ChatRequestMessage(EChatChannel channel, string text)
        {
            _channel = channel;
            _text    = text;
        }
    }
}
