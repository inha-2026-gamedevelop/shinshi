// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 인증 요청에 싣는 설치 빌드 신원, 접속 서버와 protocolVersion, contentHash가 완전히 같아야 한다 </summary>
    [DataContract]
    public sealed class ClientBuildInfo
    {
        /****************************************
        *                Fields
        ****************************************/

        // 빌드마다 유일, 거절 응답과 로그에 같은 값을 남겨 대조한다
        [DataMember(Name = "buildId")]         private string _buildId;
        [DataMember(Name = "protocolVersion")] private int    _protocolVersion;
        [DataMember(Name = "contentHash")]     private string _contentHash;

        /****************************************
        *              Properties
        ****************************************/

        public string BuildId         => _buildId;
        public int    ProtocolVersion => _protocolVersion;
        public string ContentHash     => _contentHash;

        /****************************************
        *                Methods
        ****************************************/

        public ClientBuildInfo(string buildId, int protocolVersion, string contentHash)
        {
            _buildId         = buildId;
            _protocolVersion = protocolVersion;
            _contentHash     = contentHash;
        }

        private ClientBuildInfo() { }
    }
}
