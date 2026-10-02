// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> admin_users에 저장하는 PBKDF2 결과. 원문 비밀번호는 담지 않는다 </summary>
    public sealed class AdminPasswordHash
    {
        /****************************************
        *              Properties
        ****************************************/

        public byte[] Hash { get; }
        public byte[] Salt { get; }
        public int Iterations { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminPasswordHash(byte[] hash, byte[] salt, int iterations)
        {
            ArgumentNullException.ThrowIfNull(hash);
            ArgumentNullException.ThrowIfNull(salt);
            if ((hash.Length != AdminPasswordHasher.HASH_BYTES) || (salt.Length != AdminPasswordHasher.SALT_BYTES))
            {
                throw new ArgumentException("Password hash and salt have unexpected lengths.");
            }
            if (iterations < AdminSecuritySettings.MIN_PASSWORD_ITERATIONS)
            {
                throw new ArgumentOutOfRangeException(nameof(iterations));
            }

            Hash       = hash;
            Salt       = salt;
            Iterations = iterations;
        }
    }
}
