namespace Shinshi.Launcher.Account
{
    /// <summary> LauncherSession 작업의 실패 종류 </summary>
    public enum ESessionError
    {
        None,
        NotLoggedIn,

        // 이미 로그인 중이거나 로그인돼 있다
        Busy,

        // 아이디 형식이 틀리거나 비밀번호가 비었다. 서버에 보내지 않았다
        InvalidInput,

        NoStoredLogin,

        // API 호출이 실패했다. 저장된 로그인은 그대로이고 ApiError, ReasonCode에 이유가 있다.
        // 갱신 응답을 잃었으면(IsOutcomeUnknown) 같은 토큰을 바로 다시 보내지 않게 메모리 로그인만 버려 LoggedOut이 된다
        ApiFailed,

        // 게임이 도는 중이라 갱신하지 않았다. 갱신하면 서버가 그 게임의 세션을 폐기한다
        RefreshSuspended,

        // 서버가 갱신 토큰을 만료, 폐기로 거절했다(401). 저장된 로그인을 지웠으니 다시 로그인해야 한다
        SessionLost,
        StoreFailed
    }
}
