// System
using System;
using System.IO;
using System.Linq;
using System.Reflection;

// Third Party
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Growth;

namespace Shinshi.Backend.Tests.Growth
{
    /// <summary> 옵션 재설정 규칙과 옵션 콘텐츠 검증. 기대값은 옵션 설계의 부위별 후보, 등급별 범위, 천장 표다 </summary>
    public sealed class OptionRerollRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MIN_ROLL = 0;
        private const int MAX_ROLL = EquipmentOptionRules.ROLL_RANGE - 1;

        private static readonly GrowthContent G_Content = GrowthContent.LoadEmbedded();

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void LoadEmbedded_OptionSettings_MatchDesignTables()
        {
            GrowthContent.EquipmentSection equipment = G_Content.Equipment;
            Assert.Equal(4, GrowthContent.SUPPORTED_SCHEMA_VERSION);
            Assert.Equal((300L, 3L), OptionRerollRules.Cost(G_Content));
            Assert.Equal((10, 7500, 10), (equipment.OptionPityThreshold, equipment.OptionHighTierBasisPoints,
                equipment.OptionValueStep));

            int attack = EquipmentOptionRules.KIND_ATTACK_RATE;
            int health = EquipmentOptionRules.KIND_HEALTH_RATE;
            int defense = EquipmentOptionRules.KIND_DEFENSE_RATE;
            int crit = EquipmentOptionRules.KIND_CRIT_CHANCE;
            Assert.Equal(new[] { attack, crit }, G_Content.GetOptionPool(0));
            for (int slot = 1; slot <= 4; ++slot)
            {
                Assert.Equal(new[] { health, defense }, G_Content.GetOptionPool(slot));
            }
            Assert.Equal(new[] { attack, health, defense, crit }, G_Content.GetOptionPool(5));

            (int, int)[] rate = { (50, 100), (100, 200), (150, 300), (200, 400) };
            (int, int)[] critical = { (20, 50), (50, 100), (70, 150), (100, 200) };
            for (int grade = 0; grade < GrowthContent.GRADE_COUNT; ++grade)
            {
                foreach (int kind in new[] { attack, health, defense })
                {
                    Assert.True(G_Content.TryGetOptionRange(kind, grade, out int min, out int max));
                    Assert.Equal(rate[grade], (min, max));
                }

                Assert.True(G_Content.TryGetOptionRange(crit, grade, out int critMin, out int critMax));
                Assert.Equal(critical[grade], (critMin, critMax));
            }

            // 옵션 없음과 모르는 종류, 범위 밖 등급은 범위가 없다
            Assert.False(G_Content.TryGetOptionRange(EquipmentOptionRules.KIND_NONE, 0, out _, out _));
            Assert.False(G_Content.TryGetOptionRange(EquipmentOptionRules.KIND_MAX + 1, 0, out _, out _));
            Assert.False(G_Content.TryGetOptionRange(attack, GrowthContent.GRADE_COUNT, out _, out _));
        }

        [Theory]
        [InlineData("equip_weapon_common", MIN_ROLL, EquipmentOptionRules.KIND_ATTACK_RATE)]
        [InlineData("equip_weapon_common", 4999, EquipmentOptionRules.KIND_ATTACK_RATE)]
        [InlineData("equip_weapon_common", 5000, EquipmentOptionRules.KIND_CRIT_CHANCE)]
        [InlineData("equip_weapon_common", MAX_ROLL, EquipmentOptionRules.KIND_CRIT_CHANCE)]
        [InlineData("equip_head_rare", MIN_ROLL, EquipmentOptionRules.KIND_HEALTH_RATE)]
        [InlineData("equip_feet_epic", MAX_ROLL, EquipmentOptionRules.KIND_DEFENSE_RATE)]
        [InlineData("equip_accessory_legendary", MIN_ROLL, EquipmentOptionRules.KIND_ATTACK_RATE)]
        [InlineData("equip_accessory_legendary", 2500, EquipmentOptionRules.KIND_HEALTH_RATE)]
        [InlineData("equip_accessory_legendary", 7499, EquipmentOptionRules.KIND_DEFENSE_RATE)]
        [InlineData("equip_accessory_legendary", 7500, EquipmentOptionRules.KIND_CRIT_CHANCE)]
        [InlineData("equip_accessory_legendary", MAX_ROLL, EquipmentOptionRules.KIND_CRIT_CHANCE)]
        public void Roll_KindRoll_PicksFromSlotPool(string definitionId, int kindRoll, int expectedKind)
        {
            OptionRerollDecision decision = OptionRerollRules.Roll(G_Content, Definition(definitionId),
                new[] { kindRoll, MIN_ROLL, kindRoll, MAX_ROLL }, 0);

            Assert.Equal(expectedKind, decision.Line1.Kind);
            Assert.Equal(expectedKind, decision.Line2.Kind);
        }

