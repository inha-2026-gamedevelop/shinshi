// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> 어드민 미들웨어가 DB 세션에서 확인한 운영자. 역할은 요청마다 admin_users에서 다시 읽은 값이다 </summary>
    public sealed class AdminIdentity
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid AdminId { get; }
        public EAdminRole Role { get; }
        public Guid SessionId { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminIdentity(Guid adminId, EAdminRole role, Guid sessionId)
        {
            if ((adminId == Guid.Empty) || (sessionId == Guid.Empty))
            {
                throw new ArgumentException("Admin and session IDs are required.");
            }
            if (!Enum.IsDefined(role))
            {
                throw new ArgumentOutOfRangeException(nameof(role));
            }

            AdminId   = adminId;
            Role      = role;
            SessionId = sessionId;
        }

        public bool HasRole(EAdminRole required) { return Role >= required; }
    }
}
