namespace Shinshi.Network
{
    /// <summary> 파티 방 진행 단계. Open에서만 들어오고 준비를 바꾼다 </summary>
    public enum EPartyPhase
    {
        // 모집과 준비
        Open      = 0,

        // 방장이 시작을 눌러 카운트다운 중
        Countdown = 1,

        // 예약 API 응답을 기다린다
        Reserving = 2,

        // 던전 풀이 비어 마을 대기열에서 다시 예약을 기다린다
        Queued    = 3,

        // 예약이 확정돼 참가자별 입장권을 받는 중
        Departing = 4
    }
}
