// System
using System;

// Third-party
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Patch;

namespace Shinshi.Launcher.Tests.Core
{
    /// <summary> 서명 봉투의 왕복, 변조, 키, 종류, 크기, 열기 순서를 검사한다 </summary>
    public sealed class PatchEnvelopeTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PAYLOAD_JSON = "{\"hello\":\"world\"}";
        private const string GARBAGE_JSON = "not json at all";

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void TryOpen_SealedWithTrustedKey_ReturnsOriginalPayloadBytes()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            byte[] payload = PatchTestFactory.Utf8(PAYLOAD_JSON);
            byte[] envelope = PatchEnvelope.Seal(PatchFormat.KIND_MANIFEST, payload, key);

            bool opened = PatchEnvelope.TryOpen(envelope, PatchFormat.KIND_MANIFEST, PatchTestFactory.Trust(key),
                out byte[]? result, out EPatchError error);

            Assert.True(opened);
            Assert.Equal(EPatchError.None, error);
            Assert.Equal(payload, result);
        }

        [Fact]
        public void TryOpen_PayloadByteFlipped_ReturnsBadSignature()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            byte[] payload = PatchTestFactory.Utf8(PAYLOAD_JSON);
            JObject envelope = PatchTestFactory.ParseEnvelope(PatchEnvelope.Seal(PatchFormat.KIND_MANIFEST, payload, key));

            byte[] tampered = (byte[])payload.Clone();
            tampered[tampered.Length - 2] ^= 0x01;
            envelope["payload"] = Convert.ToBase64String(tampered);

            bool opened = PatchEnvelope.TryOpen(PatchTestFactory.WriteEnvelope(envelope), PatchFormat.KIND_MANIFEST,
                PatchTestFactory.Trust(key), out byte[]? result, out EPatchError error);

            Assert.False(opened);
            Assert.Equal(EPatchError.BadSignature, error);
            Assert.Null(result);
        }

        [Fact]
        public void TryOpen_SignatureByteFlipped_ReturnsBadSignature()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            JObject envelope = PatchTestFactory.ParseEnvelope(
                PatchEnvelope.Seal(PatchFormat.KIND_CHANNEL, PatchTestFactory.Utf8(PAYLOAD_JSON), key));

            byte[] signature = Convert.FromBase64String((string)envelope["signature"]!);
            signature[0] ^= 0x01;
            envelope["signature"] = Convert.ToBase64String(signature);

            bool opened = PatchEnvelope.TryOpen(PatchTestFactory.WriteEnvelope(envelope), PatchFormat.KIND_CHANNEL,
                PatchTestFactory.Trust(key), out _, out EPatchError error);

            Assert.False(opened);
            Assert.Equal(EPatchError.BadSignature, error);
        }

        [Fact]
        public void TryOpen_KeyNotInTrustedSet_ReturnsUntrustedKey()
        {
            PatchSigningKey signer = PatchSigningKey.Generate();
            PatchSigningKey trusted = PatchSigningKey.Generate();
            byte[] envelope = PatchEnvelope.Seal(PatchFormat.KIND_MANIFEST, PatchTestFactory.Utf8(PAYLOAD_JSON), signer);

            bool opened = PatchEnvelope.TryOpen(envelope, PatchFormat.KIND_MANIFEST, PatchTestFactory.Trust(trusted),
                out _, out EPatchError error);

            Assert.False(opened);
            Assert.Equal(EPatchError.UntrustedKey, error);
        }

        [Fact]
        public void TryOpen_OtherKeySignsUnderTrustedKeyId_ReturnsBadSignature()
        {
            PatchSigningKey attacker = PatchSigningKey.Generate();
            PatchSigningKey trusted = PatchSigningKey.Generate();
            JObject envelope = PatchTestFactory.ParseEnvelope(
                PatchEnvelope.Seal(PatchFormat.KIND_MANIFEST, PatchTestFactory.Utf8(PAYLOAD_JSON), attacker));
            envelope["keyId"] = trusted.KeyId;

            bool opened = PatchEnvelope.TryOpen(PatchTestFactory.WriteEnvelope(envelope), PatchFormat.KIND_MANIFEST,
                PatchTestFactory.Trust(trusted), out _, out EPatchError error);

            Assert.False(opened);
            Assert.Equal(EPatchError.BadSignature, error);
        }

        [Fact]
        public void TryOpen_SecondTrustedKeyDuringRotation_Succeeds()
        {
            PatchSigningKey oldKey = PatchSigningKey.Generate();
            PatchSigningKey newKey = PatchSigningKey.Generate();
            byte[] envelope = PatchEnvelope.Seal(PatchFormat.KIND_CHANNEL, PatchTestFactory.Utf8(PAYLOAD_JSON), newKey);

            bool opened = PatchEnvelope.TryOpen(envelope, PatchFormat.KIND_CHANNEL, PatchTestFactory.Trust(oldKey, newKey),
                out _, out EPatchError error);

            Assert.True(opened);
            Assert.Equal(EPatchError.None, error);
        }

        [Fact]
        public void TryOpen_ChannelEnvelopeOpenedAsManifest_ReturnsKindMismatch()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            byte[] envelope = PatchEnvelope.Seal(PatchFormat.KIND_CHANNEL, PatchTestFactory.Utf8(PAYLOAD_JSON), key);

            bool opened = PatchEnvelope.TryOpen(envelope, PatchFormat.KIND_MANIFEST, PatchTestFactory.Trust(key),
                out _, out EPatchError error);

            Assert.False(opened);
            Assert.Equal(EPatchError.KindMismatch, error);
        }

        [Fact]
        public void TryOpen_KindFieldRewrittenWithSameSignature_ReturnsBadSignature()
        {
            // kind 문자열만 바꿔 끼워도 서명 바이트의 도메인이 달라 통과하지 못한다
            PatchSigningKey key = PatchSigningKey.Generate();
            JObject envelope = PatchTestFactory.ParseEnvelope(
                PatchEnvelope.Seal(PatchFormat.KIND_CHANNEL, PatchTestFactory.Utf8(PAYLOAD_JSON), key));
            envelope["kind"] = PatchFormat.KIND_MANIFEST;

            bool opened = PatchEnvelope.TryOpen(PatchTestFactory.WriteEnvelope(envelope), PatchFormat.KIND_MANIFEST,
                PatchTestFactory.Trust(key), out _, out EPatchError error);

            Assert.False(opened);
            Assert.Equal(EPatchError.BadSignature, error);
        }

        [Fact]
        public void TryOpen_ChannelEnvelopeOverLimit_ReturnsTooLarge()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            byte[] oversized = new byte[PatchFormat.MAX_CHANNEL_ENVELOPE_BYTES + 1];

            bool opened = PatchEnvelope.TryOpen(oversized, PatchFormat.KIND_CHANNEL, PatchTestFactory.Trust(key),
                out _, out EPatchError error);

            Assert.False(opened);
            Assert.Equal(EPatchError.TooLarge, error);
        }

        [Fact]
        public void TryOpen_ManifestEnvelopeOverLimit_ReturnsTooLarge()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            byte[] oversized = new byte[PatchFormat.MAX_MANIFEST_ENVELOPE_BYTES + 1];

            bool opened = PatchEnvelope.TryOpen(oversized, PatchFormat.KIND_MANIFEST, PatchTestFactory.Trust(key),
                out _, out EPatchError error);

            Assert.False(opened);
            Assert.Equal(EPatchError.TooLarge, error);
        }

        [Fact]
        public void TryOpen_ValidChannelSizeOpenedAsManifest_IsNotTooLarge()
        {
            // 채널 상한을 넘지만 매니페스트 상한 안인 봉투는 크기로 거절되지 않는다
            PatchSigningKey key = PatchSigningKey.Generate();
            byte[] payload = PatchTestFactory.Utf8("\"" + new string('x', PatchFormat.MAX_CHANNEL_ENVELOPE_BYTES) + "\"");
            byte[] envelope = PatchEnvelope.Seal(PatchFormat.KIND_MANIFEST, payload, key);

            Assert.True(envelope.Length > PatchFormat.MAX_CHANNEL_ENVELOPE_BYTES);
            Assert.True(PatchEnvelope.TryOpen(envelope, PatchFormat.KIND_MANIFEST, PatchTestFactory.Trust(key), out _, out _));
        }

        [Fact]
        public void Seal_ChannelPayloadOverLimit_Throws()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            byte[] payload = new byte[PatchFormat.MAX_CHANNEL_ENVELOPE_BYTES];

            Assert.Throws<ArgumentException>(() => PatchEnvelope.Seal(PatchFormat.KIND_CHANNEL, payload, key));
        }

        [Theory]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("[]")]
        [InlineData("null")]
        public void TryOpen_EnvelopeNotJsonObject_ReturnsMalformed(string text)
        {
            PatchSigningKey key = PatchSigningKey.Generate();

            bool opened = PatchEnvelope.TryOpen(PatchTestFactory.Utf8(text), PatchFormat.KIND_MANIFEST, PatchTestFactory.Trust(key),
                out _, out EPatchError error);

            Assert.False(opened);
            Assert.Equal(EPatchError.Malformed, error);
        }

        [Fact]
        public void TryOpen_InvalidUtf8_ReturnsMalformed()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            byte[] invalid = { 0x7B, 0xC3, 0x28, 0x7D };

            bool opened = PatchEnvelope.TryOpen(invalid, PatchFormat.KIND_MANIFEST, PatchTestFactory.Trust(key), out _, out EPatchError error);

            Assert.False(opened);
            Assert.Equal(EPatchError.Malformed, error);
        }

        [Fact]
        public void TryOpen_FormatVersionTwo_ReturnsUnsupportedFormat()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            JObject envelope = PatchTestFactory.ParseEnvelope(
                PatchEnvelope.Seal(PatchFormat.KIND_MANIFEST, PatchTestFactory.Utf8(PAYLOAD_JSON), key));
            envelope["formatVersion"] = PatchFormat.FORMAT_VERSION + 1;

            bool opened = PatchEnvelope.TryOpen(PatchTestFactory.WriteEnvelope(envelope), PatchFormat.KIND_MANIFEST,
                PatchTestFactory.Trust(key), out _, out EPatchError error);

            Assert.False(opened);
            Assert.Equal(EPatchError.UnsupportedFormat, error);
        }

        [Fact]
        public void TryOpen_UntrustedKeyWithBrokenBase64_ReportsUntrustedKeyFirst()
        {
            // keyId 신뢰 검사가 base64 디코드보다 먼저 온다
            PatchSigningKey key = PatchSigningKey.Generate();
            JObject envelope = PatchTestFactory.ParseEnvelope(
                PatchEnvelope.Seal(PatchFormat.KIND_MANIFEST, PatchTestFactory.Utf8(PAYLOAD_JSON), key));
            envelope["keyId"] = "0000000000000000";
            envelope["payload"] = "%%%";

            bool opened = PatchEnvelope.TryOpen(PatchTestFactory.WriteEnvelope(envelope), PatchFormat.KIND_MANIFEST,
                PatchTestFactory.Trust(key), out _, out EPatchError error);

            Assert.False(opened);
            Assert.Equal(EPatchError.UntrustedKey, error);
        }

        [Fact]
        public void TryOpen_BrokenBase64Payload_ReturnsMalformed()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            JObject envelope = PatchTestFactory.ParseEnvelope(
                PatchEnvelope.Seal(PatchFormat.KIND_MANIFEST, PatchTestFactory.Utf8(PAYLOAD_JSON), key));
            envelope["payload"] = "%%%";

            bool opened = PatchEnvelope.TryOpen(PatchTestFactory.WriteEnvelope(envelope), PatchFormat.KIND_MANIFEST,
                PatchTestFactory.Trust(key), out _, out EPatchError error);

            Assert.False(opened);
            Assert.Equal(EPatchError.Malformed, error);
        }

        [Fact]
        public void TryOpen_SignedNonJsonPayload_ReturnsPayloadWithoutParsing()
        {
            // 봉투 계층은 payload를 해석하지 않는다. 해석은 서명 확인 뒤 문서 계층이 한다
            PatchSigningKey key = PatchSigningKey.Generate();
            byte[] payload = PatchTestFactory.Utf8(GARBAGE_JSON);
            byte[] envelope = PatchEnvelope.Seal(PatchFormat.KIND_MANIFEST, payload, key);

            bool opened = PatchEnvelope.TryOpen(envelope, PatchFormat.KIND_MANIFEST, PatchTestFactory.Trust(key), out byte[]? result, out _);

            Assert.True(opened);
            Assert.Equal(payload, result);
        }

        [Fact]
        public void TryOpen_UnsignedGarbagePayload_ReturnsBadSignatureNotMalformed()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            JObject envelope = PatchTestFactory.ParseEnvelope(
                PatchEnvelope.Seal(PatchFormat.KIND_MANIFEST, PatchTestFactory.Utf8(PAYLOAD_JSON), key));
            envelope["payload"] = Convert.ToBase64String(PatchTestFactory.Utf8(GARBAGE_JSON));

            bool opened = PatchDocuments.TryOpenManifest(PatchTestFactory.WriteEnvelope(envelope), PatchTestFactory.Trust(key),
                out _, out _, out EPatchError error);

            Assert.False(opened);
            Assert.Equal(EPatchError.BadSignature, error);
        }
    }
}
