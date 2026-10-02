// System
using System.Runtime.Serialization;

// Third Party
using Newtonsoft.Json.Linq;

namespace Shinshi.Contracts.Admin
{
    /// <summary> outbox 한 줄. 전달 전이면 deliveredAtUnixMs가 null이다 </summary>
    [DataContract]
    public sealed class AdminOutboxEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "eventId")]             private string _eventId;
        [DataMember(Name = "aggregateId")]         private string _aggregateId;
        [DataMember(Name = "consumerKey")]         private string _consumerKey;
        [DataMember(Name = "eventKind")]           private string _eventKind;
        [DataMember(Name = "payload")]             private JToken _payload;
        [DataMember(Name = "createdAtUnixMs")]     private long   _createdAtUnixMs;
        [DataMember(Name = "deliveredAtUnixMs")]   private long?  _deliveredAtUnixMs;
        [DataMember(Name = "retries")]             private int    _retries;
        [DataMember(Name = "nextAttemptAtUnixMs")] private long   _nextAttemptAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string EventId             => _eventId;
        public string AggregateId         => _aggregateId;
        public string ConsumerKey         => _consumerKey;
        public string EventKind           => _eventKind;
        public JToken Payload             => _payload;
        public long   CreatedAtUnixMs     => _createdAtUnixMs;
        public long?  DeliveredAtUnixMs   => _deliveredAtUnixMs;
        public int    Retries             => _retries;
        public long   NextAttemptAtUnixMs => _nextAttemptAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AdminOutboxEntry(string eventId, string aggregateId, string consumerKey, string eventKind, JToken payload,
            long createdAtUnixMs, long? deliveredAtUnixMs, int retries, long nextAttemptAtUnixMs)
        {
            _eventId             = eventId;
            _aggregateId         = aggregateId;
            _consumerKey         = consumerKey;
            _eventKind           = eventKind;
            _payload             = payload;
            _createdAtUnixMs     = createdAtUnixMs;
            _deliveredAtUnixMs   = deliveredAtUnixMs;
            _retries             = retries;
            _nextAttemptAtUnixMs = nextAttemptAtUnixMs;
        }

        private AdminOutboxEntry() { }
    }
}
