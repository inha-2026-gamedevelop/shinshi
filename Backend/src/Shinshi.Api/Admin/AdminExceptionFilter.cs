// System
using System;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 어드민 요청 형식 오류를 400과 공유 거절 DTO로 바꾼다. 예외 메시지는 응답에 싣지 않는다 </summary>
    public sealed class AdminExceptionFilter : IExceptionFilter
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public AdminExceptionFilter(TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            _timeProvider = timeProvider;
        }

        public void OnException(ExceptionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context.Exception is not AdminRequestException)
            {
                return;
            }

            context.HttpContext.Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            context.Result = ApiResponseFactory.Reject(StatusCodes.Status400BadRequest,
                AdminContractCodes.RejectReason.INVALID_REQUEST, string.Empty, _timeProvider);
            context.ExceptionHandled = true;
        }
    }
}
