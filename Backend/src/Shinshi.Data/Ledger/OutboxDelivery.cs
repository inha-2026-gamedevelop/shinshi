// System
using System;

namespace Shinshi.Data.Ledger
{
    /// <summary> 소비자는 event ID를 유지하고 전달 트랜잭션만 사용한다 </summary>
    public sealed class OutboxDelivery
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid EventId { get; }
        public Guid AggregateId { get; }
        public string ConsumerKey { get; }
        public string EventKind { get; }
        public string PayloadJson { get; }
        public Guid ActorId { get; }
        public Guid OperationId { get; }
        public int RetryCount { get; }

        /****************************************
        *                Methods
        ****************************************/

        internal OutboxDelivery(Guid eventId, Guid aggregateId, string consumerKey, string eventKind, string payloadJson,
            Guid actorId, Guid operationId, int retryCount)
        {
            EventId      = eventId;
            AggregateId  = aggregateId;
            ConsumerKey  = consumerKey;
            EventKind    = eventKind;
            PayloadJson  = payloadJson;
            ActorId      = actorId;
            OperationId  = operationId;
            RetryCount   = retryCount;
        }
    }
}
