// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Auth;

namespace Shinshi.Backend
{
    [ApiController]
    [ServiceFilter(typeof(AuthInvalidModelStateFilter), Order = AuthInvalidModelStateFilter.FILTER_ORDER)]
    public sealed class AuthExchangeController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IAuthRepository _repository;
        private readonly AuthBuildPolicy _buildPolicy;
        private readonly TimeProvider _timeProvider;
        private readonly AuthFailureLimiter _failures;

        /****************************************
        *                Methods
        ****************************************/

        public AuthExchangeController(IAuthRepository repository, AuthBuildPolicy buildPolicy,
            TimeProvider timeProvider, AuthFailureLimiter failures)
        {
            _repository   = repository;
            _buildPolicy  = buildPolicy;
            _timeProvider = timeProvider;
            _failures     = failures;
        }

        /// <summary> 초대와 갱신 중 하나만 허용하며 버전 거절에서 인증 자격을 소비하지 않는다 </summary>
        [AllowAnonymous]
        [EnableRateLimiting(AuthFailureLimiter.CREDENTIAL_POLICY)]
        [HttpPost(ApiRoutes.AUTH_EXCHANGE)]
        public async Task<ActionResult<AuthExchangeResponse>> Exchange([FromBody] AuthExchangeRequest? request,
            CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            if (_failures.IsBlocked(HttpContext))
            {
                return AuthFailureLimiter.RejectTooMany(HttpContext, _timeProvider);
            }

            string buildId = request?.Build?.BuildId ?? string.Empty;
            bool invite = !string.IsNullOrEmpty(request?.InviteCode);
            bool refresh = !string.IsNullOrEmpty(request?.RefreshToken);
            if ((request == null) || (invite == refresh))
            {
                return ApiResponseFactory.Reject(StatusCodes.Status400BadRequest,
                    ContractCodes.RejectReason.INVALID_INVITE_CODE, buildId, _timeProvider);
            }
            int reason = _buildPolicy.Validate(request.Build);
            if (reason != ContractCodes.RejectReason.NONE)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status403Forbidden, reason, buildId, _timeProvider);
            }

            AuthExchangeOutcome outcome = invite
                ? await _repository.ExchangeInviteAsync(request.InviteCode, cancellationToken)
                : await _repository.RotateRefreshAsync(request.RefreshToken, cancellationToken);
            if (!outcome.IsSuccess)
            {
                // 우리 키로 만든 갱신 토큰의 만료, 재사용 폐기는 정상 클라이언트에게도 생긴다. 태그가 틀린 자격만 무차별 대입으로 센다
                string credential = invite ? request.InviteCode : request.RefreshToken;
                if (!_repository.IsAuthenticToken(credential))
                {
                    _failures.RecordFailure(HttpContext);
                }
                return ApiResponseFactory.Reject(StatusCodes.Status401Unauthorized,
                    outcome.ReasonCode, buildId, _timeProvider);
            }

            return Ok(outcome.Response);
        }
    }
}

