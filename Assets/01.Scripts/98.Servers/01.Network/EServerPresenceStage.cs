namespace Shinshi.Network
{
    /// <summary> 서버 프로세스가 백엔드 서버 풀에 자리 잡는 단계 </summary>
    public enum EServerPresenceStage
    {
        // 아직 Start 전이다
        Idle        = 0,

        // 서비스 자격으로 첫 임대를 기다린다
        Leasing     = 1,

        // 임대를 받았고 이번 bootId로 풀 등록을 기다린다
        Registering = 2,

        // 등록을 마치고 heartbeat를 보내는 중이다
        Running     = 3,

        // API가 이 프로세스를 확정 거절했다. 같은 bootId로는 되살릴 수 없어 프로세스를 내린다
        Lost        = 4
    }
}
