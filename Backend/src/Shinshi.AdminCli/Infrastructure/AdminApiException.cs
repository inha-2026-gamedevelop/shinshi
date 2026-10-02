// System
using System;

namespace Shinshi.AdminCli
{
    /// <summary> API가 2xx가 아닌 응답을 줬다. 본문은 ApiRejection이나 거절된 OperationResult이며 둘 다 reasonCode를 싣는다 </summary>
    internal sealed class AdminApiException : Exception
    {
        /****************************************
        *              Properties
        ****************************************/

        public int StatusCode { get; }

        // 본문이 없거나 JSON이 아니면 null. 허용 네트워크 밖의 404는 본문이 없다
        public int? ReasonCode { get; }

        // 거절된 쓰기면 그 operationId, 아니면 null
        public string? OperationId { get; }

        // --json 출력용 원본 본문. 없으면 빈 문자열
        public string RawBody { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminApiException(int statusCode, int? reasonCode, string? operationId, string rawBody)
            : base("Admin API returned HTTP " + statusCode + ".")
        {
            StatusCode  = statusCode;
            ReasonCode  = reasonCode;
            OperationId = operationId;
            RawBody     = rawBody;
        }
    }
}
