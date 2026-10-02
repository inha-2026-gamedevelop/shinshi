namespace Shinshi.Contracts.Admin
{
    /// <summary> 수동 세션 복구 action 값. 요청 본문의 action에 그대로 쓴다. 세션이 이미 같은 경로로 종결돼 있으면
    /// 그 작업의 저장된 결과로 확정한다(효과 없음, 감사 행 alreadyApplied). 다른 상태는 INVALID_STATE다 </summary>
    public static class AdminSessionRecoveryCodes
    {
        /****************************************
        *                Fields
        ****************************************/

        // 저장된 종료 이벤트가 있는 Settling 세션을 자동 복구와 같은 actor, operationId로 정산한다
        public const string RETRY_SETTLEMENT = "RetrySettlement";

        // 배정 서버 소실이 확인된 Running 세션을 서비스 장애로 취소한다(티켓, 물약 환급과 정기 보상).
        // 서버가 아직 살아 있다고 판정되면 INVALID_STATE, 판정 보류면 503이다
        public const string SERVICE_CANCEL   = "ServiceCancel";

        // 정산이 반복 실패해 자동 복구에서 격리된(quarantined) Settling 세션을 장애 취소와 같은 지급 규칙으로 닫는다.
        // 운영자 작업 한 트랜잭션에서 원장(actor Admin)과 감사를 함께 남긴다. 격리되지 않은 Settling이나 다른 상태는
        // INVALID_STATE, 판정 보류면 503이다. 원장은 GET operations/{운영자 ID}/{operationId}로 본다
        public const string FORCE_CLOSE      = "ForceClose";
    }
}
