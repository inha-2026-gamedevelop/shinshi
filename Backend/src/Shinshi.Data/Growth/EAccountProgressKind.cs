namespace Shinshi.Data.Growth
{
    /// <summary> 계정 진행 기록 종류. 앞의 넷은 account_unlocks.unlock_kind 문자열과 이름이 같다 </summary>
    public enum EAccountProgressKind
    {
        RegionCleared  = 0,
        BattleCleared  = 1,
        RegionUnlocked = 2,
        BattleUnlocked = 3,
        IntroCompleted = 4,
        FoxTraces      = 5
    }
}
