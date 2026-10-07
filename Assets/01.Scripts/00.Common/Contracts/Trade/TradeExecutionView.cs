// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 거래 실행 행의 현재 상태 </summary>
    [DataContract]
    public sealed class TradeExecutionView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "executionId")]      private string _executionId;
        [DataMember(Name = "tradeId")]          private string _tradeId;
        // TradeCodes.ExecutionState
        [DataMember(Name = "state")]            private string _state;
        // ERejectReason 값, Rejected나 Abandoned일 때 의미가 있다
        [DataMember(Name = "reasonCode")]       private int    _reasonCode;
        [DataMember(Name = "preparedAtUnixMs")] private long   _preparedAtUnixMs;
        // 종결 전이면 0
        [DataMember(Name = "finishedAtUnixMs")] private long   _finishedAtUnixMs;
        [DataMember(Name = "serverTimeUnixMs")] private long   _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string ExecutionId      => _executionId;
        public string TradeId          => _tradeId;
        public string State            => _state;
        public int    ReasonCode       => _reasonCode;
        public long   PreparedAtUnixMs => _preparedAtUnixMs;
        public long   FinishedAtUnixMs => _finishedAtUnixMs;
        public long   ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public TradeExecutionView(string executionId, string tradeId, string state, int reasonCode, long preparedAtUnixMs, long finishedAtUnixMs, long serverTimeUnixMs)
        {
            _executionId      = executionId;
            _tradeId          = tradeId;
            _state            = state;
            _reasonCode       = reasonCode;
            _preparedAtUnixMs = preparedAtUnixMs;
            _finishedAtUnixMs = finishedAtUnixMs;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private TradeExecutionView() { }
    }
}
