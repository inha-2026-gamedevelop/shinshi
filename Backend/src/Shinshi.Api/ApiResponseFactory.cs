// System
using System;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Contracts;

namespace Shinshi.Backend
{
    /// <summary> 확정 작업과 일반 거절을 공유 DTO로 응답한다 </summary>
    public static class ApiResponseFactory
    {
        /****************************************
        *                Methods
        ****************************************/

        public static ObjectResult FromOperation(OperationResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            int statusCode = result.Status switch
            {
                ContractCodes.OperationStatus.PENDING   => StatusCodes.Status202Accepted,
                ContractCodes.OperationStatus.COMMITTED => StatusCodes.Status200OK,
                ContractCodes.OperationStatus.REJECTED  => StatusCodes.Status409Conflict,
                _ => throw new ArgumentOutOfRangeException(nameof(result), "Unknown operation status.")
            };

            return new ObjectResult(result)
            {
                StatusCode = statusCode
            };
        }

        public static ObjectResult Reject(int statusCode, int reasonCode, string buildId, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);

            return new ObjectResult(new ApiRejection(reasonCode, buildId,
                timeProvider.GetUtcNow().ToUnixTimeMilliseconds()))
            {
                StatusCode = statusCode
            };
        }
    }
}
