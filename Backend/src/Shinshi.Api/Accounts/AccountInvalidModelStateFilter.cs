// System
using System;
using System.Linq;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

// Project
using Shinshi.Contracts;

namespace Shinshi.Backend.Accounts
{
    /// <summary> 계정, 캐릭터 본문을 읽지 못하면 공유 ApiRejection 400으로 답한다. 기본 ProblemDetails는 파서 오류에 입력 일부(비밀번호 포함)를 실을 수 있다 </summary>
    public sealed class AccountInvalidModelStateFilter : IActionFilter
    {
        /****************************************
        *                Fields
        ****************************************/

        // ApiController의 자동 ModelState 거절보다 먼저 실행한다
        public const int FILTER_ORDER = -3000;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public AccountInvalidModelStateFilter(TimeProvider timeProvider)
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
            bool character = context.ActionDescriptor.Parameters.Any(parameter =>
                parameter.ParameterType == typeof(CharacterCreateRequest));
            bool credentials = context.ActionDescriptor.Parameters.Any(parameter =>
                parameter.ParameterType == typeof(AccountCredentialsRequest));
            int reason = ContractCodes.RejectReason.INVALID_LOGIN_ID;
            if (character)
            {
                reason = ContractCodes.RejectReason.INVALID_CHARACTER_NAME;
            }
            else if (credentials)
            {
                reason = ContractCodes.RejectReason.WEAK_PASSWORD;
            }

            context.Result = ApiResponseFactory.Reject(StatusCodes.Status400BadRequest, reason, string.Empty,
                _timeProvider);
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}
