// System
using System;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data
{
    /// <summary> 최초 확정된 요청 해시와 응답. 재전송에서는 이 응답의 시각과 영수증도 보존한다 </summary>
    public sealed class StoredOperation
    {
        /****************************************
        *              Properties
        ****************************************/

        public string RequestHash { get; }
        public OperationResult Result { get; }

        /****************************************
        *                Methods
        ****************************************/

        public StoredOperation(string requestHash, OperationResult result)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
            ArgumentNullException.ThrowIfNull(result);
            RequestHash = requestHash;
            Result      = result;
        }
    }
}

