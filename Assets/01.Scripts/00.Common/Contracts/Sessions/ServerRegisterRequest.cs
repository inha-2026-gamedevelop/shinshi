// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 게임 서버 프로세스의 풀 등록, 프로세스를 새로 띄울 때마다 새 bootId를 쓴다 </summary>
    [DataContract]
    public sealed class ServerRegisterRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "instanceId")]     private string _instanceId;

        // 같은 instanceId라도 bootId가 바뀌면 이전 프로세스의 출정은 복구 대상이 된다
        [DataMember(Name = "bootId")]         private string _bootId;

        // ContractCodes.ServerRole
        [DataMember(Name = "serverRole")]     private int    _serverRole;
        [DataMember(Name = "address")]        private string _address;
        [DataMember(Name = "port")]           private int    _port;
        [DataMember(Name = "buildId")]        private string _buildId;
        [DataMember(Name = "contentVersion")] private string _contentVersion;

        /****************************************
        *              Properties
        ****************************************/

        public string InstanceId     => _instanceId;
        public string BootId         => _bootId;
        public int    ServerRole     => _serverRole;
        public string Address        => _address;
        public int    Port           => _port;
        public string BuildId        => _buildId;
        public string ContentVersion => _contentVersion;

        /****************************************
        *                Methods
        ****************************************/

        public ServerRegisterRequest(string instanceId, string bootId, int serverRole, string address, int port,
            string buildId, string contentVersion)
        {
            _instanceId     = instanceId;
            _bootId         = bootId;
            _serverRole     = serverRole;
            _address        = address;
            _port           = port;
            _buildId        = buildId;
            _contentVersion = contentVersion;
        }

        private ServerRegisterRequest() { }
    }
}
