namespace Shinshi.AI
{
    // 같은 전환 사건이 사건과 접속 스냅샷으로 두 번 오거나, 지난 연결의 사건이 늦게 오거나, 이미 끝난 구간을 재접속으로 받는 경우를
    // 재생 여부와 시작 위치로 정리한다. 판정에 쓰는 값은 서버 번호와 서버 시각뿐이고 로컬 경과 시간은 쓰지 않는다.
    // 끝난 구간이나 대기 중 취소된 구간도 번호를 소비해 같은 번호가 다시 와도 처음부터 재생하지 않는다
    /// <summary> 2페이즈 전환 사건의 중복, 지난 세션, 늦은 수신 판정 </summary>
    public sealed class WolbaekPhase2TransitionGate
    {
        /****************************************
        *              Constants
        ****************************************/

        // 이보다 먼 미래 시작 시각은 시계 오차가 아니라 잘못된 사건이다. 기믹 사건 보류 한계(WolbaekServerEventGate)와 같은 크기다
        public const double MAX_FUTURE_SECONDS = 0.5d;

        /****************************************
        *                Fields
        ****************************************/

        private int  _combatId;
        private int  _lastTransitionId;
        private bool _hasSession;

        /****************************************
        *              Properties
        ****************************************/

        public bool HasSession => _hasSession;

        public int CombatId => _combatId;

        /// <summary> 지금 세션에서 마지막으로 받아들인 전환 번호, 없으면 0이다 </summary>
        public int LastTransitionId => _lastTransitionId;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 새 연결이나 새 전투를 시작한다. 이전 세션의 번호 기록을 버린다 </summary>
        public void ResetSession(int combatId)
        {
            _combatId         = combatId;
            _lastTransitionId = 0;
            _hasSession       = true;
        }

        /// <summary> 연결이 끊겼다. 다음 ResetSession 전까지 모든 사건을 지난 세션으로 본다 </summary>
        public void EndSession()
        {
            _lastTransitionId = 0;
            _hasSession       = false;
        }

        /// <summary> 사건 하나를 판정한다. Play와 Scheduled와 Ended는 번호를 소비한다 </summary>
        public EWolbaekPhase2TransitionDecision Evaluate(in WolbaekPhase2TransitionCue cue, double serverNow)
        {
            if ((cue.TransitionId <= 0) || (!(cue.EndServerTime > cue.StartServerTime)))
            {
                return EWolbaekPhase2TransitionDecision.Invalid;
            }

            if ((!_hasSession) || (cue.CombatId != _combatId))
            {
                return EWolbaekPhase2TransitionDecision.OtherSession;
            }

            if (cue.TransitionId <= _lastTransitionId)
            {
                return EWolbaekPhase2TransitionDecision.Duplicate;
            }

            // 번호를 소비하지 않는다. 시계가 맞춰진 뒤 같은 사건이 스냅샷으로 다시 오면 그때 받아들인다
            if ((cue.StartServerTime - serverNow) > MAX_FUTURE_SECONDS)
            {
                return EWolbaekPhase2TransitionDecision.TooEarly;
            }

            _lastTransitionId = cue.TransitionId;

            if (serverNow >= cue.EndServerTime)
            {
                return EWolbaekPhase2TransitionDecision.Ended;
            }

            return (serverNow < cue.StartServerTime) ? EWolbaekPhase2TransitionDecision.Scheduled : EWolbaekPhase2TransitionDecision.Play;
        }
    }
}
