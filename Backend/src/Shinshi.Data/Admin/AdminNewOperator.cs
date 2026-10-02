// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> admin_users에 넣을 새 운영자. 비밀번호는 해시, TOTP 비밀은 암호문으로만 담는다 </summary>
    public sealed class AdminNewOperator
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid AdminId { get; }
        public string LoginName { get; }
        public EAdminRole Role { get; }
        public AdminPasswordHash Password { get; }
        public byte[] TotpSecretEncrypted { get; }

        // 부트스트랩 Owner는 만든 운영자가 없어 null이다
        public Guid? CreatedBy { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminNewOperator(Guid adminId, string loginName, EAdminRole role, AdminPasswordHash password,
            byte[] totpSecretEncrypted, Guid? createdBy)
        {
            ArgumentNullException.ThrowIfNull(password);
            ArgumentNullException.ThrowIfNull(totpSecretEncrypted);
            if ((adminId == Guid.Empty) || (createdBy == Guid.Empty))
            {
                throw new ArgumentException("Admin IDs cannot be empty.");
            }
            if ((!AdminLoginNames.TryNormalize(loginName, out string normalized))
                || (!string.Equals(normalized, loginName, StringComparison.Ordinal)))
            {
                throw new ArgumentException("Login name must be normalized.", nameof(loginName));
            }
            if (!Enum.IsDefined(role))
            {
                throw new ArgumentOutOfRangeException(nameof(role));
            }

            AdminId             = adminId;
            LoginName           = loginName;
            Role                = role;
            Password            = password;
            TotpSecretEncrypted = totpSecretEncrypted;
            CreatedBy           = createdBy;
        }
    }
}
