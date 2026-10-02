namespace Shinshi.Network
{
    /// <summary> 참가자 한 명의 정기 계산 내역, Essence가 정산안에 넣는 최종 값이다 </summary>
    public readonly struct SettlementBreakdown
    {
        /****************************************
        *              Properties
        ****************************************/

        // 완료 웨이브의 확보 기본 정기, 결과와 무관하게 보존한다
        public long BaseEssence         { get; }

        // ESSENCE_ACCRUED로 적립한 추가 정기 합계, 지급률을 곱하기 전 값이다
        public long AccruedExtraEssence { get; }
        public long PaidExtraEssence    { get; }
        public long BossEssence         { get; }
        public long Essence             { get; }
        public bool IsEssenceClamped    { get; }

        /****************************************
        *                Methods
        ****************************************/

        public SettlementBreakdown(long baseEssence, long accruedExtraEssence, long paidExtraEssence, long bossEssence,
            long essence, bool isEssenceClamped)
        {
            BaseEssence         = baseEssence;
            AccruedExtraEssence = accruedExtraEssence;
            PaidExtraEssence    = paidExtraEssence;
            BossEssence         = bossEssence;
            Essence             = essence;
            IsEssenceClamped    = isEssenceClamped;
        }
    }
}
