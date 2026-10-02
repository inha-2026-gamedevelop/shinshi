namespace Shinshi.Network
{
    /// <summary> 서버 프로세스 종료 경로의 단계 </summary>
    public enum EServerShutdownStage
    {
        // 정상 운영 중이다
        Running   = 0,

        // 드레인을 보냈고 참가자가 스스로 나가기를 기다린다
        Returning = 1,

        // 남은 연결을 끊었고 끊김 통지와 드레인 응답을 기다린다
        Flushing  = 2,

        // 종료를 요청했다
        Exited    = 3
    }
}
