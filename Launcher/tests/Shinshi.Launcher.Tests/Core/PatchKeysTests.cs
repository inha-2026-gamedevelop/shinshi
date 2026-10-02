// System
using System;
using System.Security.Cryptography;

// Third-party
using Xunit;

// Project
using Shinshi.Patch;

namespace Shinshi.Launcher.Tests.Core
{
    /// <summary> 키 생성, base64 왕복, keyId 계산, 신뢰 키 목록을 검사한다 </summary>
    public sealed class PatchKeysTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int KEY_ID_HEX_LENGTH = PatchPublicKey.KEY_ID_BYTES * 2;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void KeyId_GeneratedKey_IsFirstEightBytesOfPublicKeySha256()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            byte[] publicKey = key.PublicKey.ToBytes();

            string expected = Convert.ToHexStringLower(SHA256.HashData(publicKey)).Substring(0, KEY_ID_HEX_LENGTH);

            Assert.Equal(PatchPublicKey.KEY_BYTES, publicKey.Length);
            Assert.Equal(expected, key.KeyId);
            Assert.Equal(expected, PatchPublicKey.ComputeKeyId(publicKey));
        }

        [Fact]
        public void TryFromBase64Seed_ExportedSeed_RestoresSameKey()
        {
            PatchSigningKey original = PatchSigningKey.Generate();

            bool loaded = PatchSigningKey.TryFromBase64Seed(original.ExportBase64Seed(), out PatchSigningKey? restored);

            Assert.True(loaded);
            Assert.Equal(original.KeyId, restored!.KeyId);
            Assert.Equal(original.PublicKey.ToBase64(), restored.PublicKey.ToBase64());
        }

        [Fact]
        public void TryFromBase64Seed_RestoredKey_SignsEnvelopeTrustedByOriginalPublicKey()
        {
            PatchSigningKey original = PatchSigningKey.Generate();
            Assert.True(PatchSigningKey.TryFromBase64Seed(original.ExportBase64Seed(), out PatchSigningKey? restored));
            Assert.True(PatchPublicKey.TryFromBase64(original.PublicKey.ToBase64(), out PatchPublicKey? publicKey));

            byte[] envelope = PatchEnvelope.Seal(PatchFormat.KIND_CHANNEL, PatchTestFactory.Utf8("{}"), restored);

            Assert.True(PatchEnvelope.TryOpen(envelope, PatchFormat.KIND_CHANNEL, new TrustedKeySet(publicKey), out _, out _));
        }

        [Fact]
        public void Generate_TwoKeys_HaveDifferentKeyIds()
        {
            Assert.NotEqual(PatchSigningKey.Generate().KeyId, PatchSigningKey.Generate().KeyId);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not base64 !!")]
        [InlineData("AAAA")]
        public void TryFromBase64Seed_InvalidText_ReturnsFalse(string? text)
        {
            Assert.False(PatchSigningKey.TryFromBase64Seed(text, out PatchSigningKey? key));
            Assert.Null(key);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("%%%")]
        [InlineData("AAAA")]
        public void TryFromBase64_InvalidPublicKeyText_ReturnsFalse(string? text)
        {
            Assert.False(PatchPublicKey.TryFromBase64(text, out PatchPublicKey? key));
            Assert.Null(key);
        }

        [Fact]
        public void TrustedKeySet_DuplicateKey_Throws()
        {
            PatchSigningKey key = PatchSigningKey.Generate();

            Assert.Throws<ArgumentException>(() => new TrustedKeySet(key.PublicKey, key.PublicKey));
        }

        [Fact]
        public void TrustedKeySet_TryGet_FindsByKeyIdOnly()
        {
            PatchSigningKey first = PatchSigningKey.Generate();
            PatchSigningKey second = PatchSigningKey.Generate();
            TrustedKeySet keys = new TrustedKeySet(first.PublicKey, second.PublicKey);

            Assert.Equal(2, keys.Count);
            Assert.True(keys.TryGet(second.KeyId, out PatchPublicKey? found));
            Assert.Equal(second.PublicKey.ToBase64(), found!.ToBase64());
            Assert.False(keys.TryGet(second.KeyId.ToUpperInvariant(), out _));
            Assert.False(keys.TryGet(null, out _));
        }
    }
}
