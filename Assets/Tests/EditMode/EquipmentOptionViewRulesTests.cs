// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class EquipmentOptionViewRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MIN            = 100;
        private const int MAX            = 200;
        private const int STEP           = 10;
        private const int HIGH_TIER      = 7500;
        private const int ESSENCE_COST   = 300;
        private const int SHARD_COST     = 3;
        private const int PITY_THRESHOLD = 10;

        private static readonly int[] G_Pool = { EquipmentOptionRules.KIND_ATTACK_RATE, EquipmentOptionRules.KIND_CRIT_CHANCE };

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void LineText_WithOption_ShowsNameAndPercent()
        {
            Assert.AreEqual("공격력 +2.5%", EquipmentOptionViewRules.LineText(EquipmentOptionRules.KIND_ATTACK_RATE, 250));
            Assert.AreEqual("치명타 확률 +1%", EquipmentOptionViewRules.LineText(EquipmentOptionRules.KIND_CRIT_CHANCE, 100));
        }

        [Test]
        public void LineText_NoOption_ShowsEmptyText()
        {
            Assert.AreEqual("옵션 없음", EquipmentOptionViewRules.LineText(EquipmentOptionRules.KIND_NONE, 0));
        }

        [Test]
        public void RangeText_ShowsPercentRange()
        {
            Assert.AreEqual("1% ~ 2%", EquipmentOptionViewRules.RangeText(MIN, MAX));
            Assert.AreEqual("0.2% ~ 0.5%", EquipmentOptionViewRules.RangeText(20, 50));
        }

        [TestCase(300L, 3L, true)]
        [TestCase(299L, 3L, false)]
        [TestCase(300L, 2L, false)]
        public void CanAfford_ComparesBothCosts(long essence, long shards, bool expected)
        {
            Assert.AreEqual(expected, EquipmentOptionViewRules.CanAfford(ESSENCE_COST, SHARD_COST, essence, shards));
        }

        [Test]
        public void PityText_BeforeThreshold_ShowsRemaining()
        {
            Assert.AreEqual("상급 확정까지 10회", EquipmentOptionViewRules.PityText(0, PITY_THRESHOLD));
            Assert.AreEqual("상급 확정까지 2회", EquipmentOptionViewRules.PityText(8, PITY_THRESHOLD));
        }

        [Test]
        public void PityText_AtLastMiss_ShowsGuaranteed()
        {
            Assert.AreEqual("이번 재설정에서 첫 줄 최고값 확정", EquipmentOptionViewRules.PityText(9, PITY_THRESHOLD));
        }

        [Test]
        public void SlotText_EmptyAndEnhanced()
        {
            Assert.AreEqual("무기  -", EquipmentOptionViewRules.SlotText(0, false, null, 0));
            Assert.AreEqual("장신구  equip_accessory_rare +17", EquipmentOptionViewRules.SlotText(5, true, "equip_accessory_rare", 17));
            Assert.AreEqual("머리  equip_head_common", EquipmentOptionViewRules.SlotText(1, true, "equip_head_common", 0));
        }

        [Test]
        public void IsHighTier_UsesUpperQuarterOfRange()
        {
            Assert.IsTrue(EquipmentOptionViewRules.IsHighTier(EquipmentOptionRules.KIND_ATTACK_RATE, 180, MIN, MAX, HIGH_TIER));
            Assert.IsFalse(EquipmentOptionViewRules.IsHighTier(EquipmentOptionRules.KIND_ATTACK_RATE, 170, MIN, MAX, HIGH_TIER));
            Assert.IsFalse(EquipmentOptionViewRules.IsHighTier(EquipmentOptionRules.KIND_NONE, 0, MIN, MAX, HIGH_TIER));
        }

        // 뽑기와 검증 계산은 백엔드와 전투 서버가 같이 쓰므로 경계를 여기서도 고정한다
        [Test]
        public void PickValue_BoundaryRolls_ReturnMinAndMax()
        {
            Assert.AreEqual(MIN, EquipmentOptionRules.PickValue(0, MIN, MAX, STEP));
            Assert.AreEqual(MAX, EquipmentOptionRules.PickValue(EquipmentOptionRules.ROLL_RANGE - 1, MIN, MAX, STEP));
        }

        [Test]
        public void PickValue_AlwaysOnStepInsideRange()
        {
            for (int roll = 0; roll < EquipmentOptionRules.ROLL_RANGE; roll += 37)
            {
                int value = EquipmentOptionRules.PickValue(roll, MIN, MAX, STEP);

                Assert.IsTrue(EquipmentOptionRules.IsValidLine(EquipmentOptionRules.KIND_ATTACK_RATE, value, G_Pool, MIN, MAX, STEP),
                    "roll " + roll + " -> " + value);
            }
        }

        [Test]
        public void PickKind_BoundaryRolls_ReturnFirstAndLast()
        {
            Assert.AreEqual(G_Pool[0], EquipmentOptionRules.PickKind(0, G_Pool));
            Assert.AreEqual(G_Pool[1], EquipmentOptionRules.PickKind(EquipmentOptionRules.ROLL_RANGE - 1, G_Pool));
            Assert.AreEqual(EquipmentOptionRules.KIND_NONE, EquipmentOptionRules.PickKind(EquipmentOptionRules.ROLL_RANGE, G_Pool));
        }

        [Test]
        public void IsValidLine_NoOptionIsValidAndStrayValueIsNot()
        {
            Assert.IsTrue(EquipmentOptionRules.IsValidLine(EquipmentOptionRules.KIND_NONE, 0, G_Pool, MIN, MAX, STEP));
            Assert.IsFalse(EquipmentOptionRules.IsValidLine(EquipmentOptionRules.KIND_NONE, 100, G_Pool, MIN, MAX, STEP));
            Assert.IsFalse(EquipmentOptionRules.IsValidLine(EquipmentOptionRules.KIND_HEALTH_RATE, 100, G_Pool, MIN, MAX, STEP));
            Assert.IsFalse(EquipmentOptionRules.IsValidLine(EquipmentOptionRules.KIND_ATTACK_RATE, 105, G_Pool, MIN, MAX, STEP));
            Assert.IsFalse(EquipmentOptionRules.IsValidLine(EquipmentOptionRules.KIND_ATTACK_RATE, 210, G_Pool, MIN, MAX, STEP));
        }

        [Test]
        public void IsValidRolls_RequiresFourInRange()
        {
            Assert.IsTrue(EquipmentOptionRules.IsValidRolls(new[] { 0, 9999, 5000, 1 }));
            Assert.IsFalse(EquipmentOptionRules.IsValidRolls(new[] { 0, 9999, 5000 }));
            Assert.IsFalse(EquipmentOptionRules.IsValidRolls(new[] { 0, 10000, 5000, 1 }));
            Assert.IsFalse(EquipmentOptionRules.IsValidRolls(null));
        }
    }
}