        [Theory]
        // 일반 공격 50~100은 6단계: 난수 0과 1666은 50, 1667부터 60, 9999는 100
        [InlineData("equip_weapon_common", MIN_ROLL, MIN_ROLL, 50)]
        [InlineData("equip_weapon_common", MIN_ROLL, 1666, 50)]
        [InlineData("equip_weapon_common", MIN_ROLL, 1667, 60)]
        [InlineData("equip_weapon_common", MIN_ROLL, MAX_ROLL, 100)]
        // 일반 치명 20~50은 4단계, 전설 치명 100~200은 11단계
        [InlineData("equip_weapon_common", MAX_ROLL, MIN_ROLL, 20)]
        [InlineData("equip_weapon_common", MAX_ROLL, MAX_ROLL, 50)]
        [InlineData("equip_weapon_legendary", MAX_ROLL, MIN_ROLL, 100)]
        [InlineData("equip_weapon_legendary", MAX_ROLL, MAX_ROLL, 200)]
        // 전설 생명 200~400은 21단계
        [InlineData("equip_body_legendary", MIN_ROLL, MIN_ROLL, 200)]
        [InlineData("equip_body_legendary", MIN_ROLL, 5000, 300)]
        [InlineData("equip_body_legendary", MIN_ROLL, MAX_ROLL, 400)]
        public void Roll_ValueRoll_MapsToRangeOnStepGrid(string definitionId, int kindRoll, int valueRoll, int expectedValue)
        {
            // 줄2로 본다. 줄1은 천장이 걸리면 값이 바뀌므로 줄2가 순수한 난수 대응이다
            OptionRerollDecision decision = OptionRerollRules.Roll(G_Content, Definition(definitionId),
                new[] { MIN_ROLL, MIN_ROLL, kindRoll, valueRoll }, 0);

            Assert.Equal(expectedValue, decision.Line2.Value);
        }

        [Fact]
        public void Roll_EveryRollOnEverySlotAndGrade_ProducesValidLines()
        {
            const int ROLL_STRIDE = 37;
            foreach (GrowthContent.EquipmentDefinition definition in G_Content.Equipment.Definitions
                .Where(definition => !definition.IsTraining))
            {
                for (int roll = MIN_ROLL; roll <= MAX_ROLL; roll += ROLL_STRIDE)
                {
                    OptionRerollDecision decision = OptionRerollRules.Roll(G_Content, definition,
                        new[] { roll, MAX_ROLL - roll, MAX_ROLL - roll, roll }, 0);
                    AssertValidLine(definition, decision.Line1);
                    AssertValidLine(definition, decision.Line2);
                }
            }
        }

        [Fact]
        public void Roll_HighTierLine_ResetsMissesOtherwiseCountsUp()
        {
            GrowthContent.EquipmentDefinition weapon = Definition("equip_weapon_common");

            // 일반 공격 50~100의 상급은 90 이상이다(50 + 0.75 x 50 = 87.5)
            OptionRerollDecision low = OptionRerollRules.Roll(G_Content, weapon, new[] { MIN_ROLL, 6666, MIN_ROLL, MIN_ROLL }, 3);
            Assert.Equal((80, 50), (low.Line1.Value, low.Line2.Value));
            Assert.Equal(4, low.NextMisses);
            Assert.False(low.PityApplied);

            OptionRerollDecision firstLineHigh = OptionRerollRules.Roll(G_Content, weapon,
                new[] { MIN_ROLL, 6667, MIN_ROLL, MIN_ROLL }, 3);
            Assert.Equal(90, firstLineHigh.Line1.Value);
            Assert.Equal(0, firstLineHigh.NextMisses);

            OptionRerollDecision secondLineHigh = OptionRerollRules.Roll(G_Content, weapon,
                new[] { MIN_ROLL, MIN_ROLL, MAX_ROLL, MAX_ROLL }, 3);
            Assert.Equal((EquipmentOptionRules.KIND_CRIT_CHANCE, 50), (secondLineHigh.Line2.Kind, secondLineHigh.Line2.Value));
            Assert.Equal(0, secondLineHigh.NextMisses);

            Assert.True(OptionRerollRules.IsHighTier(G_Content, 0, new EquipmentOptionLine(EquipmentOptionRules.KIND_ATTACK_RATE, 90)));
            Assert.False(OptionRerollRules.IsHighTier(G_Content, 0, new EquipmentOptionLine(EquipmentOptionRules.KIND_ATTACK_RATE, 80)));
            Assert.False(OptionRerollRules.IsHighTier(G_Content, 0, new EquipmentOptionLine(EquipmentOptionRules.KIND_NONE, 0)));
        }

