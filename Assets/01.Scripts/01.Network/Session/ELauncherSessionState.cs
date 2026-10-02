namespace Shinshi.Network
{
    /// <summary> 런처가 넘기는 게임 세션의 수신 상태 </summary>
    public enum ELauncherSessionState
    {
        NotLaunched,
        Receiving,
        Received,
        Failed
    }
}
