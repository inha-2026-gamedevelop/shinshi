// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 계정 하나의 자산 한 종류를 직접 늘리거나 줄인다(Operator). 대상 계정은 경로의 accountId다 </summary>
    [DataContract]
    public sealed class AdminAdjustmentRequest : IAdminWriteRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")] private string _operationId;
        [DataMember(Name = "reason")]      private string _reason;

        // AdminGrantCodes.ASSET_*. AccountItem만 assetId, bindingKind를 채운다
        [DataMember(Name = "assetKind")]   private string _assetKind;
        [DataMember(Name = "assetId")]     private string _assetId;
        [DataMember(Name = "bindingKind")] private string _bindingKind;

        // Essence, Shards에만 필요하고 그 계정의 캐릭터여야 한다
        [DataMember(Name = "characterId")] private string _characterId;

        // 0이 아니다. 음수는 예약분을 뺀 가용량까지만 차감하고 모자란 만큼은 shortfall로 남긴다
        [DataMember(Name = "delta")]       private long   _delta;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId => _operationId;
        public string Reason      => _reason;
        public string AssetKind   => _assetKind;
        public string AssetId     => _assetId;
        public string BindingKind => _bindingKind;
        public string CharacterId => _characterId;
        public long   Delta       => _delta;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAdjustmentRequest(string operationId, string reason, string assetKind, string assetId,
            string bindingKind, string characterId, long delta)
        {
            _operationId = operationId;
            _reason      = reason;
            _assetKind   = assetKind;
            _assetId     = assetId;
            _bindingKind = bindingKind;
            _characterId = characterId;
            _delta       = delta;
        }

        private AdminAdjustmentRequest() { }
    }
}
