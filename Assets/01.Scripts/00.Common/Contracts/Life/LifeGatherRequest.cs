// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    // 노드 선점, 거리, 시간 판정은 마을 서버 몫이다. API는 계정 임대, 행동력, 보관 상한만 확인한다
    /// <summary> 채집 완료 한 번의 저장. 행동력 차감과 약초 지급을 함께 확정하고, 부족하면 INSUFFICIENT_ACTION, 보관 상한을 넘으면
    /// INVENTORY_FULL로 아무것도 바꾸지 않는다 </summary>
    [DataContract]
    public sealed class LifeGatherRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]    private string _operationId;
        [DataMember(Name = "accountId")]      private string _accountId;
        [DataMember(Name = "accountLeaseId")] private string _accountLeaseId;

        // 원장 출처(zoneId:nodeId)로만 남긴다. 영문, 숫자, '_', '-', '.'만 쓰는 64자 이하
        [DataMember(Name = "zoneId")]         private string _zoneId;
        [DataMember(Name = "nodeId")]         private string _nodeId;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId    => _operationId;
        public string AccountId      => _accountId;
        public string AccountLeaseId => _accountLeaseId;
        public string ZoneId         => _zoneId;
        public string NodeId         => _nodeId;

        /****************************************
        *                Methods
        ****************************************/

        public LifeGatherRequest(string operationId, string accountId, string accountLeaseId, string zoneId,
            string nodeId)
        {
            _operationId    = operationId;
            _accountId      = accountId;
            _accountLeaseId = accountLeaseId;
            _zoneId         = zoneId;
            _nodeId         = nodeId;
        }

        private LifeGatherRequest() { }
    }
}
