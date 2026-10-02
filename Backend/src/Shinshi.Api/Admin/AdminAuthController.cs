// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

// Project
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 운영자 로그인과 로그아웃. 로그인은 게임 자격 교환과 같은 IP별 요청 한도와 어드민 실패 한도를 함께 받는다 </summary>
    public sealed class AdminAuthController : AdminControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        // 인증 앱 코드 길이보다 길거나 이름 한도를 크게 넘는 입력은 해시하지 않고 거절한다
        private const int MAX_TOTP_LENGTH = 16;

        private readonly PostgresAdminSessionRepository _sessions;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAuthController(PostgresAdminSessionRepository sessions)
        {
            _sessions = sessions;
        }

        /// <summary> 실패는 원인(계정 없음, 비밀번호, TOTP, 재사용, 잠금, 비활성)과 무관하게 401 INVALID_CREDENTIALS 하나다 </summary>
        [AllowAdminAnonymous]
        [EnableRateLimiting(AuthFailureLimiter.CREDENTIAL_POLICY)]
        [HttpPost(AdminApiRoutes.AUTH_LOGIN)]
        public async Task<ActionResult<AdminLoginResponse>> Login([FromBody] AdminLoginRequest? request,
            CancellationToken cancellationToken)
        {
            AuthFailureLimiter failures = HttpContext.RequestServices.GetRequiredKeyedService<AuthFailureLimiter>(
                AdminServiceCollectionExtensions.FAILURE_LIMITER_KEY);
            if (failures.IsBlocked(HttpContext))
            {
                return AuthFailureLimiter.RejectTooMany(HttpContext,
                    HttpContext.RequestServices.GetRequiredService<TimeProvider>());
            }
            if ((request == null) || (request.LoginName == null) || (request.Password == null)
                || (request.TotpCode == null) || (request.LoginName.Length > AdminLoginNames.MAX_LENGTH)
                || (request.Password.Length > AdminPasswordHasher.MAX_PASSWORD_LENGTH)
                || (request.TotpCode.Length > MAX_TOTP_LENGTH))
            {
                return Reject(StatusCodes.Status400BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            }

            AdminLoginOutcome outcome = await _sessions.LoginAsync(request.LoginName, request.Password,
                request.TotpCode, RequestIpHash, cancellationToken);
            if (!outcome.IsSuccess)
            {
                failures.RecordFailure(HttpContext);
                return Reject(StatusCodes.Status401Unauthorized, AdminContractCodes.RejectReason.INVALID_CREDENTIALS);
            }

            AdminIdentity identity = outcome.Identity!;
            return Ok(new AdminLoginResponse(outcome.AccessToken, outcome.ExpiresAt.ToUnixTimeMilliseconds(),
                identity.AdminId.ToString("D"), (int)identity.Role));
        }

        /// <summary> 현재 어드민 세션만 폐기한다. 업무 변경이 아니어서 operationId와 reason을 받지 않는다 </summary>
        [HttpPost(AdminApiRoutes.AUTH_LOGOUT)]
        public async Task<IActionResult> Logout(CancellationToken cancellationToken)
        {
            await _sessions.LogoutAsync(CurrentAdmin, RequestIpHash, cancellationToken);
            return NoContent();
        }
    }
}
