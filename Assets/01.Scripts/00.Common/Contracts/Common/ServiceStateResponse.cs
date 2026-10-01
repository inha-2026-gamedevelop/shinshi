// System
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 점검 여부, 공지, 기능 차단, 허용 buildId 스냅샷, API가 응답하지 않으면 실행을 막는다 </summary>
    [DataContract]
    public sealed class ServiceStateResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "maintenance")]      private bool     _maintenance;
        [DataMember(Name = "notice")]           private string   _notice;

        // TODO(민성): 기능 이름 목록은 기획 미정
        [DataMember(Name = "disabledFeatures")] private string[] _disabledFeatures;

        // 설치 buildId가 여기에 없으면 업데이트를 안내하고 실행을 막는다
        [DataMember(Name = "allowedBuildIds")]  private string[] _allowedBuildIds;

        [DataMember(Name = "serverTimeUnixMs")] private long     _serverTimeUnixMs;

        // TODO(민성): 세션 시작 때 고정할 운영 이벤트 스냅샷의 필드는 기획 미정

        /****************************************
        *              Properties
        ****************************************/

        public bool                  Maintenance      => _maintenance;
        public string                Notice           => _notice;
        public IReadOnlyList<string> DisabledFeatures => _disabledFeatures;
        public IReadOnlyList<string> AllowedBuildIds  => _allowedBuildIds;
        public long                  ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public ServiceStateResponse(bool maintenance, string notice, string[] disabledFeatures,
            string[] allowedBuildIds, long serverTimeUnixMs)
        {
            _maintenance      = maintenance;
            _notice           = notice;
            _disabledFeatures = disabledFeatures;
            _allowedBuildIds  = allowedBuildIds;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private ServiceStateResponse() { }
    }
}
