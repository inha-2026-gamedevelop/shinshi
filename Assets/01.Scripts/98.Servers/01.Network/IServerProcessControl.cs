namespace Shinshi.Network
{
    /// <summary> 서버 프로세스 종료 경로가 쓰는 연결과 프로세스 제어, 시험에서는 가짜로 바꾼다 </summary>
    public interface IServerProcessControl
    {
        // 아직 열려 있는 클라이언트 연결 수(인증 전 포함)
        int ConnectionCount { get; }

        // 남은 연결을 모두 끊는다
        void DisconnectAll();

        // 프로세스를 끝낸다. 에디터에서는 끝내지 않고 기록만 남긴다
        void Quit(int exitCode);
    }
}
