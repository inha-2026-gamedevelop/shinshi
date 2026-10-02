// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 지급 초안 작성(Operator). 대상 우편함에는 execute가 넣는다. 영수증 ID가 새 지급 ID다 </summary>
    [DataContract]
    public sealed class AdminGrantCreateRequest : IAdminWriteRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]      private string             _operationId;
        [DataMember(Name = "reason")]           private string             _reason;

        // 계정 ID 1..1000개, 중복이 있으면 거절한다
        [DataMember(Name = "targetAccountIds")] private string[]           _targetAccountIds;
        [DataMember(Name = "rewards")]          private AdminGrantReward[] _rewards;

        // 수령 기한(일). 없으면 AdminGrantCodes.DEFAULT_CLAIM_DAYS
        [DataMember(Name = "claimDays")]        private int?               _claimDays;

        // AdminGrantCodes.SOURCE_*. 없으면 AdminGrant. RecoveryCompensation은 sourceRef(복구 세션 ID 등)가 필수다
        [DataMember(Name = "sourceKind")]       private string             _sourceKind;
        [DataMember(Name = "sourceRef")]        private string             _sourceRef;

        /****************************************
        *              Properties
        ****************************************/

        public string             OperationId      => _operationId;
        public string             Reason           => _reason;
        public string[]           TargetAccountIds => _targetAccountIds;
        public AdminGrantReward[] Rewards          => _rewards;
        public int?               ClaimDays        => _claimDays;
        public string             SourceKind       => _sourceKind;
        public string             SourceRef        => _sourceRef;

        /****************************************
        *                Methods
        ****************************************/

        public AdminGrantCreateRequest(string operationId, string reason, string[] targetAccountIds,
            AdminGrantReward[] rewards, int? claimDays, string sourceKind, string sourceRef)
        {
            _operationId      = operationId;
            _reason           = reason;
            _targetAccountIds = targetAccountIds;
            _rewards          = rewards;
            _claimDays        = claimDays;
            _sourceKind       = sourceKind;
            _sourceRef        = sourceRef;
        }

        private AdminGrantCreateRequest() { }
    }
}
