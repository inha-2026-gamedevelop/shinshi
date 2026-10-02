// System
using System;
using System.Globalization;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Contracts;

namespace Shinshi.Backend.Accounts
{
    /// <summary> 계정 경로의 거절 응답. 응답 본문은 공유 ApiRejection이며 입력 아이디, 비밀번호를 싣지 않는다 </summary>
    public static class AccountRejections
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MIN_RETRY_AFTER_SECONDS = 1;

        /****************************************
        *                Methods
        ****************************************/

        public static ObjectResult Reject(HttpContext context, int statusCode, int reasonCode, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Response.Headers.CacheControl = "no-store";
            return ApiResponseFactory.Reject(statusCode, reasonCode, string.Empty, timeProvider);
        }

        /// <summary> 429와 올림한 초 단위 Retry-After </summary>
        public static ObjectResult TooMany(HttpContext context, int reasonCode, TimeSpan retryAfter,
            TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(context);
            int seconds = Math.Max(MIN_RETRY_AFTER_SECONDS, (int)Math.Ceiling(retryAfter.TotalSeconds));
            context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            return Reject(context, StatusCodes.Status429TooManyRequests, reasonCode, timeProvider);
        }

        /// <summary> 비밀번호 확인 거절을 HTTP 응답으로 바꾼다. 틀린 비밀번호의 상태 코드는 경로마다 다르다(로그인 401, 변경 403) </summary>
        public static ObjectResult FromCheck(HttpContext context, PasswordCheckResult check, int invalidStatusCode,
            TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(check);
            switch (check.Status)
            {
                case EPasswordCheckStatus.Locked:
                    return TooMany(context, ContractCodes.RejectReason.ACCOUNT_LOCKED, check.RetryAfter, timeProvider);
                case EPasswordCheckStatus.Slowed:
                case EPasswordCheckStatus.Busy:
                    return TooMany(context, ContractCodes.RejectReason.RATE_LIMITED, check.RetryAfter, timeProvider);
                case EPasswordCheckStatus.InvalidCredentials:
                    return Reject(context, invalidStatusCode, ContractCodes.RejectReason.INVALID_CREDENTIALS, timeProvider);
                default:
                    throw new ArgumentOutOfRangeException(nameof(check), "A verified check is not a rejection.");
            }
        }
    }
}
