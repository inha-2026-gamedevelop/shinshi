// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> auth_invites 한 행의 공개 가능한 값. 코드 해시는 담지 않는다 </summary>
    public sealed class AdminInviteRecord
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid InviteId { get; }
        public Guid AccountId { get; }
        public DateTimeOffset ExpiresAt { get; }
        public long MaxUses { get; }
        public long UsedCount { get; }
        public DateTimeOffset? UsedAt { get; }
        public DateTimeOffset? RevokedAt { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminInviteRecord(Guid inviteId, Guid accountId, DateTimeOffset expiresAt, long maxUses, long usedCount,
            DateTimeOffset? usedAt, DateTimeOffset? revokedAt)
        {
            InviteId  = inviteId;
            AccountId = accountId;
            ExpiresAt = expiresAt;
            MaxUses   = maxUses;
            UsedCount = usedCount;
            UsedAt    = usedAt;
            RevokedAt = revokedAt;
        }
    }
}
