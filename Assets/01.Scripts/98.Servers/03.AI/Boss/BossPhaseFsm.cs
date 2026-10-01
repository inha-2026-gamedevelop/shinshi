using BossDefinition = Shinshi.Data.BossPatternCatalogSO.BossDefinition;

namespace Shinshi.AI
{
    // TODO(기웅): 2페이즈 규칙은 입력 대기다. AwaitingHandoff에서 다음 페이즈로 넘기는 경로는 두지 않았다
    /// <summary> 페이즈 FSM, 전환 조건 판정과 새 선택 허용 여부를 맡는다 </summary>
    public sealed class BossPhaseFsm
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int FIRST_PHASE = 1;

        /****************************************
        *              Properties
        ****************************************/

        public EBossPhaseState State { get; private set; } = EBossPhaseState.Active;

        public int Phase { get; private set; } = FIRST_PHASE;

        public bool AllowsNewSelection => State == EBossPhaseState.Active;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 체력 비율이 전환 기준에 닿았는가, 값이 0(미지정)이면 전환하지 않는다 </summary>
        public bool ShouldBeginTransition(IBossVitals vitals, BossDefinition boss)
        {
            if (State != EBossPhaseState.Active)
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
            if (State != EBossPhaseState.Active)
            {
                return;
            }

            State = EBossPhaseState.TransitionPending;
        }

        public void CompleteTransition()
        {
            if (State != EBossPhaseState.TransitionPending)
            {
                return;
            }

            State = EBossPhaseState.AwaitingHandoff;
        }
    }
}
