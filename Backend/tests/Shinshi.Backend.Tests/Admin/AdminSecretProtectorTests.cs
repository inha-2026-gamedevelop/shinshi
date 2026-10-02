// System
using System;
using System.Net;
using System.Security.Cryptography;

// Third Party
using Xunit;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> TOTP 비밀 AES-GCM 왕복과 변조 거절, 허용 네트워크, 역할 코드 일치 </summary>
    public sealed class AdminSecretProtectorTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void ProtectThenUnprotect_SameAdmin_RoundTrips()
        {
            AdminSecretProtector protector = AdminSecretProtector.FromBase64(AdminTestSupport.CreateKeyBase64());
            byte[] secret = AdminTotp.CreateSecret();
            Guid adminId = Guid.NewGuid();

            byte[] protectedSecret = protector.Protect(secret, adminId);

            Assert.Equal(1 + AdminSecretProtector.NONCE_BYTES + AdminSecretProtector.TAG_BYTES + secret.Length,
                protectedSecret.Length);
            Assert.Equal(secret, protector.Unprotect(protectedSecret, adminId));
            Assert.NotEqual(protectedSecret, protector.Protect(secret, adminId));
        }

        [Fact]
        public void Unprotect_OtherAdminTamperOrKey_Throws()
        {
            AdminSecretProtector protector = AdminSecretProtector.FromBase64(AdminTestSupport.CreateKeyBase64());
            Guid adminId = Guid.NewGuid();
            byte[] protectedSecret = protector.Protect(AdminTotp.CreateSecret(), adminId);
            byte[] tampered = (byte[])protectedSecret.Clone();
            tampered[tampered.Length - 1] ^= 1;

            Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(protectedSecret, Guid.NewGuid()));
            Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tampered, adminId));
            Assert.ThrowsAny<CryptographicException>(() =>
                AdminSecretProtector.FromBase64(AdminTestSupport.CreateKeyBase64()).Unprotect(protectedSecret, adminId));
        }

        [Theory]
        [InlineData("")]
        [InlineData("not base64")]
        [InlineData("AAAA")]
        public void FromBase64_InvalidKey_ThrowsWithoutValue(string key)
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                AdminSecretProtector.FromBase64(key));

            Assert.Contains("Admin:TotpKeyBase64", error.Message);
            if (key.Length > 0)
            {
                Assert.DoesNotContain(key, error.Message);
            }
        }

        [Fact]
        public void NetworkPolicy_EmptyList_AllowsOnlyLoopback()
        {
            AdminNetworkPolicy policy = AdminNetworkPolicy.Parse(Array.Empty<string>());

            Assert.True(policy.IsAllowed(IPAddress.Loopback));
            Assert.True(policy.IsAllowed(IPAddress.IPv6Loopback));
            Assert.True(policy.IsAllowed(IPAddress.Loopback.MapToIPv6()));
            Assert.False(policy.IsAllowed(IPAddress.Parse("10.0.0.1")));
            Assert.False(policy.IsAllowed(null));
        }

        [Fact]
        public void NetworkPolicy_CidrAndAddress_AllowsOnlyListed()
        {
            AdminNetworkPolicy policy = AdminNetworkPolicy.Parse(new[] { "10.0.0.0/8", "192.0.2.7", "2001:db8::/32" });

            Assert.True(policy.IsAllowed(IPAddress.Parse("10.200.1.2")));
            Assert.True(policy.IsAllowed(IPAddress.Parse("10.200.1.2").MapToIPv6()));
            Assert.True(policy.IsAllowed(IPAddress.Parse("192.0.2.7")));
            Assert.True(policy.IsAllowed(IPAddress.Parse("2001:db8::1")));
            Assert.False(policy.IsAllowed(IPAddress.Parse("192.0.2.8")));
            Assert.False(policy.IsAllowed(IPAddress.Loopback));
        }

        [Theory]
        [InlineData("10.0.0.5/8")]
        [InlineData("not-an-ip")]
        [InlineData("10.0.0.0/33")]
        public void NetworkPolicy_InvalidEntry_FailsStartup(string value)
        {
            Assert.Throws<InvalidOperationException>(() => AdminNetworkPolicy.Parse(new[] { value }));
        }

        [Fact]
        public void RoleCodes_MatchServerEnum()
        {
            Assert.Equal(AdminContractCodes.Role.VIEWER, (int)EAdminRole.Viewer);
            Assert.Equal(AdminContractCodes.Role.OPERATOR, (int)EAdminRole.Operator);
            Assert.Equal(AdminContractCodes.Role.ADMIN, (int)EAdminRole.Admin);
            Assert.Equal(AdminContractCodes.Role.OWNER, (int)EAdminRole.Owner);
            Assert.Equal(AdminContractCodes.MAX_REASON_LENGTH, AdminAuditEntry.MAX_REASON_LENGTH);
        }
    }
}
