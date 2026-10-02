// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 게임 서버가 쥐고 있는 계정 접속 임대. 만료 시각이 지나면 접속 중이 아니다 </summary>
    [DataContract]
    public sealed class AdminAccountLeaseView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "leaseId")]         private string _leaseId;
        [DataMember(Name = "instanceId")]      private string _instanceId;

        // ContractCodes.ServerRole
        [DataMember(Name = "role")]            private int    _role;
        [DataMember(Name = "expiresAtUnixMs")] private long   _expiresAtUnixMs;
        [DataMember(Name = "online")]          private bool   _online;

        /****************************************
        *              Properties
        ****************************************/

        public string LeaseId         => _leaseId;
        public string InstanceId      => _instanceId;
        public int    Role            => _role;
        public long   ExpiresAtUnixMs => _expiresAtUnixMs;
        public bool   Online          => _online;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAccountLeaseView(string leaseId, string instanceId, int role, long expiresAtUnixMs, bool online)
        {
            _leaseId         = leaseId;
            _instanceId      = instanceId;
            _role            = role;
            _expiresAtUnixMs = expiresAtUnixMs;
            _online          = online;
        }

        private AdminAccountLeaseView() { }
    }
}
