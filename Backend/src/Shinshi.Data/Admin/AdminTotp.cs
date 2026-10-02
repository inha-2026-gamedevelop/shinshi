// System
using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Shinshi.Data.Admin
{
    /// <summary> RFC 6238 TOTP(HMAC-SHA1, 30초, 6자리)와 인증 앱 등록용 Base32, otpauth URI.
    /// 일반 인증 앱이 기본값만 지원하므로 SHA1을 쓴다. HOTP 구성에서 SHA1의 충돌 약점은 의미가 없다 </summary>
    public static class AdminTotp
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int STEP_SECONDS = 30;
        public const int DIGITS       = 6;

        // 앞뒤 한 스텝까지 받는다. 사람이 코드를 옮겨 적는 시간과 서버 시계 차이를 흡수한다
        public const int ALLOWED_DRIFT_STEPS = 1;

        // RFC 4226이 권장하는 160비트 비밀
        public const int SECRET_BYTES = 20;

        public const int MIN_DIGITS = 6;
        public const int MAX_DIGITS = 8;

        private const int    DECIMAL_BASE          = 10;
        private const int    OFFSET_MASK           = 0x0F;
        private const int    DYNAMIC_TRUNCATE_MASK = 0x7FFFFFFF;
        private const int    BASE32_BITS           = 5;
        private const int    BYTE_BITS             = 8;
        private const int    BASE32_MASK           = 0x1F;
        private const string BASE32_ALPHABET       = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        /****************************************
        *                Methods
        ****************************************/

        public static long StepOf(DateTimeOffset time)
        {
            long seconds = time.ToUnixTimeSeconds();
            if (seconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(time));
            }

            return seconds / STEP_SECONDS;
        }

        /// <summary> 한 시간 스텝의 코드. 앞자리 0을 유지한 고정 길이 문자열이다 </summary>
        public static string ComputeCode(byte[] secret, long step, int digits = DIGITS)
        {
            ArgumentNullException.ThrowIfNull(secret);
            if ((digits < MIN_DIGITS) || (digits > MAX_DIGITS))
            {
                throw new ArgumentOutOfRangeException(nameof(digits));
            }
            if (step < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(step));
            }

            Span<byte> counter = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64BigEndian(counter, step);
            byte[] hash = HMACSHA1.HashData(secret, counter);
            int offset = hash[hash.Length - 1] & OFFSET_MASK;
            int binary = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, sizeof(int))) & DYNAMIC_TRUNCATE_MASK;
            int modulus = 1;
            for (int index = 0; index < digits; ++index)
            {
                modulus *= DECIMAL_BASE;
            }

            return (binary % modulus).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(digits, '0');
        }

        /// <summary> 현재 시각 앞뒤 한 스텝 안에서 맞는 가장 늦은 스텝을 돌려준다. 형식이 틀리거나 맞지 않으면 null이다 </summary>
        public static long? Match(byte[] secret, string? code, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(secret);
            if ((code == null) || (code.Length != DIGITS))
            {
                return null;
            }
            foreach (char character in code)
            {
                if ((character < '0') || (character > '9'))
                {
                    return null;
                }
            }

            byte[] expected = Encoding.ASCII.GetBytes(code);
            long current = StepOf(now);
            for (long step = current + ALLOWED_DRIFT_STEPS; step >= current - ALLOWED_DRIFT_STEPS; --step)
            {
                if (step < 0)
                {
                    continue;
                }

                byte[] candidate = Encoding.ASCII.GetBytes(ComputeCode(secret, step));
                if (CryptographicOperations.FixedTimeEquals(candidate, expected))
                {
                    return step;
                }
            }

            return null;
        }

        public static byte[] CreateSecret()
        {
            return RandomNumberGenerator.GetBytes(SECRET_BYTES);
        }

        /// <summary> RFC 4648 Base32, 인증 앱 입력용으로 패딩을 붙이지 않는다 </summary>
        public static string ToBase32(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            StringBuilder builder = new StringBuilder((data.Length * BYTE_BITS + BASE32_BITS - 1) / BASE32_BITS);
            int buffer = 0;
            int bits = 0;
            foreach (byte value in data)
            {
                buffer = (buffer << BYTE_BITS) | value;
                bits += BYTE_BITS;
                while (bits >= BASE32_BITS)
                {
                    bits -= BASE32_BITS;
                    builder.Append(BASE32_ALPHABET[(buffer >> bits) & BASE32_MASK]);
                }
            }
            if (bits > 0)
            {
                builder.Append(BASE32_ALPHABET[(buffer << (BASE32_BITS - bits)) & BASE32_MASK]);
            }

            return builder.ToString();
        }

        /// <summary> 인증 앱 QR 등록용 otpauth URI. 비밀이 들어 있으므로 로그에 남기지 않는다 </summary>
        public static string BuildUri(string issuer, string accountName, byte[] secret)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
            ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
            string label = Uri.EscapeDataString(issuer) + ":" + Uri.EscapeDataString(accountName);
            return "otpauth://totp/" + label + "?secret=" + ToBase32(secret) + "&issuer=" + Uri.EscapeDataString(issuer)
                + "&algorithm=SHA1&digits=" + DIGITS.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "&period=" + STEP_SECONDS.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
