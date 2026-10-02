// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> economy_ledger 한 줄. reason은 EEconomyReason 값, ID는 Guid 문자열이고 계정 공유 자산이면 characterId가 null이다 </summary>
    [DataContract]
    public sealed class AdminLedgerEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "ledgerId")]         private string _ledgerId;
        [DataMember(Name = "occurredAtUnixMs")] private long   _occurredAtUnixMs;
        [DataMember(Name = "accountId")]        private string _accountId;
        [DataMember(Name = "characterId")]      private string _characterId;
        [DataMember(Name = "assetKind")]        private string _assetKind;
        [DataMember(Name = "assetId")]          private string _assetId;
        [DataMember(Name = "bindingKind")]      private string _bindingKind;
        [DataMember(Name = "delta")]            private long   _delta;
        [DataMember(Name = "balanceAfter")]     private long   _balanceAfter;
        [DataMember(Name = "reason")]           private int    _reason;
        [DataMember(Name = "sourceType")]       private string _sourceType;
        [DataMember(Name = "sourceId")]         private string _sourceId;
        [DataMember(Name = "operationId")]      private string _operationId;
        [DataMember(Name = "actorKind")]        private string _actorKind;
        [DataMember(Name = "actorId")]          private string _actorId;

        /****************************************
        *              Properties
        ****************************************/

        public string LedgerId         => _ledgerId;
        public long   OccurredAtUnixMs => _occurredAtUnixMs;
        public string AccountId        => _accountId;
        public string CharacterId      => _characterId;
        public string AssetKind        => _assetKind;
        public string AssetId          => _assetId;
        public string BindingKind      => _bindingKind;
        public long   Delta            => _delta;
        public long   BalanceAfter     => _balanceAfter;
        public int    Reason           => _reason;
        public string SourceType       => _sourceType;
        public string SourceId         => _sourceId;
        public string OperationId      => _operationId;
        public string ActorKind        => _actorKind;
        public string ActorId          => _actorId;

        /****************************************
        *                Methods
        ****************************************/

        public AdminLedgerEntry(string ledgerId, long occurredAtUnixMs, string accountId, string characterId,
            string assetKind, string assetId, string bindingKind, long delta, long balanceAfter, int reason,
            string sourceType, string sourceId, string operationId, string actorKind, string actorId)
        {
            _ledgerId         = ledgerId;
            _occurredAtUnixMs = occurredAtUnixMs;
            _accountId        = accountId;
            _characterId      = characterId;
            _assetKind        = assetKind;
            _assetId          = assetId;
            _bindingKind      = bindingKind;
            _delta            = delta;
            _balanceAfter     = balanceAfter;
            _reason           = reason;
            _sourceType       = sourceType;
            _sourceId         = sourceId;
            _operationId      = operationId;
            _actorKind        = actorKind;
            _actorId          = actorId;
        }

        private AdminLedgerEntry() { }
    }
}
