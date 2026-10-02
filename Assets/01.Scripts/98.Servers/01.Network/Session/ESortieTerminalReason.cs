namespace Shinshi.Network
{
    /// <summary> 던전 프로세스가 맡은 출정이 더 진행될 수 없게 된 이유, None이 아니면 프로세스를 재활용한다 </summary>
    public enum ESortieTerminalReason
    {
        None                = 0,

        // 정산 영수증을 받았다
        Settled             = 1,

        // 정산이 확정 거절됐다. 종료 이벤트가 저장돼 있어 API 복구 작업이 끝낸다
        SettlementAbandoned = 2,

        // 서비스 장애 취소로 끝났다(저장 확정 거절, 저장 보존 한계, API 예약 취소). API 장애 취소가 환급한다
        ServiceCancelled    = 3,

        // start가 영구 거절됐다
        StartRejected       = 4,

        // 시작 전에 API가 예약을 해제했다(만료). 티켓은 예약 만료가 돌려준다
        ReservationReleased = 5,

        // start를 확정받았는데 세션을 열지 못했다. 코드 결함이라 오류 종료 코드로 내린다
        InvariantViolation  = 6
    }
}
