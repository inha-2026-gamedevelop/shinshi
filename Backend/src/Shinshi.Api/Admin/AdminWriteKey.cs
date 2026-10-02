// System
using System;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 검증을 마친 어드민 쓰기 요청의 operationId와 reason </summary>
    public sealed class AdminWriteKey
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid OperationId { get; }
        public string Reason { get; }

        /****************************************
        *                Methods
        ****************************************/

        private AdminWriteKey(Guid operationId, string reason)
        {
            OperationId = operationId;
            Reason      = reason;
        }

        /// <summary> 본문이 없거나 operationId가 Guid가 아니거나 reason이 비었거나 500자를 넘으면 AdminRequestException이다 </summary>
        public static AdminWriteKey Read(IAdminWriteRequest? request)
        {
            if (request == null)
            {
                throw new AdminRequestException("Admin write request body is required.");
            }
            if ((!Guid.TryParse(request.OperationId, out Guid operationId)) || (operationId == Guid.Empty))
            {
                throw new AdminRequestException("operationId must be a non-empty GUID.");
            }
            if (string.IsNullOrWhiteSpace(request.Reason) || (request.Reason.Length > AdminContractCodes.MAX_REASON_LENGTH))
            {
                throw new AdminRequestException("reason must be a non-empty string of at most 500 characters.");
            }

            return new AdminWriteKey(operationId, request.Reason);
        }
    }
}
