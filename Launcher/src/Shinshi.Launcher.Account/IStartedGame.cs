namespace Shinshi.Launcher.Account
{
    /// <summary> 세션 전달용으로 런처가 시작한 게임 프로세스 하나 </summary>
    public interface IStartedGame
    {
        int ProcessId { get; }

        /// <summary> 전달 실패 때 부른다. 이미 끝난 프로세스면 아무것도 하지 않고 예외를 던지지 않는다 </summary>
        void Kill();
    }
}
