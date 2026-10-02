// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Sessions
{
    /// <summary> 인증된 마을 서버 요청에서만 예약 참가자의 짧은 입장권을 발급한다 </summary>
    public sealed class JoinTokenIssuer : IJoinTokenIssuer
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IAuthRepository _repository;
        private readonly IHttpContextAccessor _contexts;

        /****************************************
        *                Methods
        ****************************************/

        public JoinTokenIssuer(IAuthRepository repository, IHttpContextAccessor contexts)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(contexts);
            _repository = repository;
            _contexts   = contexts;
        }

        public async Task<JoinTicket> IssueDungeonTicketAsync(DungeonJoinGrant grant,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(grant);
            HttpContext? context = _contexts.HttpContext;
            ServerAuthIdentity? server = context == null ? null : AuthActors.GetServer(context);
            if ((server == null) || (server.Role != ContractCodes.ServerRole.TOWN))
            {
                throw new JoinTokenIssuanceException(ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            AuthValidationOutcome<IssuedAuthToken> outcome = await _repository.IssueDungeonJoinTokenAsync(
                server, grant, cancellationToken);
            if (!outcome.IsSuccess)
            {
                throw new JoinTokenIssuanceException(outcome.ReasonCode);
            }

            IssuedAuthToken issued = outcome.Identity!;
            return new JoinTicket(issued.Token, issued.ExpiresAtUnixMs, ContractCodes.ServerRole.DUNGEON,
                grant.InstanceId.ToString("D"), grant.Address, grant.Port, grant.CharacterId.ToString("D"),
                grant.ParticipantId.ToString("D"), issued.ServerTimeUnixMs);
        }
    }
}