// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> 원장 조회 필터. 비어 있는 값은 조건에서 빠진다. 시각 범위는 From 포함, To 제외다 </summary>
    public sealed class AdminLedgerQuery
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid?           AccountId   { get; init; }
        public Guid?           CharacterId { get; init; }
        public string?         AssetKind   { get; init; }
        public string?         AssetId     { get; init; }
        public int?            ReasonCode  { get; init; }
        public string?         SourceType  { get; init; }
        public string?         SourceId    { get; init; }
        public DateTimeOffset? From        { get; init; }
        public DateTimeOffset? To          { get; init; }
        public AdminLogCursor? Cursor      { get; init; }
        public int             Limit       { get; init; }

        // 계정 인덱스(ix_economy_ledger_asset)나 원인 인덱스(ix_economy_ledger_source)를 타는 조건이 있어야 한다.
        // 둘 다 없으면 원장 전체를 훑으므로 API가 400으로 거절한다
        public bool HasIndexedScope => (AccountId.HasValue)
            || ((!string.IsNullOrEmpty(SourceType)) && (!string.IsNullOrEmpty(SourceId)));
    }
}
