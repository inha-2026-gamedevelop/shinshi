// System
using System;
using System.Numerics;

// Third Party
using Xunit;

// Project
using Shinshi.Contracts;

namespace Shinshi.Backend.Tests.Market
{
    /// <summary> 경매장 수수료와 거래 가능 판정 순수 규칙. 기대값은 설계 4.1, 4.2의 수치다 </summary>
    public sealed class MarketRulesTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData(0, 0)]
        [InlineData(-5, 0)]
        [InlineData(1, 1)]
        [InlineData(19, 1)]
        [InlineData(20, 1)]
        [InlineData(21, 2)]
        [InlineData(40, 2)]
        [InlineData(41, 3)]
        [InlineData(100, 5)]
        [InlineData(101, 6)]
        public void Fee_Boundaries_RoundUpFivePercent(long gross, long expected)
        {
            Assert.Equal(expected, MarketFeeRules.Fee(gross));
        }

        [Fact]
        public void Fee_MaxValues_NeverOverflowAndMatchFormula()
        {
            long maxGross = MarketLimits.MAX_UNIT_PRICE * MarketLimits.MAX_QUANTITY;
            long[] samples = { maxGross, maxGross - 1, long.MaxValue, long.MaxValue - 1, (long.MaxValue / 5) + 1 };

            foreach (long gross in samples)
            {
                BigInteger expected = ((new BigInteger(gross) * 5) + 99) / 100;
                Assert.Equal((long)expected, MarketFeeRules.Fee(gross));
            }
        }

        [Fact]
        public void Fee_Sweep_MatchesDesignFormula()
        {
            for (long gross = 1; gross <= 5000; ++gross)
            {
                Assert.Equal(((gross * 5) + 99) / 100, MarketFeeRules.Fee(gross));
            }
        }

        [Fact]
        public void Net_PriceOneQuantityOne_IsZero()
        {
            Assert.Equal(0, MarketFeeRules.Net(1));
            Assert.Equal(19, MarketFeeRules.Net(20));
        }

        [Fact]
        public void TryGross_Range_RejectsOutOfRangeAndComputesMax()
        {
            Assert.True(MarketFeeRules.TryGross(MarketLimits.MAX_UNIT_PRICE, MarketLimits.MAX_QUANTITY, out long gross));
            Assert.Equal(999900000000L, gross);
            Assert.False(MarketFeeRules.TryGross(0, 1, out _));
            Assert.False(MarketFeeRules.TryGross(MarketLimits.MAX_UNIT_PRICE + 1, 1, out _));
            Assert.False(MarketFeeRules.TryGross(1, 0, out _));
            Assert.False(MarketFeeRules.TryGross(1, MarketLimits.MAX_QUANTITY + 1, out _));
        }

        [Fact]
        public void Limits_MatchDesignValues()
        {
            Assert.Equal(100000000L, MarketLimits.MAX_UNIT_PRICE);
            Assert.Equal(9999, MarketLimits.MAX_QUANTITY);
            Assert.Equal(10, MarketLimits.MAX_ACTIVE_LISTINGS);
            Assert.Equal(24, MarketLimits.LISTING_HOURS);
            Assert.Equal(20, MarketLimits.PAGE_SIZE);
            Assert.Equal(8, TradeCodes.TRADE_MAX_ITEMS);
            Assert.Equal(120, TradeCodes.TRADE_PREPARE_TTL_SECONDS);
        }

        [Theory]
        [InlineData("item_herb", true)]
        [InlineData("T01-1A", true)]
        [InlineData("T45-3Z", true)]
        [InlineData("item_potion_small", false)]
        [InlineData("item_potion_large", false)]
        [InlineData("item_boss_material_a", false)]
        [InlineData("item_auction_material_a", false)]
        [InlineData("item_boss_ticket", false)]
        [InlineData("item_refine_shard", false)]
        [InlineData("item_essence", false)]
        [InlineData("item_yeopjeon", false)]
        [InlineData("t01-1A", false)]
        [InlineData("T01-1a", false)]
        [InlineData("T01_1A", false)]
        [InlineData("T1-1A", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsTradableAccountItem_OnlyHerbAndUnregisteredTalismanCards(string? definitionId, bool expected)
        {
            Assert.Equal(expected, MarketTradeRules.IsTradableAccountItem(definitionId));
        }

        [Fact]
        public void IsTradableEquipment_OnlyPlainCraftedUnboundEquipment()
        {
            const string CRAFT = GrowthCodes.EquipmentOrigin.CRAFT;

            Assert.True(MarketTradeRules.IsTradableEquipment(0, 0, false, false, false, false, CRAFT, false));
            Assert.False(MarketTradeRules.IsTradableEquipment(1, 0, false, false, false, false, CRAFT, false));
            Assert.False(MarketTradeRules.IsTradableEquipment(0, 1, false, false, false, false, CRAFT, false));
            Assert.False(MarketTradeRules.IsTradableEquipment(0, 0, true, false, false, false, CRAFT, false));
            Assert.False(MarketTradeRules.IsTradableEquipment(0, 0, false, true, false, false, CRAFT, false));
            Assert.False(MarketTradeRules.IsTradableEquipment(0, 0, false, false, true, false, CRAFT, false));
            Assert.False(MarketTradeRules.IsTradableEquipment(0, 0, false, false, false, true, CRAFT, false));
            Assert.False(MarketTradeRules.IsTradableEquipment(0, 0, false, false, false, false, CRAFT, true));
            Assert.False(MarketTradeRules.IsTradableEquipment(0, 0, false, false, false, false,
                GrowthCodes.EquipmentOrigin.INITIAL_GRANT, false));
            Assert.False(MarketTradeRules.IsTradableEquipment(0, 0, false, false, false, false,
                GrowthCodes.EquipmentOrigin.TRIAL_GRANT, false));
            Assert.False(MarketTradeRules.IsTradableEquipment(0, 0, false, false, false, false, null, false));
        }

        [Fact]
        public void CategoryOf_MapsKinds()
        {
            Assert.Equal(MarketCodes.Category.HERB, MarketTradeRules.CategoryOf(MarketCodes.ASSET_ACCOUNT_ITEM, "item_herb"));
            Assert.Equal(MarketCodes.Category.TALISMAN, MarketTradeRules.CategoryOf(MarketCodes.ASSET_ACCOUNT_ITEM, "T01-1A"));
            Assert.Equal(MarketCodes.Category.EQUIPMENT, MarketTradeRules.CategoryOf(MarketCodes.ASSET_EQUIPMENT, "eq_x"));
            Assert.Equal(0, MarketTradeRules.CategoryOf(MarketCodes.ASSET_ACCOUNT_ITEM, "item_potion_small"));
        }

        [Fact]
        public void Routes_MatchDesignPaths()
        {
            Assert.Equal("/v1/town/market/search", MarketRoutes.SEARCH);
            Assert.Equal("/v1/town/market/listings/query", MarketRoutes.LISTINGS_QUERY);
            Assert.Equal("/v1/town/characters/c/market/listings/l/cancel",
                string.Format(MarketRoutes.CANCEL_FORMAT, "c", "l"));
            Assert.Equal("/v1/town/trades/prepare", TradeRoutes.PREPARE);
            Assert.Equal("/v1/town/trades/e/commit", string.Format(TradeRoutes.COMMIT_FORMAT, "e"));
            Assert.Equal("/v1/town/trades/e/resolve", string.Format(TradeRoutes.RESOLVE_FORMAT, "e"));
        }
    }
}
