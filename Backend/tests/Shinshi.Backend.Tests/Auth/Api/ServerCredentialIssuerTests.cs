// System
using System;
using System.Security.Cryptography;
using System.Text;

// Third Party
using Xunit;

// Project
using Shinshi.Backend;
using Shinshi.Contracts;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests
{
    public sealed class ServerCredentialIssuerTests
    {
        [Fact]
        public void Match_ValidCredential_UsesConfiguredServiceAndRole()
        {
            AuthTokenHasher hasher = new AuthTokenHasher(new AuthTokenOptions
            {
                HashKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            });
            string credential = hasher.CreateToken();
            ServiceCredentialRegistration binding = new ServiceCredentialRegistration
            {
                InstanceId = Guid.NewGuid(),
                ServiceId = Guid.NewGuid(),
                Role = ContractCodes.ServerRole.DUNGEON,
                CredentialHashBase64 = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(credential)))
            };
            ServiceCredentialOptions options = new ServiceCredentialOptions { Registrations = new[] { binding } };
            Assert.Same(binding, ServerCredentialIssuer.Match(options, binding.InstanceId, credential));
            Assert.Null(ServerCredentialIssuer.Match(options, Guid.NewGuid(), credential));
            Assert.Null(ServerCredentialIssuer.Match(options, binding.InstanceId, hasher.CreateToken()));
            Assert.Null(ServerCredentialIssuer.Match(options, binding.InstanceId, "invalid"));
        }

        [Fact]
        public void Match_DuplicateInstanceOrInvalidRole_RejectsConfiguration()
        {
            string credential = new string('A', 43);
            Guid instance = Guid.NewGuid();
            ServiceCredentialRegistration first = new ServiceCredentialRegistration
            {
                InstanceId = instance,
                ServiceId = Guid.NewGuid(),
                Role = ContractCodes.ServerRole.TOWN,
                CredentialHashBase64 = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(credential)))
            };
            Assert.Throws<InvalidOperationException>(() => ServerCredentialIssuer.Match(
                new ServiceCredentialOptions { Registrations = new[] { first, first } }, instance, credential));
            first.Role = int.MaxValue;
            Assert.Throws<InvalidOperationException>(() => ServerCredentialIssuer.Match(
                new ServiceCredentialOptions { Registrations = new[] { first } }, instance, credential));
        }
    }
}
