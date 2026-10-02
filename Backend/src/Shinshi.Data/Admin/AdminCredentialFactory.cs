// System
using System;
using System.Security.Cryptography;

namespace Shinshi.Data.Admin
{
    /// <summary> 새 운영자의 무작위 비밀번호와 TOTP 비밀을 만들고 저장용 해시와 암호문으로 바꾼다.
    /// 느린 해시는 DB 트랜잭션 밖에서 계산하도록 작업 실행 전에 부른다 </summary>
    public sealed class AdminCredentialFactory
    {
        /****************************************
        *                Fields
        ****************************************/

        // 인증 앱에 보이는 발급자 이름
        public const string TOTP_ISSUER = "Shinshi Admin";

        private readonly AdminPasswordHasher _passwordHasher;
        private readonly AdminSecretProtector _secretProtector;

        /****************************************
        *                Methods
        ****************************************/

        public AdminCredentialFactory(AdminPasswordHasher passwordHasher, AdminSecretProtector secretProtector)
        {
            ArgumentNullException.ThrowIfNull(passwordHasher);
            ArgumentNullException.ThrowIfNull(secretProtector);
            _passwordHasher  = passwordHasher;
            _secretProtector = secretProtector;
        }

        public AdminIssuedCredentials Create(Guid adminId, string normalizedLoginName, EAdminRole role, Guid? createdBy)
        {
            string password = AdminPasswordHasher.CreateRandomPassword();
            AdminPasswordHash passwordHash = _passwordHasher.Hash(password);
            byte[] secret = AdminTotp.CreateSecret();
            try
            {
                byte[] encrypted = _secretProtector.Protect(secret, adminId);
                AdminNewOperator newOperator = new AdminNewOperator(adminId, normalizedLoginName, role, passwordHash,
                    encrypted, createdBy);
                return new AdminIssuedCredentials(newOperator, password, AdminTotp.ToBase32(secret),
                    AdminTotp.BuildUri(TOTP_ISSUER, normalizedLoginName, secret));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }
    }
}
