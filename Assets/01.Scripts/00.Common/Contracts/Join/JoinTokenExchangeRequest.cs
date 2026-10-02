// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 게임 서버가 클라이언트에게 받은 일회용 접속 토큰을 참가자 정보로 바꿔 달라는 요청 </summary>
    [DataContract]
    public sealed class JoinTokenExchangeRequest
    {
        // 토큰 원문이 들어 있으므로 로그에 직렬화하지 않는다

        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "joinToken")]  private string _joinToken;

        // 토큰이 묶인 인스턴스와 다르면 API가 SessionMismatch로 거절하고 토큰을 소모하지 않는다
        [DataMember(Name = "instanceId")] private string _instanceId;

        // ContractCodes.ServerRole
        [DataMember(Name = "serverRole")] private int    _serverRole;

        /****************************************
        *              Properties
        ****************************************/

        public string JoinToken  => _joinToken;
        public string InstanceId => _instanceId;
        public int    ServerRole => _serverRole;

        /****************************************
        *                Methods
        ****************************************/

        public JoinTokenExchangeRequest(string joinToken, string instanceId, int serverRole)
        {
            _joinToken  = joinToken;
            _instanceId = instanceId;
            _serverRole = serverRole;
        }

        private JoinTokenExchangeRequest() { }
    }
}
