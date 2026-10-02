// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 서버 풀 등록, heartbeat, 드레인 결과 </summary>
    [DataContract]
    public sealed class ServerInstanceResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "instanceId")]       private string _instanceId;

        // GameSessionCodes.InstanceState
        [DataMember(Name = "state")]            private int    _state;

        // ContractCodes.RejectReason, 0이면 승인
        [DataMember(Name = "reasonCode")]       private int    _reasonCode;
        [DataMember(Name = "serverTimeUnixMs")] private long   _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string InstanceId       => _instanceId;
        public int    State            => _state;
        public int    ReasonCode       => _reasonCode;
        public long   ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public ServerInstanceResponse(string instanceId, int state, int reasonCode, long serverTimeUnixMs)
        {
            _instanceId       = instanceId;
            _state            = state;
            _reasonCode       = reasonCode;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private ServerInstanceResponse() { }
    }
}
