// 서드파티
using Mirror;

namespace Shinshi.Network
{
    /// <summary> 서버가 검증, 정리한 채팅 한 줄, 채널의 수신 대상 모두(보낸 사람 포함)에게 한 번 온다 </summary>
    public readonly struct ChatBroadcastMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EChatChannel _channel;
        private readonly string       _sender;
        private readonly string       _text;

        /****************************************
        *              Properties
        ****************************************/

        public EChatChannel Channel => _channel;
        public string       Sender  => _sender;
        public string       Text    => _text;

        /****************************************
        *                Methods
        ****************************************/

        public ChatBroadcastMessage(EChatChannel channel, string sender, string text)
        {
            _channel = channel;
            _sender  = sender;
            _text    = text;
        }
    }
}
