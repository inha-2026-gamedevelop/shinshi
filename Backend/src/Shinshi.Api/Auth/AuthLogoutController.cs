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
    /// <summary> 런처 로그아웃. 갱신 토큰 원문으로 로그인 묶음과 하위 게임 세션을 폐기한다 </summary>
    [ApiController]
    [ServiceFilter(typeof(AuthInvalidModelStateFilter), Order = AuthInvalidModelStateFilter.FILTER_ORDER)]
    public sealed class AuthLogoutController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        // 본문은 갱신 토큰 하나뿐이라 이 정도면 충분하고, 큰 본문은 읽기 전에 끊는다
        public const int MAX_BODY_BYTES = 1024;

        private readonly IAuthRepository _repository;
        private readonly TimeProvider _timeProvider;
        private readonly AuthFailureLimiter _failures;

        /****************************************
        *                Methods
        ****************************************/

        public AuthLogoutController(IAuthRepository repository, TimeProvider timeProvider, AuthFailureLimiter failures)
        {
            _repository   = repository;
            _timeProvider = timeProvider;
            _failures     = failures;
        }

        /// <summary> bearer 없이 부른다. 접근 토큰이 만료됐어도 로그아웃할 수 있어야 하므로 일반 bearer 검증을 거치지 않는다.
        /// 진짜 토큰이면 만료, 폐기 여부와 무관하게 묶음을 폐기하고, 못 찾아도 204라 토큰 존재 여부를 알려 주지 않는다.
        /// 태그가 틀린 토큰만 무차별 대입으로 보고 실패 한도에 센다 </summary>
        [AllowAnonymous]
        [EnableRateLimiting(AuthFailureLimiter.CREDENTIAL_POLICY)]
        [RequestSizeLimit(MAX_BODY_BYTES)]
        [HttpPost(ApiRoutes.AUTH_LOGOUT)]
        public async Task<IActionResult> Logout([FromBody] AuthLogoutRequest? request, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            if (_failures.IsBlocked(HttpContext))
            {
                return AuthFailureLimiter.RejectTooMany(HttpContext, _timeProvider);
            }

            string refreshToken = request?.RefreshToken ?? string.Empty;
            if (string.IsNullOrEmpty(refreshToken))
            {
                return ApiResponseFactory.Reject(StatusCodes.Status400BadRequest,
                    ContractCodes.RejectReason.TOKEN_REVOKED, string.Empty, _timeProvider);
            }

            if (!_repository.IsAuthenticToken(refreshToken))
            {
                _failures.RecordFailure(HttpContext);
                return NoContent();
            }

            await _repository.RevokeFamilyByRefreshTokenAsync(refreshToken, cancellationToken);
            return NoContent();
        }
    }
}
