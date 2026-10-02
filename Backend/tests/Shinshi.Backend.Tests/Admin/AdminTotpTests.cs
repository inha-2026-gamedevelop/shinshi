// System
using System;
using System.Text;

// Third Party
using Xunit;

// Project
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> RFC 6238 부록 B의 SHA1 공식 벡터와 허용 창, Base32, otpauth URI </summary>
    public sealed class AdminTotpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        // RFC 6238 부록 B의 SHA1 시드
        private const string RFC_SEED        = "12345678901234567890";
        private const string RFC_SEED_BASE32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        private const int    RFC_DIGITS      = 8;

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData(59L, "94287082")]
        [InlineData(1111111109L, "07081804")]
        [InlineData(1111111111L, "14050471")]
        [InlineData(1234567890L, "89005924")]
        [InlineData(2000000000L, "69279037")]
        [InlineData(20000000000L, "65353130")]
        public void ComputeCode_Rfc6238Sha1Vector_MatchesEightAndSixDigits(long unixSeconds, string expected)
        {
            byte[] seed = Encoding.ASCII.GetBytes(RFC_SEED);
            long step = AdminTotp.StepOf(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

            Assert.Equal(expected, AdminTotp.ComputeCode(seed, step, RFC_DIGITS));
            Assert.Equal(expected.Substring(RFC_DIGITS - AdminTotp.DIGITS), AdminTotp.ComputeCode(seed, step));
        }

        [Fact]
        public void ToBase32_RfcSeed_MatchesAuthenticatorEncoding()
        {
            Assert.Equal(RFC_SEED_BASE32, AdminTotp.ToBase32(Encoding.ASCII.GetBytes(RFC_SEED)));
            Assert.Equal(Encoding.ASCII.GetBytes(RFC_SEED), AdminTestSupport.FromBase32(RFC_SEED_BASE32));
        }

        [Fact]
        public void Match_WithinOneStep_ReturnsStepAndRejectsOutsideWindow()
        {
            byte[] secret = AdminTotp.CreateSecret();
            DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
            long step = AdminTotp.StepOf(now);

            Assert.Equal(step, AdminTotp.Match(secret, AdminTotp.ComputeCode(secret, step), now));
            Assert.Equal(step - 1, AdminTotp.Match(secret, AdminTotp.ComputeCode(secret, step - 1), now));
            Assert.Equal(step + 1, AdminTotp.Match(secret, AdminTotp.ComputeCode(secret, step + 1), now));
            Assert.Null(AdminTotp.Match(secret, AdminTotp.ComputeCode(secret, step - 2), now));
            Assert.Null(AdminTotp.Match(secret, AdminTotp.ComputeCode(secret, step + 2), now));
            Assert.Null(AdminTotp.Match(secret, "12345", now));
            Assert.Null(AdminTotp.Match(secret, "12a456", now));
            Assert.Null(AdminTotp.Match(secret, null, now));
        }

        [Fact]
        public void BuildUri_LoginName_ContainsIssuerSecretAndParameters()
        {
            string uri = AdminTotp.BuildUri(AdminCredentialFactory.TOTP_ISSUER, "owner.one", Encoding.ASCII.GetBytes(RFC_SEED));

            Assert.Equal("otpauth://totp/Shinshi%20Admin:owner.one?secret=" + RFC_SEED_BASE32
                + "&issuer=Shinshi%20Admin&algorithm=SHA1&digits=6&period=30", uri);
        }
    }
}
