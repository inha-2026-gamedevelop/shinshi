// System
using System;

namespace Shinshi.Data.Ledger
{
    /// <summary> PostgreSQL SUM(bigint)의 무제한 numeric 합계를 문자열로 보존해 오버플로를 피한다 </summary>
    public sealed class EconomyReconciliationMismatch
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid AccountId { get; }
        public Guid? CharacterId { get; }
        public string AssetKind { get; }
        public string AssetId { get; }
        public string BindingKind { get; }
        public long? CurrentBalance { get; }
        public string LedgerTotal { get; }
        public EconomyReconciliationCursor Cursor { get; }

        /****************************************
        *                Methods
        ****************************************/

        internal EconomyReconciliationMismatch(Guid accountId, Guid? characterId, string assetKind, string assetId,
            string bindingKind, long? currentBalance, string ledgerTotal)
        {
            AccountId      = accountId;
            CharacterId    = characterId;
            AssetKind      = assetKind;
            AssetId        = assetId;
            BindingKind    = bindingKind;
            CurrentBalance = currentBalance;
            LedgerTotal    = ledgerTotal;
            Cursor         = new EconomyReconciliationCursor(accountId, characterId, assetKind, assetId, bindingKind);
        }
    }
}
