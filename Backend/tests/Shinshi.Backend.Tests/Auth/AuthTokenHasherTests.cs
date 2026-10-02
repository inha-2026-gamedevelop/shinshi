// System
using System;
using System.Security.Cryptography;

// Third Party
using Xunit;

// Project
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests.Auth
{
    /// <summary> DB 없이 비밀키 검증과 고엔트로피 토큰의 용도 분리를 검사한다 </summary>
    public sealed class AuthTokenHasherTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int KEY_BYTES = 32;
        private const int SHORT_KEY_BYTES = 16;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void CreateToken_NewTokens_HaveCanonicalDistinctRandomValues()
        {
            AuthTokenHasher hasher = CreateHasher();
            string first = hasher.CreateToken();
            string second = hasher.CreateToken();

            Assert.True(AuthTokenHasher.IsWellFormed(first));
            Assert.True(AuthTokenHasher.IsWellFormed(second));
            Assert.NotEqual(first, second);
        }

        [Fact]
        public void Hash_SameTokenDifferentDomains_DoesNotCrossTokenPurposes()
        {
            AuthTokenHasher hasher = CreateHasher();
            string token = hasher.CreateToken();

            Assert.Equal(hasher.Hash(AuthTokenHasher.ACCESS_DOMAIN, token),
                hasher.Hash(AuthTokenHasher.ACCESS_DOMAIN, token));
            Assert.NotEqual(hasher.Hash(AuthTokenHasher.ACCESS_DOMAIN, token),
                hasher.Hash(AuthTokenHasher.REFRESH_DOMAIN, token));
            Assert.NotEqual(hasher.Hash(AuthTokenHasher.INVITE_DOMAIN, token),
                hasher.Hash(AuthTokenHasher.JOIN_DOMAIN, token));
        }

        [Fact]
        public void Hash_SameTokenDifferentSecrets_UsesConfiguredSecret()
        {
            string token = CreateHasher().CreateToken();

            Assert.NotEqual(CreateHasher().Hash(AuthTokenHasher.ACCESS_DOMAIN, token),
                CreateHasher().Hash(AuthTokenHasher.ACCESS_DOMAIN, token));
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-a-base64-key")]
        public void Constructor_MissingOrMalformedKey_RejectsConfiguration(string key)
        {
            Assert.Throws<ArgumentException>(() => new AuthTokenHasher(new AuthTokenOptions { HashKeyBase64 = key }));
        }

        [Fact]
        public void Constructor_InsufficientKeyEntropy_RejectsConfiguration()
        {
            Assert.Throws<ArgumentException>(() => new AuthTokenHasher(new AuthTokenOptions
            {
                HashKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SHORT_KEY_BYTES))
            }));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("untrusted-account-id")]
        [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
        [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB")]
        public void IsWellFormed_MalformedOrNoncanonicalToken_ReturnsFalse(string? token)
        {
            Assert.False(AuthTokenHasher.IsWellFormed(token));
        }

        internal static AuthTokenOptions CreateOptions()
        {
            return new AuthTokenOptions { HashKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KEY_BYTES)) };
        }

        private static AuthTokenHasher CreateHasher()
        {
            return new AuthTokenHasher(CreateOptions());
        }
    }
}
