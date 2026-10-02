// System
using System.Runtime.Serialization;

// Third Party
using Newtonsoft.Json.Linq;

namespace Shinshi.Contracts.Admin
{
    /// <summary> session_events 한 줄. eventKind는 GameSessionCodes.EventKind, payload는 저장된 jsonb 객체 그대로다 </summary>
    [DataContract]
    public sealed class AdminSessionEvent
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "eventSeq")]          private long   _eventSeq;
        [DataMember(Name = "eventId")]           private string _eventId;
        [DataMember(Name = "eventKind")]         private int    _eventKind;
        [DataMember(Name = "participantId")]     private string _participantId;
        [DataMember(Name = "payload")]           private JToken _payload;
        [DataMember(Name = "operationId")]       private string _operationId;
        [DataMember(Name = "committedAtUnixMs")] private long   _committedAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public long   EventSeq          => _eventSeq;
        public string EventId           => _eventId;
        public int    EventKind         => _eventKind;
        public string ParticipantId     => _participantId;
        public JToken Payload           => _payload;
        public string OperationId       => _operationId;
        public long   CommittedAtUnixMs => _committedAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AdminSessionEvent(long eventSeq, string eventId, int eventKind, string participantId, JToken payload,
            string operationId, long committedAtUnixMs)
        {
            _eventSeq          = eventSeq;
            _eventId           = eventId;
            _eventKind         = eventKind;
            _participantId     = participantId;
            _payload           = payload;
            _operationId       = operationId;
            _committedAtUnixMs = committedAtUnixMs;
        }

        private AdminSessionEvent() { }
    }
}
