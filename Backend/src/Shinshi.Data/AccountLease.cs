// System
using System;

namespace Shinshi.Data
{
    /// <summary> DB 시각으로 만료를 판단하는 계정별 활성 게임 연결 소유권 </summary>
    public sealed class AccountLease
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid AccountId { get; }
        public Guid LeaseId { get; }
        public Guid InstanceId { get; }
        public int Role { get; }
        public DateTimeOffset ExpiresAt { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AccountLease(Guid accountId, Guid leaseId, Guid instanceId, int role, DateTimeOffset expiresAt)
        {
            AccountId  = accountId;
            LeaseId    = leaseId;
            InstanceId = instanceId;
            Role       = role;
            ExpiresAt  = expiresAt.ToUniversalTime();
        }
    }
}

