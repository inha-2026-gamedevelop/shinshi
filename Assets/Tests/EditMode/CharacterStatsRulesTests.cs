// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Tests
{
    public class CharacterStatsRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  ATTACK_TOLERANCE = 0.001f;
        private const string CONTENT_VERSION  = "test";
        private const string TRAINING_SUFFIX  = "training";
        private const string COMMON_SUFFIX    = "common";
        private const string RARE_SUFFIX      = "rare";
        private const string EPIC_SUFFIX      = "epic";
        private const string LEGENDARY_SUFFIX = "legendary";
        private const float  LIFE_STEAL_TOLERANCE = 0.000001f;
        private const int    TRAINING_CAP     = 40;
        private const float  HIGH_BASE_CRIT   = 0.5f;

        private static readonly string[] G_SlotNames = { "weapon", "head", "body", "hands", "feet", "accessory" };

        private PlayerConfigSO     _player;
        private EquipmentCatalogSO _equipment;
        private GrowthConfigSO     _growth;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _player    = ScriptableObject.CreateInstance<PlayerConfigSO>();
            _equipment = ScriptableObject.CreateInstance<EquipmentCatalogSO>();
            _growth    = ScriptableObject.CreateInstance<GrowthConfigSO>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_player);
            Object.DestroyImmediate(_equipment);
            Object.DestroyImmediate(_growth);
        }

        /****************************************
        *                Tests
        ****************************************/

        // 전투 기준안의 고정 권장 기준표(수련 0, 부적 0, 지정 장비 6부위)
        [TestCase(TRAINING_SUFFIX, 0, true, 0, 220f, 1950f)]
        [TestCase(TRAINING_SUFFIX, 0, true, 10, 268f, 2130f)]
        [TestCase(COMMON_SUFFIX, 0, false, 17, 503.2f, 3012f)]
        [TestCase(RARE_SUFFIX, 1, false, 10, 553.6f, 3201f)]
        [TestCase(RARE_SUFFIX, 1, false, 17, 644.32f, 3541f)]
        [TestCase(EPIC_SUFFIX, 2, false, 17, 834.832f, 4255f)]
        public void Compute_ReferenceLoadout_MatchesRecommendedTable(string suffix, int grade, bool isTraining, int enhancement,
            float attack, float maxHealth)
        {
            CharacterStats stats = CharacterStatsRules.Compute(_player, _equipment, _growth,
                Loadout(suffix, grade, isTraining, enhancement, new int[Constants.Combat.TRAINING_STAT_COUNT]));

            Assert.AreEqual(attack, stats.Attack, ATTACK_TOLERANCE);
            Assert.AreEqual(maxHealth, stats.MaxHealth);
            Assert.AreEqual(_player.BaseMaxEnergy, stats.MaxEnergy);
            Assert.AreEqual(_player.MoveSpeed, stats.MoveSpeed);
        }

        [Test]
        public void Compute_RareSeventeen_DefenseAndFixedAccessoryCrit()
        {
            CharacterStats stats = CharacterStatsRules.Compute(_player, _equipment, _growth,
                Loadout(RARE_SUFFIX, 1, false, 17, new int[Constants.Combat.TRAINING_STAT_COUNT]));

            // 방어 100 + 100 x 1.35 x 1.68, 치명 5% + 장신구 고정 2%p(등급과 강화를 곱하지 않는다)
            Assert.AreEqual(326.8f, stats.Defense, ATTACK_TOLERANCE);
            Assert.AreEqual(0.07f, stats.CritChance, ATTACK_TOLERANCE);
        }

        [Test]
        public void Compute_TrainingGear_AccessoryCritIsHalf()
        {
            CharacterStats stats = CharacterStatsRules.Compute(_player, _equipment, _growth,
                Loadout(TRAINING_SUFFIX, 0, true, 10, new int[Constants.Combat.TRAINING_STAT_COUNT]));

            Assert.AreEqual(0.06f, stats.CritChance, ATTACK_TOLERANCE);
        }

        [Test]
        public void Compute_AttackTrainingForty_AddsTwentyPercentOfPreTrainingAttack()
        {
            int[] training = new int[Constants.Combat.TRAINING_STAT_COUNT];
            training[(int)ETrainingStat.Attack] = TRAINING_CAP;

            CharacterStats stats = CharacterStatsRules.Compute(_player, _equipment, _growth,
                Loadout(RARE_SUFFIX, 1, false, 17, training));

            Assert.AreEqual(644.32f * 1.2f, stats.Attack, ATTACK_TOLERANCE);
        }

        [Test]
        public void Compute_HealthAndEnergyTraining_FloorsOnlyFinalValues()
        {
            int[] training = new int[Constants.Combat.TRAINING_STAT_COUNT];
            training[(int)ETrainingStat.Health]    = TRAINING_CAP;
            training[(int)ETrainingStat.MaxEnergy] = TRAINING_CAP;
            training[(int)ETrainingStat.Defense]   = 20;

        // 장신구 기본 1%에 등급 단계마다 0.5%p, 수련용은 일반의 절반. 강화는 곱하지 않는다
        [TestCase(TRAINING_SUFFIX, 0, true, 10, 0.005f)]
        [TestCase(COMMON_SUFFIX, 0, false, 0, 0.01f)]
        [TestCase(RARE_SUFFIX, 1, false, 17, 0.015f)]
        [TestCase(EPIC_SUFFIX, 2, false, 10, 0.02f)]
        [TestCase(LEGENDARY_SUFFIX, 3, false, 25, 0.025f)]
        public void Compute_FullGear_LifeStealComesFromAccessoryGradeOnly(string suffix, int grade, bool isTraining,
            int enhancement, float lifeSteal)
        {
            CharacterStats stats = CharacterStatsRules.Compute(_player, _equipment, _growth,
                Loadout(suffix, grade, isTraining, enhancement, new int[Constants.Combat.TRAINING_STAT_COUNT]));

            Assert.AreEqual(lifeSteal, stats.LifeSteal, LIFE_STEAL_TOLERANCE);
        }

        [Test]
        public void Compute_NoAccessory_LifeStealIsZeroEvenWithHighGradeGear()
        {
            CharacterCombatLoadout loadout = Loadout(LEGENDARY_SUFFIX, 3, false, 25,
                new int[Constants.Combat.TRAINING_STAT_COUNT]);
            loadout.Equipped[(int)EEquipmentSlot.Accessory] = null;

            CharacterStats stats = CharacterStatsRules.Compute(_player, _equipment, _growth, loadout);

            Assert.AreEqual(0f, stats.LifeSteal);
        }

        [Test]
        public void Base_NoGear_LifeStealIsZero()
        {
            Assert.AreEqual(0f, CharacterStatsRules.Base(_player).LifeSteal);
        }

            CharacterStats stats = CharacterStatsRules.Compute(_player, _equipment, _growth,
                Loadout(RARE_SUFFIX, 1, false, 17, training));

            // 생명 B = 3541.2(버림 전) x 1.4 = 4957.68 -> 4957, 기력 100 + 40, 방어 326.8 x 1.4
            Assert.AreEqual(4957f, stats.MaxHealth);
            Assert.AreEqual(140f, stats.MaxEnergy);
            Assert.AreEqual(326.8f * 1.4f, stats.Defense, ATTACK_TOLERANCE);
        }

        [Test]
        public void Compute_CritAboveCap_ClampsToSixtyPercent()
        {
            var serialized = new SerializedObject(_player);
            serialized.FindProperty("_baseCritChance").floatValue = HIGH_BASE_CRIT;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            int[] training = new int[Constants.Combat.TRAINING_STAT_COUNT];
            training[(int)ETrainingStat.CritChance] = TRAINING_CAP;

            CharacterStats stats = CharacterStatsRules.Compute(_player, _equipment, _growth,
                Loadout(RARE_SUFFIX, 1, false, 17, training));

            Assert.AreEqual(_player.CritChanceCap, stats.CritChance, ATTACK_TOLERANCE);
        }

        [Test]
        public void Compute_EmptySlots_UsesBodyOnly()
        {
            var loadout = new CharacterCombatLoadout((int)EPlayerClass.Warrior,
                new CombatEquipmentEntry[Constants.Combat.EQUIPMENT_SLOT_COUNT], new int[Constants.Combat.TRAINING_STAT_COUNT],
                new CombatTalismanEntry[0], CONTENT_VERSION);

            CharacterStats stats = CharacterStatsRules.Compute(_player, _equipment, _growth, loadout);
            CharacterStats body  = CharacterStatsRules.Base(_player);

            Assert.AreEqual(body.Attack, stats.Attack);
            Assert.AreEqual(body.MaxHealth, stats.MaxHealth);
            Assert.AreEqual(_player.BaseAttack, body.Attack);
            Assert.AreEqual(_player.BaseHealth, body.MaxHealth);
        }

        [TestCaseSource(nameof(InvalidLoadouts))]
        public void TryCompute_InvalidLoadout_ReturnsFalseWithReason(string name, CharacterCombatLoadout loadout)
        {
            Assert.IsFalse(CharacterStatsRules.TryCompute(_player, _equipment, _growth, loadout, out _, out string error), name);
            Assert.IsFalse(string.IsNullOrEmpty(error), name);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static IEnumerable<TestCaseData> InvalidLoadouts()
        {
            int[] none = new int[Constants.Combat.TRAINING_STAT_COUNT];
            yield return new TestCaseData("null", null);

            CharacterCombatLoadout unknown = Loadout(RARE_SUFFIX, 1, false, 17, none);
            unknown.Equipped[0] = new CombatEquipmentEntry("equip_unknown", 1, false, 17);
            yield return new TestCaseData("unknown", unknown);

            CharacterCombatLoadout wrongSlot = Loadout(RARE_SUFFIX, 1, false, 17, none);
            wrongSlot.Equipped[0] = new CombatEquipmentEntry("equip_head_rare", 1, false, 17);
            yield return new TestCaseData("wrongSlot", wrongSlot);

            CharacterCombatLoadout wrongGrade = Loadout(RARE_SUFFIX, 2, false, 17, none);
            yield return new TestCaseData("wrongGrade", wrongGrade);

            CharacterCombatLoadout overEnhanced = Loadout(RARE_SUFFIX, 1, false, 26, none);
            yield return new TestCaseData("overEnhanced", overEnhanced);

            int[] overStat = new int[Constants.Combat.TRAINING_STAT_COUNT];
            overStat[(int)ETrainingStat.Attack] = TRAINING_CAP + 1;
            yield return new TestCaseData("overStat", Loadout(RARE_SUFFIX, 1, false, 17, overStat));

            int[] overTotal = { TRAINING_CAP, TRAINING_CAP, TRAINING_CAP, 0, 0 };
            yield return new TestCaseData("overTotal", Loadout(RARE_SUFFIX, 1, false, 17, overTotal));

            yield return new TestCaseData("shortEquipment", new CharacterCombatLoadout(0, new CombatEquipmentEntry[1], none,
                new CombatTalismanEntry[0], CONTENT_VERSION));
        }

        private static CharacterCombatLoadout Loadout(string suffix, int grade, bool isTraining, int enhancement, int[] training)
        {
            var equipped = new CombatEquipmentEntry[Constants.Combat.EQUIPMENT_SLOT_COUNT];
            for (int i = 0; i < equipped.Length; ++i)
            {
                equipped[i] = new CombatEquipmentEntry("equip_" + G_SlotNames[i] + "_" + suffix, grade, isTraining, enhancement);
            }

            return new CharacterCombatLoadout((int)EPlayerClass.Warrior, equipped, training, new CombatTalismanEntry[0],
                CONTENT_VERSION);
        }
    }
}
