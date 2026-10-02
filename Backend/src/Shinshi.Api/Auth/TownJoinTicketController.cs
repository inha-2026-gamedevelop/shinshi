// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Auth;

namespace Shinshi.Backend
{
    /// <summary> 접근 토큰의 주체가 소유한 캐릭터로 API가 선택한 마을에 들어갈 일회 입장권을 받는다 </summary>
    [ApiController]
    [ServiceFilter(typeof(AuthInvalidModelStateFilter), Order = AuthInvalidModelStateFilter.FILTER_ORDER)]
    public sealed class TownJoinTicketController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresAuthRepository _repository;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public TownJoinTicketController(PostgresAuthRepository repository, TimeProvider timeProvider)
        {
            _repository   = repository;
            _timeProvider = timeProvider;
        }

        /// <summary> 캐릭터 소유권과 로그인 묶음을 재검증하고 등록 boot, 임대, heartbeat가 살아 있는 마을을 고른다 </summary>
        [EnableRateLimiting(AuthFailureLimiter.CREDENTIAL_POLICY)]
        [HttpPost(ApiRoutes.TOWN_JOIN_TICKET)]
        public async Task<IActionResult> Issue([FromBody] TownJoinTicketRequest? request,
            CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            PlayerAuthIdentity? player = AuthActors.GetPlayer(HttpContext);
            if (player == null)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status401Unauthorized,
                    ContractCodes.RejectReason.TOKEN_REVOKED, string.Empty, _timeProvider);
            }
            if ((request == null) || (!Guid.TryParse(request.CharacterId, out Guid characterId))
                || (characterId == Guid.Empty))
            {
                return ApiResponseFactory.Reject(StatusCodes.Status400BadRequest,
                    ContractCodes.RejectReason.SESSION_MISMATCH, string.Empty, _timeProvider);
            }

            AuthValidationOutcome<JoinTicket> outcome = await _repository.IssueTownJoinTokenAsync(player, characterId,
                cancellationToken);
            if (outcome.IsSuccess)
            {
                return Ok(outcome.Identity);
            }

            int status = outcome.ReasonCode switch
            {
                ContractCodes.RejectReason.TOKEN_EXPIRED or ContractCodes.RejectReason.TOKEN_REVOKED
                    => StatusCodes.Status401Unauthorized,
                ContractCodes.RejectReason.SESSION_MISMATCH => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status409Conflict
            };
            return ApiResponseFactory.Reject(status, outcome.ReasonCode, string.Empty, _timeProvider);
        }
    }
}
