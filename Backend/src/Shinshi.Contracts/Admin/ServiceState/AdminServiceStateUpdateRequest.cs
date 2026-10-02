// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 점검, 기능 차단, 허용 빌드를 한 번에 저장한다(Admin). 빠진 값을 기본값으로 저장하지 않도록 모든 필드가 필수다 </summary>
    [DataContract]
    public sealed class AdminServiceStateUpdateRequest : IAdminWriteRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 같은 요청 안의 허용 빌드 수와 buildId 길이 한도. DB CHECK가 64개까지 받는다
        public const int MAX_BUILD_IDS       = 64;
        public const int MAX_BUILD_ID_LENGTH = 128;

        [DataMember(Name = "operationId")]      private string   _operationId;
        [DataMember(Name = "reason")]           private string   _reason;
        [DataMember(Name = "maintenance")]      private bool?    _maintenance;

        // ServiceFeatureKeys 값만 받는다. 빈 배열이면 모든 기능을 연다
        [DataMember(Name = "disabledFeatures")] private string[] _disabledFeatures;

        // 빈 배열이면 어떤 빌드도 로그인할 수 없다
        [DataMember(Name = "allowedBuildIds")]  private string[] _allowedBuildIds;

        // 조회한 revision. 그 사이 다른 운영자가 저장했으면 INVALID_STATE로 거절한다
        [DataMember(Name = "expectedRevision")] private long?    _expectedRevision;

        /****************************************
        *              Properties
        ****************************************/

        public string   OperationId      => _operationId;
        public string   Reason           => _reason;
        public bool?    Maintenance      => _maintenance;
        public string[] DisabledFeatures => _disabledFeatures;
        public string[] AllowedBuildIds  => _allowedBuildIds;
        public long?    ExpectedRevision => _expectedRevision;

        /****************************************
        *                Methods
        ****************************************/

        public AdminServiceStateUpdateRequest(string operationId, string reason, bool? maintenance,
            string[] disabledFeatures, string[] allowedBuildIds, long? expectedRevision)
        {
            _operationId      = operationId;
            _reason           = reason;
            _maintenance      = maintenance;
            _disabledFeatures = disabledFeatures;
            _allowedBuildIds  = allowedBuildIds;
            _expectedRevision = expectedRevision;
        }

        private AdminServiceStateUpdateRequest() { }
    }
}
