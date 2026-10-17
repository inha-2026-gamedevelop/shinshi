// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 귓속말과 길드 채팅은 백엔드 전달함 행의 ID(DeliveryId)를 싣는다. 같은 행이 재전송이나 서버 이동으로 두 번 올 수 있어
    // 클라이언트가 이 값으로 중복을 버리고 수신 확인을 보낸다. 전체, 파티, 보낸 귓속말 확인 줄은 0이다
    /// <summary> 서버가 검증, 정리한 채팅 한 줄. 전체와 파티는 수신 대상 모두(보낸 사람 포함)에게 한 번 온다 </summary>
    public readonly struct ChatBroadcastMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EChatChannel _channel;
        private readonly string       _sender;
        private readonly string       _text;
        private readonly long         _deliveryId;

        /****************************************
        *              Properties
        ****************************************/

        public EChatChannel Channel    => _channel;

        // 귓속말(Whisper, WhisperSent)이면 상대 이름이다
        public string       Sender     => _sender;
        public string       Text       => _text;
        public long         DeliveryId => _deliveryId;

        /****************************************
        *                Methods
        ****************************************/

        public ChatBroadcastMessage(EChatChannel channel, string sender, string text, long deliveryId = 0)
        {
            _channel    = channel;
            _sender     = sender;
            _text       = text;
            _deliveryId = deliveryId;
        }
    }
}
