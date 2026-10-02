namespace Shinshi.AI
{
    /// <summary> 공격 FSM의 상태, Ready에서만 BT가 새 패턴을 고른다 </summary>
    public enum EBossAttackState
    {
        Ready     = 0,
        Telegraph = 1,
        Execute   = 2,
        Recover   = 3
    }
}
