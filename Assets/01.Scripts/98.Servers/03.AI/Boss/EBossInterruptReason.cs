namespace Shinshi.AI
{
    /// <summary> 진행 중인 패턴과 지속물을 끊는 이유, 상위 FSM이 정하고 패턴과 수명 관리자가 이유별로 정리한다 </summary>
    public enum EBossInterruptReason
    {
        None            = 0,
        Death           = 1,
        CombatEnd       = 2,
        PhaseTransition = 3,
        DirectControl   = 4,

        // 체력 기믹이 시작되어 진행 중인 공격과 지속물을 모두 정리한다
        GimmickEntry    = 5
    }
}
