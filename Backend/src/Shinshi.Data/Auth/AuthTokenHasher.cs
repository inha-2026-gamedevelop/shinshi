// System
using System;
using System.Security.Cryptography;
using System.Text;

namespace Shinshi.Data.Auth
{
    /// <summary> 고엔트로피 원문은 반환만 하고 용도가 분리된 HMAC 해시만 저장한다 </summary>
    public sealed class AuthTokenHasher
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string INVITE_DOMAIN       = "shinshi.auth.invite.v1";
        public const string ACCESS_DOMAIN       = "shinshi.auth.access.v1";
        public const string REFRESH_DOMAIN      = "shinshi.auth.refresh.v1";
        public const string GAME_SESSION_DOMAIN = "shinshi.auth.game-session.v1";
        public const string SERVER_LEASE_DOMAIN = "shinshi.auth.server-lease.v1";
        public const string JOIN_DOMAIN         = "shinshi.auth.join.v1";
        public const string ROTATION_REQUEST_DOMAIN = "shinshi.auth.rotation-request.v1";

        // 원문 뒤 8바이트는 무작위 부분의 HMAC 태그다. 길이와 문자 집합이 이전 형식과 같아 클라이언트 계약은 그대로이고,
        // 태그가 맞지 않는 무작위 bearer는 DB 조회 전에 거절해 토큰 폭주가 DB 부하가 되지 않게 한다
        private const string TAG_DOMAIN       = "shinshi.auth.token-tag.v1";
        private const int    TOKEN_BYTES      = 32;
        private const int    TAG_BYTES        = 8;
        private const int    RANDOM_BYTES     = TOKEN_BYTES - TAG_BYTES;
        private const int    TOKEN_CHARACTERS = 43;
        private readonly byte[] _key;

        /****************************************
        *                Methods
        ****************************************/

        public AuthTokenHasher(AuthTokenOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _key = options.GetValidatedHashKey();
        }

        public string CreateToken()
        {
            byte[] bytes = new byte[TOKEN_BYTES];
            RandomNumberGenerator.Fill(bytes.AsSpan(0, RANDOM_BYTES));
            WriteTag(bytes.AsSpan(0, RANDOM_BYTES), bytes.AsSpan(RANDOM_BYTES, TAG_BYTES));
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public string CreateRotationToken(string domain, string refreshToken, string requestId)
        {
            if ((domain != REFRESH_DOMAIN) && (domain != ACCESS_DOMAIN))
            {
                throw new ArgumentException("Unsupported rotation token purpose.", nameof(domain));
            }

            byte[] bytes = Hash("shinshi.auth.rotation.v1." + domain, refreshToken + "\0" + requestId);
            try
            {
                WriteTag(bytes.AsSpan(0, RANDOM_BYTES), bytes.AsSpan(RANDOM_BYTES, TAG_BYTES));
                return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        /// <summary> 형식과 이 키로 만든 태그를 DB 없이 확인한다. 실패한 토큰은 조회하지 않고 거절한다 </summary>
        public bool IsAuthentic(string? token)
        {
            if (!IsWellFormed(token))
            {
                return false;
            }

            byte[] bytes = Convert.FromBase64String(token!.Replace('-', '+').Replace('_', '/') + "=");
            Span<byte> expected = stackalloc byte[TAG_BYTES];
            WriteTag(bytes.AsSpan(0, RANDOM_BYTES), expected);
            return CryptographicOperations.FixedTimeEquals(expected, bytes.AsSpan(RANDOM_BYTES, TAG_BYTES));
        }

        public static bool IsWellFormed(string? token)
        {
            if ((token == null) || (token.Length != TOKEN_CHARACTERS))
            {
                return false;
            }

            foreach (char character in token)
            {
                if (!(((character >= 'A') && (character <= 'Z'))
                    || ((character >= 'a') && (character <= 'z'))
                    || ((character >= '0') && (character <= '9'))
                    || (character == '-') || (character == '_')))
                {
                    return false;
                }
            }

            // 마지막 base64url 문자의 사용되지 않는 비트까지 확인해 한 원문의 다른 표기를 막는다.
            try
            {
                byte[] bytes = Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=");
                string canonical = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                return (bytes.Length == TOKEN_BYTES) && string.Equals(token, canonical, StringComparison.Ordinal);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public byte[] Hash(string domain, string token)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(domain);
            ArgumentNullException.ThrowIfNull(token);
            if (domain.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("Hash domain cannot contain a delimiter.", nameof(domain));
            }

            // 비밀번호용 느린 해시가 아니라 256비트 무작위 bearer의 검색용 해시다.
            // DB만 유출되어도 HMAC 키 없이는 원문 후보를 확인할 수 없으며 용도 간 토큰 재사용도 막는다.
            return HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(domain + "\0" + token));
        }

        private void WriteTag(ReadOnlySpan<byte> random, Span<byte> tag)
        {
            byte[] domain = Encoding.UTF8.GetBytes(TAG_DOMAIN + "\0");
            byte[] input = new byte[domain.Length + random.Length];
            domain.CopyTo(input, 0);
            random.CopyTo(input.AsSpan(domain.Length));
            Span<byte> mac = stackalloc byte[TOKEN_BYTES];
            HMACSHA256.HashData(_key, input, mac);
            mac.Slice(0, TAG_BYTES).CopyTo(tag);
        }
    }
}
