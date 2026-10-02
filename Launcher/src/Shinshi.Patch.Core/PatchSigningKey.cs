// System
using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

// Third-party
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Shinshi.Patch
{
    /// <summary> 게시 도구가 쓰는 Ed25519 개인키, 32바이트 seed로 저장한다 </summary>
    public sealed class PatchSigningKey
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int SEED_BYTES = 32;

        private readonly Ed25519PrivateKeyParameters _key;
        private readonly PatchPublicKey              _publicKey;

        /****************************************
        *              Properties
        ****************************************/

        public PatchPublicKey PublicKey => _publicKey;
        public string         KeyId     => _publicKey.KeyId;

        /****************************************
        *                Methods
        ****************************************/

        private PatchSigningKey(byte[] seed)
        {
            _key = new Ed25519PrivateKeyParameters(seed, 0);
            byte[] encodedPublic = _key.GeneratePublicKey().GetEncoded();
            if (!PatchPublicKey.TryCreate(encodedPublic, out PatchPublicKey? publicKey))
            {
                throw new InvalidOperationException("Ed25519 public key has an unexpected length.");
            }

            _publicKey = publicKey;
        }

        /// <summary> OS 난수로 새 키를 만든다 </summary>
        public static PatchSigningKey Generate()
        {
            byte[] seed = RandomNumberGenerator.GetBytes(SEED_BYTES);
            try
            {
                return new PatchSigningKey(seed);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(seed);
            }
        }

        /// <summary> 32바이트 seed로 만든다. 길이가 다르면 false </summary>
        public static bool TryFromSeed(ReadOnlySpan<byte> seed, [NotNullWhen(true)] out PatchSigningKey? key)
        {
            key = null;
            if (seed.Length != SEED_BYTES)
            {
                return false;
            }

            byte[] copy = seed.ToArray();
            try
            {
                key = new PatchSigningKey(copy);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(copy);
            }
        }

        /// <summary> base64 seed 문자열(개인키 파일 내용)로 만든다. 형식이 틀리면 false </summary>
        public static bool TryFromBase64Seed(string? base64, [NotNullWhen(true)] out PatchSigningKey? key)
        {
            key = null;
            if (string.IsNullOrWhiteSpace(base64))
            {
                return false;
            }

            byte[] seed;
            try
            {
                seed = Convert.FromBase64String(base64.Trim());
            }
            catch (FormatException)
            {
                return false;
            }

            try
            {
                return TryFromSeed(seed, out key);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(seed);
            }
        }

        /// <summary> 개인키 파일에 쓸 base64 seed. 로그나 콘솔에 남기지 않는다 </summary>
        public string ExportBase64Seed()
        {
            byte[] seed = _key.GetEncoded();
            try
            {
                return Convert.ToBase64String(seed);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(seed);
            }
        }

        internal byte[] Sign(byte[] message)
        {
            Ed25519Signer signer = new Ed25519Signer();
            signer.Init(true, _key);
            signer.BlockUpdate(message, 0, message.Length);
            return signer.GenerateSignature();
        }
    }
}
