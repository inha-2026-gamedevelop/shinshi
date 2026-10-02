// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 게임 서버 프로세스가 서비스 자격으로 서버 임대를 받는다, 인스턴스 행이 없으면 API가 만든다 </summary>
    [DataContract]
    public sealed class ServerLeaseRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "instanceId")] private string _instanceId;

        // ContractCodes.ServerRole
        [DataMember(Name = "serverRole")] private int    _serverRole;
        [DataMember(Name = "bootId")]     private string _bootId;

        /****************************************
        *              Properties
        ****************************************/

        public string InstanceId => _instanceId;
        public int    ServerRole => _serverRole;
        public string BootId     => _bootId;

        /****************************************
        *                Methods
        ****************************************/

        public ServerLeaseRequest(string instanceId, int serverRole, string bootId)
        {
            _instanceId = instanceId;
            _serverRole = serverRole;
            _bootId     = bootId;
        }

        private ServerLeaseRequest() { }
    }
}
