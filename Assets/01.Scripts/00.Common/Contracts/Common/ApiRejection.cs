// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> operationId가 없는 요청이 거절됐을 때의 응답 본문, 인증과 조회 요청이 쓴다 </summary>
    [DataContract]
    public sealed class ApiRejection
    {
        /****************************************
        *                Fields
        ****************************************/

        // ContractCodes.RejectReason
        [DataMember(Name = "reasonCode")]       private int    _reasonCode;

        // 버전 불일치를 로그와 대조하려고 거절 응답에 buildId를 남긴다
        // TODO(민성): 요청한 클라이언트의 buildId인지 서버가 허용하는 buildId인지 기획 미정
        [DataMember(Name = "buildId")]          private string _buildId;

        [DataMember(Name = "serverTimeUnixMs")] private long   _serverTimeUnixMs;

        // TODO(민성): 거절별 HTTP 상태 코드 대응은 기획 미정

        /****************************************
        *              Properties
        ****************************************/

        public int    ReasonCode       => _reasonCode;
        public string BuildId          => _buildId;
        public long   ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public ApiRejection(int reasonCode, string buildId, long serverTimeUnixMs)
        {
            _reasonCode       = reasonCode;
            _buildId          = buildId;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private ApiRejection() { }
    }
}
