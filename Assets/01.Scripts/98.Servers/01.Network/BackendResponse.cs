namespace Shinshi.Network
{
    /// <summary> 백엔드 호출 하나의 최종 결과, 재시도가 끝난 뒤 한 번만 전달된다 </summary>
    public readonly struct BackendResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int             _requestId;
        private readonly EBackendOutcome _outcome;
        private readonly int             _statusCode;
        private readonly int             _reasonCode;
        private readonly object          _payload;
        private readonly int             _attempts;
        private readonly double          _retryAfterSeconds;

        /****************************************
        *              Properties
        ****************************************/

        // BackendGateway.Send가 돌려준 번호
        public int             RequestId  => _requestId;
        public EBackendOutcome Outcome    => _outcome;

        // 마지막 시도의 HTTP 상태 코드, 응답을 받지 못했으면 0
        public int             StatusCode => _statusCode;

        // 거절 본문(ApiRejection)의 ContractCodes.RejectReason 값, 없으면 0
        public int             ReasonCode => _reasonCode;

        // Succeeded일 때 요청의 ResponseType으로 읽은 DTO
        public object          Payload    => _payload;

        // 실제로 보낸 횟수, 회로가 열려 보내지 않았으면 0
        public int             Attempts   => _attempts;

        // API가 Retry-After로 정한 최소 대기(초), 없으면 0. 호출자는 다음 시도를 이보다 앞당기지 않는다
        public double          RetryAfterSeconds => _retryAfterSeconds;

        public bool IsSucceeded => _outcome == EBackendOutcome.Succeeded;

        /****************************************
        *                Methods
        ****************************************/

        public BackendResponse(int requestId, EBackendOutcome outcome, int statusCode, int reasonCode, object payload,
            int attempts, double retryAfterSeconds = 0.0)
        {
            _requestId  = requestId;
            _outcome    = outcome;
            _statusCode = statusCode;
            _reasonCode = reasonCode;
            _payload    = payload;
            _attempts   = attempts;

            _retryAfterSeconds = retryAfterSeconds;
        }
    }
}
