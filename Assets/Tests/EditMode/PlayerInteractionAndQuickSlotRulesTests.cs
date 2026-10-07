// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class PlayerInteractionAndQuickSlotRulesTests
    {
        // 상호작용 대상 분류(고정 콜라이더 우선, 아니면 로컬 플레이어 여부)와 퀵슬롯 물약 개수 계산

        [Test]
        public void Classify_UsesFixedCollidersFirstThenLocalPlayer()
        {
            Assert.AreEqual(PlayerInteractionRules.ETarget.Self, PlayerInteractionRules.Classify(true, false, false, false));
            Assert.AreEqual(PlayerInteractionRules.ETarget.Other, PlayerInteractionRules.Classify(false, true, false, false));
            Assert.AreEqual(PlayerInteractionRules.ETarget.Self, PlayerInteractionRules.Classify(false, false, true, true));
            Assert.AreEqual(PlayerInteractionRules.ETarget.Other, PlayerInteractionRules.Classify(false, false, true, false));
            Assert.AreEqual(PlayerInteractionRules.ETarget.None, PlayerInteractionRules.Classify(false, false, false, true));
        }

        [Test]
        public void CountAvailable_SubtractsReservedAndIgnoresOtherDefinitions()
        {
            var potions = new[]
            {
                new TownItemEntry("potion_small", "Account", 10, 3),
                new TownItemEntry("potion_small", "Bound", 2, 5),
                new TownItemEntry("potion_large", "Account", 4, 0),
            };

            Assert.AreEqual(7L, QuickSlotRules.CountAvailable(potions, "potion_small"));
            Assert.AreEqual(4L, QuickSlotRules.CountAvailable(potions, "potion_large"));
            Assert.AreEqual(0L, QuickSlotRules.CountAvailable(potions, "potion_none"));
            Assert.AreEqual(0L, QuickSlotRules.CountAvailable(null, "potion_small"));
            Assert.AreEqual(0L, QuickSlotRules.CountAvailable(potions, null));
        }

        [Test]
        public void Format_ShowsDashUntilSnapshotKnown()
        {
            Assert.AreEqual("-", QuickSlotRules.Format(false, 5));
            Assert.AreEqual("5", QuickSlotRules.Format(true, 5));
            Assert.AreEqual("0", QuickSlotRules.Format(true, -2));
        }
    }
}
