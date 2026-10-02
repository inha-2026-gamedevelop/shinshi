// System
using System;
using System.Globalization;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

// Project
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Backend
{
    /// <summary> 저장하지 않은 일시 실패를 503, reasonCode, Retry-After로 바꾼다. 호출자는 같은 operationId로 다시 보낸다.
    /// 생존 판정 보류와 DB 경합, 제한 초과, 연결 실패만 다루고 그 밖의 예외는 그대로 둔다 </summary>
    public sealed class BackendUnavailableExceptionFilter : IExceptionFilter
    {
        /****************************************
        *                Fields
        ****************************************/

        // DB 경합과 제한 초과는 API 재시도 계약(1/2/4초)의 첫 간격으로 다시 보낸다
        public const int DATABASE_RETRY_AFTER_SECONDS = 1;

        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public BackendUnavailableExceptionFilter(TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            _timeProvider = timeProvider;
        }

        public void OnException(ExceptionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            Exception exception = context.Exception;
            int reasonCode;
            int retryAfterSeconds;
            if (exception is TransientRejectionException transient)
            {
                reasonCode        = transient.ReasonCode;
                retryAfterSeconds = transient.RetryAfterSeconds;
            }
            else if (PostgresTransactionPolicy.IsRetryable(exception) || PostgresTransactionPolicy.IsConnectionFailure(exception))
            {
                reasonCode        = ContractCodes.RejectReason.BACKEND_UNAVAILABLE;
                retryAfterSeconds = DATABASE_RETRY_AFTER_SECONDS;
            }
            else
            {
                return;
            }

            // 예외 메시지에는 연결 정보가 섞일 수 있어 응답과 로그에 싣지 않는다
            context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.Result = ApiResponseFactory.Reject(StatusCodes.Status503ServiceUnavailable, reasonCode, string.Empty,
                _timeProvider);
            context.ExceptionHandled = true;
        }
    }
}
