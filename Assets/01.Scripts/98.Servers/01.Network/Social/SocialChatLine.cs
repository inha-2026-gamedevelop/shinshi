namespace Shinshi.Network
{
    /// <summary> 백엔드로 보낼 귓속말 또는 길드 채팅 한 줄, 채팅 서버가 정리와 정규화를 끝낸 값이다 </summary>
    public readonly struct SocialChatLine
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EChatChannel _channel;
        private readonly string       _characterId;
        private readonly string       _senderName;
        private readonly string       _targetName;
        private readonly string       _text;
        private readonly string       _apiParticipantId;

        /****************************************
        *              Properties
        ****************************************/

        // Whisper 또는 Guild
        public EChatChannel Channel          => _channel;
        public string       CharacterId      => _characterId;
        public string       SenderName       => _senderName;

        // 귓속말 대상의 정규화한 이름, 길드 채팅은 null
        public string       TargetName       => _targetName;
        public string       Text             => _text;

        // 채팅 기록용, 던전만 있다
        public string       ApiParticipantId => _apiParticipantId;

        /****************************************
        *                Methods
        ****************************************/

        public SocialChatLine(EChatChannel channel, string characterId, string senderName, string targetName, string text,
            string apiParticipantId)
        {
            _channel          = channel;
            _characterId      = characterId;
            _senderName       = senderName;
            _targetName       = targetName;
            _text             = text;
            _apiParticipantId = apiParticipantId;
        }
    }
}
