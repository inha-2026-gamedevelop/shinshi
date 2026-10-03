using BossDefinition = Shinshi.Data.BossPatternCatalogSO.BossDefinition;

namespace Shinshi.AI
{
    // 흐름은 Active(1) -> TransitionPending -> AwaitingHandoff(2) -> Active(2) 또는 AwaitingReadiness -> Active(2)이다
    // 전환은 1페이즈 Active에서만 시작하므로 페이즈 번호가 2가 된 뒤에는 체력이 더 내려가도 다시 시작하지 않는다
    // 페이즈 번호는 전환을 마치는 순간 2가 되고, 새 선택은 Active일 때만 허용한다. 연출 시간과 무적은 두지 않는다
    /// <summary> 페이즈 FSM, 전환 조건 판정과 인계와 실행 준비 대기와 새 선택 허용 여부를 맡는다 </summary>
    public sealed class BossPhaseFsm
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int FIRST_PHASE  = 1;
        private const int SECOND_PHASE = 2;

        /****************************************
        *              Properties
        ****************************************/

        public EBossPhaseState State { get; private set; } = EBossPhaseState.Active;

        public int Phase { get; private set; } = FIRST_PHASE;

        public bool AllowsNewSelection => State == EBossPhaseState.Active;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 1페이즈에서 체력 비율이 전환 기준 이하(경계 포함)가 됐는가, 값이 0(미지정)이면 전환하지 않는다. 한 전투에 한 번만 참이 된다 </summary>
        public bool ShouldBeginTransition(IBossVitals vitals, BossDefinition boss)
        {
            if ((State != EBossPhaseState.Active) || (Phase != FIRST_PHASE))
            {
                return false;
            }

            if (boss.PhaseTransitionHealthRate <= 0f)
            {
                return false;
            }

            return vitals.HealthRate <= boss.PhaseTransitionHealthRate;
        }

        public void BeginTransition()
        {
            if ((State != EBossPhaseState.Active) || (Phase != FIRST_PHASE))
            {
                return;
            }

            State = EBossPhaseState.TransitionPending;
        }

        /// <summary> 정리를 마친 뒤 부른다, 페이즈 번호를 올리고 인계를 기다린다 </summary>
        public void CompleteTransition()
        {
            if (State != EBossPhaseState.TransitionPending)
            {
                return;
            }

            Phase = SECOND_PHASE;
            State = EBossPhaseState.AwaitingHandoff;
        }

        /// <summary> 인계를 마친다, 실행 가능한 패턴이 있으면 Active이고 없으면 AwaitingReadiness로 기다린다. 인계 대기가 아니면 거짓이다 </summary>
        public bool CompleteHandoff(bool hasExecutablePattern)
        {
            if (State != EBossPhaseState.AwaitingHandoff)
            {
                return false;
            }

            State = hasExecutablePattern ? EBossPhaseState.Active : EBossPhaseState.AwaitingReadiness;
            return true;
        }

        /// <summary> 실행 준비 대기에서 Active로 돌아간다, 대기 중이 아니면 거짓이다 </summary>
        public bool ResumeFromReadiness()
        {
            if (State != EBossPhaseState.AwaitingReadiness)
            {
                return false;
            }

            State = EBossPhaseState.Active;
            return true;
        }
    }
}
