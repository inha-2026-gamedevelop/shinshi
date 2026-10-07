// Third Party
using Microsoft.AspNetCore.Http;

namespace Shinshi.Admin.Web.Infrastructure
{
    /// <summary> API 진단 본문을 보관하거나 화면에 전달하지 않는다. Pending과 응답 유실은 성공이 아니다 </summary>
    public sealed class AdminWebApiResult<T>
    {
        /****************************************
        *              Properties
        ****************************************/

        public bool IsSuccess { get; }
        public bool IsPending { get; }
        public bool RequiresLogin => StatusCode == StatusCodes.Status401Unauthorized;
        public bool CanRetry { get; }
        public bool ResponseLost { get; }
        public int StatusCode { get; }
        public int? ReasonCode { get; }
        public T? Value { get; }
        public string ErrorMessage { get; }
        public string? OperationId { get; }

        /****************************************
        *                Methods
        ****************************************/

        internal AdminWebApiResult(int statusCode, T? value, string errorMessage = "", int? reasonCode = null,
            string? operationId = null, bool isSuccess = false, bool isPending = false, bool canRetry = false,
            bool responseLost = false)
        {
            StatusCode   = statusCode;
            Value        = value;
            ErrorMessage = errorMessage;
            ReasonCode   = reasonCode;
            OperationId  = operationId;
            IsSuccess    = isSuccess;
            IsPending    = isPending;
            CanRetry     = canRetry;
            ResponseLost = responseLost;
        }
    }
}
