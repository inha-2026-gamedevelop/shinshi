namespace Shinshi.AI
{
    /// <summary> BossView가 외부 제어권을 거둔 이유 </summary>
    public enum EBossViewControlRevoke
    {
        Defeated     = 0, // 서버 사망, 처치 모션이 바로 이어진다
        Reset        = 1, // ResetView, 연결 해제와 재사용
        CombatEnded  = 2, // 사망 없는 전투 종료(전멸 등)
        Destroyed    = 3, // 보스 표현 파괴, 씬 이동
        Replaced     = 4  // 다른 소유자가 제어권을 가져갔다
    }
}
