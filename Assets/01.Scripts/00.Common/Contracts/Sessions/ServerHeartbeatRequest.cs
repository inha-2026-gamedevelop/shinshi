// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 5초 주기 heartbeat, 15초 넘게 끊긴 뒤에는 되살리지 않고 새 bootId로 다시 등록한다 </summary>
    [DataContract]
    public sealed class ServerHeartbeatRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "bootId")] private string _bootId;

        /****************************************
        *              Properties
        ****************************************/

        public string BootId => _bootId;

        /****************************************
        *                Methods
        ****************************************/

        public ServerHeartbeatRequest(string bootId)
        {
            _bootId = bootId;
        }

        private ServerHeartbeatRequest() { }
    }
}
