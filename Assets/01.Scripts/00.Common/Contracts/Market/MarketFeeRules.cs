namespace Shinshi.Contracts
{
    /// <summary> 성사 수수료 순수 규칙. 구매 한 건 총액에 5%를 올림한다 </summary>
    public static class MarketFeeRules
    {
        /****************************************
        *                Fields
        ****************************************/

        // 5 / 100 = 1 / 20
        private const long FEE_DIVISOR = 100 / MarketLimits.FEE_PERCENT;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> (gross * 5 + 99) / 100과 같은 값이다. 곱셈 넘침이 없게 나머지로 올림한다. gross가 0 이하면 0이다 </summary>
        public static long Fee(long gross)
        {
            if (gross <= 0)
            {
                return 0;
            }

            long quotient = gross / FEE_DIVISOR;
            return ((gross % FEE_DIVISOR) == 0) ? quotient : (quotient + 1);
        }

        /// <summary> 판매자 수령액 = 총액 - 수수료. 총액 1이면 0이다 </summary>
        public static long Net(long gross)
        {
            return gross - Fee(gross);
        }

        /// <summary> 개당 가격 * 수량. 범위를 벗어나면 false </summary>
        public static bool TryGross(long unitPrice, int quantity, out long gross)
        {
            gross = 0;
            if ((unitPrice < MarketLimits.MIN_UNIT_PRICE) || (unitPrice > MarketLimits.MAX_UNIT_PRICE)
                || (quantity < 1) || (quantity > MarketLimits.MAX_QUANTITY))
            {
                return false;
            }

            gross = unitPrice * quantity;
            return true;
        }
    }
}
