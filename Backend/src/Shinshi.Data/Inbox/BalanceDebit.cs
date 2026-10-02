namespace Shinshi.Data.Inbox
{
    /// <summary> 가용량 차감 결과. Applied는 실제 차감량(0 이상)이다 </summary>
    public sealed class BalanceDebit
    {
        /****************************************
        *              Properties
        ****************************************/

        public long BalanceBefore { get; }
        public long Reserved      { get; }
        public long Applied       { get; }
        public long BalanceAfter  { get; }

        /****************************************
        *                Methods
        ****************************************/

        public BalanceDebit(long balanceBefore, long reserved, long applied, long balanceAfter)
        {
            BalanceBefore = balanceBefore;
            Reserved      = reserved;
            Applied       = applied;
            BalanceAfter  = balanceAfter;
        }
    }
}
