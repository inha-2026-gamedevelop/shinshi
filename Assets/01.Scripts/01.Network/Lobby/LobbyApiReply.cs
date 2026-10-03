namespace Shinshi.Network
{
    /// <summary> 로비 화면의 API 응답 하나. 성공이면 Value가 있고, 실패면 HTTP 상태와 거절 코드만 있다 </summary>
    public sealed class LobbyApiReply<T> where T : class
    {
        /****************************************
        *              Properties
        ****************************************/

        public T    Value      { get; }

        // 0이면 서버에 닿지 못했다(연결 실패, 시간 초과)
        public long StatusCode { get; }

        // ContractCodes.RejectReason, 거절 본문이 없으면 NONE이다
        public int  ReasonCode { get; }

        public bool IsSuccess  => Value != null;

        /****************************************
        *                Methods
        ****************************************/

        public LobbyApiReply(T value, long statusCode, int reasonCode)
        {
            Value      = value;
            StatusCode = statusCode;
            ReasonCode = reasonCode;
        }
    }
}
