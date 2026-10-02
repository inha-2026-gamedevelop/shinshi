// System
using System.Runtime.Serialization;

// Third Party
using Newtonsoft.Json.Linq;

namespace Shinshi.Contracts.Admin
{
    /// <summary> item_history 한 줄. beforeState와 afterState는 저장된 jsonb 객체를 그대로 싣고 없으면 null이다 </summary>
    [DataContract]
    public sealed class AdminItemHistoryEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "itemId")]           private string _itemId;
        [DataMember(Name = "seq")]              private long   _seq;
        [DataMember(Name = "eventKind")]        private string _eventKind;
        [DataMember(Name = "beforeState")]      private JToken _beforeState;
        [DataMember(Name = "afterState")]       private JToken _afterState;
        [DataMember(Name = "ownerId")]          private string _ownerId;
        [DataMember(Name = "actorId")]          private string _actorId;
        [DataMember(Name = "operationId")]      private string _operationId;
        [DataMember(Name = "occurredAtUnixMs")] private long   _occurredAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string ItemId           => _itemId;
        public long   Seq              => _seq;
        public string EventKind        => _eventKind;
        public JToken BeforeState      => _beforeState;
        public JToken AfterState       => _afterState;
        public string OwnerId          => _ownerId;
        public string ActorId          => _actorId;
        public string OperationId      => _operationId;
        public long   OccurredAtUnixMs => _occurredAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AdminItemHistoryEntry(string itemId, long seq, string eventKind, JToken beforeState, JToken afterState,
            string ownerId, string actorId, string operationId, long occurredAtUnixMs)
        {
            _itemId           = itemId;
            _seq              = seq;
            _eventKind        = eventKind;
            _beforeState      = beforeState;
            _afterState       = afterState;
            _ownerId          = ownerId;
            _actorId          = actorId;
            _operationId      = operationId;
            _occurredAtUnixMs = occurredAtUnixMs;
        }

        private AdminItemHistoryEntry() { }
    }
}
