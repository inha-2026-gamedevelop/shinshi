// System
using System;
using System.Linq;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

// Project
using Shinshi.Contracts;

namespace Shinshi.Backend
{
    /// <summary> 인증 JSON 바인딩 거절도 공유 DTO로 응답하고 입력 토큰을 오류 상세에 싣지 않는다 </summary>
    public sealed class AuthInvalidModelStateFilter : IActionFilter
    {
        /****************************************
        *                Fields
        ****************************************/

        // ApiController의 자동 ModelState 거절보다 먼저 실행해야 공유 응답 계약을 유지한다
        public const int FILTER_ORDER = -3000;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public AuthInvalidModelStateFilter(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context.ModelState.IsValid)
            {
                return;
            }

            context.HttpContext.Response.Headers.CacheControl = "no-store";
            bool server = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<RequireServerLeaseAttribute>() != null;
            bool townJoin = context.ActionDescriptor.Parameters.Any(parameter =>
                parameter.ParameterType == typeof(TownJoinTicketRequest));
            bool gameSession = context.ActionDescriptor.Parameters.Any(parameter =>
                parameter.ParameterType == typeof(GameSessionIssueRequest));
            int reason = (server || townJoin)
                ? ContractCodes.RejectReason.SESSION_MISMATCH : ContractCodes.RejectReason.INVALID_INVITE_CODE;
            // 게임 세션 발급 본문은 빌드 정보뿐이라 읽지 못하면 버전 거절로 돌려준다
            if (gameSession)
            {
                reason = ContractCodes.RejectReason.VERSION_MISMATCH;
            }
            string buildId = string.Empty;
            if (context.ActionArguments.TryGetValue("request", out object? request)
                && (request is AuthExchangeRequest exchange))
            {
                buildId = exchange.Build?.BuildId ?? string.Empty;
            }
            context.Result = ApiResponseFactory.Reject(StatusCodes.Status400BadRequest, reason, buildId, _timeProvider);
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}
