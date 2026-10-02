// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// Project
using Shinshi.Backend;
using Shinshi.Backend.Sessions;
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Auth
{
    public sealed class JoinTokenIssuerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int DUNGEON_PORT = 7780;
        private const int TOKEN_SECONDS = 60;
        private static readonly string _token = new string('A', 43);

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task IssueDungeonTicketAsync_WithoutServerIdentity_RejectsBeforeRepository()
        {
            FakeRepository repository = new FakeRepository();
            JoinTokenIssuer issuer = new JoinTokenIssuer(repository, new HttpContextAccessor());
            JoinTokenIssuanceException missing = await Assert.ThrowsAsync<JoinTokenIssuanceException>(
                () => issuer.IssueDungeonTicketAsync(Grant(), CancellationToken.None));
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED, missing.ReasonCode);
            Assert.Equal(0, repository.IssueCalls);
        }

        [Fact]
        public async Task IssueDungeonTicketAsync_PlayerIdentity_RejectsBeforeRepository()
        {
            FakeRepository repository = new FakeRepository();
            DungeonJoinGrant grant = Grant();
            JoinTokenIssuanceException denied = await Assert.ThrowsAsync<JoinTokenIssuanceException>(
                () => InvokeAuthenticatedAsync(repository, grant, player: true));
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED, denied.ReasonCode);
            Assert.Equal(0, repository.IssueCalls);
        }

        [Fact]
        public async Task IssueDungeonTicketAsync_ServerIdentity_DelegatesGrantAndMapsStoredToken()
        {
            FakeRepository repository = new FakeRepository();
            DungeonJoinGrant grant = Grant();
            JoinTicket ticket = await InvokeAuthenticatedAsync(repository, grant);
            Assert.Same(grant, repository.SeenGrant);
            Assert.Same(repository.Server, repository.SeenServer);
            Assert.Equal(repository.Issued.Token, ticket.JoinToken);
            Assert.Equal(repository.Issued.ExpiresAtUnixMs, ticket.ExpiresAtUnixMs);
            Assert.Equal(repository.Issued.ServerTimeUnixMs, ticket.ServerTimeUnixMs);
            Assert.Equal(ContractCodes.ServerRole.DUNGEON, ticket.ServerRole);
            Assert.Equal(grant.InstanceId.ToString("D"), ticket.InstanceId);
            Assert.Equal(grant.CharacterId.ToString("D"), ticket.CharacterId);
            Assert.Equal(grant.ParticipantId.ToString("D"), ticket.ParticipantId);
            Assert.Equal(grant.Address, ticket.Address);
            Assert.Equal(grant.Port, ticket.Port);
        }

        [Fact]
        public async Task IssueDungeonTicketAsync_NoActiveFamily_PropagatesTokenRevoked()
        {
            FakeRepository repository = new FakeRepository { ReasonCode = ContractCodes.RejectReason.TOKEN_REVOKED };
            JoinTokenIssuanceException revoked = await Assert.ThrowsAsync<JoinTokenIssuanceException>(
                () => InvokeAuthenticatedAsync(repository, Grant()));
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED, revoked.ReasonCode);
            Assert.Equal(1, repository.IssueCalls);
        }

        private static DungeonJoinGrant Grant()
        {
            return new DungeonJoinGrant(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "127.0.0.1", DUNGEON_PORT, TimeSpan.FromSeconds(TOKEN_SECONDS), DateTimeOffset.MaxValue);
        }

        private static async Task<JoinTicket> InvokeAuthenticatedAsync(FakeRepository repository, DungeonJoinGrant grant,
            bool player = false)
        {
            using ServiceProvider services = new ServiceCollection().AddSingleton<IAuthRepository>(repository)
                .AddSingleton<AuthFailureLimiter>().BuildServiceProvider();
            DefaultHttpContext context = new DefaultHttpContext { RequestServices = services };
            context.Request.Headers[ApiRoutes.AUTHORIZATION_HEADER] = ApiRoutes.BEARER_PREFIX + _token;
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, player
                ? new EndpointMetadataCollection() : new EndpointMetadataCollection(new RequireServerLeaseAttribute()), "test"));
            HttpContextAccessor contexts = new HttpContextAccessor { HttpContext = context };
            JoinTokenIssuer issuer = new JoinTokenIssuer(repository, contexts);
            JoinTicket? ticket = null;
            BearerTokenMiddleware middleware = new BearerTokenMiddleware(async _ =>
            {
                ticket = await issuer.IssueDungeonTicketAsync(grant, CancellationToken.None);
            }, TimeProvider.System);
            try
            {
                await middleware.InvokeAsync(context);
                return ticket!;
            }
            finally
            {
                contexts.HttpContext = null;
            }
        }

        private sealed class FakeRepository : IAuthRepository
        {
            public ServerAuthIdentity Server { get; } = new ServerAuthIdentity(Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), ContractCodes.ServerRole.TOWN, long.MaxValue);
            public IssuedAuthToken Issued { get; } = new IssuedAuthToken(_token, Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddSeconds(TOKEN_SECONDS).ToUnixTimeMilliseconds(),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            public ServerAuthIdentity? SeenServer { get; private set; }
            public DungeonJoinGrant? SeenGrant { get; private set; }
            public int IssueCalls { get; private set; }
            public int ReasonCode { get; set; }

            public Task<AuthValidationOutcome<IssuedAuthToken>> IssueDungeonJoinTokenAsync(ServerAuthIdentity server,
                DungeonJoinGrant grant, CancellationToken cancellationToken = default)
            {
                ++IssueCalls;
                SeenServer = server;
                SeenGrant  = grant;
                return Task.FromResult(ReasonCode == ContractCodes.RejectReason.NONE
                    ? AuthValidationOutcome<IssuedAuthToken>.Success(Issued)
                    : AuthValidationOutcome<IssuedAuthToken>.Rejected(ReasonCode));
            }

            public Task<AuthValidationOutcome<ServerAuthIdentity>> ValidateServerLeaseAsync(string serverLeaseToken,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(AuthValidationOutcome<ServerAuthIdentity>.Success(Server));
            }

            public Task<AuthValidationOutcome<PlayerAuthIdentity>> ValidateAccessAsync(string accessToken,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(AuthValidationOutcome<PlayerAuthIdentity>.Success(
                    new PlayerAuthIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())));
            }

            public Task<AuthExchangeOutcome> ExchangeInviteAsync(string inviteCode, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<AuthExchangeOutcome> RotateRefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<AuthValidationOutcome<JoinAuthIdentity>> ConsumeJoinTokenAsync(string joinToken, Guid instanceId,
                int serverRole, Guid serverLeaseId, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }
    }
}