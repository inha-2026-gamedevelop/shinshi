// System
using System;
using System.Text;

// Third Party
using Xunit;

// Project
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> PBKDF2-HMAC-SHA256 공식 벡터와 비밀번호 정책 </summary>
    public sealed class AdminPasswordHasherTests
    {
        /****************************************
        *                Fields
        ****************************************/

        // RFC 7914 11장 PBKDF2-HMAC-SHA256 벡터의 앞 32바이트(첫 블록)
        private const string RFC7914_PASSWD_SALT_1       = "55ac046e56e3089fec1691c22544b605f94185216dde0465e68b9d57c20dacbc";
        private const string RFC7914_PASSWORD_NACL_80000 = "4ddcd8f60b98be21830cee5ef22701f9641a4418d04c0414aeff08876b34ab56";
        private const int    RFC7914_ITERATIONS          = 80000;

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData("passwd", "salt", 1, RFC7914_PASSWD_SALT_1)]
        [InlineData("Password", "NaCl", RFC7914_ITERATIONS, RFC7914_PASSWORD_NACL_80000)]
        public void Derive_Rfc7914Vector_MatchesFirstBlock(string password, string salt, int iterations, string expectedHex)
        {
            byte[] derived = AdminPasswordHasher.Derive(password, Encoding.ASCII.GetBytes(salt), iterations);

            Assert.Equal(expectedHex, Convert.ToHexString(derived).ToLowerInvariant());
        }

        [Fact]
        public void Hash_ThenVerify_AcceptsOnlySamePassword()
        {
            AdminPasswordHasher hasher = new AdminPasswordHasher(AdminTestSupport.TEST_ITERATIONS);
            AdminPasswordHash hash = hasher.Hash(AdminTestSupport.TEST_PASSWORD);

            Assert.Equal(AdminPasswordHasher.SALT_BYTES, hash.Salt.Length);
            Assert.Equal(AdminPasswordHasher.HASH_BYTES, hash.Hash.Length);
            Assert.True(AdminPasswordHasher.Verify(AdminTestSupport.TEST_PASSWORD, hash.Salt, hash.Hash, hash.Iterations));
            Assert.False(AdminPasswordHasher.Verify(AdminTestSupport.TEST_PASSWORD + "x", hash.Salt, hash.Hash, hash.Iterations));
            Assert.False(AdminPasswordHasher.Verify(null, hash.Salt, hash.Hash, hash.Iterations));
            Assert.NotEqual(hash.Salt, hasher.Hash(AdminTestSupport.TEST_PASSWORD).Salt);
        }

        [Fact]
        public void Hash_ShortPassword_RejectsPolicy()
        {
            AdminPasswordHasher hasher = new AdminPasswordHasher(AdminTestSupport.TEST_ITERATIONS);

            Assert.Throws<ArgumentException>(() => hasher.Hash(new string('a', AdminPasswordHasher.MIN_PASSWORD_LENGTH - 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AdminPasswordHasher(AdminSecuritySettings.MIN_PASSWORD_ITERATIONS - 1));
        }

        [Fact]
        public void CreateRandomPassword_MeetsPolicyAndDiffers()
        {
            string first = AdminPasswordHasher.CreateRandomPassword();

            Assert.True(AdminPasswordHasher.MeetsPolicy(first));
            Assert.NotEqual(first, AdminPasswordHasher.CreateRandomPassword());
        }

        [Theory]
        [InlineData("Admin.One", "admin.one")]
        [InlineData("ops_2-x", "ops_2-x")]
        public void TryNormalize_ValidName_ReturnsLowercase(string input, string expected)
        {
            Assert.True(AdminLoginNames.TryNormalize(input, out string normalized));
            Assert.Equal(expected, normalized);
        }

        [Theory]
        [InlineData("ab")]
        [InlineData(".abc")]
        [InlineData("abc!")]
        [InlineData("admKin")]
        [InlineData(null)]
        public void TryNormalize_InvalidName_Rejects(string? input)
        {
            Assert.False(AdminLoginNames.TryNormalize(input, out _));
        }
    }
}
