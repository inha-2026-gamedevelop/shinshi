// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 거래 실행 준비. operationId는 executionId와 같다. 경제 효과가 없고 동결된 제안만 남긴다 </summary>
    [DataContract]
    public sealed class TradePrepareRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // executionId와 같은 Guid "D" 형식
        [DataMember(Name = "operationId")]       private string         _operationId;
        [DataMember(Name = "executionId")]       private string         _executionId;
        [DataMember(Name = "tradeId")]           private string         _tradeId;
        [DataMember(Name = "townInstanceId")]    private string         _townInstanceId;
        [DataMember(Name = "sideA")]             private TradeSideOffer _sideA;
        [DataMember(Name = "sideB")]             private TradeSideOffer _sideB;
        // commit 작업의 operationId(Guid "D"). prepare가 실행 행에 저장하고 commit 요청이 같은 값을 보내야 한다.
        // 결과 불명일 때 resolve가 이 작업의 저장 결과로 실행 행을 종결한다
        [DataMember(Name = "commitOperationId")] private string         _commitOperationId;

        /****************************************
        *              Properties
        ****************************************/

        public string         OperationId       => _operationId;
        public string         ExecutionId       => _executionId;
        public string         TradeId           => _tradeId;
        public string         TownInstanceId    => _townInstanceId;
        public TradeSideOffer SideA             => _sideA;
        public TradeSideOffer SideB             => _sideB;
        public string         CommitOperationId => _commitOperationId;

        /****************************************
        *                Methods
        ****************************************/

        public TradePrepareRequest(string operationId, string executionId, string tradeId, string townInstanceId, TradeSideOffer sideA, TradeSideOffer sideB,
            string commitOperationId)
        {
            _operationId       = operationId;
            _executionId       = executionId;
            _tradeId           = tradeId;
            _townInstanceId    = townInstanceId;
            _sideA             = sideA;
            _sideB             = sideB;
            _commitOperationId = commitOperationId;
        }

        private TradePrepareRequest() { }
    }
}
