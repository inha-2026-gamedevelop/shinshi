// System
using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

// Third-party
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Shinshi.Patch
{
    /// <summary> 서명 검증용 Ed25519 공개키와 그 keyId </summary>
    public sealed class PatchPublicKey
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int KEY_BYTES    = 32;
        public const int KEY_ID_BYTES = 8;

        private readonly Ed25519PublicKeyParameters _key;
        private readonly byte[]                     _encoded;
        private readonly string                     _keyId;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 공개키 SHA-256 앞 8바이트의 소문자 hex </summary>
        public string KeyId => _keyId;

        /****************************************
        *                Methods
        ****************************************/

        private PatchPublicKey(byte[] encoded, Ed25519PublicKeyParameters key)
        {
            _encoded = encoded;
            _key     = key;
            _keyId   = ComputeKeyId(encoded);
        }

        /// <summary> 32바이트 공개키로 만든다. 길이가 다르면 false </summary>
        public static bool TryCreate(ReadOnlySpan<byte> encoded, [NotNullWhen(true)] out PatchPublicKey? key)
        {
            key = null;
            if (encoded.Length != KEY_BYTES)
            {
                return false;
            }

            byte[] copy = encoded.ToArray();
            Ed25519PublicKeyParameters parameters;
            try
            {
                parameters = new Ed25519PublicKeyParameters(copy, 0);
            }
            catch (ArgumentException)
            {
                // 곡선 위의 점이 아닌 32바이트
                return false;
            }

            key = new PatchPublicKey(copy, parameters);
            return true;
        }

        /// <summary> base64 공개키 문자열로 만든다. 형식이 틀리면 false </summary>
        public static bool TryFromBase64(string? base64, [NotNullWhen(true)] out PatchPublicKey? key)
        {
            key = null;
            if (string.IsNullOrWhiteSpace(base64))
            {
                return false;
            }

            byte[] encoded;
            try
            {
                encoded = Convert.FromBase64String(base64.Trim());
            }
            catch (FormatException)
            {
                return false;
            }

            return TryCreate(encoded, out key);
        }

        public static string ComputeKeyId(ReadOnlySpan<byte> encodedPublicKey)
        {
            byte[] digest = SHA256.HashData(encodedPublicKey);
            return Convert.ToHexStringLower(digest, 0, KEY_ID_BYTES);
        }

        public string ToBase64() { return Convert.ToBase64String(_encoded); }

        public byte[] ToBytes() { return (byte[])_encoded.Clone(); }

        internal bool Verify(byte[] message, byte[] signature)
        {
            if (signature.Length != Ed25519PrivateKeyParameters.SignatureSize)
            {
                return false;
            }

            Ed25519Signer verifier = new Ed25519Signer();
            verifier.Init(false, _key);
            verifier.BlockUpdate(message, 0, message.Length);
            return verifier.VerifySignature(signature);
        }
    }
}
