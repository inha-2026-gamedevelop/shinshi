// System
using System;

namespace Shinshi.Data.Ledger
{
    /// <summary> 장비 삭제 이후에도 item ID와 이전 상태를 보존한다. owner는 계정 ID다 </summary>
    public sealed class ItemHistoryEntry
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid ItemId { get; }
        public long Sequence { get; }
        public string EventKind { get; }
        public string? BeforeJson { get; }
        public string? AfterJson { get; }
        public Guid OwnerId { get; }
        public Guid ActorId { get; }
        public Guid OperationId { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ItemHistoryEntry(Guid itemId, long sequence, string eventKind, string? beforeJson, string? afterJson,
            Guid ownerId, Guid actorId, Guid operationId)
        {
            if ((itemId == Guid.Empty) || (ownerId == Guid.Empty)
                || (actorId == Guid.Empty) || (operationId == Guid.Empty))
            {
                throw new ArgumentException("Item, owner, actor and operation IDs are required.");
            }
            if (sequence < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence));
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(eventKind);
            if ((beforeJson == null) && (afterJson == null))
            {
                throw new ArgumentException("An item transition requires at least one state.");
            }
            if (beforeJson != null)
            {
                LedgerJson.RequireObject(beforeJson, nameof(beforeJson));
            }
            if (afterJson != null)
            {
                LedgerJson.RequireObject(afterJson, nameof(afterJson));
            }

            ItemId      = itemId;
            Sequence    = sequence;
            EventKind   = eventKind;
            BeforeJson  = beforeJson;
            AfterJson   = afterJson;
            OwnerId     = ownerId;
            ActorId     = actorId;
            OperationId = operationId;
        }
    }
}