        [Fact]
        public void Roll_TenthMissInARow_ForcesLineOneMaxAndResetsMisses()
        {
            GrowthContent.EquipmentDefinition weapon = Definition("equip_weapon_common");
            int[] lowest = { MIN_ROLL, MIN_ROLL, MIN_ROLL, MIN_ROLL };
            int misses = 0;

            // 가장 낮은 값만 아홉 번 뽑으면 누계가 9까지 오르고 천장은 걸리지 않는다
            for (int attempt = 1; attempt < G_Content.Equipment.OptionPityThreshold; ++attempt)
            {
                OptionRerollDecision miss = OptionRerollRules.Roll(G_Content, weapon, lowest, misses);
                Assert.False(miss.PityApplied);
                Assert.Equal((50, 50), (miss.Line1.Value, miss.Line2.Value));
                Assert.Equal(attempt, miss.NextMisses);
                misses = miss.NextMisses;
            }

            Assert.Equal(9, misses);
            OptionRerollDecision forced = OptionRerollRules.Roll(G_Content, weapon, lowest, misses);
            Assert.True(forced.PityApplied);
            Assert.Equal((EquipmentOptionRules.KIND_ATTACK_RATE, 100), (forced.Line1.Kind, forced.Line1.Value));
            Assert.Equal((EquipmentOptionRules.KIND_ATTACK_RATE, 50), (forced.Line2.Kind, forced.Line2.Value));
            Assert.Equal(0, forced.NextMisses);

            // 종류는 난수대로 두고 그 종류의 최댓값을 준다. 누계가 기준을 넘어 있어도 같다
            OptionRerollDecision crit = OptionRerollRules.Roll(G_Content, Definition("equip_weapon_legendary"),
                new[] { MAX_ROLL, MIN_ROLL, MIN_ROLL, MIN_ROLL }, 15);
            Assert.True(crit.PityApplied);
            Assert.Equal((EquipmentOptionRules.KIND_CRIT_CHANCE, 200), (crit.Line1.Kind, crit.Line1.Value));
            Assert.Equal(0, crit.NextMisses);

            Assert.False(OptionRerollRules.IsPityReroll(G_Content, 8));
            Assert.True(OptionRerollRules.IsPityReroll(G_Content, 9));
        }

        [Fact]
        public void Roll_SameInput_GivesSameResult()
        {
            GrowthContent.EquipmentDefinition accessory = Definition("equip_accessory_epic");
            int[] rolls = { 1234, 5678, 9012, 3456 };

            OptionRerollDecision first = OptionRerollRules.Roll(G_Content, accessory, rolls, 2);
            OptionRerollDecision second = OptionRerollRules.Roll(G_Content, accessory, rolls, 2);

            Assert.Equal((first.Line1.Kind, first.Line1.Value, first.Line2.Kind, first.Line2.Value, first.NextMisses),
                (second.Line1.Kind, second.Line1.Value, second.Line2.Kind, second.Line2.Value, second.NextMisses));
        }

