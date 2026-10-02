namespace Shinshi.Launcher.Account
{
    /// <summary> LauncherSession 작업 결과. 토큰을 담지 않는다 </summary>
    public class SessionResult
    {
        /****************************************
        *              Properties
        ****************************************/

        public ESessionError Error      { get; }
        public EAuthApiError ApiError   { get; }
        public int           ReasonCode { get; }

        // 로그인은 됐지만 자동 로그인용 갱신 토큰을 저장하지 못했다
        public bool          IsStoreFailed { get; }

        public bool IsSuccess => Error == ESessionError.None;

        /****************************************
        *                Methods
        ****************************************/

        internal SessionResult(ESessionError error, EAuthApiError apiError, int reasonCode, bool isStoreFailed)
        {
            Error         = error;
            ApiError      = apiError;
            ReasonCode    = reasonCode;
            IsStoreFailed = isStoreFailed;
        }

        internal static SessionResult Success(bool isStoreFailed = false)
        {
            return new SessionResult(ESessionError.None, EAuthApiError.None, 0, isStoreFailed);
        }

        internal static SessionResult Fail(ESessionError error) { return new SessionResult(error, EAuthApiError.None, 0, false); }

        internal static SessionResult Fail(ESessionError error, AuthApiResult api)
        {
            return new SessionResult(error, api.Error, api.ReasonCode, false);
        }
    }

    /// <summary> 값이 있는 LauncherSession 작업 결과. 게임 세션 토큰을 담으므로 로그에 직렬화하지 않는다 </summary>
    public sealed class SessionResult<T> : SessionResult where T : class
    {
        /****************************************
        *              Properties
        ****************************************/

        public T? Value { get; }

        /****************************************
        *                Methods
        ****************************************/

        internal SessionResult(ESessionError error, EAuthApiError apiError, int reasonCode, T? value)
            : base(error, apiError, reasonCode, false)
        {
            Value = value;
        }

        internal static SessionResult<T> Success(T value) { return new SessionResult<T>(ESessionError.None, EAuthApiError.None, 0, value); }

        internal static SessionResult<T> From(SessionResult failure)
        {
            return new SessionResult<T>(failure.Error, failure.ApiError, failure.ReasonCode, null);
        }
    }
}
