// System
using System;
using System.Net;

// Third Party
using Xunit;

// Project
using Shinshi.Admin.Web.Infrastructure;

namespace Shinshi.Backend.Tests.AdminWeb
{
    public sealed class AdminWebNetworkPolicyTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData("127.0.0.1", true)]
        [InlineData("::1", true)]
        [InlineData("::ffff:127.0.0.1", true)]
        [InlineData("10.1.2.3", false)]
        [InlineData("203.0.113.5", false)]
        public void IsAllowed_EmptyConfiguration_AllowsOnlyLoopback(string address, bool allowed)
        {
            AdminWebNetworkPolicy policy = AdminWebNetworkPolicy.Parse(Array.Empty<string>());

            Assert.Equal(allowed, policy.IsAllowed(IPAddress.Parse(address)));
            Assert.False(policy.IsAllowed(null));
            Assert.False(policy.TrustsForwardedHeaders);
        }

        [Theory]
        [InlineData("10.1.2.3", true)]
        [InlineData("::ffff:10.1.2.3", true)]
        [InlineData("2001:db8::5", true)]
        [InlineData("192.0.2.8", true)]
        [InlineData("192.0.2.9", false)]
        [InlineData("127.0.0.1", false)]
        [InlineData("203.0.113.5", false)]
        public void IsAllowed_ExplicitNetworks_ReplacesLoopbackDefaultAndMatchesMappedIPv4(string address, bool allowed)
        {
            AdminWebNetworkPolicy policy = AdminWebNetworkPolicy.Parse(
                new[] { "10.0.0.0/8", "2001:db8::/32", "192.0.2.8" });

            Assert.Equal(allowed, policy.IsAllowed(IPAddress.Parse(address)));
        }

        [Theory]
        [InlineData("not-an-ip")]
        [InlineData("10.0.0.5/8")]
        [InlineData("2001:db8::5/32")]
        [InlineData("10.0.0.0/33")]
        [InlineData("")]
        public void Parse_MalformedNetworkOrHostBits_Throws(string network)
        {
            Assert.Throws<InvalidOperationException>(() => AdminWebNetworkPolicy.Parse(new[] { network }));
        }

        [Theory]
        [InlineData(new string[0], "127.0.0.1")]
        [InlineData(new string[0], "::1")]
        [InlineData(new[] { "10.0.0.0/8" }, "10.0.0.5")]
        [InlineData(new[] { "10.1.2.3" }, "::ffff:10.1.2.3")]
        public void Parse_TrustedProxyInsideAllowedNetworks_Throws(string[] allowed, string proxy)
        {
            Assert.Throws<InvalidOperationException>(() => AdminWebNetworkPolicy.Parse(allowed,
                new[] { IPAddress.Parse(proxy) }));
        }

        [Fact]
        public void Parse_TrustedProxyOutsideAllowedNetworks_DoesNotAllowProxyItself()
        {
            IPAddress proxy = IPAddress.Parse("192.168.10.5");
            AdminWebNetworkPolicy policy = AdminWebNetworkPolicy.Parse(new[] { "10.0.0.0/8" }, new[] { proxy });

            Assert.True(policy.TrustsForwardedHeaders);
            Assert.True(policy.IsAllowed(IPAddress.Parse("10.1.2.3")));
            Assert.False(policy.IsAllowed(proxy));
        }
    }
}