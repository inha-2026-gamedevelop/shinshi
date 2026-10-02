// System
using System;

namespace Shinshi.Data.Ledger
{
    /// <summary> 불일치 목록의 다음 페이지 키. 같은 대사 주기의 스캔 중 잔액은 수정하지 않는다 </summary>
    public sealed class EconomyReconciliationCursor
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid AccountId { get; }
        public Guid? CharacterId { get; }
        public string AssetKind { get; }
        public string AssetId { get; }
        public string BindingKind { get; }

        /****************************************
        *                Methods
        ****************************************/

        public EconomyReconciliationCursor(Guid accountId, Guid? characterId, string assetKind, string assetId,
            string bindingKind)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(assetKind);
            ArgumentNullException.ThrowIfNull(assetId);
            ArgumentNullException.ThrowIfNull(bindingKind);

            AccountId   = accountId;
            CharacterId = characterId;
            AssetKind   = assetKind;
            AssetId     = assetId;
            BindingKind = bindingKind;
        }
    }
}
