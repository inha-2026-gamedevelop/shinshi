// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> 운영자 목록과 비활성화 감사에 쓰는 공개 가능한 열만 담는다 </summary>
    public sealed class AdminOperatorRecord
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid AdminId { get; }
        public string LoginName { get; }
        public EAdminRole Role { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset? DisabledAt { get; }
        public DateTimeOffset? LockedUntil { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminOperatorRecord(Guid adminId, string loginName, EAdminRole role, DateTimeOffset createdAt,
            DateTimeOffset? disabledAt, DateTimeOffset? lockedUntil)
        {
            AdminId     = adminId;
            LoginName   = loginName;
            Role        = role;
            CreatedAt   = createdAt;
            DisabledAt  = disabledAt;
            LockedUntil = lockedUntil;
        }
    }
}
