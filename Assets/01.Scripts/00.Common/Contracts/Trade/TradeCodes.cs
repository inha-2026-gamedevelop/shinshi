namespace Shinshi.Contracts
{
    /// <summary> 1:1 거래 한도와 DTO 코드 값 </summary>
    public static class TradeCodes
    {
        /****************************************
        *                Fields
        ****************************************/

        // 한 쪽 품목 칸 수
        public const int TRADE_MAX_ITEMS = 8;

        // prepare 뒤 commit이 이 시간을 넘기면 TradeExpired로 거절하고, resolve나 정리가 Abandoned로 종결한다
        public const int TRADE_PREPARE_TTL_SECONDS = 120;

        // 원장 entryScope. 같은 계정에 같은 사유의 줄이 두 개 생기므로 방향을 구분한다
        public const string SCOPE_A_TO_B = "a2b";
        public const string SCOPE_B_TO_A = "b2a";

        /// <summary> player_trade_executions.state </summary>
        public static class ExecutionState
        {
            /****************************************
            *                Fields
            ****************************************/

            public const string PREPARED  = "Prepared";
            public const string COMMITTED = "Committed";
            public const string REJECTED  = "Rejected";
            public const string ABANDONED = "Abandoned";
        }
    }
}
