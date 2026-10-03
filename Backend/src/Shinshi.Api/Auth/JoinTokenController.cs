// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Auth;

namespace Shinshi.Backend
{
    [ApiController]
    [ServiceFilter(typeof(AuthInvalidModelStateFilter), Order = AuthInvalidModelStateFilter.FILTER_ORDER)]
    [RequireServerLease]
    public sealed class JoinTokenController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IAuthRepository _repository;
        private readonly TimeProvider _timeProvider;
        private readonly IOptions<ServiceStateOptions> _serviceState;

        /****************************************
        *                Methods
        ****************************************/

        public JoinTokenController(IAuthRepository repository, TimeProvider timeProvider,
            IOptions<ServiceStateOptions> serviceState)
        {
            _repository   = repository;
            _timeProvider = timeProvider;
            _serviceState = serviceState;
        }

        /// <summary> 서버 임대, 헤더와 본문 인스턴스를 대조하고 저장된 토큰 참가자만 한 번 교환한다 </summary>
        [HttpPost(JoinApiRoutes.JOIN_TOKEN_EXCHANGE)]
        public async Task<ActionResult<JoinTokenExchangeResponse>> Exchange([FromBody] JoinTokenExchangeRequest? request,
            CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            ServerAuthIdentity? server = AuthActors.GetServer(HttpContext);
            if (server == null)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status401Unauthorized,
                    ContractCodes.RejectReason.TOKEN_REVOKED, string.Empty, _timeProvider);
            }

            Microsoft.Extensions.Primitives.StringValues headers = Request.Headers[JoinApiRoutes.SERVER_INSTANCE_HEADER];
            if ((headers.Count != 1) || !Guid.TryParse(headers[0], out Guid headerInstance)
                || (headerInstance != server.InstanceId)
                || (request == null) || !Guid.TryParse(request.InstanceId, out Guid instanceId)
                || (instanceId != server.InstanceId) || (request.ServerRole != server.Role))
            {
                return ApiResponseFactory.Reject(StatusCodes.Status403Forbidden,
                    ContractCodes.RejectReason.SESSION_MISMATCH, string.Empty, _timeProvider);
            }
            if (_serviceState.Value.Maintenance)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status503ServiceUnavailable,
                    ContractCodes.RejectReason.MAINTENANCE, string.Empty, _timeProvider);
            }

            AuthValidationOutcome<JoinAuthIdentity> outcome = await _repository.ConsumeJoinTokenAsync(
                request.JoinToken, server.InstanceId, server.Role, server.LeaseId, cancellationToken);
            if (!outcome.IsSuccess)
            {
                int status = outcome.ReasonCode switch
                {
                    ContractCodes.RejectReason.TOKEN_EXPIRED or ContractCodes.RejectReason.TOKEN_REVOKED
                        => StatusCodes.Status401Unauthorized,
                    ContractCodes.RejectReason.SESSION_MISMATCH or ContractCodes.RejectReason.SANCTIONED
                        => StatusCodes.Status403Forbidden,
                    ContractCodes.RejectReason.MAINTENANCE => StatusCodes.Status503ServiceUnavailable,
                    _ => StatusCodes.Status409Conflict
                };
                return ApiResponseFactory.Reject(status, outcome.ReasonCode, string.Empty, _timeProvider);
            }

            // 마을 입장은 출정 참가자가 없으므로 participantId, sortieId를 값 없음(null)으로 보낸다.
            // 계정 접속 임대는 마을 입장 교환만 얻으므로 던전 입장의 accountLeaseId도 값 없음이다
            JoinAuthIdentity identity = outcome.Identity!;
            bool town = identity.ParticipantId == Guid.Empty;
            string? accountLeaseId = identity.AccountLeaseId == Guid.Empty ? null : identity.AccountLeaseId.ToString("D");
            return Ok(new JoinTokenExchangeResponse(identity.CharacterId.ToString("D"), identity.InstanceId.ToString("D"),
                server.Role, town ? null : identity.ParticipantId.ToString("D"),
                town ? null : identity.SessionId.ToString("D"), identity.ServerTimeUnixMs, accountLeaseId, identity.ClassId,
                identity.CharacterName));
        }
    }
}