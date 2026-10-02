// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> 검증을 마친 초대 발급. AccountId가 없으면 같은 트랜잭션에서 새 계정을 만든다. 원문 코드는 담지 않고 HMAC만 담는다 </summary>
    public sealed class AdminInviteDraft
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int CODE_HASH_BYTES = 32;

        /****************************************
        *              Properties
        ****************************************/

        public Guid InviteId { get; }
        public Guid? AccountId { get; }
        public byte[] CodeHash { get; }
        public int MaxUses { get; }
        public TimeSpan Lifetime { get; }
        public bool RevokeExistingSessions { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminInviteDraft(Guid inviteId, Guid? accountId, byte[] codeHash, int maxUses, TimeSpan lifetime,
            bool revokeExistingSessions)
        {
            if ((inviteId == Guid.Empty) || (accountId == Guid.Empty))
            {
                throw new ArgumentException("Invite and account IDs cannot be empty.");
            }
            ArgumentNullException.ThrowIfNull(codeHash);
            if (codeHash.Length != CODE_HASH_BYTES)
            {
                throw new ArgumentException("Invite code hash must contain 32 bytes.", nameof(codeHash));
            }
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxUses);
            if (lifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(lifetime));
            }

            InviteId               = inviteId;
            AccountId              = accountId;
            CodeHash               = codeHash;
            MaxUses                = maxUses;
            Lifetime               = lifetime;
            RevokeExistingSessions = revokeExistingSessions;
        }
    }
}
