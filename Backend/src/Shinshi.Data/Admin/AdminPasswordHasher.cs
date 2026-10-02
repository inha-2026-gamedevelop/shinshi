// System
using System;
using System.Security.Cryptography;
using System.Text;

namespace Shinshi.Data.Admin
{
    /// <summary> 운영자 비밀번호의 PBKDF2-HMAC-SHA256 해시와 고정 시간 비교. 비밀번호는 사람이 고르므로 토큰용 HMAC 대신 느린 해시를 쓴다 </summary>
    public sealed class AdminPasswordHasher
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int SALT_BYTES          = 16;
        public const int HASH_BYTES          = 32;
        public const int MIN_PASSWORD_LENGTH = 12;

        // 아주 긴 입력으로 요청마다 큰 문자열을 처리하지 않게 막는다
        public const int MAX_PASSWORD_LENGTH = 256;

        // base64url 24자(144비트). 사람이 옮겨 적을 수 있으면서 사전 대입이 무의미한 길이다
        private const int GENERATED_PASSWORD_BYTES = 18;

        // 없는 계정의 로그인도 같은 시간이 걸리게 하는 비교 대상. 어떤 비밀번호와도 맞지 않는다
        private static readonly byte[] G_DummySalt = new byte[SALT_BYTES];
        private static readonly byte[] G_DummyHash = new byte[HASH_BYTES];

        /****************************************
        *              Properties
        ****************************************/

        public int Iterations { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminPasswordHasher(int iterations)
        {
            if ((iterations < AdminSecuritySettings.MIN_PASSWORD_ITERATIONS)
                || (iterations > AdminSecuritySettings.MAX_PASSWORD_ITERATIONS))
            {
                throw new ArgumentOutOfRangeException(nameof(iterations));
            }

            Iterations = iterations;
        }

        /// <summary> 길이 정책을 통과한 비밀번호만 새 솔트로 해시한다 </summary>
        public AdminPasswordHash Hash(string password)
        {
            if (!MeetsPolicy(password))
            {
                throw new ArgumentException("Password does not meet the length policy.", nameof(password));
            }

            byte[] salt = RandomNumberGenerator.GetBytes(SALT_BYTES);
            return new AdminPasswordHash(Derive(password, salt, Iterations), salt, Iterations);
        }

        /// <summary> 저장된 솔트와 회수로 다시 계산해 고정 시간으로 비교한다 </summary>
        public static bool Verify(string? password, byte[] salt, byte[] expectedHash, int iterations)
        {
            ArgumentNullException.ThrowIfNull(salt);
            ArgumentNullException.ThrowIfNull(expectedHash);
            if ((password == null) || (password.Length > MAX_PASSWORD_LENGTH)
                || (iterations < AdminSecuritySettings.MIN_PASSWORD_ITERATIONS))
            {
                return false;
            }

            byte[] actual = Derive(password, salt, iterations);
            return CryptographicOperations.FixedTimeEquals(actual, expectedHash);
        }

        /// <summary> 없는 계정에도 같은 비용을 들여 응답 시간으로 계정 존재를 알 수 없게 한다 </summary>
        public void VerifyDummy(string? password)
        {
            Verify(password ?? string.Empty, G_DummySalt, G_DummyHash, Iterations);
        }

        public static byte[] Derive(string password, byte[] salt, int iterations)
        {
            ArgumentNullException.ThrowIfNull(password);
            ArgumentNullException.ThrowIfNull(salt);
            return Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations,
                HashAlgorithmName.SHA256, HASH_BYTES);
        }

        public static bool MeetsPolicy(string? password)
        {
            return (password != null) && (password.Length >= MIN_PASSWORD_LENGTH) && (password.Length <= MAX_PASSWORD_LENGTH);
        }

        /// <summary> 최초 비밀번호. 응답이나 표준 출력으로 한 번만 보여 주고 저장하지 않는다 </summary>
        public static string CreateRandomPassword()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(GENERATED_PASSWORD_BYTES);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_');
        }
    }
}
