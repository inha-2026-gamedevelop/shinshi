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
    /// <summary> 진행 중 출정에서 끊긴 플레이어가 접근 토큰으로 복귀용 일회 입장권을 받는다 </summary>
    [ApiController]
    public sealed class RejoinTicketController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        // ApiRoutes.REJOIN_TICKET_FORMAT과 같은 경로의 ASP.NET Core 템플릿
        public const string ROUTE_TEMPLATE = "/v1/game-sessions/{sessionId}/rejoin-ticket";

        private readonly PostgresAuthRepository _repository;
        private readonly AccountRequestLimiter _accounts;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public RejoinTicketController(PostgresAuthRepository repository, AccountRequestLimiter accounts,
            TimeProvider timeProvider)
        {
            _repository   = repository;
            _accounts     = accounts;
            _timeProvider = timeProvider;
        }

        /// <summary> 요청자 계정이 그 출정의 Active 참가자일 때만 발급한다. 다시 부르면 이전 미교환 토큰은 폐기된다.
        /// IP별 자격 경로 한도와 계정별 1분 한도를 함께 받는다. 발급마다 계정 행을 잠그므로 반복 호출이 같은 계정의 다른 쓰기를 막지 않게 한다 </summary>
        [EnableRateLimiting(AuthFailureLimiter.CREDENTIAL_POLICY)]
        [HttpPost(ROUTE_TEMPLATE)]
        public async Task<IActionResult> Issue(string sessionId, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            PlayerAuthIdentity? player = AuthActors.GetPlayer(HttpContext);
            if (player == null)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status401Unauthorized,
                    ContractCodes.RejectReason.TOKEN_REVOKED, string.Empty, _timeProvider);
            }
            if (!Guid.TryParse(sessionId, out Guid session) || (session == Guid.Empty))
            {
                return ApiResponseFactory.Reject(StatusCodes.Status400BadRequest,
                    ContractCodes.RejectReason.SESSION_MISMATCH, string.Empty, _timeProvider);
            }
            if (!_accounts.TryAcquire(player.AccountId))
            {
                return AuthFailureLimiter.RejectTooMany(HttpContext, _timeProvider);
            }

            AuthValidationOutcome<JoinTicket> outcome = await _repository.IssueRejoinTicketAsync(player, session,
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
