// System
using System;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Data.Admin
{
    /// <summary> economy_ledger 조회 행. SQL은 FROM economy_ledger AS ledger 별칭을 쓴다 </summary>
    internal sealed class AdminLedgerRow
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SELECT_COLUMNS =
            """
            ledger.ledger_id AS "LedgerId", ledger.occurred_at AS "OccurredAt", ledger.account_id AS "AccountId",
                ledger.character_id AS "CharacterId", ledger.asset_kind AS "AssetKind", ledger.asset_id AS "AssetId",
                ledger.binding_kind AS "BindingKind", ledger.delta AS "Delta", ledger.balance_after AS "BalanceAfter",
                ledger.reason AS "Reason", ledger.source_type AS "SourceType", ledger.source_id AS "SourceId",
                ledger.operation_id AS "OperationId", ledger.actor_kind AS "ActorKind", ledger.actor_id AS "ActorId"
            """;

        /****************************************
        *              Properties
        ****************************************/

        public Guid     LedgerId     { get; set; }
        public DateTime OccurredAt   { get; set; }
        public Guid     AccountId    { get; set; }
        public Guid?    CharacterId  { get; set; }
        public string   AssetKind    { get; set; } = string.Empty;
        public string   AssetId      { get; set; } = string.Empty;
        public string   BindingKind  { get; set; } = string.Empty;
        public long     Delta        { get; set; }
        public long     BalanceAfter { get; set; }
        public int      Reason       { get; set; }
        public string   SourceType   { get; set; } = string.Empty;
        public string   SourceId     { get; set; } = string.Empty;
        public Guid     OperationId  { get; set; }
        public string   ActorKind    { get; set; } = string.Empty;
        public Guid     ActorId      { get; set; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminLedgerEntry ToContract()
        {
            return new AdminLedgerEntry(LedgerId.ToString("D"), AdminLogValues.ToUnixMs(OccurredAt),
                AccountId.ToString("D"), AdminLogValues.ToText(CharacterId), AssetKind, AssetId, BindingKind, Delta,
                BalanceAfter, Reason, SourceType, SourceId, OperationId.ToString("D"), ActorKind, ActorId.ToString("D"));
        }
    }
}
