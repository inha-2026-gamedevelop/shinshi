namespace Shinshi.Network
{
    /// <summary> 전송 계층이 받은 HTTP 상태 코드와 본문 </summary>
    public readonly struct BackendTransportResult
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int    _statusCode;
        private readonly string _body;
        private readonly double _retryAfterSeconds;

        /****************************************
        *              Properties
        ****************************************/

        public int    StatusCode        => _statusCode;
        public string Body              => _body;

        // Retry-After 헤더(초), 없으면 0. 전송 계층이 상한으로 잘라 둔다
        public double RetryAfterSeconds => _retryAfterSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public BackendTransportResult(int statusCode, string body, double retryAfterSeconds = 0.0)
        {
            _statusCode        = statusCode;
            _body              = body;
            _retryAfterSeconds = retryAfterSeconds;
        }
    }
}
