// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Primitives;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Auth;

namespace Shinshi.Backend
{
    /// <summary> 런처가 게임 실행 직전에 받는 게임 전용 하위 세션의 발급, 회전, 폐기 </summary>
    [ApiController]
    [ServiceFilter(typeof(AuthInvalidModelStateFilter), Order = AuthInvalidModelStateFilter.FILTER_ORDER)]
    public sealed class GameSessionController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        // 저장소가 접근 토큰 수명(15분)과 부모 갱신 토큰 만료를 넘지 않게 다시 자른다
        public const int GAME_SESSION_MINUTES = 15;
        private static readonly TimeSpan _gameSessionLifetime = TimeSpan.FromMinutes(GAME_SESSION_MINUTES);

        private readonly IAuthRepository _repository;
        private readonly AuthBuildPolicy _buildPolicy;
        private readonly TimeProvider _timeProvider;
        private readonly AuthFailureLimiter _failures;

        /****************************************
        *                Methods
        ****************************************/

        public GameSessionController(IAuthRepository repository, AuthBuildPolicy buildPolicy,
            TimeProvider timeProvider, AuthFailureLimiter failures)
        {
            _repository   = repository;
            _buildPolicy  = buildPolicy;
            _timeProvider = timeProvider;
            _failures     = failures;
        }

        /// <summary> 런처의 접근 토큰으로만 발급한다. 게임 하위 세션이 자기 자신으로 새 하위 세션을 늘리지 못하게 막는다 </summary>
        [EnableRateLimiting(AuthFailureLimiter.CREDENTIAL_POLICY)]
        [HttpPost(ApiRoutes.AUTH_GAME_SESSION)]
        public async Task<IActionResult> Issue([FromBody] GameSessionIssueRequest? request,
            CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            string buildId = request?.Build?.BuildId ?? string.Empty;
            PlayerAuthIdentity? player = AuthActors.GetPlayer(HttpContext);
            if (player == null)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status401Unauthorized,
                    ContractCodes.RejectReason.TOKEN_REVOKED, buildId, _timeProvider);
            }
            if (AuthActors.GetPlayerTokenKind(HttpContext) != EPlayerTokenKind.Access)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status403Forbidden,
                    ContractCodes.RejectReason.SESSION_MISMATCH, buildId, _timeProvider);
            }
            if (request == null)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status400BadRequest,
                    ContractCodes.RejectReason.VERSION_MISMATCH, buildId, _timeProvider);
            }

            int reason = await _buildPolicy.ValidateAsync(request.Build, cancellationToken);
            if (reason != ContractCodes.RejectReason.NONE)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status403Forbidden, reason, buildId, _timeProvider);
            }

            AuthValidationOutcome<IssuedAuthToken> outcome = await _repository.IssueGameSessionForPlayerAsync(player,
                _gameSessionLifetime, cancellationToken);
            return ToResponse(player, outcome, buildId);
        }

        /// <summary> 게임 하위 세션 bearer로만 부른다. 이전 토큰은 같은 트랜잭션에서 폐기되며 동시 회전은 하나만 성공한다 </summary>
        [EnableRateLimiting(AuthFailureLimiter.CREDENTIAL_POLICY)]
        [HttpPost(ApiRoutes.AUTH_GAME_SESSION_REFRESH)]
        public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            PlayerAuthIdentity? player = AuthActors.GetPlayer(HttpContext);
            if ((player == null) || !TryReadBearer(HttpContext, out string token))
            {
                return ApiResponseFactory.Reject(StatusCodes.Status401Unauthorized,
                    ContractCodes.RejectReason.TOKEN_REVOKED, string.Empty, _timeProvider);
            }
            if (AuthActors.GetPlayerTokenKind(HttpContext) != EPlayerTokenKind.GameSession)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status403Forbidden,
                    ContractCodes.RejectReason.SESSION_MISMATCH, string.Empty, _timeProvider);
            }

            AuthValidationOutcome<IssuedAuthToken> outcome = await _repository.RotateGameSessionAsync(token,
                _gameSessionLifetime, cancellationToken);
            return ToResponse(player, outcome, string.Empty);
        }

        /// <summary> 런처가 전달에 실패한 하위 세션을 그 토큰으로 폐기한다. 이미 폐기, 만료된 하위 세션도 204라 재시도해도 같은 결과다.
        /// 폐기된 토큰도 받아야 하므로 미들웨어 검증 대신 여기서 bearer를 읽고 위조 bearer만 실패 한도에 센다 </summary>
        [AllowAnonymous]
        [HttpPost(ApiRoutes.AUTH_GAME_SESSION_REVOKE)]
        public async Task<IActionResult> Revoke(CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            if (_failures.IsBlocked(HttpContext))
            {
                return AuthFailureLimiter.RejectTooMany(HttpContext, _timeProvider);
            }
            if (!TryReadBearer(HttpContext, out string token) || !_repository.IsAuthenticToken(token))
            {
                _failures.RecordFailure(HttpContext);
                return RejectBearer(ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            // 접근 토큰 등 다른 용도의 우리 토큰은 하위 세션 표에 없으므로 폐기하지 않고 거절한다
            if (!await _repository.RevokeGameSessionAsync(token, cancellationToken))
            {
                return RejectBearer(ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            return NoContent();
        }

        private IActionResult ToResponse(PlayerAuthIdentity player, AuthValidationOutcome<IssuedAuthToken> outcome,
            string buildId)
        {
            if (!outcome.IsSuccess)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status401Unauthorized, outcome.ReasonCode, buildId,
                    _timeProvider);
            }

            IssuedAuthToken issued = outcome.Identity!;
            return Ok(new GameSessionIssueResponse(player.AccountId.ToString("D"), issued.Token,
                issued.ExpiresAtUnixMs, issued.ServerTimeUnixMs));
        }

        private IActionResult RejectBearer(int reasonCode)
        {
            Response.Headers.WWWAuthenticate = "Bearer";
            return ApiResponseFactory.Reject(StatusCodes.Status401Unauthorized, reasonCode, string.Empty, _timeProvider);
        }

        // 미들웨어와 같은 규칙으로 헤더 하나, Bearer 접두, 토큰 형식을 본다
        private static bool TryReadBearer(HttpContext context, out string token)
        {
            token = string.Empty;
            StringValues values = context.Request.Headers[ApiRoutes.AUTHORIZATION_HEADER];
            string header = values.Count == 1 ? values[0] ?? string.Empty : string.Empty;
            if (!header.StartsWith(ApiRoutes.BEARER_PREFIX, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string candidate = header.Substring(ApiRoutes.BEARER_PREFIX.Length);
            if (!AuthTokenHasher.IsWellFormed(candidate))
            {
                return false;
            }

            token = candidate;
            return true;
        }
    }
}
