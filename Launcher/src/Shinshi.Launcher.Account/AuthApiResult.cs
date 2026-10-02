namespace Shinshi.Launcher.Account
{
    /// <summary> 본문이 없는 인증 API 호출 결과. 토큰을 담지 않는다 </summary>
    public class AuthApiResult
    {
        /****************************************
        *              Properties
        ****************************************/

        public EAuthApiError Error      { get; }

        // ApiRejection.reasonCode, 거절 본문이 없으면 0
        public int           ReasonCode { get; }

        // HTTP 상태, 응답을 받지 못했으면 0
        public int           StatusCode { get; }

        public bool IsSuccess => Error == EAuthApiError.None;

        // 서버가 요청을 처리했는지 알 수 없다. 회전형 자격(갱신 토큰, 초대 코드)은 같은 값을 다시 보내지 않는다
        public bool IsOutcomeUnknown => (Error == EAuthApiError.ResponseLost) || (Error == EAuthApiError.Malformed);

        /****************************************
        *                Methods
        ****************************************/

        internal AuthApiResult(EAuthApiError error, int reasonCode, int statusCode)
        {
            Error      = error;
            ReasonCode = reasonCode;
            StatusCode = statusCode;
        }

        internal static AuthApiResult Success(int statusCode) { return new AuthApiResult(EAuthApiError.None, 0, statusCode); }

        internal static AuthApiResult Fail(EAuthApiError error, int reasonCode, int statusCode)
        {
            return new AuthApiResult(error, reasonCode, statusCode);
        }
    }

    /// <summary> 본문이 있는 인증 API 호출 결과. Value가 토큰을 담을 수 있으므로 로그에 직렬화하지 않는다 </summary>
    public sealed class AuthApiResult<T> : AuthApiResult where T : class
    {
        /****************************************
        *              Properties
        ****************************************/

        public T? Value { get; }

        /****************************************
        *                Methods
        ****************************************/

        internal AuthApiResult(EAuthApiError error, int reasonCode, int statusCode, T? value)
            : base(error, reasonCode, statusCode)
        {
            Value = value;
        }

        internal static AuthApiResult<T> Success(T value, int statusCode)
        {
            return new AuthApiResult<T>(EAuthApiError.None, 0, statusCode, value);
        }

        internal static AuthApiResult<T> From(AuthApiResult failure)
        {
            return new AuthApiResult<T>(failure.Error, failure.ReasonCode, failure.StatusCode, null);
        }
    }
}
