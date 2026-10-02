// System
using System;
using System.Collections.Generic;

namespace Shinshi.Data.Inbox
{
    /// <summary> 수령할 수 있는 우편 한 행 </summary>
    public sealed class InboxRecord
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid                      InboxId    { get; }
        public string                    SourceKind { get; }
        public DateTimeOffset            CreatedAt  { get; }
        public DateTimeOffset            ExpiresAt  { get; }
        public IReadOnlyList<RewardLine> Rewards    { get; }

        /****************************************
        *                Methods
        ****************************************/

        public InboxRecord(Guid inboxId, string sourceKind, DateTimeOffset createdAt, DateTimeOffset expiresAt,
            IReadOnlyList<RewardLine> rewards)
        {
            ArgumentNullException.ThrowIfNull(sourceKind);
            ArgumentNullException.ThrowIfNull(rewards);
            InboxId    = inboxId;
            SourceKind = sourceKind;
            CreatedAt  = createdAt;
            ExpiresAt  = expiresAt;
            Rewards    = rewards;
        }
    }
}
