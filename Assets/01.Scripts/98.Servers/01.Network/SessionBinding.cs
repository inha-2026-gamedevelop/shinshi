namespace Shinshi.Network
{
    /// <summary> 인증을 통과한 연결과 출정 참가자의 결합, 인증기가 연결의 authenticationData에 넣는다 </summary>
    public sealed class SessionBinding
    {
        // 연결 하나에 하나만 만든다. 연결이 끊기면 ServerSessionRegistry.Release가 비활성으로 바꾼다

        /****************************************
        *                Fields
        ****************************************/

        private readonly string _participantKey;
        private readonly int    _participantId;
        private readonly string _accountLeaseId;

        /****************************************
        *              Properties
        ****************************************/

        // 토큰이 증명한 영구 참가자 키. 재접속해도 같다
        public string ParticipantKey => _participantKey;

        // 이 서버 프로세스가 키에 붙인 번호, 입력 헤더의 참가자 번호로 쓴다
        public int ParticipantId => _participantId;

        // 마을 계정 접속 임대 ID, 연결이 끊기면 인증기가 해제를 보낸다. 없으면 null
        public string AccountLeaseId => _accountLeaseId;

        public bool IsActive { get; internal set; }

        /****************************************
        *                Methods
        ****************************************/

        public SessionBinding(string participantKey, int participantId)
            : this(participantKey, participantId, null)
        {
        }

        public SessionBinding(string participantKey, int participantId, string accountLeaseId)
        {
            _participantKey = participantKey;
            _participantId  = participantId;
            _accountLeaseId = accountLeaseId;
            IsActive        = true;
        }
    }
}
