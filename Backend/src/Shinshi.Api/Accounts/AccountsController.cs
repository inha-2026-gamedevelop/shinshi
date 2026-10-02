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
using Shinshi.Data.Accounts;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Accounts
{
    /// <summary> 공개 가입, 아이디 가용성, 내 계정 정보, 자격 변경. 비밀번호 원문은 로그, 예외, 응답 어디에도 싣지 않는다 </summary>
    [ApiController]
    [ServiceFilter(typeof(AccountInvalidModelStateFilter), Order = AccountInvalidModelStateFilter.FILTER_ORDER)]
    public sealed class AccountsController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IAccountRepository _accounts;
        private readonly AuthBuildPolicy _buildPolicy;
        private readonly PasswordCredentialVerifier _passwords;
        private readonly AccountSignupLimiter _signups;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public AccountsController(IAccountRepository accounts, AuthBuildPolicy buildPolicy,
            PasswordCredentialVerifier passwords, AccountSignupLimiter signups, TimeProvider timeProvider)
        {
            _accounts     = accounts;
            _buildPolicy  = buildPolicy;
            _passwords    = passwords;
            _signups      = signups;
            _timeProvider = timeProvider;
        }

        /// <summary> 201 가입, 400 형식(INVALID_LOGIN_ID, WEAK_PASSWORD, INVALID_EMAIL), 409 LOGIN_ID_TAKEN, 429 RATE_LIMITED,
        /// 503 MAINTENANCE. 미인증 이메일은 중복을 허용하므로 EMAIL_TAKEN을 돌려주지 않는다. 토큰은 주지 않는다 </summary>
        [AllowAnonymous]
        [EnableRateLimiting(AuthFailureLimiter.CREDENTIAL_POLICY)]
        [HttpPost(ApiRoutes.ACCOUNTS)]
        public async Task<IActionResult> Signup([FromBody] AccountSignupRequest? request,
            CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            if (await _buildPolicy.CheckMaintenanceAsync(cancellationToken) != ContractCodes.RejectReason.NONE)
            {
                return Reject(StatusCodes.Status503ServiceUnavailable, ContractCodes.RejectReason.MAINTENANCE);
            }
            if ((request == null) || !AccountCredentialRules.TryNormalizeLoginId(request.LoginId, out string loginId))
            {
                return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.INVALID_LOGIN_ID);
            }
            if (!AccountCredentialRules.IsAcceptablePassword(request.Password, loginId))
            {
                return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.WEAK_PASSWORD);
            }
            if (!AccountCredentialRules.TryNormalizeEmail(request.Email, out string email, out string emailKey))
            {
                return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.INVALID_EMAIL);
            }

            // 중복 아이디 시도도 한도에 센다. 가입 응답으로 아이디 존재를 대량 조회하지 못하게 한다
            if (!_signups.TryAcquire(AuthFailureLimiter.KeyOf(HttpContext), out TimeSpan retryAfter))
            {
                return AccountRejections.TooMany(HttpContext, ContractCodes.RejectReason.RATE_LIMITED, retryAfter,
                    _timeProvider);
            }

            // 해시 비용을 쓰기 전에 흔한 중복을 거른다. 조회와 저장 사이 경합은 저장의 유일 제약이 가른다
            if (await _accounts.IsLoginIdTakenAsync(loginId, cancellationToken))
            {
                return Reject(StatusCodes.Status409Conflict, ContractCodes.RejectReason.LOGIN_ID_TAKEN);
            }

            string? passwordHash = await _passwords.HashAsync(request.Password, cancellationToken);
            if (passwordHash == null)
            {
                return BusyRejection();
            }

            AccountSignupOutcome outcome = await _accounts.CreateAccountAsync(loginId, passwordHash, email, emailKey,
                cancellationToken);
            if (!outcome.IsSuccess)
            {
                return Reject(StatusCodes.Status409Conflict, outcome.ReasonCode);
            }

            return StatusCode(StatusCodes.Status201Created, new AccountSignupResponse(outcome.AccountId.ToString("D"),
                loginId, false, outcome.ServerTime.ToUnixTimeMilliseconds()));
        }

        /// <summary> 200과 AvailabilityResponse. 형식 오류는 available=false, INVALID_LOGIN_ID다. IP당 1분 30회 </summary>
        [AllowAnonymous]
        [EnableRateLimiting(AccountSignupLimiter.AVAILABILITY_POLICY)]
        [HttpGet(ApiRoutes.ACCOUNT_LOGIN_ID_AVAILABILITY)]
        public async Task<IActionResult> LoginIdAvailability([FromQuery] string? loginId,
            CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            if (!AccountCredentialRules.TryNormalizeLoginId(loginId, out string normalized))
            {
                return Ok(Availability(ContractCodes.RejectReason.INVALID_LOGIN_ID));
            }

            bool taken = await _accounts.IsLoginIdTakenAsync(normalized, cancellationToken);
            return Ok(Availability(taken ? ContractCodes.RejectReason.LOGIN_ID_TAKEN : ContractCodes.RejectReason.NONE));
        }

        /// <summary> 접근 토큰과 게임 하위 세션 모두 읽을 수 있다 </summary>
        [HttpGet(ApiRoutes.ACCOUNT_ME)]
        public async Task<IActionResult> Me(CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            PlayerAuthIdentity? player = AuthActors.GetPlayer(HttpContext);
            if (player == null)
            {
                return Reject(StatusCodes.Status401Unauthorized, ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            AccountProfileRecord? profile = await _accounts.GetProfileAsync(player.AccountId, cancellationToken);
            if (profile == null)
            {
                return Reject(StatusCodes.Status401Unauthorized, ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            return Ok(new AccountProfileResponse(profile.AccountId.ToString("D"), profile.LoginId, profile.Email,
                profile.EmailVerified, profile.CreatedAt.ToUnixTimeMilliseconds()));
        }

        /// <summary> 일반 변경은 currentPassword 재확인이 필수다. currentPassword 없는 변경은 초대(복구 코드)로 시작한 로그인 묶음에서
        /// 30분 안에만 되며, 아이디가 없는 계정은 이때 아이디와 이메일도 정한다.
        /// 204 성공(호출한 로그인 묶음을 포함해 계정의 모든 로그인, 게임 세션, 남은 초대를 폐기하므로 다시 로그인한다).
        /// 400 형식, 403 INVALID_CREDENTIALS(현재 비밀번호 틀림, 복구 조건 밖) 또는 SESSION_MISMATCH(게임 하위 세션),
        /// 409 LOGIN_ID_TAKEN이나 INVALID_CREDENTIALS(동시 변경), 429 ACCOUNT_LOCKED, RATE_LIMITED </summary>
        [EnableRateLimiting(AuthFailureLimiter.CREDENTIAL_POLICY)]
        [HttpPost(ApiRoutes.ACCOUNT_CREDENTIALS)]
        public async Task<IActionResult> Credentials([FromBody] AccountCredentialsRequest? request,
            CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            PlayerAuthIdentity? player = AuthActors.GetPlayer(HttpContext);
            if (player == null)
            {
                return Reject(StatusCodes.Status401Unauthorized, ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            // 게임 클라이언트에 넘긴 하위 세션으로는 자격을 바꾸지 못한다. 런처, 홈페이지의 로그인 토큰만 받는다
            if (AuthActors.GetPlayerTokenKind(HttpContext) != EPlayerTokenKind.Access)
            {
                return Reject(StatusCodes.Status403Forbidden, ContractCodes.RejectReason.SESSION_MISMATCH);
            }
            if (request == null)
            {
                return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.WEAK_PASSWORD);
            }

            AccountCredentialRecord? current = await _accounts.FindCredentialByAccountAsync(player.AccountId,
                cancellationToken);
            if (string.IsNullOrEmpty(request.CurrentPassword))
            {
                return await RecoverAsync(player, current, request, cancellationToken);
            }

            // 아이디가 없는 계정은 확인할 현재 비밀번호가 없다
            return current == null
                ? Reject(StatusCodes.Status403Forbidden, ContractCodes.RejectReason.INVALID_CREDENTIALS)
                : await ChangePasswordAsync(current, request, cancellationToken);
        }

        private async Task<IActionResult> ChangePasswordAsync(AccountCredentialRecord current,
            AccountCredentialsRequest request, CancellationToken cancellationToken)
        {
            // 이미 아이디가 있는 계정은 아이디, 이메일을 이 경로로 바꾸지 않는다
            if (!string.IsNullOrEmpty(request.LoginId))
            {
                return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.INVALID_LOGIN_ID);
            }
            if (!string.IsNullOrEmpty(request.Email))
            {
                return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.INVALID_EMAIL);
            }
            if (!AccountCredentialRules.IsAcceptablePassword(request.NewPassword, current.LoginId))
            {
                return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.WEAK_PASSWORD);
            }

            // 훔친 접근 토큰으로 현재 비밀번호를 대입하지 못하게 로그인과 같은 (아이디, IP) 제한을 쓴다
            PasswordCheckResult check = await _passwords.CheckAsync(current.LoginId,
                AuthFailureLimiter.KeyOf(HttpContext), request.CurrentPassword,
                _ => Task.FromResult<AccountCredentialRecord?>(current), cancellationToken);
            if (check.Status != EPasswordCheckStatus.Verified)
            {
                return AccountRejections.FromCheck(HttpContext, check, StatusCodes.Status403Forbidden, _timeProvider);
            }

            string? newHash = await _passwords.HashAsync(request.NewPassword, cancellationToken);
            if (newHash == null)
            {
                return BusyRejection();
            }

            ECredentialUpdateStatus status = await _accounts.ChangePasswordAsync(current.AccountId, current.PasswordHash,
                newHash, cancellationToken);
            return ToCredentialResult(status);
        }

        // 복구 코드 로그인 뒤의 재설정. 묶음의 시작 수단과 나이는 저장소가 같은 트랜잭션에서 DB 시각으로 판정한다
        private async Task<IActionResult> RecoverAsync(PlayerAuthIdentity player, AccountCredentialRecord? current,
            AccountCredentialsRequest request, CancellationToken cancellationToken)
        {
            string? loginId = null;
            string? email = null;
            string? emailKey = null;
            if (current == null)
            {
                if (!AccountCredentialRules.TryNormalizeLoginId(request.LoginId, out string normalized))
                {
                    return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.INVALID_LOGIN_ID);
                }
                loginId = normalized;
            }
            else if (!string.IsNullOrEmpty(request.LoginId))
            {
                // 이미 있는 아이디는 바꾸지 않는다
                return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.INVALID_LOGIN_ID);
            }

            if (!AccountCredentialRules.IsAcceptablePassword(request.NewPassword, loginId ?? current!.LoginId))
            {
                return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.WEAK_PASSWORD);
            }

            // 아이디가 없는 계정은 이메일이 필수이고, 있는 계정은 보냈을 때만 바꾼다
            if ((current == null) || !string.IsNullOrEmpty(request.Email))
            {
                if (!AccountCredentialRules.TryNormalizeEmail(request.Email, out string trimmed, out string key))
                {
                    return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.INVALID_EMAIL);
                }
                email = trimmed;
                emailKey = key;
            }

            if ((loginId != null) && await _accounts.IsLoginIdTakenAsync(loginId, cancellationToken))
            {
                return Reject(StatusCodes.Status409Conflict, ContractCodes.RejectReason.LOGIN_ID_TAKEN);
            }

            string? passwordHash = await _passwords.HashAsync(request.NewPassword, cancellationToken);
            if (passwordHash == null)
            {
                return BusyRejection();
            }

            ECredentialUpdateStatus status = await _accounts.RecoverCredentialsAsync(player.AccountId, player.FamilyId,
                loginId, passwordHash, email, emailKey, cancellationToken);
            return ToCredentialResult(status);
        }

        private IActionResult ToCredentialResult(ECredentialUpdateStatus status)
        {
            switch (status)
            {
                case ECredentialUpdateStatus.Updated:
                    return NoContent();
                case ECredentialUpdateStatus.LoginIdTaken:
                    return Reject(StatusCodes.Status409Conflict, ContractCodes.RejectReason.LOGIN_ID_TAKEN);
                case ECredentialUpdateStatus.RecoveryNotAllowed:
                    // 복구 코드로 시작한 30분 안의 묶음이 아니면 현재 비밀번호가 필요하다
                    return Reject(StatusCodes.Status403Forbidden, ContractCodes.RejectReason.INVALID_CREDENTIALS);
                default:
                    // 확인한 뒤 다른 요청이 먼저 자격을 바꿨다. 최신 자격으로 다시 시도해야 한다
                    return Reject(StatusCodes.Status409Conflict, ContractCodes.RejectReason.INVALID_CREDENTIALS);
            }
        }

        private AvailabilityResponse Availability(int reasonCode)
        {
            return new AvailabilityResponse(reasonCode == ContractCodes.RejectReason.NONE, reasonCode,
                _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
        }

        private ObjectResult BusyRejection()
        {
            return AccountRejections.TooMany(HttpContext, ContractCodes.RejectReason.RATE_LIMITED,
                TimeSpan.FromSeconds(PasswordHashGate.RETRY_AFTER_SECONDS), _timeProvider);
        }

        private ObjectResult Reject(int statusCode, int reasonCode)
        {
            return AccountRejections.Reject(HttpContext, statusCode, reasonCode, _timeProvider);
        }
    }
}
