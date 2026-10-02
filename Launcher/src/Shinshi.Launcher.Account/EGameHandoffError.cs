namespace Shinshi.Launcher.Account
{
    /// <summary> 게임 세션 전달의 실패 종류 </summary>
    public enum EGameHandoffError
    {
        None,

        // 발급한 세션 하나로 두 번째 전달을 시도했다
        AlreadyUsed,

        PipeCreateFailed,
        StartFailed,

        // 제한 시간 안에 게임이 접속하지 않았거나 수신 확인을 보내지 않았다
        Timeout,

        Cancelled,

        // 파이프에 접속한 프로세스가 런처가 시작한 게임이 아니거나 PID를 확인하지 못했다
        PidMismatch,

        // 수신 확인 프레임이 없거나 형식, 프로토콜 버전, PID가 맞지 않는다
        BadAck,

        PipeBroken
    }
}
