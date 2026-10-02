// System
using System;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Data.Admin
{
    /// <summary> item_history 조회 행. SQL은 FROM item_history AS history 별칭을 쓴다 </summary>
    internal sealed class AdminItemHistoryRow
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SELECT_COLUMNS =
            """
            history.item_id AS "ItemId", history.seq AS "Seq", history.event_kind AS "EventKind",
                history.before_state::text AS "BeforeState", history.after_state::text AS "AfterState",
                history.owner_id AS "OwnerId", history.actor_id AS "ActorId", history.operation_id AS "OperationId",
                history.occurred_at AS "OccurredAt"
            """;

        /****************************************
        *              Properties
        ****************************************/

        public Guid     ItemId      { get; set; }
        public long     Seq         { get; set; }
        public string   EventKind   { get; set; } = string.Empty;
        public string?  BeforeState { get; set; }
        public string?  AfterState  { get; set; }
        public Guid     OwnerId     { get; set; }
        public Guid     ActorId     { get; set; }
        public Guid     OperationId { get; set; }
        public DateTime OccurredAt  { get; set; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminItemHistoryEntry ToContract()
        {
            return new AdminItemHistoryEntry(ItemId.ToString("D"), Seq, EventKind, AdminLogValues.ToJson(BeforeState),
                AdminLogValues.ToJson(AfterState), OwnerId.ToString("D"), ActorId.ToString("D"),
                OperationId.ToString("D"), AdminLogValues.ToUnixMs(OccurredAt));
        }
    }
}
