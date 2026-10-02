// System
using System;

namespace Shinshi.Data.Ledger
{
    /// <summary> event ID는 재전송에도 유지하며 consumer key는 동일 DB 소비자의 고정 이름이다 </summary>
    public sealed class OutboxMessage
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

        /****************************************
        *                Methods
        ****************************************/

        public OutboxMessage(Guid eventId, Guid aggregateId, string consumerKey, string eventKind, string payloadJson,
            Guid actorId, Guid operationId)
        {
            if ((eventId == Guid.Empty) || (aggregateId == Guid.Empty)
                || (actorId == Guid.Empty) || (operationId == Guid.Empty))
            {
                throw new ArgumentException("Event, aggregate, actor and operation IDs are required.");
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(eventKind);
            LedgerJson.RequireObject(payloadJson, nameof(payloadJson));

            EventId      = eventId;
            AggregateId  = aggregateId;
            ConsumerKey  = consumerKey;
            EventKind    = eventKind;
            PayloadJson  = payloadJson;
            ActorId      = actorId;
            OperationId  = operationId;
        }
    }
}
