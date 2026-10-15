// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class EquipmentEnhanceRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  RATE_TOLERANCE = 0.0001f;
        private const float  STAT_TOLERANCE = 0.001f;
        private const string RARE_WEAPON    = "equip_weapon_rare";

        private EquipmentCatalogSO _catalog;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _catalog = ScriptableObject.CreateInstance<EquipmentCatalogSO>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_catalog);
        }

        /****************************************
        *                Tests
        ****************************************/

        // +17 시도는 기본 50%, 실패마다 10%p
        [TestCase(16, 0, 0.5f)]
        [TestCase(16, 2, 0.7f)]
        [TestCase(16, 9, 1f)]
        [TestCase(0, 0, 1f)]
        public void SuccessRate_WithFailures_AddsBonusUpToFull(int level, int failures, float expected)
        {
            Assert.AreEqual(expected, EquipmentEnhanceRules.SuccessRate(_catalog, level, failures), RATE_TOLERANCE);
        }

        // +11 시도는 65/75/85/95/100%로 다섯 번째에서 확정한다
        [TestCase(10, 0, 5)]
        [TestCase(10, 3, 2)]
        [TestCase(10, 4, 1)]
        [TestCase(0, 0, 1)]
        [TestCase(24, 0, 10)]
        public void AttemptsUntilGuaranteed_FromFailures_CountsCurrentAttempt(int level, int failures, int expected)
        {
            Assert.AreEqual(expected, EquipmentEnhanceRules.AttemptsUntilGuaranteed(_catalog, level, failures));
        }

        [TestCase(10, 0, 0f)]
        [TestCase(10, 4, 1f)]
        [TestCase(0, 0, 1f)]
        public void PityProgress_ByFailures_ReturnsShareTowardFull(int level, int failures, float expected)
        {
            Assert.AreEqual(expected, EquipmentEnhanceRules.PityProgress(_catalog, level, failures), RATE_TOLERANCE);
        }

        [Test]
        public void FindFailures_MixedEntries_MatchesSlotAndStageOnly()
        {
            var pity = new[]
            {
                new TownPityEntry((byte)EEquipmentSlot.Weapon, 15, 4),
                new TownPityEntry((byte)EEquipmentSlot.Weapon, 16, 2),
                new TownPityEntry((byte)EEquipmentSlot.Head, 16, 7),
            };

            Assert.AreEqual(2, EquipmentEnhanceRules.FindFailures(pity, EEquipmentSlot.Weapon, 16));
            Assert.AreEqual(7, EquipmentEnhanceRules.FindFailures(pity, EEquipmentSlot.Head, 16));
            Assert.AreEqual(0, EquipmentEnhanceRules.FindFailures(pity, EEquipmentSlot.Body, 16));
            Assert.AreEqual(0, EquipmentEnhanceRules.FindFailures(null, EEquipmentSlot.Weapon, 16));
        }

        [Test]
        public void ScaledStat_RareWeaponSeventeen_MatchesGrowthTable()
        {
            Assert.IsTrue(_catalog.TryGetEquipment(RARE_WEAPON, out EquipmentCatalogSO.EquipmentDefinition definition));
            float attack = _catalog.GetSlotBase(EEquipmentSlot.Weapon).Attack;

            // 200 x 1.35 x (1 + 0.04 x 17)
            Assert.AreEqual(453.6f, EquipmentEnhanceRules.ScaledStat(_catalog, definition, attack, 17), STAT_TOLERANCE);
        }

        [Test]
        public void ScaledStat_TrainingEquipment_IsHalfOfCommon()
        {
            Assert.IsTrue(_catalog.TryGetTraining(EEquipmentSlot.Weapon, out EquipmentCatalogSO.EquipmentDefinition definition));
            float attack = _catalog.GetSlotBase(EEquipmentSlot.Weapon).Attack;

            // 200 x 1 x (1 + 0.04 x 10) x 0.5
            Assert.AreEqual(140f, EquipmentEnhanceRules.ScaledStat(_catalog, definition, attack, 10), STAT_TOLERANCE);
        }

        [Test]
        public void DisplayName_GradeOrTraining_UsesPrefixAndSlot()
        {
            Assert.IsTrue(_catalog.TryGetTraining(EEquipmentSlot.Accessory, out EquipmentCatalogSO.EquipmentDefinition training));

            Assert.AreEqual("희귀 무기", EquipmentEnhanceRules.DisplayName(_catalog, RARE_WEAPON));
            Assert.AreEqual("수련용 장신구", EquipmentEnhanceRules.DisplayName(_catalog, training.DefinitionId));
            Assert.AreEqual("알 수 없는 장비", EquipmentEnhanceRules.DisplayName(_catalog, "missing"));
        }

        [Test]
        public void IsMaxLevel_AroundCatalogMax_TrueOnlyAtMax()
        {
            Assert.IsFalse(EquipmentEnhanceRules.IsMaxLevel(_catalog, _catalog.MaxEnhancement - 1));
            Assert.IsTrue(EquipmentEnhanceRules.IsMaxLevel(_catalog, _catalog.MaxEnhancement));
        }
    }
}