        [Fact]
        public void Roll_TrainingEquipmentOrBadInput_Throws()
        {
            GrowthContent.EquipmentDefinition weapon = Definition("equip_weapon_common");
            GrowthContent.EquipmentDefinition training = Definition("equip_weapon_training");
            int[] rolls = { MIN_ROLL, MIN_ROLL, MIN_ROLL, MIN_ROLL };

            Assert.True(OptionRerollRules.CanReroll(weapon));
            Assert.False(OptionRerollRules.CanReroll(training));
            Assert.Throws<ArgumentException>(() => OptionRerollRules.Roll(G_Content, training, rolls, 0));
            Assert.Throws<ArgumentException>(() => OptionRerollRules.Roll(G_Content, weapon, new[] { 0, 0, 0 }, 0));
            Assert.Throws<ArgumentException>(() => OptionRerollRules.Roll(G_Content, weapon, new[] { 0, 0, 0, MAX_ROLL + 1 }, 0));
            Assert.Throws<ArgumentException>(() => OptionRerollRules.Roll(G_Content, weapon, new[] { 0, -1, 0, 0 }, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => OptionRerollRules.Roll(G_Content, weapon, rolls, -1));
        }

        [Fact]
        public void CarryOption_UpperGradeRaisesBelowMinAndKeepsNone()
        {
            GrowthContent.EquipmentDefinition rare = Definition("equip_weapon_rare");
            GrowthContent.EquipmentDefinition epic = Definition("equip_weapon_epic");
            int attack = EquipmentOptionRules.KIND_ATTACK_RATE;
            int crit = EquipmentOptionRules.KIND_CRIT_CHANCE;

            // 희귀 공격 100~200을 영웅 150~300으로: 150 미만은 150으로 올리고 그 이상은 그대로다
            Assert.Equal((attack, 150), Carry(rare, epic, attack, 100));
            Assert.Equal((attack, 150), Carry(rare, epic, attack, 150));
            Assert.Equal((attack, 200), Carry(rare, epic, attack, 200));

            // 희귀 치명 50~100을 영웅 70~150으로
            Assert.Equal((crit, 70), Carry(rare, epic, crit, 50));
            Assert.Equal((crit, 100), Carry(rare, epic, crit, 100));

            // 같은 등급은 값을 건드리지 않고, 옵션 없는 줄은 등급이 올라도 없음이다
            Assert.Equal((attack, 100), Carry(rare, rare, attack, 100));
            Assert.Equal((EquipmentOptionRules.KIND_NONE, 0), Carry(rare, epic, EquipmentOptionRules.KIND_NONE, 0));
            Assert.Equal((EquipmentOptionRules.KIND_NONE, 0), Carry(rare, rare, EquipmentOptionRules.KIND_NONE, 0));

            // 옮긴 줄은 대상 등급에서 유효한 줄이다
            for (int value = 100; value <= 200; value += G_Content.Equipment.OptionValueStep)
            {
                (int kind, int carried) = Carry(rare, epic, attack, value);
                AssertValidLine(epic, new EquipmentOptionLine(kind, carried));
            }
        }

        [Fact]
        public void ToHistoryJson_OptionsAppendedOnlyWhenPresent()
        {
            EquipmentRecord record = new EquipmentRecord
            {
                CharacterId = Guid.Parse("7b4c1c0e-8f69-4d0a-9d55-1f3c8fd1a001"),
                DefinitionId = "equip_weapon_common",
                Origin = GrowthCodes.EquipmentOrigin.CRAFT,
                Bound = true
            };
            string[] baseKeys =
            {
                "characterId", "definitionId", "slot", "grade", "isTraining", "origin", "enhancement", "equipped", "locked",
                "bound"
            };

            Assert.Equal(baseKeys, JObject.Parse(record.ToHistoryJson()).Properties().Select(property => property.Name));

            record.Option2Kind = EquipmentOptionRules.KIND_CRIT_CHANCE;
            record.Option2Value = 30;
            JObject withOptions = JObject.Parse(record.ToHistoryJson());
            Assert.Equal(baseKeys.Concat(new[] { "option1Kind", "option1Value", "option2Kind", "option2Value" }),
                withOptions.Properties().Select(property => property.Name));
            Assert.Equal((0, 0, EquipmentOptionRules.KIND_CRIT_CHANCE, 30), ((int)withOptions["option1Kind"]!,
                (int)withOptions["option1Value"]!, (int)withOptions["option2Kind"]!, (int)withOptions["option2Value"]!));

            EquipmentRecord clone = record.Clone();
            Assert.Equal((record.Option1Kind, record.Option1Value, record.Option2Kind, record.Option2Value),
                (clone.Option1Kind, clone.Option1Value, clone.Option2Kind, clone.Option2Value));
        }

        [Fact]
        public void Parse_InvalidOptionContent_ThrowsInvalidData()
        {
            AssertInvalid(equipment => equipment["optionRerollEssenceCost"] = 0);
            AssertInvalid(equipment => equipment["optionRerollShardCost"] = 0);
            AssertInvalid(equipment => equipment["optionPityThreshold"] = 0);
            AssertInvalid(equipment => equipment["optionHighTierBasisPoints"] = EquipmentOptionRules.VALUE_SCALE + 1);
            AssertInvalid(equipment => equipment["optionHighTierBasisPoints"] = -1);
            AssertInvalid(equipment => equipment["optionValueStep"] = 0);
            AssertInvalid(equipment => equipment.Remove("optionValueStep"));

            // 부위 6개, 빈 후보, 범위 표에 없는 종류
            AssertInvalid(equipment => ((JArray)equipment["optionSlotPools"]!).RemoveAt(0));
            AssertInvalid(equipment => ((JArray)equipment["optionSlotPools"]!)[0] = new JArray());
            AssertInvalid(equipment => ((JArray)equipment["optionSlotPools"]!)[0] = new JArray(EquipmentOptionRules.KIND_MAX + 1));
            AssertInvalid(equipment => ((JArray)equipment["optionSlotPools"]!)[0] = new JArray(EquipmentOptionRules.KIND_NONE));

            // 종류 1~4가 모두 한 번씩, 등급 4칸, 범위는 간격에 맞고 0보다 커야 한다
            AssertInvalid(equipment => ((JArray)equipment["optionRanges"]!).RemoveAt(3));
            AssertInvalid(equipment => equipment["optionRanges"]![3]!["kind"] = EquipmentOptionRules.KIND_ATTACK_RATE);
            AssertInvalid(equipment => ((JArray)equipment["optionRanges"]![0]!["min"]!).RemoveAt(0));
            AssertInvalid(equipment => ((JArray)equipment["optionRanges"]![0]!["max"]!).Add(500));
            AssertInvalid(equipment => equipment["optionRanges"]![0]!["min"]![0] = 55);
            AssertInvalid(equipment => equipment["optionRanges"]![0]!["min"]![0] = 0);
            AssertInvalid(equipment => equipment["optionRanges"]![0]!["max"]![3] = EquipmentOptionRules.MAX_VALUE + 10);
            AssertInvalid(equipment => equipment["optionRanges"]![0]!["max"]![0] = 40);

            // 등급이 오를 때 범위가 내려가면 상위 계승이 옮긴 값이 대상 범위를 벗어난다
            AssertInvalid(equipment =>
            {
                equipment["optionRanges"]![0]!["min"]![1] = 40;
                equipment["optionRanges"]![0]!["max"]![1] = 90;
            });

            // 손대지 않은 내용은 통과한다
            Assert.NotNull(GrowthContent.Parse(ReadEmbeddedJson()));
        }

        private static (int Kind, int Value) Carry(GrowthContent.EquipmentDefinition source,
            GrowthContent.EquipmentDefinition target, int kind, int value)
        {
            EquipmentOptionLine line = InheritanceRules.CarryOption(G_Content, source, target,
                new EquipmentOptionLine(kind, value));
            return (line.Kind, line.Value);
        }

        private static void AssertValidLine(GrowthContent.EquipmentDefinition definition, EquipmentOptionLine line)
        {
            Assert.NotEqual(EquipmentOptionRules.KIND_NONE, line.Kind);
            Assert.True(G_Content.TryGetOptionRange(line.Kind, definition.Grade, out int min, out int max));
            Assert.True(EquipmentOptionRules.IsValidLine(line.Kind, line.Value, G_Content.GetOptionPool(definition.Slot), min, max,
                G_Content.Equipment.OptionValueStep));
        }

        private static void AssertInvalid(Action<JObject> mutate)
        {
            JObject root = JObject.Parse(ReadEmbeddedJson());
            mutate((JObject)root["equipment"]!);
            Assert.Throws<InvalidDataException>(() => GrowthContent.Parse(root.ToString()));
        }

        private static GrowthContent.EquipmentDefinition Definition(string definitionId)
        {
            Assert.True(G_Content.TryGetEquipment(definitionId, out GrowthContent.EquipmentDefinition definition));
            return definition;
        }

        private static string ReadEmbeddedJson()
        {
            Assembly assembly = typeof(GrowthContent).Assembly;
            using Stream stream = assembly.GetManifestResourceStream(GrowthContent.RESOURCE_NAME)!;
            using StreamReader reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
