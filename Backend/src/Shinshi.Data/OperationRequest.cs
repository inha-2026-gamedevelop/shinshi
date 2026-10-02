// System
using System;

namespace Shinshi.Data
{
    /// <summary> 인증된 호출자가 정한 actor와 작업 식별자. HTTP 본문의 actor를 그대로 전달하지 않는다 </summary>
    public sealed class OperationRequest
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid ActorId { get; }
        public Guid OperationId { get; }
        public string OperationKind { get; }
        public string RequestHash { get; }

        /****************************************
        *                Methods
        ****************************************/

        public OperationRequest(Guid actorId, Guid operationId, string operationKind, string requestJson)
        {
            if (actorId == Guid.Empty)
            {
                throw new ArgumentException("Actor ID is required.", nameof(actorId));
            }

            if (operationId == Guid.Empty)
            {
                throw new ArgumentException("Operation ID is required.", nameof(operationId));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
            ArgumentNullException.ThrowIfNull(requestJson);

            ActorId       = actorId;
            OperationId   = operationId;
            OperationKind = operationKind;
            RequestHash   = OperationRequestHasher.ComputeHash(operationKind, requestJson);
        }
    }
}

