// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 세션 배정 서버의 현재 등록 상태. bootId가 세션의 instanceBootId와 다르면 서버가 재시작된 것이다.
    /// assignedBootAlive는 세션의 boot가 Offline이 아니고 heartbeat 만료 전인지다 </summary>
    [DataContract]
    public sealed class AdminServerInstanceState
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "instanceId")]        private string _instanceId;
        [DataMember(Name = "bootId")]            private string _bootId;
        [DataMember(Name = "state")]             private string _state;
        [DataMember(Name = "heartbeatAtUnixMs")] private long   _heartbeatAtUnixMs;
        [DataMember(Name = "assignedBootAlive")] private bool   _assignedBootAlive;

        /****************************************
        *              Properties
        ****************************************/

        public string InstanceId        => _instanceId;
        public string BootId            => _bootId;
        public string State             => _state;
        public long   HeartbeatAtUnixMs => _heartbeatAtUnixMs;
        public bool   AssignedBootAlive => _assignedBootAlive;

        /****************************************
        *                Methods
        ****************************************/

        public AdminServerInstanceState(string instanceId, string bootId, string state, long heartbeatAtUnixMs,
            bool assignedBootAlive)
        {
            _instanceId        = instanceId;
            _bootId            = bootId;
            _state             = state;
            _heartbeatAtUnixMs = heartbeatAtUnixMs;
            _assignedBootAlive = assignedBootAlive;
        }

        private AdminServerInstanceState() { }
    }
}
