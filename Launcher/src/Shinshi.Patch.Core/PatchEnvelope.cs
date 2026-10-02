// System
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

// Third-party
using Newtonsoft.Json;

// Project
using Shinshi.Contracts;

namespace Shinshi.Patch
{
    /// <summary> 원본 payload 바이트에 Ed25519 서명을 붙이고, 검증이 끝난 payload만 돌려준다 </summary>
    public static class PatchEnvelope
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string DOMAIN_PREFIX = "shinshi.patch.";
        private const string DOMAIN_SUFFIX = ".v1\n";

        private static readonly UTF8Encoding G_StrictUtf8 = new UTF8Encoding(false, true);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> payload를 서명해 봉투 JSON의 UTF-8 바이트를 만든다 </summary>
        public static byte[] Seal(string kind, byte[] payload, PatchSigningKey key)
        {
            ArgumentNullException.ThrowIfNull(payload);
            ArgumentNullException.ThrowIfNull(key);
            if (!PatchFormat.IsKnownKind(kind))
            {
                throw new ArgumentException("Unknown envelope kind.", nameof(kind));
            }

            if (payload.Length == 0)
            {
                throw new ArgumentException("Envelope payload cannot be empty.", nameof(payload));
            }

            byte[] sealedBytes = SealUnchecked(kind, payload, key);
            if (sealedBytes.Length > PatchFormat.GetMaxEnvelopeBytes(kind))
            {
                throw new ArgumentException("Sealed envelope exceeds the size limit for its kind.", nameof(payload));
            }

            return sealedBytes;
        }

        /// <summary> 봉투를 열어 서명이 맞는 payload 바이트를 돌려준다. payload는 이 안에서 파싱하지 않는다 </summary>
        public static bool TryOpen(byte[] envelopeBytes, string expectedKind, TrustedKeySet trustedKeys, [NotNullWhen(true)] out byte[]? payload,
            out EPatchError error)
        {
            ArgumentNullException.ThrowIfNull(envelopeBytes);
            ArgumentNullException.ThrowIfNull(trustedKeys);
            if (!PatchFormat.IsKnownKind(expectedKind))
            {
                throw new ArgumentException("Unknown envelope kind.", nameof(expectedKind));
            }

            payload = null;

            // 순서를 바꾸지 않는다. 신뢰할 수 없는 입력은 크기, 형식, 키, 서명 순으로 싼 검사부터 거르고
            // 서명이 맞기 전에는 payload JSON을 파서에 넣지 않는다
            if (envelopeBytes.Length > PatchFormat.GetMaxEnvelopeBytes(expectedKind))
            {
                error = EPatchError.TooLarge;
                return false;
            }

            if (!TryParseEnvelope(envelopeBytes, out SignedEnvelope? envelope))
            {
                error = EPatchError.Malformed;
                return false;
            }

            if (envelope.FormatVersion != PatchFormat.FORMAT_VERSION)
            {
                error = EPatchError.UnsupportedFormat;
                return false;
            }

            if (!string.Equals(envelope.Kind, expectedKind, StringComparison.Ordinal))
            {
                error = EPatchError.KindMismatch;
                return false;
            }

            if (!trustedKeys.TryGet(envelope.KeyId, out PatchPublicKey? publicKey))
            {
                error = EPatchError.UntrustedKey;
                return false;
            }

            if (!TryDecodeBase64(envelope.Payload, out byte[]? payloadBytes)
                || !TryDecodeBase64(envelope.Signature, out byte[]? signature)
                || (payloadBytes.Length == 0))
            {
                error = EPatchError.Malformed;
                return false;
            }

            if (!publicKey.Verify(BuildSigningBytes(expectedKind, payloadBytes), signature))
            {
                error = EPatchError.BadSignature;
                return false;
            }

            payload = payloadBytes;
            error   = EPatchError.None;
            return true;
        }

        internal static byte[] SealUnchecked(string kind, byte[] payload, PatchSigningKey key)
        {
            byte[] signature = key.Sign(BuildSigningBytes(kind, payload));
            SignedEnvelope envelope = new SignedEnvelope(PatchFormat.FORMAT_VERSION, kind, key.KeyId,
                Convert.ToBase64String(payload), Convert.ToBase64String(signature));
            return G_StrictUtf8.GetBytes(JsonConvert.SerializeObject(envelope, ContractJson.CreateSettings()));
        }

        // 종류 이름을 서명에 섞어 채널 포인터 서명을 매니페스트 자리에 끼워 넣는 식의 바꿔치기를 막는다
        internal static byte[] BuildSigningBytes(string kind, byte[] payload)
        {
            byte[] domain = G_StrictUtf8.GetBytes(DOMAIN_PREFIX + kind + DOMAIN_SUFFIX);
            byte[] message = new byte[domain.Length + payload.Length];
            Buffer.BlockCopy(domain, 0, message, 0, domain.Length);
            Buffer.BlockCopy(payload, 0, message, domain.Length, payload.Length);
            return message;
        }

        internal static bool TryDecodeUtf8(byte[] bytes, [NotNullWhen(true)] out string? text)
        {
            try
            {
                text = G_StrictUtf8.GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                text = null;
                return false;
            }
        }

        private static bool TryParseEnvelope(byte[] envelopeBytes, [NotNullWhen(true)] out SignedEnvelope? envelope)
        {
            envelope = null;
            if (!TryDecodeUtf8(envelopeBytes, out string? text))
            {
                return false;
            }

            try
            {
                envelope = JsonConvert.DeserializeObject<SignedEnvelope>(text, ContractJson.CreateSettings());
            }
            catch (JsonException)
            {
                return false;
            }

            return envelope != null;
        }

        private static bool TryDecodeBase64(string? base64, [NotNullWhen(true)] out byte[]? bytes)
        {
            bytes = null;
            if (string.IsNullOrEmpty(base64))
            {
                return false;
            }

            try
            {
                bytes = Convert.FromBase64String(base64);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
