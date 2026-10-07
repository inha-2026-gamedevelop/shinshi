namespace Shinshi.Contracts
{
    /// <summary> 유저 경매장과 1:1 거래가 함께 쓰는 거래 가능 판정. 클라이언트 목록은 표시용이고 실제 판정은 API가 잠근 행으로 다시 한다 </summary>
    public static class MarketTradeRules
    {
        /****************************************
        *                Fields
        ****************************************/

        // 백엔드의 약초 ID 상수와 값이 같아야 한다.
        // 물약(item_potion_*)은 지급 물약과 제작 물약이 한 행에 합쳐져 이번 범위에서 제외한다
        public const string HERB_ID = "item_herb";

        // 백엔드의 부적 효과 ID 판정과 같은 형식이어야 한다. 정규식으로는 ^T\d{2}-\d[A-Z]$
        private const int  TALISMAN_ID_LENGTH       = 6;
        private const char TALISMAN_ID_PREFIX       = 'T';
        private const char TALISMAN_ID_SEPARATOR    = '-';
        private const int  TALISMAN_SEPARATOR_INDEX = 3;
        private const int  TALISMAN_CHOICE_INDEX    = 5;

        // 거래 가능 장비의 등급과 강화. 착용, 강화, 계승 이력이 있으면 bound가 켜져 자동 제외된다
        private const int TRADABLE_GRADE       = 0;
        private const int TRADABLE_ENHANCEMENT = 0;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 부적 효과 ID 형식(예 T01-1A)인지 본다. 미등록 부적 카드는 account_items에 효과 ID로 쌓인다 </summary>
        public static bool IsTalismanEffectId(string effectId)
        {
            if ((effectId == null) || (effectId.Length != TALISMAN_ID_LENGTH) || (effectId[0] != TALISMAN_ID_PREFIX)
                || (effectId[TALISMAN_SEPARATOR_INDEX] != TALISMAN_ID_SEPARATOR))
            {
                return false;
            }

            for (int index = 1; index < TALISMAN_CHOICE_INDEX; ++index)
            {
                if ((index != TALISMAN_SEPARATOR_INDEX) && ((effectId[index] < '0') || (effectId[index] > '9')))
                {
                    return false;
                }
            }

            char choice = effectId[TALISMAN_CHOICE_INDEX];
            return (choice >= 'A') && (choice <= 'Z');
        }

        /// <summary> 수량형 거래 가능 여부. 약초와 미등록 부적 카드만 가능하다 </summary>
        public static bool IsTradableAccountItem(string definitionId)
        {
            return (definitionId == HERB_ID) || IsTalismanEffectId(definitionId);
        }

        /// <summary> 검색 분류(MarketCodes.Category). 거래 가능한 품목이 아니면 0이다 </summary>
        public static int CategoryOf(string assetKind, string definitionId)
        {
            if (assetKind == MarketCodes.ASSET_EQUIPMENT)
            {
                return MarketCodes.Category.EQUIPMENT;
            }
            if (definitionId == HERB_ID)
            {
                return MarketCodes.Category.HERB;
            }

            return IsTalismanEffectId(definitionId) ? MarketCodes.Category.TALISMAN : 0;
        }

        /// <summary> 장비 거래 가능 여부. 등급 0, 강화 0, 착용, 귀속, 잠금, 수련용 아님, 제작품, 보관 중 아님 </summary>
        public static bool IsTradableEquipment(int grade, int enhancement, bool equipped, bool bound, bool locked,
            bool isTraining, string origin, bool inEscrow)
        {
            return (grade == TRADABLE_GRADE) && (enhancement == TRADABLE_ENHANCEMENT)
                && !equipped && !bound && !locked && !isTraining && !inEscrow
                && (origin == GrowthCodes.EquipmentOrigin.CRAFT);
        }

        public static bool IsUnitPriceInRange(long unitPrice)
        {
            return (unitPrice >= MarketLimits.MIN_UNIT_PRICE) && (unitPrice <= MarketLimits.MAX_UNIT_PRICE);
        }

        public static bool IsQuantityInRange(int quantity)
        {
            return (quantity >= 1) && (quantity <= MarketLimits.MAX_QUANTITY);
        }
    }
}
