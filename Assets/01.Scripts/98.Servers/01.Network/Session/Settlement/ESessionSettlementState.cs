namespace Shinshi.Network
{
    /// <summary> 출정 정산 호출의 진행 상태 </summary>
    public enum ESessionSettlementState
    {
        // 아직 SessionEnded 저장 확인을 받지 못했다
        Idle      = 0,

        // 같은 operationId로 응답을 기다리는 중이다
        InFlight  = 1,

        // 일시 실패로 다음 시각에 같은 operationId로 다시 부른다
        Retrying  = 2,

        // API가 확정했거나 이미 정산된 출정이다
        Settled   = 3,

        // 확정 거절(400, 저장된 거절), 이후는 API 복구 작업이 같은 operationId로 맡는다
        Abandoned = 4
    }
}
