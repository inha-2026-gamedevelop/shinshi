// System
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Sessions
{
    /// <summary> 게임 서버 프로세스가 서비스 자격으로 서버 임대를 받거나 갱신한다. 서버 경로 중 임대 없이 부르는 유일한 경로다 </summary>
    public sealed class ServerLeaseController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ServerCredentialIssuer _issuer;
        private readonly AuthFailureLimiter _failures;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        // 서비스 자격 실패는 플레이어 인증 실패와 다른 한도에서 센다. 플레이어의 잘못된 토큰이 서버 임대 갱신을 막지 않는다
        public ServerLeaseController(ServerCredentialIssuer issuer,
            [FromKeyedServices(AuthFailureLimiter.SERVICE_CREDENTIAL_KEY)] AuthFailureLimiter failures,
            TimeProvider timeProvider)
        {
            _issuer       = issuer;
            _failures     = failures;
            _timeProvider = timeProvider;
        }

        // ApiController 자동 400을 쓰지 않는다. 형식 오류도 공유 거절 DTO로 돌려준다
        [AllowAnonymous]
        [EnableRateLimiting(AuthFailureLimiter.SERVER_LEASE_POLICY)]
        [HttpPost(GameServerRoutes.SERVER_LEASE)]
        public async Task<IActionResult> Lease([FromBody] ServerLeaseRequest? request, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            if (_failures.IsBlocked(HttpContext))
            {
                return AuthFailureLimiter.RejectTooMany(HttpContext, _timeProvider);
            }

            StringValues instanceHeaders = Request.Headers[JoinApiRoutes.SERVER_INSTANCE_HEADER];
            if ((instanceHeaders.Count != 1) || !Guid.TryParse(instanceHeaders[0], out Guid headerInstance)
                || (request == null) || !Guid.TryParse(request.InstanceId, out Guid instanceId)
                || !Guid.TryParse(request.BootId, out Guid bootId)
                || (instanceId == Guid.Empty) || (bootId == Guid.Empty) || (instanceId != headerInstance))
            {
                return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            // 서비스 자격 비교는 설정의 해시와 메모리에서만 한다. 틀린 자격은 DB를 열지 않는다
            StringValues authorization = Request.Headers[ApiRoutes.AUTHORIZATION_HEADER];
            string header = authorization.Count == 1 ? authorization[0] ?? string.Empty : string.Empty;
            string credential = header.StartsWith(ApiRoutes.BEARER_PREFIX, StringComparison.OrdinalIgnoreCase)
                ? header.Substring(ApiRoutes.BEARER_PREFIX.Length) : string.Empty;
            AuthValidationOutcome<IssuedAuthToken>? outcome = await _issuer.IssueAsync(instanceId, request.ServerRole,
                bootId, credential, cancellationToken);
            if (outcome == null)
            {
                _failures.RecordFailure(HttpContext);
                return Reject(StatusCodes.Status401Unauthorized, ContractCodes.RejectReason.TOKEN_REVOKED);
            }
            if (!outcome.IsSuccess && (outcome.ReasonCode == ContractCodes.RejectReason.BACKEND_UNAVAILABLE))
            {
                // 같은 인스턴스의 다른 boot가 아직 살아 있다. 저장한 것이 없으므로 잠시 뒤 같은 요청을 다시 보낸다
                Response.Headers.RetryAfter = ServerPoolTimings.LIVENESS_RETRY_AFTER_SECONDS.ToString(CultureInfo.InvariantCulture);
                return Reject(StatusCodes.Status503ServiceUnavailable, outcome.ReasonCode);
            }
            if (!outcome.IsSuccess)
            {
                // SESSION_TERMINAL: 장애 취소로 확정된 boot다. 프로세스를 새 bootId로 다시 시작해야 한다
                return Reject(outcome.ReasonCode == ContractCodes.RejectReason.SESSION_TERMINAL
                    ? StatusCodes.Status409Conflict : StatusCodes.Status403Forbidden, outcome.ReasonCode);
            }

            IssuedAuthToken issued = outcome.Identity!;
            return Ok(new ServerLeaseResponse(issued.Token, issued.ExpiresAtUnixMs, issued.ServerTimeUnixMs));
        }

        private ObjectResult Reject(int statusCode, int reasonCode)
        {
            return ApiResponseFactory.Reject(statusCode, reasonCode, string.Empty, _timeProvider);
        }
    }
}
