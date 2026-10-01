namespace Shinshi.AI
{
    // 단계 타이머는 공격 FSM이 가진다. 패턴은 각 단계 안에서 무엇을 할지만 맡는다
    // 취소 뒤에는 FSM이 이 패턴을 더는 부르지 않지만, 패턴도 예약해 둔 타격을 스스로 버려야 한다
    /// <summary> 패턴 한 번의 시전 실행, 서버 시각으로만 진행한다 </summary>
    public interface IBossPattern
    {
        /// <summary> 예고를 시작한다, Telegraph 진입과 같은 시점에 한 번 부른다 </summary>
        void Begin(in BossPatternContext context, double serverTime);

        /// <summary> 단계에 들어갈 때 한 번, serverTime은 실제 틱이 아니라 단계 경계 시각이다 </summary>
        void EnterState(EBossAttackState state, double serverTime);

        /// <summary> 단계가 이어지는 동안 틱마다 부른다 </summary>
        void Tick(EBossAttackState state, double serverTime);

        /// <summary> 중단됐다, 이미 적용된 피해는 되돌리지 않고 남은 예약만 버린다 </summary>
        void Cancel(EBossInterruptReason reason);

        /// <summary> 회복까지 정상으로 끝났다 </summary>
        void Finish();
    }
}
