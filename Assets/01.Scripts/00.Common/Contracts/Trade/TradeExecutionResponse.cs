// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> prepare, commit, resolve의 결과. Prepared는 성공이 아니다 </summary>
    [DataContract]
    public sealed class TradeExecutionResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "result")]    private OperationResult    _result;
        // 실행 행이 없으면 null
        [DataMember(Name = "execution")] private TradeExecutionView _execution;

        /****************************************
        *              Properties
        ****************************************/

        public OperationResult    Result    => _result;
        public TradeExecutionView Execution => _execution;

        /****************************************
        *                Methods
        ****************************************/

        public TradeExecutionResponse(OperationResult result, TradeExecutionView execution)
        {
            _result    = result;
            _execution = execution;
        }

        private TradeExecutionResponse() { }
    }
}
