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
    /// <summary> 매니페스트와 채널 포인터를 서명 봉투로 만들고, 서명과 필드 검사를 모두 통과한 문서만 연다 </summary>
    public static class PatchDocuments
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 필드를 검사한 뒤 매니페스트 봉투를 만든다. payloadSha256은 채널 포인터의 manifestSha256에 넣는다 </summary>
        public static bool TrySealManifest(ReleaseManifest manifest, PatchSigningKey key, [NotNullWhen(true)] out byte[]? envelope,
            [NotNullWhen(true)] out string? payloadSha256, out EPatchError error)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            ArgumentNullException.ThrowIfNull(key);

            envelope      = null;
            payloadSha256 = null;
            error         = PatchValidation.ValidateManifest(manifest);
            if (error != EPatchError.None)
            {
                return false;
            }

            byte[] payload = SerializePayload(manifest);
            if (!TrySeal(PatchFormat.KIND_MANIFEST, payload, key, out envelope))
            {
                error = EPatchError.TooLarge;
                return false;
            }

            payloadSha256 = PatchHash.Compute(payload);
            return true;
        }

        /// <summary> 필드를 검사한 뒤 채널 포인터 봉투를 만든다 </summary>
        public static bool TrySealChannelPointer(ChannelPointer pointer, PatchSigningKey key, [NotNullWhen(true)] out byte[]? envelope,
            out EPatchError error)
        {
            ArgumentNullException.ThrowIfNull(pointer);
            ArgumentNullException.ThrowIfNull(key);

            envelope = null;
            error    = PatchValidation.ValidateChannelPointer(pointer);
            if (error != EPatchError.None)
            {
                return false;
            }

            if (!TrySeal(PatchFormat.KIND_CHANNEL, SerializePayload(pointer), key, out envelope))
            {
                error = EPatchError.TooLarge;
                return false;
            }

            return true;
        }

        /// <summary> 매니페스트 봉투를 연다. payloadSha256은 서명이 확인된 payload 바이트의 SHA-256 </summary>
        public static bool TryOpenManifest(byte[] envelope, TrustedKeySet trustedKeys, [NotNullWhen(true)] out ReleaseManifest? manifest,
            [NotNullWhen(true)] out string? payloadSha256, out EPatchError error)
        {
            manifest      = null;
            payloadSha256 = null;
            if (!PatchEnvelope.TryOpen(envelope, PatchFormat.KIND_MANIFEST, trustedKeys, out byte[]? payload, out error))
            {
                return false;
            }

            if (!TryDeserializePayload(payload, out ReleaseManifest? parsed))
            {
                error = EPatchError.Malformed;
                return false;
            }

            error = PatchValidation.ValidateManifest(parsed);
            if (error != EPatchError.None)
            {
                return false;
            }

            manifest      = parsed;
            payloadSha256 = PatchHash.Compute(payload);
            return true;
        }

        /// <summary> 채널 포인터 봉투를 연다 </summary>
        public static bool TryOpenChannelPointer(byte[] envelope, TrustedKeySet trustedKeys,
            [NotNullWhen(true)] out ChannelPointer? pointer, out EPatchError error)
        {
            pointer = null;
            if (!PatchEnvelope.TryOpen(envelope, PatchFormat.KIND_CHANNEL, trustedKeys, out byte[]? payload, out error))
            {
                return false;
            }

            if (!TryDeserializePayload(payload, out ChannelPointer? parsed))
            {
                error = EPatchError.Malformed;
                return false;
            }

            error = PatchValidation.ValidateChannelPointer(parsed);
            if (error != EPatchError.None)
            {
                return false;
            }

            pointer = parsed;
            return true;
        }

        private static bool TrySeal(string kind, byte[] payload, PatchSigningKey key, [NotNullWhen(true)] out byte[]? envelope)
        {
            envelope = null;

            // base64는 4/3배로 늘어나므로 payload가 상한 안이어도 봉투가 넘을 수 있다. 봉투를 만든 뒤 크기로 판단한다
            if (payload.Length >= PatchFormat.GetMaxEnvelopeBytes(kind))
            {
                return false;
            }

            byte[] sealedBytes = PatchEnvelope.SealUnchecked(kind, payload, key);
            if (sealedBytes.Length > PatchFormat.GetMaxEnvelopeBytes(kind))
            {
                return false;
            }

            envelope = sealedBytes;
            return true;
        }

        private static byte[] SerializePayload(object document)
        {
            string json = JsonConvert.SerializeObject(document, ContractJson.CreateSettings());
            return new UTF8Encoding(false).GetBytes(json);
        }

        // 서명이 확인된 payload만 여기에 온다
        private static bool TryDeserializePayload<T>(byte[] payload, [NotNullWhen(true)] out T? document)
            where T : class
        {
            document = null;
            if (!PatchEnvelope.TryDecodeUtf8(payload, out string? text))
            {
                return false;
            }

            try
            {
                document = JsonConvert.DeserializeObject<T>(text, ContractJson.CreateSettings());
            }
            catch (JsonException)
            {
                return false;
            }

            return document != null;
        }
    }
}
