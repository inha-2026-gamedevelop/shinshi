// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 어드민 서비스 상태 조회(Viewer). 값은 캐시가 아니라 DB 행을 그대로 읽은 것이다 </summary>
    [DataContract]
    public sealed class AdminServiceStateResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        // source 값. Configuration이면 이 API 인스턴스는 연결 문자열이 없어 DB 행 대신 ServiceState 설정을 공개한다
        public const string SOURCE_DATABASE      = "Database";
        public const string SOURCE_CONFIGURATION = "Configuration";

        [DataMember(Name = "maintenance")]      private bool     _maintenance;
        [DataMember(Name = "disabledFeatures")] private string[] _disabledFeatures;
        [DataMember(Name = "allowedBuildIds")]  private string[] _allowedBuildIds;

        // 저장할 때 expectedRevision으로 그대로 보낸다
        [DataMember(Name = "revision")]         private long     _revision;
        [DataMember(Name = "updatedAtUnixMs")]  private long     _updatedAtUnixMs;

        // 마지막으로 저장한 운영자. 마이그레이션 초기 행이면 null
        [DataMember(Name = "updatedBy")]        private string   _updatedBy;
        [DataMember(Name = "source")]           private string   _source;

        // 저장에서 받는 기능 키 전체(ServiceFeatureKeys)
        [DataMember(Name = "knownFeatures")]    private string[] _knownFeatures;

        /****************************************
        *              Properties
        ****************************************/

        public bool     Maintenance      => _maintenance;
        public string[] DisabledFeatures => _disabledFeatures;
        public string[] AllowedBuildIds  => _allowedBuildIds;
        public long     Revision         => _revision;
        public long     UpdatedAtUnixMs  => _updatedAtUnixMs;
        public string   UpdatedBy        => _updatedBy;
        public string   Source           => _source;
        public string[] KnownFeatures    => _knownFeatures;

        /****************************************
        *                Methods
        ****************************************/

        public AdminServiceStateResponse(bool maintenance, string[] disabledFeatures, string[] allowedBuildIds,
            long revision, long updatedAtUnixMs, string updatedBy, string source, string[] knownFeatures)
        {
            _maintenance      = maintenance;
            _disabledFeatures = disabledFeatures;
            _allowedBuildIds  = allowedBuildIds;
            _revision         = revision;
            _updatedAtUnixMs  = updatedAtUnixMs;
            _updatedBy        = updatedBy;
            _source           = source;
            _knownFeatures    = knownFeatures;
        }

        private AdminServiceStateResponse() { }
    }
}
