// System
using System;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 어드민 JSON 바인딩과 경로 ID 형식 오류를 공유 거절 DTO로 응답한다. 입력 값(비밀번호 등)을 오류 상세에 싣지 않는다 </summary>
    public sealed class AdminInvalidModelStateFilter : IActionFilter
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

        public AdminInvalidModelStateFilter(TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            _timeProvider = timeProvider;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context.ModelState.IsValid)
            {
                return;
            }

            context.HttpContext.Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            context.Result = ApiResponseFactory.Reject(StatusCodes.Status400BadRequest,
                AdminContractCodes.RejectReason.INVALID_REQUEST, string.Empty, _timeProvider);
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}
