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
using Shinshi.Backend.Accounts;
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
        private readonly PasswordCredentialVerifier _passwords;

        /****************************************
        *                Methods
        ****************************************/

        public AuthExchangeController(IAuthRepository repository, AuthBuildPolicy buildPolicy,
            TimeProvider timeProvider, AuthFailureLimiter failures, PasswordCredentialVerifier passwords)
        {
            _repository   = repository;
            _buildPolicy  = buildPolicy;
            _timeProvider = timeProvider;
            _failures     = failures;
            _passwords    = passwords;
        }

        /// <summary> 초대, 갱신, 아이디와 비밀번호 중 정확히 하나만 허용하며 버전 거절에서 인증 자격을 소비하지 않는다 </summary>
        [AllowAnonymous]
        [EnableRateLimiting(AuthFailureLimiter.CREDENTIAL_POLICY)]
        [HttpPost(ApiRoutes.AUTH_EXCHANGE)]
        public async Task<ActionResult<AuthExchangeResponse>> Exchange([FromBody] AuthExchangeRequest? request,
            CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            bool credentials = !string.IsNullOrEmpty(request?.LoginId) || !string.IsNullOrEmpty(request?.Password);

            // 비밀번호 시도는 bearer 위조 실패 한도와 섞지 않고 PasswordLoginLimiter가 따로 센다
            if (!credentials && _failures.IsBlocked(HttpContext))
            {
                return AuthFailureLimiter.RejectTooMany(HttpContext, _timeProvider);
            }

            string buildId = request?.Build?.BuildId ?? string.Empty;
            bool invite = !string.IsNullOrEmpty(request?.InviteCode);
            bool refresh = !string.IsNullOrEmpty(request?.RefreshToken);
            int methods = (invite ? 1 : 0) + (refresh ? 1 : 0) + (credentials ? 1 : 0);
            if ((request == null) || (methods != 1))
            {
                return ApiResponseFactory.Reject(StatusCodes.Status400BadRequest,
                    ContractCodes.RejectReason.INVALID_INVITE_CODE, buildId, _timeProvider);
            }
            if (credentials)
            {
                return await ExchangeCredentialsAsync(request, buildId, cancellationToken);
            }
            // 갱신(자동 로그인)은 build가 없으면 점검만 본다. 게임에 들어가려면 게임 세션 발급의 빌드 검사를 따로 통과해야 하므로
            // 설치가 없거나 낡은 런처도 로그인은 유지한다. 초대 코드 교환은 첫 로그인이라 build를 계속 요구한다
            int reason = invite
                ? await _buildPolicy.ValidateAsync(request.Build, cancellationToken)
                : await _buildPolicy.ValidateOptionalBuildAsync(request.Build, cancellationToken);
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

        // build가 없으면(홈페이지) 빌드 검사를 건너뛴다. 실패는 아이디가 없든 비밀번호가 틀리든 같은 401 INVALID_CREDENTIALS다
        private async Task<ActionResult<AuthExchangeResponse>> ExchangeCredentialsAsync(AuthExchangeRequest request,
            string buildId, CancellationToken cancellationToken)
        {
            int reason = await _buildPolicy.ValidateOptionalBuildAsync(request.Build, cancellationToken);
            if (reason != ContractCodes.RejectReason.NONE)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status403Forbidden, reason, buildId, _timeProvider);
            }

            // 형식이 틀린 아이디는 존재할 수 없어 해시 없이 거절해도 존재 정보가 드러나지 않는다
            if (!AccountCredentialRules.TryNormalizeLoginId(request.LoginId, out string loginId))
            {
                return ApiResponseFactory.Reject(StatusCodes.Status401Unauthorized,
                    ContractCodes.RejectReason.INVALID_CREDENTIALS, buildId, _timeProvider);
            }

            PasswordCheckResult check = await _passwords.CheckAsync(loginId, AuthFailureLimiter.KeyOf(HttpContext),
                request.Password, token => _repository.FindCredentialByLoginIdAsync(loginId, token), cancellationToken);
            if (check.Status != EPasswordCheckStatus.Verified)
            {
                return AccountRejections.FromCheck(HttpContext, check, StatusCodes.Status401Unauthorized, _timeProvider);
            }

            AuthExchangeOutcome outcome = await _repository.IssuePasswordLoginAsync(check.Credential!.AccountId,
                check.Credential.PasswordHash, check.UpgradedPasswordHash, cancellationToken);
            if (!outcome.IsSuccess)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status401Unauthorized, outcome.ReasonCode, buildId,
                    _timeProvider);
            }

            return Ok(outcome.Response);
        }
    }
}

