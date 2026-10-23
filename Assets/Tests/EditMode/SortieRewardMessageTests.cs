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
    public sealed class SortieRewardMessageTests
    {
        // 정산 보상 내역을 서버 정산안에서 메시지로, 메시지에서 결과 창 값으로 옮기는 경로를 본다

        /****************************************
        *                Fields
        ****************************************/

        private const string MATERIAL_ID = "item_boss_material_epic";
        private const string TALISMAN_ID = "T01-1A";
        private const string BINDING     = "Account";
        private const string REGION      = "battle_forest";

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Serializer_RoundTrip_KeepsAllValuesAndLines()
        {
            SortieRewardMessage sent = Sample();

            SortieRewardMessage read;
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                writer.WriteSortieRewardMessage(sent);
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    read = reader.ReadSortieRewardMessage();
                }
            }

            Assert.AreEqual(sent.BaseEssence, read.BaseEssence);
            Assert.AreEqual(sent.BossEssence, read.BossEssence);
            Assert.AreEqual(sent.PaidExtraEssence, read.PaidExtraEssence);
            Assert.AreEqual(sent.LostExtraEssence, read.LostExtraEssence);
            Assert.AreEqual(sent.Essence, read.Essence);
            Assert.AreEqual(sent.RefineShards, read.RefineShards);
            Assert.AreEqual(sent.Tickets, read.Tickets);
            Assert.AreEqual(sent.Currency, read.Currency);
            Assert.AreEqual(1, read.Items.Length);
            Assert.AreEqual(MATERIAL_ID, read.Items[0].DefinitionId);
            Assert.AreEqual(3L, read.Items[0].Quantity);
            Assert.AreEqual(1, read.Talismans.Length);
            Assert.AreEqual(TALISMAN_ID, read.Talismans[0].DefinitionId);
        }

        [Test]
        public void Serializer_DefaultMessage_ReadsEmptyLines()
        {
            SortieRewardMessage read;
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                writer.WriteSortieRewardMessage(default);
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    read = reader.ReadSortieRewardMessage();
                }
            }

            Assert.AreEqual(0, read.Items.Length);
            Assert.AreEqual(0, read.Talismans.Length);
        }

        [Test]
        public void PublishRules_Build_CopiesGrantAndComputesLostExtra()
        {
            var grant = new SettlementGrant("p1", 750L, new[] { new SettlementItemGrant(MATERIAL_ID, BINDING, 3L) }, 40L, 1L,
                120L, new[] { new SettlementItemGrant(TALISMAN_ID, BINDING, 2L) });
            var breakdown = new SettlementBreakdown(200L, 100L, 50L, 500L, 750L, false);

            SortieRewardMessage message = SortieRewardPublishRules.Build(grant, in breakdown);

            Assert.AreEqual(200L, message.BaseEssence);
            Assert.AreEqual(500L, message.BossEssence);
            Assert.AreEqual(50L, message.PaidExtraEssence);
            Assert.AreEqual(50L, message.LostExtraEssence, "적립 100 중 50만 받았다");
            Assert.AreEqual(750L, message.Essence);
            Assert.AreEqual(40L, message.RefineShards);
            Assert.AreEqual(1L, message.Tickets);
            Assert.AreEqual(120L, message.Currency);
            Assert.AreEqual(MATERIAL_ID, message.Items[0].DefinitionId);
            Assert.AreEqual(2L, message.Talismans[0].Quantity);
        }

        [Test]
        public void PublishRules_Build_NullLines_GivesEmptyArrays()
        {
            var grant = new SettlementGrant("p1", 10L, null, 0L, 0L, 0L, null);

            SortieRewardMessage message = SortieRewardPublishRules.Build(grant, default);

            Assert.AreEqual(0, message.Items.Length);
            Assert.AreEqual(0, message.Talismans.Length);
            Assert.AreEqual(0L, message.LostExtraEssence);
        }

        [Test]
        public void ResultRules_BuildWithReward_FillsEssenceAndSplitsItems()
        {
            var status = new SortieStatusMessage(ESortiePhase.Ended, ESessionEndReason.Success, false, ESessionEndReason.Success,
                false, ESortieSettlementState.Pending);
            SortieRewardMessage reward = Sample();

            ResultScreenData data = SortieResultRules.Build(in status, REGION, in reward, null);

            Assert.AreEqual(ESessionEndReason.Success, data.Reason);
            Assert.AreEqual(REGION, data.RegionName);
            Assert.AreEqual(700, data.BaseEssence, "기본 줄은 웨이브 정기와 보스 정기의 합이다");
            Assert.AreEqual(50, data.ExtraEssence);
            Assert.AreEqual(50, data.LostEssence);

            Assert.AreEqual(3, data.ConfirmedItems.Count, "정련편, 엽전, 보스 재료");
            Assert.AreEqual(Constants.Session.SETTLEMENT_FORBIDDEN_SHARD_ID, data.ConfirmedItems[0].Id);
            Assert.AreEqual(40, data.ConfirmedItems[0].Count);
            Assert.AreEqual(Constants.Session.SETTLEMENT_FORBIDDEN_CURRENCY_ID, data.ConfirmedItems[1].Id);
            Assert.AreEqual(MATERIAL_ID, data.ConfirmedItems[2].Id);

            Assert.AreEqual(2, data.ChanceItems.Count, "보스 입장권과 부적");
            Assert.AreEqual(Constants.Session.SETTLEMENT_FORBIDDEN_TICKET_ID, data.ChanceItems[0].Id);
            Assert.AreEqual(TALISMAN_ID, data.ChanceItems[1].Id);
        }

        [Test]
        public void ResultRules_BuildWithEmptyReward_ShowsNoItems()
        {
            var status = new SortieStatusMessage(ESortiePhase.Ended, ESessionEndReason.Timeout, false, ESessionEndReason.Success,
                false, ESortieSettlementState.Pending);
            var reward = new SortieRewardMessage(100L, 0L, 0L, 30L, 100L, 0L, 0L, 0L, null, null);

            ResultScreenData data = SortieResultRules.Build(in status, REGION, in reward, null);

            Assert.AreEqual(100, data.BaseEssence);
            Assert.AreEqual(30, data.LostEssence);
            Assert.AreEqual(0, data.ConfirmedItems.Count);
            Assert.AreEqual(0, data.ChanceItems.Count);
        }

        /****************************************
        *                Methods
        ****************************************/

        private static SortieRewardMessage Sample()
        {
            return new SortieRewardMessage(200L, 500L, 50L, 50L, 750L, 40L, 1L, 120L,
                new[] { new SortieRewardLine(MATERIAL_ID, 3L) }, new[] { new SortieRewardLine(TALISMAN_ID, 1L) });
        }
    }
}
