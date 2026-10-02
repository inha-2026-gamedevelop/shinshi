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

        // 지금 노출 중인 공지 전체(중요도, 시작 시각 내림차순). notice에는 그중 런처 대상 첫 공지의 제목이 들어간다.
        // 이 필드를 모르는 이전 응답과 생성자는 null이므로 키를 내보내지 않는다
        [DataMember(Name = "notices", EmitDefaultValue = false)] private ServiceNotice[] _notices;

        private static readonly ServiceNotice[] G_EmptyNotices = new ServiceNotice[0];

        // TODO(민성): 세션 시작 때 고정할 운영 이벤트 스냅샷의 필드는 기획 미정

        /****************************************
        *              Properties
        ****************************************/

        public bool                         Maintenance      => _maintenance;
        public string                       Notice           => _notice;
        public IReadOnlyList<string>        DisabledFeatures => _disabledFeatures;
        public IReadOnlyList<string>        AllowedBuildIds  => _allowedBuildIds;
        public long                         ServerTimeUnixMs => _serverTimeUnixMs;
        public IReadOnlyList<ServiceNotice> Notices          => _notices ?? G_EmptyNotices;

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

        public ServiceStateResponse(bool maintenance, string notice, string[] disabledFeatures,
            string[] allowedBuildIds, long serverTimeUnixMs, ServiceNotice[] notices)
            : this(maintenance, notice, disabledFeatures, allowedBuildIds, serverTimeUnixMs)
        {
            _notices = notices;
        }

        private ServiceStateResponse() { }
    }
}
