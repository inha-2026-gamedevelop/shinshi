namespace Shinshi.Combat
{
    /// <summary> 모든 플레이어 입력의 공통 머리, 서버가 프로토콜과 세션과 참가자와 순번과 시각을 먼저 검증한다 </summary>
    public readonly struct PlayerInputHeader
    {
        // 직렬화는 PlayerInputSerializer가 한다. 필드를 더하거나 순서를 바꾸면 그쪽도 같이 고친다

        /****************************************
        *                Fields
        ****************************************/

        private readonly int    _protocolVersion;
        private readonly int    _sessionEpoch;
        private readonly int    _participantId;
        private readonly uint   _sequence;
        private readonly double _estimatedServerTime;

        /****************************************
        *              Properties
        ****************************************/

        public int ProtocolVersion => _protocolVersion;

        // 서버가 이 출정(던전 프로세스 한 판)에 발급한 번호. 이전 판의 입력이 늦게 도착해도 섞이지 않게 한다
        public int SessionEpoch => _sessionEpoch;

        // 서버가 배정한 참가자 번호, 연결이 아니라 출정 참가자에 묶인다
        public int ParticipantId => _participantId;

        // 참가자별로 1부터 늘어나는 입력 순번. 재접속해도 마지막 승인 순번 뒤로 이어진다
        public uint Sequence => _sequence;

        // 클라이언트가 추정한 서버 시각(NetworkTime 기준 초). 서버는 허용 창으로 자른 뒤에만 쓴다
        public double EstimatedServerTime => _estimatedServerTime;

        /****************************************
        *                Methods
        ****************************************/

        public PlayerInputHeader(int protocolVersion, int sessionEpoch, int participantId, uint sequence,
            double estimatedServerTime)
        {
            _protocolVersion     = protocolVersion;
            _sessionEpoch        = sessionEpoch;
            _participantId       = participantId;
            _sequence            = sequence;
            _estimatedServerTime = estimatedServerTime;
        }
    }
}
