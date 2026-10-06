namespace Shinshi.AI
{
    /// <summary> 무철 돌진 유도 기믹의 진행 단계 </summary>
    public enum EMucheolChargeGimmickState
    {
        // 아직 발동하지 않았다
        Dormant,

        // 발동했고 뿔 들이받기(P06)를 반복해서 고른다
        Active,

        // 필요한 구조물이 모두 부서졌고 그 파괴를 낸 시전의 회복이 끝나기를 기다린다
        AwaitingFinalRecovery,

        // 그로기 중, 새 공격을 고르지 않고 피해는 받는다
        Groggy,

        // 그로기까지 끝났다, 전투당 한 번이라 다시 발동하지 않는다
        Completed,

        // 사망이나 전투 종료나 진행 불가로 멈췄다
        Stopped
    }
}
