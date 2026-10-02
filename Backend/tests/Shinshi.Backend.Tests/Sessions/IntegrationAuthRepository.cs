// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Sessions
{
    /// <summary> 서버 자격 어댑터와 조립 테스트에서 DB 검증 결과를 통제한다 </summary>
    internal sealed class IntegrationAuthRepository : IAuthRepository
    {
        /****************************************
        *              Properties
        ****************************************/

        public const string TOKEN = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        public ServerAuthIdentity Server { get; set; } = new ServerAuthIdentity(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), ContractCodes.ServerRole.TOWN, long.MaxValue);
        public int ServerReason { get; set; }
        public int ServerCalls { get; private set; }
        public int AccessCalls { get; private set; }
        public CancellationToken LastCancellation { get; private set; }
        public string LastToken { get; private set; } = string.Empty;

        /****************************************
        *                Methods
        ****************************************/

        public Task<AuthValidationOutcome<ServerAuthIdentity>> ValidateServerLeaseAsync(string token,
            CancellationToken cancellationToken = default)
        {
            ++ServerCalls;
            LastCancellation = cancellationToken;
            LastToken = token;
            return Task.FromResult(ServerReason == ContractCodes.RejectReason.NONE
                ? AuthValidationOutcome<ServerAuthIdentity>.Success(Server)
                : AuthValidationOutcome<ServerAuthIdentity>.Rejected(ServerReason));
        }

        public Task<AuthValidationOutcome<PlayerAuthIdentity>> ValidateAccessAsync(string token,
            CancellationToken cancellationToken = default)
        {
            ++AccessCalls;
            return Task.FromResult(AuthValidationOutcome<PlayerAuthIdentity>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED));
        }

        public Task<AuthExchangeOutcome> ExchangeInviteAsync(string token, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Unexpected invite exchange in a server guard test.");
        }

        public Task<AuthExchangeOutcome> RotateRefreshAsync(string token, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Unexpected refresh rotation in a server guard test.");
        }

        public Task<AuthValidationOutcome<JoinAuthIdentity>> ConsumeJoinTokenAsync(string token, Guid instanceId,
            int serverRole, Guid serverLeaseId, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Unexpected token consumption in a server guard test.");
        }

        public Task<AuthValidationOutcome<IssuedAuthToken>> IssueDungeonJoinTokenAsync(ServerAuthIdentity server,
            DungeonJoinGrant grant, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Unexpected token issuance in a server guard test.");
        }
    }
}