namespace Shinshi.Network
{
    /// <summary> 화면에 표시할 채팅 한 줄. 채널과 보낸 사람은 서버 수신 경로에서 정한다 </summary>
    public readonly struct ChatEntry
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

        public ChatEntry(EChatChannel channel, string sender, string text)
        {
            _channel = channel;
            _sender  = sender ?? string.Empty;
            _text    = text ?? string.Empty;
        }
    }
}
