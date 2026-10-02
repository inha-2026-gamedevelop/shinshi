namespace Shinshi.Network
{
    /// <summary> 개인 처치 보상의 확정 판정, 저장 전에는 소비 가능한 재고나 잔액으로 적용하지 않는다 </summary>
    public readonly struct LootRollResult
    {
        /****************************************
        *              Properties
        ****************************************/

        public long   ExtraEssence      { get; }
        public int    TicketQuantity    { get; }
        public int    TalismanQuantity  { get; }
        public string TalismanEffectId  { get; }

        /****************************************
        *                Methods
        ****************************************/

        public LootRollResult(long extraEssence, int ticketQuantity, int talismanQuantity, string talismanEffectId)
        {
            ExtraEssence     = extraEssence;
            TicketQuantity   = ticketQuantity;
            TalismanQuantity = talismanQuantity;
            TalismanEffectId = talismanEffectId;
        }
    }
}
