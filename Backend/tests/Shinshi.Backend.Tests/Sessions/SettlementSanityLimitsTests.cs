// System
using System;
using System.Collections.Generic;
using System.Linq;

// Third Party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Sessions
{
    /// <summary> 정산 상한의 거절 코드와 지갑 자산의 아이템 지급 금지를 검사한다 </summary>
    public sealed class SettlementSanityLimitsTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ITEM_ID = "item_boss_material_common";
        private const long   ONE     = 1;

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData(SettlementSanityLimits.ESSENCE_DEFINITION_ID)]
        [InlineData(SettlementSanityLimits.SHARD_DEFINITION_ID)]
        [InlineData(SettlementSanityLimits.TICKET_DEFINITION_ID)]
        [InlineData(SettlementSanityLimits.CURRENCY_DEFINITION_ID)]
        public void IsAllowedItem_WalletDefinition_Rejects(string definitionId)
        {
            Assert.False(SettlementSanityLimits.IsAllowedItem(definitionId));
        }

        [Theory]
        [InlineData(ITEM_ID)]
        [InlineData("item_auction_material_rare")]
        public void IsAllowedItem_MaterialDefinition_Accepts(string definitionId)
        {
            Assert.True(SettlementSanityLimits.IsAllowedItem(definitionId));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("item_")]
        [InlineData("Item_boss_material_common")]
        [InlineData("item_boss-material-common")]
        public void IsAllowedItem_InvalidDefinition_Rejects(string? definitionId)
        {
            Assert.False(SettlementSanityLimits.IsAllowedItem(definitionId));
        }

        [Theory]
        [InlineData(SettlementSanityLimits.MAX_SESSION_ESSENCE + ONE)]
        [InlineData(long.MaxValue)]
        public void GetRejectReason_EssenceEventAboveLimit_ReturnsSettlementLimitExceeded(long amount)
        {
            SessionEventInput entry = SessionEventTests.Essence(ONE, Guid.NewGuid(), amount);

            AssertLimitExceeded(new[] { entry });
        }

        [Fact]
        public void GetRejectReason_EssenceBatchAboveLimit_ReturnsSettlementLimitExceeded()
        {
            Guid participantId = Guid.NewGuid();
            SessionEventInput[] events =
            {
                SessionEventTests.Essence(ONE, participantId, SettlementSanityLimits.MAX_SESSION_ESSENCE),
                SessionEventTests.Essence(ONE + ONE, participantId, ONE)
            };

            AssertLimitExceeded(events);
        }

        [Fact]
        public void GetRejectReason_EssenceAtLimitForSeparateParticipants_ReturnsNone()
        {
            SessionEventInput[] events =
            {
                SessionEventTests.Essence(ONE, Guid.NewGuid(), SettlementSanityLimits.MAX_SESSION_ESSENCE),
                SessionEventTests.Essence(ONE + ONE, Guid.NewGuid(), SettlementSanityLimits.MAX_SESSION_ESSENCE)
            };

            Assert.Equal(ContractCodes.RejectReason.NONE, SessionEventValidator.GetRejectReason(events));
            Assert.True(SessionEventValidator.IsValid(events));
        }

        [Fact]
        public void GetRejectReason_GrantEssenceAboveLimit_ReturnsSettlementLimitExceeded()
        {
            SessionEventInput entry = Ended(SettlementSanityLimits.MAX_SESSION_ESSENCE + ONE);

            AssertLimitExceeded(new[] { entry });
        }

        [Theory]
        [InlineData(SettlementSanityLimits.MAX_ITEM_QUANTITY + ONE)]
        [InlineData(long.MaxValue)]
        public void GetRejectReason_ItemQuantityAboveLimit_ReturnsSettlementLimitExceeded(long quantity)
        {
            SessionEventInput entry = Ended(0, Item(ITEM_ID, quantity));

            AssertLimitExceeded(new[] { entry });
        }

        [Fact]
        public void GetRejectReason_ItemQuantitySplitAcrossRows_ReturnsSettlementLimitExceeded()
        {
            SessionEventInput entry = Ended(0, Item(ITEM_ID, SettlementSanityLimits.MAX_ITEM_QUANTITY), Item(ITEM_ID, ONE));

            AssertLimitExceeded(new[] { entry });
        }

        [Fact]
        public void GetRejectReason_ItemRowsAboveLimit_ReturnsSettlementLimitExceeded()
        {
            SettlementItemInput[] items = Enumerable.Range(0, SessionEventValidator.MAX_GRANT_ITEMS + (int)ONE)
                .Select(index => Item(ITEM_ID, ONE)).ToArray();
            SessionEventInput entry = Ended(0, items);

            AssertLimitExceeded(new[] { entry });
        }

        [Theory]
        [InlineData(SettlementSanityLimits.ESSENCE_DEFINITION_ID)]
        [InlineData(SettlementSanityLimits.SHARD_DEFINITION_ID)]
        [InlineData(SettlementSanityLimits.TICKET_DEFINITION_ID)]
        [InlineData(SettlementSanityLimits.CURRENCY_DEFINITION_ID)]
        public void GetRejectReason_WalletGrant_RejectsAsInvalidItem(string definitionId)
        {
            SessionEventInput entry = Ended(0, Item(definitionId, ONE));

            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH,
                SessionEventValidator.GetRejectReason(new[] { entry }));
            Assert.False(SessionEventValidator.IsValid(new[] { entry }));
        }

        [Fact]
        public void GetRejectReason_GrantAtLimits_ReturnsNone()
        {
            SessionEventInput entry = Ended(SettlementSanityLimits.MAX_SESSION_ESSENCE,
                Item(ITEM_ID, SettlementSanityLimits.MAX_ITEM_QUANTITY));

            Assert.Equal(ContractCodes.RejectReason.NONE, SessionEventValidator.GetRejectReason(new[] { entry }));
        }

        [Fact]
        public void GetRejectReason_NonPositiveEssence_ReturnsSessionMismatch()
        {
            SessionEventInput entry = SessionEventTests.Essence(ONE, Guid.NewGuid(), 0);

            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH,
                SessionEventValidator.GetRejectReason(new[] { entry }));
        }

        private static void AssertLimitExceeded(IReadOnlyList<SessionEventInput> events)
        {
            Assert.Equal(ContractCodes.RejectReason.SETTLEMENT_LIMIT_EXCEEDED,
                SessionEventValidator.GetRejectReason(events));
            Assert.False(SessionEventValidator.IsValid(events));
        }

        private static SessionEventInput Ended(long essence, params SettlementItemInput[] items)
        {
            return SessionEventTests.Ended(ONE, Guid.NewGuid(),
                new[] { SessionEventTests.Grant(Guid.NewGuid(), essence, items) });
        }

        private static SettlementItemInput Item(string definitionId, long quantity)
        {
            return new SettlementItemInput
            {
                DefinitionId = definitionId,
                BindingKind = SessionTestEnvironment.BINDING,
                Quantity = quantity
            };
        }
    }
}
