// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Http;
using Xunit;

// Project
using Shinshi.Backend.Sessions;
using Shinshi.Contracts;

namespace Shinshi.Backend.Tests.Sessions
{
    public sealed class ServerCallerAuthenticatorTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData(ContractCodes.ServerRole.TOWN)]
        [InlineData(ContractCodes.ServerRole.DUNGEON)]
        public async Task AuthenticateAsync_ValidLease_MapsStoredInstanceAndRole(int role)
        {
            IntegrationAuthRepository auth = new IntegrationAuthRepository();
            auth.Server = new Shinshi.Data.Auth.ServerAuthIdentity(auth.Server.InstanceId, auth.Server.LeaseId,
                auth.Server.ServiceId, role, long.MaxValue);
            DefaultHttpContext context = Context(auth);
            using CancellationTokenSource cancellation = new CancellationTokenSource();

            ServerCaller? caller = await new ServerCallerAuthenticator(auth).AuthenticateAsync(context, cancellation.Token);

            Assert.NotNull(caller);
            Assert.Equal(auth.Server.InstanceId, caller.InstanceId);
            Assert.Equal(role, caller.Role);
            Assert.Equal(IntegrationAuthRepository.TOKEN, auth.LastToken);
            Assert.Equal(cancellation.Token, auth.LastCancellation);
            Assert.Equal(1, auth.ServerCalls);
            Assert.Equal(0, auth.AccessCalls);
        }

        [Theory]
        [InlineData(ContractCodes.RejectReason.TOKEN_EXPIRED)]
        [InlineData(ContractCodes.RejectReason.TOKEN_REVOKED)]
        public async Task AuthenticateAsync_InvalidLease_ReturnsNoCaller(int reason)
        {
            IntegrationAuthRepository auth = new IntegrationAuthRepository { ServerReason = reason };
            Assert.Null(await new ServerCallerAuthenticator(auth).AuthenticateAsync(Context(auth), CancellationToken.None));
        }

        [Fact]
        public async Task AuthenticateAsync_InstanceHeaderMismatch_ReturnsNoCaller()
        {
            IntegrationAuthRepository auth = new IntegrationAuthRepository();
            DefaultHttpContext context = Context(auth);
            context.Request.Headers[JoinApiRoutes.SERVER_INSTANCE_HEADER] = Guid.NewGuid().ToString("D");
            Assert.Null(await new ServerCallerAuthenticator(auth).AuthenticateAsync(context, CancellationToken.None));
            Assert.Equal(1, auth.ServerCalls);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Basic abc")]
        [InlineData("Bearer malformed")]
        public async Task AuthenticateAsync_MalformedAuthorization_DoesNotQueryRepository(string header)
        {
            IntegrationAuthRepository auth = new IntegrationAuthRepository();
            DefaultHttpContext context = Context(auth);
            context.Request.Headers[ApiRoutes.AUTHORIZATION_HEADER] = header;
            Assert.Null(await new ServerCallerAuthenticator(auth).AuthenticateAsync(context, CancellationToken.None));
            Assert.Equal(0, auth.ServerCalls);
        }

        [Fact]
        public async Task AuthenticateAsync_DuplicateOrMissingHeaders_DoesNotQueryRepository()
        {
            IntegrationAuthRepository auth = new IntegrationAuthRepository();
            DefaultHttpContext context = Context(auth);
            context.Request.Headers.Remove(JoinApiRoutes.SERVER_INSTANCE_HEADER);
            Assert.Null(await new ServerCallerAuthenticator(auth).AuthenticateAsync(context, CancellationToken.None));
            context = Context(auth);
            context.Request.Headers.Append(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + IntegrationAuthRepository.TOKEN);
            Assert.Null(await new ServerCallerAuthenticator(auth).AuthenticateAsync(context, CancellationToken.None));
            Assert.Equal(0, auth.ServerCalls);
        }

        private static DefaultHttpContext Context(IntegrationAuthRepository auth)
        {
            DefaultHttpContext context = new DefaultHttpContext();
            context.Request.Headers[ApiRoutes.AUTHORIZATION_HEADER] = ApiRoutes.BEARER_PREFIX + IntegrationAuthRepository.TOKEN;
            context.Request.Headers[JoinApiRoutes.SERVER_INSTANCE_HEADER] = auth.Server.InstanceId.ToString("D");
            return context;
        }
    }
}