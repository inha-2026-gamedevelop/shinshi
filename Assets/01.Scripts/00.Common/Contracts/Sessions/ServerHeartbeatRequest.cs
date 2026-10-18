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

        [DataMember(Name = "bootId")]      private string _bootId;
        // 마을 입장권 배정용 선택 보고, 없으면(null) 구버전 서버와 같이 정원을 제한하지 않는 것으로 본다
        [DataMember(Name = "connections")] private int?    _connections;
        [DataMember(Name = "capacity")]    private int?    _capacity;

        /****************************************
        *              Properties
        ****************************************/

        public string BootId      => _bootId;
        public int?   Connections => _connections;
        public int?   Capacity    => _capacity;

        /****************************************
        *                Methods
        ****************************************/

        public ServerHeartbeatRequest(string bootId)
        {
            _bootId = bootId;
        }

        public ServerHeartbeatRequest(string bootId, int? connections, int? capacity)
        {
            _bootId      = bootId;
            _connections = connections;
            _capacity    = capacity;
        }

        private ServerHeartbeatRequest() { }
    }
}
