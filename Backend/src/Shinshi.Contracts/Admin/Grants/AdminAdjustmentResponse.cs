// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 개인 조정 결과. 확정한 작업이면 재전송도 감사 기록에서 같은 값을 다시 읽어 준다 </summary>
    [DataContract]
    public sealed class AdminAdjustmentResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operation")]      private OperationResult _operation;
        [DataMember(Name = "requestedDelta")] private long            _requestedDelta;

        // 실제로 바뀐 양. 차감이면 음수이고 가용량이 없으면 0이다(원장 행도 없다)
        [DataMember(Name = "appliedDelta")]   private long            _appliedDelta;

        // 차감하지 못한 양(0 이상). 운영자가 따로 판단한다
        [DataMember(Name = "shortfall")]      private long            _shortfall;
        [DataMember(Name = "balanceAfter")]   private long            _balanceAfter;

        /****************************************
        *              Properties
        ****************************************/

        public OperationResult Operation      => _operation;
        public long            RequestedDelta => _requestedDelta;
        public long            AppliedDelta   => _appliedDelta;
        public long            Shortfall      => _shortfall;
        public long            BalanceAfter   => _balanceAfter;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAdjustmentResponse(OperationResult operation, long requestedDelta, long appliedDelta, long shortfall,
            long balanceAfter)
        {
            _operation      = operation;
            _requestedDelta = requestedDelta;
            _appliedDelta   = appliedDelta;
            _shortfall      = shortfall;
            _balanceAfter   = balanceAfter;
        }

        private AdminAdjustmentResponse() { }
    }
}
