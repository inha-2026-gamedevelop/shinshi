// System
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Accounts
{
    /// <summary> PBKDF2-HMAC-SHA256 비밀번호 해시. 저장 형식은 pbkdf2-sha256$회수$솔트$해시(base64)이고 비교는 고정 시간이다 </summary>
    public sealed class AccountPasswordHasher : IAccountPasswordHasher
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ALGORITHM          = "pbkdf2-sha256";
        public const int    DEFAULT_ITERATIONS = 600000;
        public const int    SALT_BYTES         = 16;
        public const int    HASH_BYTES         = 32;

        // 테스트 호스트만 낮춘다. 저장된 해시는 행의 회수로 검증하므로 기본값을 올려도 기존 해시가 그대로 맞는다
        public const int MIN_ITERATIONS = 1000;
        public const int MAX_ITERATIONS = 10000000;

        private const char SEPARATOR   = '$';
        private const int  PART_COUNT  = 4;
        private const int  ALGORITHM_PART  = 0;
        private const int  ITERATIONS_PART = 1;
        private const int  SALT_PART       = 2;
        private const int  HASH_PART       = 3;

        // 없는 아이디의 비교 대상. 어떤 비밀번호와도 맞지 않으며 실제 계산은 설정 회수로 한다
        private static readonly byte[] G_DummySalt = new byte[SALT_BYTES];
        private static readonly byte[] G_DummyHash = new byte[HASH_BYTES];

        /****************************************
        *              Properties
        ****************************************/

        public int Iterations { get; }

        // 테스트가 PBKDF2가 실제로 몇 번, 몇 회수로 계산됐는지 세는 관찰 지점. 운영 등록은 null이다
        private readonly Action<int>? _onDerive;

        /****************************************
        *                Methods
        ****************************************/

        public AccountPasswordHasher(int iterations = DEFAULT_ITERATIONS) : this(iterations, null)
        {
        }

        /// <summary> onDerive는 PBKDF2 계산마다 그 회수로 불린다. 비밀번호나 결과는 넘기지 않는다 </summary>
        public AccountPasswordHasher(int iterations, Action<int>? onDerive)
        {
            if ((iterations < MIN_ITERATIONS) || (iterations > MAX_ITERATIONS))
            {
                throw new ArgumentOutOfRangeException(nameof(iterations));
            }

            Iterations = iterations;
            _onDerive  = onDerive;
        }

        public string Hash(string password)
        {
            if ((password == null) || (password.Length > AccountCredentialRules.PASSWORD_MAX_LENGTH)
                || (password.Length < AccountCredentialRules.PASSWORD_MIN_LENGTH))
            {
                throw new ArgumentException("Password does not meet the length policy.", nameof(password));
            }

            byte[] salt = RandomNumberGenerator.GetBytes(SALT_BYTES);
            byte[] hash = Derive(password, salt, Iterations);
            return ALGORITHM + SEPARATOR + Iterations.ToString(CultureInfo.InvariantCulture) + SEPARATOR
                + Convert.ToBase64String(salt) + SEPARATOR + Convert.ToBase64String(hash);
        }

        public bool Verify(string? password, string encodedHash)
        {
            if (!TryParse(encodedHash, out int iterations, out byte[] salt, out byte[] expected))
            {
                return false;
            }

            return Compare(password, salt, expected, iterations);
        }

        // 더미 비교도 현재 기본 회수로 계산해 실제 계정과 같은 시간이 걸리게 한다
        public bool VerifyDummy(string? password)
        {
            Compare(password, G_DummySalt, G_DummyHash, Iterations);
            return false;
        }

        public bool NeedsRehash(string encodedHash)
        {
            return TryParse(encodedHash, out int iterations, out _, out _) && (iterations < Iterations);
        }

        /// <summary> 저장값이 이 클래스가 만든 형식인지 본다. 회수, 솔트, 해시 길이를 모두 검사한다 </summary>
        public static bool TryParse(string? encodedHash, out int iterations, out byte[] salt, out byte[] hash)
        {
            iterations = 0;
            salt = Array.Empty<byte>();
            hash = Array.Empty<byte>();
            if (string.IsNullOrEmpty(encodedHash))
            {
                return false;
            }

            string[] parts = encodedHash.Split(SEPARATOR);
            if ((parts.Length != PART_COUNT) || !string.Equals(parts[ALGORITHM_PART], ALGORITHM, StringComparison.Ordinal)
                || !int.TryParse(parts[ITERATIONS_PART], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
                || (iterations < MIN_ITERATIONS) || (iterations > MAX_ITERATIONS))
            {
                return false;
            }

            try
            {
                salt = Convert.FromBase64String(parts[SALT_PART]);
                hash = Convert.FromBase64String(parts[HASH_PART]);
            }
            catch (FormatException)
            {
                return false;
            }

            return (salt.Length == SALT_BYTES) && (hash.Length == HASH_BYTES);
        }

        // 길이 상한을 넘는 입력은 계산 없이 거절한다. 없는 아이디도 같은 분기를 타므로 존재가 드러나지 않는다
        private bool Compare(string? password, byte[] salt, byte[] expected, int iterations)
        {
            if ((password == null) || (password.Length > AccountCredentialRules.PASSWORD_MAX_LENGTH))
            {
                return false;
            }

            byte[] actual = Derive(password, salt, iterations);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        private byte[] Derive(string password, byte[] salt, int iterations)
        {
            _onDerive?.Invoke(iterations);
            byte[] bytes = Encoding.UTF8.GetBytes(password);
            try
            {
                return Rfc2898DeriveBytes.Pbkdf2(bytes, salt, iterations, HashAlgorithmName.SHA256, HASH_BYTES);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }
}
