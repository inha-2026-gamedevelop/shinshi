namespace Shinshi.Network
{
    /// <summary> 던전 출정의 진행 단계, 출정 상태 메시지로 소유 클라이언트에 보낸다 </summary>
    public enum ESortiePhase : byte
    {
        // 입장자를 모으고 start 확정을 기다린다
        Waiting = 0,

        // 전투가 진행 중이다
        Running = 1,

        // 종료가 판정됐고 종료 저장, 정산을 기다린다
        Ended   = 2,

        // 보스 성공 정산 뒤 파티 경매가 진행 중이다
        Auction = 3,

        // 출정이 닫혀 프로세스가 내려간다
        Closed  = 4
    }
}
