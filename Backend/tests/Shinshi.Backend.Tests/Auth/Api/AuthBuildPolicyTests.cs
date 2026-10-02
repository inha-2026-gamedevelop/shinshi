// System
using System;

// Third Party
using Microsoft.Extensions.Options;
using Xunit;

// Project
using Shinshi.Backend;
using Shinshi.Contracts;

namespace Shinshi.Backend.Tests
{
    public sealed class AuthBuildPolicyTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int PROTOCOL = 7;
        private const string CONTENT = "content-hash";
        private const string BUILD = "allowed-build";

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData(PROTOCOL, CONTENT, BUILD, ContractCodes.RejectReason.NONE)]
        [InlineData(PROTOCOL + 1, CONTENT, BUILD, ContractCodes.RejectReason.VERSION_MISMATCH)]
        [InlineData(PROTOCOL, "other-hash", BUILD, ContractCodes.RejectReason.VERSION_MISMATCH)]
        [InlineData(PROTOCOL, CONTENT, "other-build", ContractCodes.RejectReason.BUILD_NOT_ALLOWED)]
        [InlineData(PROTOCOL, CONTENT, "", ContractCodes.RejectReason.BUILD_NOT_ALLOWED)]
        public void Validate_ClientBuild_ReturnsExistingReason(int protocol, string content,
            string build, int reason)
        {
            AuthBuildPolicy policy = CreatePolicy();
            Assert.Equal(reason, policy.Validate(new ClientBuildInfo(build, protocol, content)));
        }

        [Fact]
        public void Validate_MaintenanceOrUnsetDeployment_RefusesAuthentication()
        {
            ClientBuildInfo client = new ClientBuildInfo(BUILD, PROTOCOL, CONTENT);
            Assert.Equal(ContractCodes.RejectReason.MAINTENANCE, CreatePolicy(maintenance: true).Validate(client));
            Assert.Equal(ContractCodes.RejectReason.VERSION_MISMATCH, CreatePolicy(content: "").Validate(client));
            Assert.Equal(ContractCodes.RejectReason.VERSION_MISMATCH, CreatePolicy().Validate(null));
            Assert.Equal(ContractCodes.RejectReason.BUILD_NOT_ALLOWED,
                CreatePolicy(allowed: Array.Empty<string>()).Validate(client));
        }

        private static AuthBuildPolicy CreatePolicy(bool maintenance = false, string content = CONTENT,
            string[]? allowed = null)
        {
            return new AuthBuildPolicy(new FixedOptions<AuthBuildOptions>(new AuthBuildOptions
            {
                ProtocolVersion = PROTOCOL,
                ContentHash     = content
            }), new FixedOptions<ServiceStateOptions>(new ServiceStateOptions
            {
                Maintenance     = maintenance,
                AllowedBuildIds = allowed ?? new[] { BUILD }
            }));
        }
    }

    internal sealed class FixedOptions<T> : IOptionsMonitor<T>
    {
        /****************************************
        *              Properties
        ****************************************/

        public T CurrentValue { get; }

        /****************************************
        *                Methods
        ****************************************/

        public FixedOptions(T value) { CurrentValue = value; }
        public T Get(string? name) { return CurrentValue; }
        public IDisposable? OnChange(Action<T, string?> listener) { return null; }
    }
}
