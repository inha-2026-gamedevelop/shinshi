// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 값이 바뀔 때와 (재)접속 때만 그 참가자에게 보낸다. 받는 사람마다 개인 이탈 값이 달라 브로드캐스트하지 않는다
    /// <summary> 출정 진행과 이 참가자의 귀환 가능 여부 </summary>
    public readonly struct SortieStatusMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ESortiePhase           _phase;
        private readonly ESessionEndReason      _endReason;
        private readonly bool                   _hasPersonalClose;
        private readonly ESessionEndReason      _personalCloseReason;
        private readonly bool                   _canReturn;
        private readonly ESortieSettlementState _settlementState;

        /****************************************
        *              Properties
        ****************************************/

        public ESortiePhase           Phase               => _phase;

        // Phase가 Ended 이상일 때만 뜻이 있다
        public ESessionEndReason      EndReason           => _endReason;

        // 출정 진행 중에 이 참가자만 먼저 떠났다(포기, 유예 만료). 이유는 PersonalCloseReason이다
        public bool                   HasPersonalClose    => _hasPersonalClose;
        public ESessionEndReason      PersonalCloseReason => _personalCloseReason;

        // 이탈 또는 종료 기록이 저장돼 마을 입장권을 받을 수 있다. 참이 되기 전 마을 입장은 이미 접속 중으로 거절될 수 있다
        public bool                   CanReturn           => _canReturn;
        public ESortieSettlementState SettlementState     => _settlementState;

        /****************************************
        *                Methods
        ****************************************/

        public SortieStatusMessage(ESortiePhase phase, ESessionEndReason endReason, bool hasPersonalClose,
            ESessionEndReason personalCloseReason, bool canReturn, ESortieSettlementState settlementState)
        {
            _phase               = phase;
            _endReason           = endReason;
            _hasPersonalClose    = hasPersonalClose;
            _personalCloseReason = personalCloseReason;
            _canReturn           = canReturn;
            _settlementState     = settlementState;
        }

        /// <summary> 값이 모두 같은지, 바뀐 것만 보내기 위해 쓴다 </summary>
        public bool SameAs(in SortieStatusMessage other)
        {
            return (_phase == other._phase) && (_endReason == other._endReason)
                && (_hasPersonalClose == other._hasPersonalClose) && (_personalCloseReason == other._personalCloseReason)
                && (_canReturn == other._canReturn) && (_settlementState == other._settlementState);
        }
    }
}
