// System
using System;
using System.Collections.Generic;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class TradeMarketRulesTests
    {
        // 거래 제안 검사, 요청 표, 편집본, 거래 창 규칙, 경매장 창 규칙, 우편함 규칙, 거래 메시지 직렬화

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void ValidateFormat_RejectsDuplicatesTooManyLinesAndNegativeCurrency()
        {
            var tooMany = new TradeOfferLine[TradeCodes.TRADE_MAX_ITEMS + 1];
            for (int i = 0; i < tooMany.Length; ++i)
            {
                tooMany[i] = new TradeOfferLine(MarketTradeRules.HERB_ID, null, 1);
            }

            var duplicate = new[] { new TradeOfferLine(MarketTradeRules.HERB_ID, null, 1), new TradeOfferLine(MarketTradeRules.HERB_ID, null, 2) };

            Assert.AreEqual(ERejectReason.InvalidTarget, TradeOfferRules.ValidateFormat(tooMany, 0));
            Assert.AreEqual(ERejectReason.InvalidTarget, TradeOfferRules.ValidateFormat(duplicate, 0));
            Assert.AreEqual(ERejectReason.InvalidTarget, TradeOfferRules.ValidateFormat(null, -1));
            Assert.AreEqual(ERejectReason.None, TradeOfferRules.ValidateFormat(null, 0));
        }

        [Test]
        public void ValidateFormat_PotionIsNotTradable()
        {
            var potion = new[] { new TradeOfferLine("item_potion_small", null, 1) };

            Assert.AreEqual(ERejectReason.TradeNotTradable, TradeOfferRules.ValidateFormat(potion, 0));
        }

        [Test]
        public void PreCheck_UsesLastSnapshotForHerbEquipmentAndCurrency()
        {
            string itemId = Guid.NewGuid().ToString("D");
            var equipment = new[]
            {
                new TownEquipmentEntry(itemId, "equip_a", 0, 0, false, GrowthCodes.EquipmentOrigin.CRAFT, 0, false, false, false),
                new TownEquipmentEntry(Guid.NewGuid().ToString("D"), "equip_b", 0, 0, false, GrowthCodes.EquipmentOrigin.CRAFT, 0, true, false, false),
                new TownEquipmentEntry(Guid.NewGuid().ToString("D"), "equip_c", 0, 0, false, GrowthCodes.EquipmentOrigin.CRAFT, 0, false, false, false, true)
            };
            var growth = new GrowthSnapshotMessage(0, 0, 0, 0, new int[5], equipment, null, null, null,
                new[] { new TownItemEntry("currency", "Account", 50, 10) }, 0, 0, "v");
            var life = new LifeSnapshotMessage(0, 0, 0, MarketTradeRules.HERB_ID, 4, 0, null);

            Assert.AreEqual(ERejectReason.None,
                TradeOfferRules.PreCheck(new[] { new TradeOfferLine("equip_a", itemId, 1) }, 40, true, in growth, true, in life, "currency"));
            Assert.AreEqual(ERejectReason.TradeInsufficientCurrency,
                TradeOfferRules.PreCheck(null, 41, true, in growth, true, in life, "currency"));
            Assert.AreEqual(ERejectReason.TradeItemChanged,
                TradeOfferRules.PreCheck(new[] { new TradeOfferLine(MarketTradeRules.HERB_ID, null, 5) }, 0, true, in growth, true, in life, null));
            Assert.AreEqual(ERejectReason.TradeNotTradable,
                TradeOfferRules.PreCheck(new[] { new TradeOfferLine("equip_b", equipment[1].ItemId, 1) }, 0, true, in growth, true, in life, null));
            Assert.AreEqual(ERejectReason.TradeNotTradable,
                TradeOfferRules.PreCheck(new[] { new TradeOfferLine("equip_c", equipment[2].ItemId, 1) }, 0, true, in growth, true, in life, null));
            Assert.AreEqual(ERejectReason.None,
                TradeOfferRules.PreCheck(new[] { new TradeOfferLine(MarketTradeRules.HERB_ID, null, 5) }, 0, false, in growth, false, in life, null));
        }

        [Test]
        public void PreCheck_WithoutGrowthSnapshot_RejectsEquipmentLines()
        {
            var growth = default(GrowthSnapshotMessage);
            var life   = default(LifeSnapshotMessage);
            string itemId = Guid.NewGuid().ToString("D");

            Assert.AreEqual(ERejectReason.TradeItemChanged,
                TradeOfferRules.PreCheck(new[] { new TradeOfferLine("equip_a", itemId, 1) }, 0, false, in growth, false, in life, null));
            Assert.AreEqual(ERejectReason.InvalidTarget,
                TradeOfferRules.PreCheck(new[] { new TradeOfferLine(string.Empty, itemId, 1) }, 0, false, in growth, false, in life, null));
            Assert.AreEqual(ERejectReason.InvalidTarget,
                TradeOfferRules.PreCheck(new[] { new TradeOfferLine(new string('x', 65), itemId, 1) }, 0, false, in growth, false, in life, null));
        }

        [Test]
        public void RequestTable_TryFindPair_FindsPendingRequest()
        {
            var table = new TradeRequestTable(8, 30.0, 2);
            table.Add(1, 2, 0.0, out Guid tradeId);

            Assert.IsTrue(table.TryFindPair(1, 2, out TradeRequestEntry entry));
            Assert.AreEqual(tradeId, entry.TradeId);
            Assert.IsFalse(table.TryFindPair(2, 1, out _));
        }

        [Test]
        public void RequestTable_LimitsPerRequesterRefreshesPairAndExpires()
        {
            var table = new TradeRequestTable(8, 30.0, 2);

            Assert.IsTrue(table.Add(1, 2, 0.0, out Guid first));
            Assert.IsTrue(table.Add(1, 3, 0.0, out _));
            Assert.IsFalse(table.Add(1, 4, 0.0, out _));
            Assert.IsTrue(table.Add(1, 2, 10.0, out Guid again));
            Assert.AreEqual(first, again);
            Assert.AreEqual(2, table.Count);

            var expired = new List<TradeRequestEntry>();
            table.Prune(35.0, expired);
            Assert.AreEqual(1, expired.Count);
            Assert.AreEqual(3, expired[0].TargetId);
            Assert.AreEqual(1, table.Count);
            Assert.IsTrue(table.TryGet(first, 2, out _));
        }

        [Test]
        public void Draft_MergesStacksBlocksDuplicateEquipmentAndCapsLines()
        {
            var draft = new TradeOfferDraft();
            var herb  = new SellableItem(MarketTradeRules.HERB_ID, null, 5, 0);
            var gear  = new SellableItem("equip_a", Guid.NewGuid().ToString("D"), 1, 0);

            Assert.IsTrue(draft.TryAdd(in herb, 3));
            Assert.IsTrue(draft.TryAdd(in herb, 2));
            Assert.IsFalse(draft.TryAdd(in herb, 1));
            Assert.AreEqual(1, draft.Count);
            Assert.AreEqual(5, draft.LineAt(0).Quantity);
            Assert.IsTrue(draft.TryAdd(in gear, 1));
            Assert.IsFalse(draft.TryAdd(in gear, 1));
            Assert.IsTrue(draft.RemoveAt(0));
            Assert.AreEqual(1, draft.Count);

            for (int i = 0; i < TradeCodes.TRADE_MAX_ITEMS; ++i)
            {
                draft.TryAdd(new SellableItem("e" + i, Guid.NewGuid().ToString("D"), 1, 0), 1);
            }

            Assert.AreEqual(TradeCodes.TRADE_MAX_ITEMS, draft.Count);
        }

        [Test]
        public void TradeViewRules_ButtonsFollowStateAndCommittingLocksEverything()
        {
            TradeButtonStates editing = TradeViewRules.ButtonsFor(ETradeState.Editing, false, false);
            TradeButtonStates accepting = TradeViewRules.ButtonsFor(ETradeState.Accepting, true, false);
            TradeButtonStates acceptedAlready = TradeViewRules.ButtonsFor(ETradeState.Accepting, true, true);
            TradeButtonStates committing = TradeViewRules.ButtonsFor(ETradeState.Committing, true, true);

            Assert.IsTrue(editing.CanEdit);
            Assert.IsFalse(editing.CanAccept);
            Assert.IsTrue(accepting.CanAccept);
            Assert.IsTrue(accepting.LockIsRelease);
            Assert.IsFalse(acceptedAlready.CanAccept);
            Assert.IsFalse(committing.CanEdit);
            Assert.IsFalse(committing.CanToggleLock);
            Assert.IsFalse(committing.CanAccept);
            Assert.IsFalse(committing.CanCancel);
            Assert.IsTrue(committing.IsBusy);
        }

        [Test]
        public void TradeViewRules_CountdownAndCurrencyParsing()
        {
            double end = TradeViewRules.LocalEnd(10.0, 5f);

            Assert.AreEqual(3, TradeViewRules.CeilSeconds(TradeViewRules.RemainingAt(end, 12.0)));
            Assert.AreEqual(0f, TradeViewRules.RemainingAt(end, 20.0));
            Assert.IsTrue(TradeViewRules.TryParseCurrency("1,500", out long value));
            Assert.AreEqual(1500, value);
            Assert.IsTrue(TradeViewRules.TryParseCurrency("", out value));
            Assert.AreEqual(0, value);
            Assert.IsFalse(TradeViewRules.TryParseCurrency("-3", out _));
            Assert.IsFalse(TradeViewRules.TryParseCurrency("abc", out _));
            Assert.IsFalse(TradeViewRules.TryParseCurrency((TradeSettings.MAX_CURRENCY + 1).ToString(), out _));
        }

        [Test]
        public void AuctionViewRules_PreviewValidationAndSearch()
        {
            Assert.IsTrue(AuctionViewRules.TryPreview(1, 1, out long gross, out long fee, out long net));
            Assert.AreEqual(1, gross);
            Assert.AreEqual(1, fee);
            Assert.AreEqual(0, net);
            Assert.IsTrue(AuctionViewRules.TryPreview(100, 3, out gross, out fee, out net));
            Assert.AreEqual(300, gross);
            Assert.AreEqual(15, fee);
            Assert.AreEqual(285, net);

            Assert.AreEqual(EAuctionFormError.NoItem, AuctionViewRules.ValidateRegister(false, 10, 1, 1));
            Assert.AreEqual(EAuctionFormError.PriceRange, AuctionViewRules.ValidateRegister(true, 0, 1, 5));
            Assert.AreEqual(EAuctionFormError.QuantityRange, AuctionViewRules.ValidateRegister(true, 10, 6, 5));
            Assert.AreEqual(EAuctionFormError.None, AuctionViewRules.ValidateRegister(true, 10, 5, 5));

            var found = new List<string>();
            AuctionViewRules.FindDefinitionIds("herb", new[] { "item_herb", "equip_a", "ITEM_HERB_2" }, 10, found);
            Assert.AreEqual(2, found.Count);
            AuctionViewRules.FindDefinitionIds("  ", new[] { "item_herb" }, 10, found);
            Assert.AreEqual(0, found.Count);

            Assert.AreEqual("만료", AuctionViewRules.FormatRemaining(1000, 2000));
            Assert.AreEqual("1일 1시간", AuctionViewRules.FormatRemaining(25L * 3600L * 1000L, 0));
            Assert.AreEqual(3, AuctionViewRules.PageCount(41, 20));
            Assert.AreEqual(1, AuctionViewRules.PageCount(0, 20));
        }

        [Test]
        public void SellableItemRules_ListsOnlyTradableItems()
        {
            string tradable = Guid.NewGuid().ToString("D");
            var equipment = new[]
            {
                new TownEquipmentEntry(tradable, "equip_a", 0, 0, false, GrowthCodes.EquipmentOrigin.CRAFT, 0, false, false, false),
                new TownEquipmentEntry(Guid.NewGuid().ToString("D"), "equip_b", 0, 0, false, GrowthCodes.EquipmentOrigin.CRAFT, 2, false, false, false),
                new TownEquipmentEntry(Guid.NewGuid().ToString("D"), "equip_c", 0, 0, false, GrowthCodes.EquipmentOrigin.CRAFT, 0, false, false, false, true)
            };
            var items = new[]
            {
                new TownItemEntry("T01-1A", "Account", 4, 1),
                new TownItemEntry("item_boss_material", "Account", 9, 0)
            };
            var growth = new GrowthSnapshotMessage(0, 0, 0, 0, new int[5], equipment, null, null, null, items, 0, 0, "v");
            var life = new LifeSnapshotMessage(0, 0, 0, MarketTradeRules.HERB_ID, 6, 0, null);
            var into = new List<SellableItem>();

            SellableItemRules.Build(true, in growth, true, in life, into);

            Assert.AreEqual(3, into.Count);
            Assert.AreEqual("equip_a", into[0].DefinitionId);
            Assert.AreEqual(3, into[1].MaxQuantity);
            Assert.AreEqual(MarketTradeRules.HERB_ID, into[2].DefinitionId);
        }

        [Test]
        public void MailBoxViewRules_BuildsTitleBodyAndPaging()
        {
            var reward = new InboxReward("Currency", "currency", "Account", 1200);
            var entry  = new InboxEntry(Guid.NewGuid().ToString("D"), "SessionReward", 0, 3L * 86400000L, new[] { reward }, true);

            Assert.AreEqual("출정 보상", MailBoxViewRules.TitleOf(entry.SourceKind));
            StringAssert.Contains("엽전 x1,200", MailBoxViewRules.Body(entry));
            Assert.AreEqual("3일 남음", MailBoxViewRules.ExpiryText(entry.ExpiresAtUnixMs, 0));
            Assert.AreEqual("만료", MailBoxViewRules.ExpiryText(0, 10));
            MailBoxViewRules.PageRange(25, 10, 2, out int start, out int count);
            Assert.AreEqual(20, start);
            Assert.AreEqual(5, count);
        }

        [Test]
        public void TradeSerializer_RoundTripsStateAndRequest()
        {
            var lines = new[] { new TradeOfferLine("equip_a", Guid.NewGuid().ToString("D"), 1), new TradeOfferLine(MarketTradeRules.HERB_ID, null, 7) };
            var state = new TradeStateMessage(Guid.NewGuid(), ETradeState.Countdown, ERejectReason.TradeTooFar, "Beta", lines, null, 10, 20, true,
                false, false, true, 3, 4, 2.5f);
            var writer = new NetworkWriter();
            TradeMessageSerializer.WriteTradeStateMessage(writer, state);
            TradeStateMessage read = TradeMessageSerializer.ReadTradeStateMessage(new NetworkReader(writer.ToArraySegment()));

            Assert.AreEqual(state.TradeId, read.TradeId);
            Assert.AreEqual(ETradeState.Countdown, read.State);
            Assert.AreEqual(2, read.MyLines.Length);
            Assert.IsTrue(read.MyLines[0].IsEquipment);
            Assert.AreEqual(7, read.MyLines[1].Quantity);
            Assert.AreEqual(20, read.PartnerCurrency);
            Assert.AreEqual(4, read.ConfirmGeneration);
            Assert.AreEqual(2.5f, read.CountdownRemaining);

            var request = new TradeRequestMessage(Guid.NewGuid(), state.TradeId, ETradeAction.Accept, null, false, null, 0, 3, 4);
            var requestWriter = new NetworkWriter();
            TradeMessageSerializer.WriteTradeRequestMessage(requestWriter, request);
            TradeRequestMessage readRequest = TradeMessageSerializer.ReadTradeRequestMessage(new NetworkReader(requestWriter.ToArraySegment()));
            Assert.AreEqual(ETradeAction.Accept, readRequest.Action);
            Assert.AreEqual(3, readRequest.OfferRevision);
            Assert.AreEqual(4, readRequest.ConfirmGeneration);
        }

        [Test]
        public void GrowthSerializer_RoundTripsInMarketFlag()
        {
            var equipment = new[]
            {
                new TownEquipmentEntry("i", "equip_a", 0, 0, false, GrowthCodes.EquipmentOrigin.CRAFT, 0, false, false, false, true)
            };
            var snapshot = new GrowthSnapshotMessage(0, 1, 0, 0, new int[5], equipment, null, null, null, null, 0, 0, "v");
            var writer = new NetworkWriter();
            GrowthMessageSerializer.WriteGrowthSnapshotMessage(writer, snapshot);

            GrowthSnapshotMessage read = GrowthMessageSerializer.ReadGrowthSnapshotMessage(new NetworkReader(writer.ToArraySegment()));

            Assert.IsTrue(read.Equipment[0].InMarket);
        }
    }
}
