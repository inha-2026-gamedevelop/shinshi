// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 지급 한 건의 상태와 우편 집계. 집계는 조회 시점의 DB 시각으로 만료를 나눈다 </summary>
    [DataContract]
    public sealed class AdminGrantDetail
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "grantId")]           private string             _grantId;

        // AdminGrantCodes.STATE_*, AdminGrantCodes.SOURCE_*
        [DataMember(Name = "state")]             private string             _state;
        [DataMember(Name = "sourceKind")]        private string             _sourceKind;
        [DataMember(Name = "sourceRef")]         private string             _sourceRef;
        [DataMember(Name = "reason")]            private string             _reason;
        [DataMember(Name = "createdBy")]         private string             _createdBy;
        [DataMember(Name = "createdAtUnixMs")]   private long               _createdAtUnixMs;
        [DataMember(Name = "executedAtUnixMs")]  private long?              _executedAtUnixMs;
        [DataMember(Name = "cancelledAtUnixMs")] private long?              _cancelledAtUnixMs;
        [DataMember(Name = "claimDays")]         private int                _claimDays;
        [DataMember(Name = "rewards")]           private AdminGrantReward[] _rewards;
        [DataMember(Name = "targetCount")]       private int                _targetCount;

        // delivered = claimed + open + expired + cancelled. 실행 전에는 모두 0이다
        [DataMember(Name = "deliveredCount")]    private int                _deliveredCount;
        [DataMember(Name = "claimedCount")]      private int                _claimedCount;
        [DataMember(Name = "openCount")]         private int                _openCount;
        [DataMember(Name = "expiredCount")]      private int                _expiredCount;
        [DataMember(Name = "cancelledCount")]    private int                _cancelledCount;

        /****************************************
        *              Properties
        ****************************************/

        public string             GrantId           => _grantId;
        public string             State             => _state;
        public string             SourceKind        => _sourceKind;
        public string             SourceRef         => _sourceRef;
        public string             Reason            => _reason;
        public string             CreatedBy         => _createdBy;
        public long               CreatedAtUnixMs   => _createdAtUnixMs;
        public long?              ExecutedAtUnixMs  => _executedAtUnixMs;
        public long?              CancelledAtUnixMs => _cancelledAtUnixMs;
        public int                ClaimDays         => _claimDays;
        public AdminGrantReward[] Rewards           => _rewards;
        public int                TargetCount       => _targetCount;
        public int                DeliveredCount    => _deliveredCount;
        public int                ClaimedCount      => _claimedCount;
        public int                OpenCount         => _openCount;
        public int                ExpiredCount      => _expiredCount;
        public int                CancelledCount    => _cancelledCount;

        /****************************************
        *                Methods
        ****************************************/

        public AdminGrantDetail(string grantId, string state, string sourceKind, string sourceRef, string reason,
            string createdBy, long createdAtUnixMs, long? executedAtUnixMs, long? cancelledAtUnixMs, int claimDays,
            AdminGrantReward[] rewards, int targetCount, int deliveredCount, int claimedCount, int openCount,
            int expiredCount, int cancelledCount)
        {
            _grantId           = grantId;
            _state             = state;
            _sourceKind        = sourceKind;
            _sourceRef         = sourceRef;
            _reason            = reason;
            _createdBy         = createdBy;
            _createdAtUnixMs   = createdAtUnixMs;
            _executedAtUnixMs  = executedAtUnixMs;
            _cancelledAtUnixMs = cancelledAtUnixMs;
            _claimDays         = claimDays;
            _rewards           = rewards;
            _targetCount       = targetCount;
            _deliveredCount    = deliveredCount;
            _claimedCount      = claimedCount;
            _openCount         = openCount;
            _expiredCount      = expiredCount;
            _cancelledCount    = cancelledCount;
        }

        private AdminGrantDetail() { }
    }
}
