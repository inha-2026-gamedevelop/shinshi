// System
using System;

namespace Shinshi.Data.Auth
{
    /// <summary> 서비스 자격 검증 후 사용하는 인스턴스와 역할에 묶인 서버 임대 </summary>
    public sealed class ServerAuthIdentity
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid InstanceId { get; }
        public Guid LeaseId { get; }
        public Guid ServiceId { get; }
        public int Role { get; }
        public long ExpiresAtUnixMs { get; }

        // 임대를 발급받은 프로세스. 0005 이전 임대는 값이 없다
        public Guid? BootId { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ServerAuthIdentity(Guid instanceId, Guid leaseId, Guid serviceId, int role, long expiresAtUnixMs,
            Guid? bootId = null)
        {
            InstanceId      = instanceId;
            LeaseId         = leaseId;
            ServiceId       = serviceId;
            Role            = role;
            ExpiresAtUnixMs = expiresAtUnixMs;
            BootId          = bootId;
        }
    }
}
