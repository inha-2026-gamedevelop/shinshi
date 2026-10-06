namespace Shinshi.Network
{
    /// <summary> 출정 정산의 클라이언트 표시 단계 </summary>
    public enum ESortieSettlementState : byte
    {
        // 출정이 아직 끝나지 않았다
        None     = 0,

        // 종료 저장 또는 정산 확정을 기다린다
        Pending  = 1,

        // API가 정산을 확정했다
        Settled  = 2,

        // 정산이 확정 거절돼 API 복구 작업이 맡는다. 보상은 복구 뒤 우편, 지갑으로 확인한다
        Deferred = 3
    }
}
