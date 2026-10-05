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

        // 수령 요청에 캐릭터 ID가 필요한지. 회수함 행은 획득한 캐릭터에 묶여 있어 필요 없다
        public bool                      RequiresCharacter { get; }

        /****************************************
        *                Methods
        ****************************************/

        public InboxRecord(Guid inboxId, string sourceKind, DateTimeOffset createdAt, DateTimeOffset expiresAt,
            IReadOnlyList<RewardLine> rewards)
        {
            ArgumentNullException.ThrowIfNull(sourceKind);
            ArgumentNullException.ThrowIfNull(rewards);
            InboxId           = inboxId;
            SourceKind        = sourceKind;
            CreatedAt         = createdAt;
            ExpiresAt         = expiresAt;
            Rewards           = rewards;
            RequiresCharacter = !RecoveryInbox.IsRecoverySource(sourceKind) && RewardLines.RequiresCharacter(rewards);
        }
    }
}
