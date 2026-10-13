namespace Shinshi.AI
{
    /// <summary> 월백 사방 부적진 기믹의 진행 단계 </summary>
    public enum EWolbaekTalismanState
    {
        // 아직 발동하지 않았다
        Dormant,

        // 경계에서 발동했고 공격 없는 전환 시간이다, 월백은 전장 중앙으로 이동하고 보스는 무적이다
        Transition,

        // 자리 유지 단계, 보스는 무적이다
        Active,

        // 자리 유지에 성공했고 반격 시간이다, 보스는 피해를 받는다
        Counter,

        // 반격 시간까지 끝났거나 발동하지 못하고 넘어갔다, 전투당 한 번이라 다시 발동하지 않는다
        Completed,

        // 자리를 채울 수 없어 실패했다, 보스전 실패는 세션이 확정한다
        Failed,

        // 전장 정보가 없거나 월백이 중앙에 닿지 못해 안전하게 멈췄다, 보스는 무적으로 남고 끝내는 방법은 정해지지 않았다
        Halted,

        // 사망이나 전투 종료로 멈췄다
        Stopped
    }
}
