// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 투표 값이 바뀐 틱에 참가자마다 보낸다. 받는 사람마다 자기 표와 거절 사유가 달라 브로드캐스트하지 않는다
    /// <summary> 보스전 토벌 중단 투표의 진행과 이 참가자의 표 </summary>
    public readonly struct SortieStopVoteStateMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ESortieStopVoteState  _state;
        private readonly byte                  _agreeCount;
        private readonly byte                  _disagreeCount;
        private readonly byte                  _requiredCount;
        private readonly byte                  _eligibleCount;
        private readonly ESortieStopVoteChoice _myChoice;
        private readonly float                 _remainingSeconds;
        private readonly ERejectReason         _reject;

        /****************************************
        *              Properties
        ****************************************/

        public ESortieStopVoteState  State            => _state;
        public byte                  AgreeCount       => _agreeCount;
        public byte                  DisagreeCount    => _disagreeCount;
        public byte                  RequiredCount    => _requiredCount;
        public byte                  EligibleCount    => _eligibleCount;
        public ESortieStopVoteChoice MyChoice         => _myChoice;
        public float                 RemainingSeconds => _remainingSeconds;

        // None이 아니면 이 참가자가 방금 보낸 표를 받지 않은 이유다(Cooldown, AlreadySettled, WrongPhase, NotEligible)
        public ERejectReason         Reject           => _reject;

        /****************************************
        *                Methods
        ****************************************/

        public SortieStopVoteStateMessage(ESortieStopVoteState state, byte agreeCount, byte disagreeCount, byte requiredCount,
            byte eligibleCount, ESortieStopVoteChoice myChoice, float remainingSeconds, ERejectReason reject)
        {
            _state            = state;
            _agreeCount       = agreeCount;
            _disagreeCount    = disagreeCount;
            _requiredCount    = requiredCount;
            _eligibleCount    = eligibleCount;
            _myChoice         = myChoice;
            _remainingSeconds = remainingSeconds;
            _reject           = reject;
        }
    }
}
