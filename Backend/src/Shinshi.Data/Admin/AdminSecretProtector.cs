// System
using System;
using System.Security.Cryptography;
using System.Text;

namespace Shinshi.Data.Admin
{
    /// <summary> TOTP 비밀의 AES-256-GCM 암복호. 운영자 ID를 추가 인증 데이터로 묶어 다른 행에 옮긴 암호문은 풀리지 않는다.
    /// 형식은 버전 1바이트, nonce 12바이트, 태그 16바이트, 암호문 순서다 </summary>
    public sealed class AdminSecretProtector
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int KEY_BYTES   = 32;
        public const int NONCE_BYTES = 12;
        public const int TAG_BYTES   = 16;

        private const byte   FORMAT_VERSION    = 1;
        private const int    VERSION_BYTES     = 1;
        private const int    HEADER_BYTES      = VERSION_BYTES + NONCE_BYTES + TAG_BYTES;
        private const string ASSOCIATED_DOMAIN = "shinshi.admin.totp-secret.v1";

        private readonly byte[] _key;

        /****************************************
        *                Methods
        ****************************************/

        public AdminSecretProtector(byte[] key)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (key.Length != KEY_BYTES)
            {
                throw new ArgumentException("The admin secret key must contain exactly 32 bytes.", nameof(key));
            }

            _key = (byte[])key.Clone();
        }

        /// <summary> 설정의 Base64 키를 읽는다. 오류 메시지에 설정 값을 싣지 않는다 </summary>
        public static AdminSecretProtector FromBase64(string? keyBase64)
        {
            byte[] key;
            try
            {
                key = Convert.FromBase64String(keyBase64 ?? string.Empty);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("Admin:TotpKeyBase64 must contain a base64 encoded 32 byte key.");
            }
            if (key.Length != KEY_BYTES)
            {
                throw new InvalidOperationException("Admin:TotpKeyBase64 must contain a base64 encoded 32 byte key.");
            }

            return new AdminSecretProtector(key);
        }

        public byte[] Protect(ReadOnlySpan<byte> plaintext, Guid adminId)
        {
            byte[] output = new byte[HEADER_BYTES + plaintext.Length];
            output[0] = FORMAT_VERSION;
            Span<byte> nonce = output.AsSpan(VERSION_BYTES, NONCE_BYTES);
            Span<byte> tag = output.AsSpan(VERSION_BYTES + NONCE_BYTES, TAG_BYTES);
            RandomNumberGenerator.Fill(nonce);
            using AesGcm aes = new AesGcm(_key, TAG_BYTES);
            aes.Encrypt(nonce, plaintext, output.AsSpan(HEADER_BYTES), tag, CreateAssociatedData(adminId));
            return output;
        }

        /// <summary> 키, 운영자 ID, 암호문 중 하나라도 다르면 CryptographicException을 던진다 </summary>
        public byte[] Unprotect(byte[] protectedSecret, Guid adminId)
        {
            ArgumentNullException.ThrowIfNull(protectedSecret);
            if ((protectedSecret.Length <= HEADER_BYTES) || (protectedSecret[0] != FORMAT_VERSION))
            {
                throw new CryptographicException("Unsupported protected secret format.");
            }

            byte[] plaintext = new byte[protectedSecret.Length - HEADER_BYTES];
            using AesGcm aes = new AesGcm(_key, TAG_BYTES);
            aes.Decrypt(protectedSecret.AsSpan(VERSION_BYTES, NONCE_BYTES), protectedSecret.AsSpan(HEADER_BYTES),
                protectedSecret.AsSpan(VERSION_BYTES + NONCE_BYTES, TAG_BYTES), plaintext, CreateAssociatedData(adminId));
            return plaintext;
        }

        private static byte[] CreateAssociatedData(Guid adminId)
        {
            return Encoding.UTF8.GetBytes(ASSOCIATED_DOMAIN + "\0" + adminId.ToString("D"));
        }
    }
}
