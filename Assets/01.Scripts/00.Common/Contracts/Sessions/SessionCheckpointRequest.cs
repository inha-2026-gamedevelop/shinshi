// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 30초 주기 진단용 체크포인트, 경제 복구에는 쓰지 않는다 </summary>
    [DataContract]
    public sealed class SessionCheckpointRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "checkpointSeq")] private long   _checkpointSeq;

        // 이 상태에 반영된 마지막 이벤트 순번
        [DataMember(Name = "lastEventSeq")]  private long   _lastEventSeq;
        [DataMember(Name = "schemaVersion")] private int    _schemaVersion;
        [DataMember(Name = "stateBase64")]   private string _stateBase64;

        /****************************************
        *              Properties
        ****************************************/

        public long   CheckpointSeq => _checkpointSeq;
        public long   LastEventSeq  => _lastEventSeq;
        public int    SchemaVersion => _schemaVersion;
        public string StateBase64   => _stateBase64;

        /****************************************
        *                Methods
        ****************************************/

        public SessionCheckpointRequest(long checkpointSeq, long lastEventSeq, int schemaVersion, string stateBase64)
        {
            _checkpointSeq = checkpointSeq;
            _lastEventSeq  = lastEventSeq;
            _schemaVersion = schemaVersion;
            _stateBase64   = stateBase64;
        }

        private SessionCheckpointRequest() { }
    }
}
