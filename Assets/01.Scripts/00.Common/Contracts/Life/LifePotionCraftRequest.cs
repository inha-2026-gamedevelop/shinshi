// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 물약 제작. 가용 약초(보유 - 예약)가 비용 x count보다 적으면 INSUFFICIENT_ITEM, 완제품이 보관 상한을 넘으면
    /// INVENTORY_FULL로 아무것도 바꾸지 않는다. 모르는 물약 정의나 count 0 이하는 400이다 </summary>
    [DataContract]
    public sealed class LifePotionCraftRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]        private string _operationId;
        [DataMember(Name = "accountId")]          private string _accountId;
        [DataMember(Name = "accountLeaseId")]     private string _accountLeaseId;
        [DataMember(Name = "potionDefinitionId")] private string _potionDefinitionId;

        // 제작 횟수. 완제품 수는 count x 레시피 산출 수다
        [DataMember(Name = "count")]              private int    _count;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId        => _operationId;
        public string AccountId          => _accountId;
        public string AccountLeaseId     => _accountLeaseId;
        public string PotionDefinitionId => _potionDefinitionId;
        public int    Count              => _count;

        /****************************************
        *                Methods
        ****************************************/

        public LifePotionCraftRequest(string operationId, string accountId, string accountLeaseId,
            string potionDefinitionId, int count)
        {
            _operationId        = operationId;
            _accountId          = accountId;
            _accountLeaseId     = accountLeaseId;
            _potionDefinitionId = potionDefinitionId;
            _count              = count;
        }

        private LifePotionCraftRequest() { }
    }
}
