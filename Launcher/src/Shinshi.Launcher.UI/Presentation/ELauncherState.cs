namespace Shinshi.Launcher.Presentation
{
    /// <summary> 런처 화면의 현재 상태, 큰 버튼의 글자와 동작이 이것을 따른다 </summary>
    public enum ELauncherState
    {
        Checking,
        ConfigError,
        CheckFailed,
        LauncherOutdated,
        NotInstalled,
        UpdateAvailable,
        Ready,
        Updating,
        Repairing,

        // 서비스 상태 확인, 게임 세션 발급, 게임 실행과 세션 전달까지
        Launching,

        GameRunning
    }
}
