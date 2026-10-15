// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Third Party
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Backend.Growth;
using Shinshi.Contracts;
using Shinshi.Data.Growth;

namespace Shinshi.Backend.Tests.Growth
{
    /// <summary> 내장 성장 콘텐츠와 순수 규칙. 기대값은 SO 에셋과 성장, 계승, 부적 기획 문서의 수치다 </summary>
    public sealed class GrowthRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly GrowthContent G_Content = GrowthContent.LoadEmbedded();

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void LoadEmbedded_AssetValues_MatchDesignTables()
        {
            Assert.Equal("0.1.3", G_Content.ContentVersion);
            Assert.Equal(30, G_Content.Equipment.Definitions.Length);
            Assert.Equal(90, G_Content.Talismans.Effects.Length);
            Assert.Equal(15, G_Content.Talismans.Effects.Count(effect => effect.PlayerClass == ContractCodes.PlayerClass.WARRIOR));
            Assert.Equal(new[] { 1m, 1.35m, 1.8225m, 2.460375m }, G_Content.Equipment.GradeStatMultipliers);
            Assert.Equal(0.04m, G_Content.Equipment.EnhancementStatRate);
            Assert.Equal(0.5m, G_Content.Equipment.TrainingStatRate);
            Assert.Equal(100, G_Content.Equipment.StorageSlotsPerCharacter);
            Assert.Equal(500, G_Content.Equipment.SameGradeInheritCost);
            Assert.Equal(1500, G_Content.Equipment.UpperGradeInheritCost);
            Assert.Equal(new[] { 1m, 1.15m, 1.3m, 1.45m, 1.6m }, G_Content.Talismans.LevelBonusMultipliers);
            Assert.Equal(new[] { 0.5m, 0.65m, 0.8m, 1m }, G_Content.Growth.RegisterSuccessRates);
            Assert.Equal(150, G_Content.Growth.RegisterEssenceCost);
            Assert.Equal(50, G_Content.Growth.CardDisassembleEssence);
            Assert.Equal(new[] { (1L, 300L), (1L, 600L), (2L, 1000L), (3L, 1600L) },
                G_Content.Growth.EffectUpgradeCosts.Select(cost => (cost.CardCost, cost.EssenceCost)));

            // 6부위 합계: 공격 240, 생명 900, 방어 100, 치명 2%p
            Assert.Equal(240m, G_Content.Equipment.SlotBaseStats.Sum(slot => slot.Attack));
            Assert.Equal(900m, G_Content.Equipment.SlotBaseStats.Sum(slot => slot.Health));
            Assert.Equal(100m, G_Content.Equipment.SlotBaseStats.Sum(slot => slot.Defense));
            Assert.Equal(0.02m, G_Content.Equipment.SlotBaseStats.Sum(slot => slot.CritChance));

            // 일반, 희귀, 영웅, 전설 6부위 재료 합계 20, 28, 40, 52와 장신구 경매 재료 1개
            for (int grade = 0; grade < GrowthContent.GRADE_COUNT; ++grade)
            {
                GrowthContent.EquipmentDefinition[] crafted = G_Content.Equipment.Definitions
                    .Where(definition => !definition.IsTraining && (definition.Grade == grade)).ToArray();
                Assert.Equal(new[] { 20, 28, 40, 52 }[grade], crafted.Sum(definition => definition.BossMaterialCount));
                Assert.Equal(1, crafted.Sum(definition => definition.AuctionMaterialCount));
            }
        }

        [Fact]
        public void LoadEmbedded_SixClasses_HaveFifteenEffectsEachAndTwentyFourSkills()
        {
            Assert.Equal(90, G_Content.Talismans.Effects.Length);
            for (int playerClass = 0; playerClass < GrowthContent.PLAYER_CLASS_COUNT; ++playerClass)
            {
                Assert.Equal(15, G_Content.Talismans.Effects.Count(effect => effect.PlayerClass == playerClass));
            }

            Assert.Equal(GrowthContent.REGULAR_SKILL_COUNT,
                G_Content.Talismans.Effects.Select(effect => effect.SkillId).Distinct().Count());
            Assert.Equal(12, G_Content.Talismans.Effects.Where(effect => effect.PlayerClass <= 2)
                .Select(effect => effect.SkillId).Distinct().Count());
            Assert.Equal(12, G_Content.Talismans.Effects.Where(effect => effect.PlayerClass >= 3)
                .Select(effect => effect.SkillId).Distinct().Count());

            // 한 기술의 효과는 한 직업에만 속한다
            Assert.All(G_Content.Talismans.Effects.GroupBy(effect => effect.SkillId),
                group => Assert.Single(group.Select(effect => effect.PlayerClass).Distinct()));
        }

        [Theory]
        [InlineData("T01-1A", 0)]
        [InlineData("T12-3A", 2)]
        [InlineData("T13-1A", 3)]
        [InlineData("T17-1A", 4)]
        [InlineData("T24-3A", 5)]
        public void TryGetEffect_KnownIds_ResolveToTheirClass(string effectId, int playerClass)
        {
            Assert.True(G_Content.TryGetEffect(effectId, out GrowthContent.TalismanEffect effect));
            Assert.Equal(playerClass, effect.PlayerClass);
        }

        [Theory]
        [InlineData("T25-1A")]
        [InlineData("T00-1A")]
        [InlineData("T13-4A")]
        public void TryGetEffect_UnknownIds_AreRejected(string effectId)
        {
            Assert.False(G_Content.TryGetEffect(effectId, out _));
        }

        [Fact]
        public void Parse_EffectOfClassSix_ThrowsInvalidData()
        {
            JObject content = JObject.Parse(ReadEmbeddedJson());
            ((JArray)content["talismans"]!["effects"]!)[0]["playerClass"] = 6;

            Assert.Throws<InvalidDataException>(() => GrowthContent.Parse(content.ToString()));
        }

        [Fact]
        public void Parse_EffectCountedUnderWrongClass_ThrowsInvalidData()
        {
            // 새 직업 효과를 다른 직업으로 옮기면 직업별 15개가 깨진다
            JObject content = JObject.Parse(ReadEmbeddedJson());
            JToken moved = ((JArray)content["talismans"]!["effects"]!).First(effect => (string?)effect["definitionId"] == "T13-1A");
            moved["playerClass"] = 0;

            Assert.Throws<InvalidDataException>(() => GrowthContent.Parse(content.ToString()));
        }

        [Fact]
        public void Parse_EffectIdTierMismatch_ThrowsInvalidData()
        {
            JObject content = JObject.Parse(ReadEmbeddedJson());
            JToken effect = ((JArray)content["talismans"]!["effects"]!).First(entry => (string?)entry["definitionId"] == "T13-1A");
            effect["tier"] = 1;

            Assert.Throws<InvalidDataException>(() => GrowthContent.Parse(content.ToString()));
        }

        [Fact]
        public void LoadEmbedded_Grants_MatchProgressionCatalog()
        {
            GrowthContent.GrantBundle trialCharacter = G_Content.GetCharacterGrant(EGrowthContentProfile.Trial);
            Assert.Equal(500, trialCharacter.Items.Single(item => item.ItemId == GrowthAssets.ESSENCE_ITEM_ID).Quantity);
            Assert.Equal(6, trialCharacter.Equipment.Length);
            Assert.All(trialCharacter.Equipment, equipment => Assert.Equal(17, equipment.Enhancement));
            Assert.All(trialCharacter.Equipment, equipment => Assert.EndsWith("_rare", equipment.EquipmentId));

            GrowthContent.GrantBundle standardCharacter = G_Content.GetCharacterGrant(EGrowthContentProfile.Standard);
            Assert.All(standardCharacter.Equipment, equipment => Assert.EndsWith("_training", equipment.EquipmentId));

            GrowthContent.GrantBundle trialAccount = G_Content.GetAccountGrant(EGrowthContentProfile.Trial);
            Assert.Equal(100, trialAccount.ActionEnergy);
            Assert.Equal(new[] { ("item_potion_large", 9L), ("item_potion_small", 9L) },
                trialAccount.Items.Select(item => (item.ItemId, item.Quantity)).OrderBy(item => item.ItemId));
            Assert.Equal(new[] { ("item_potion_small", 9L) }, G_Content.GetAccountGrant(EGrowthContentProfile.Standard)
                .Items.Select(item => (item.ItemId, item.Quantity)));
        }

        [Fact]
        public void Parse_WrongSchemaOrMissingEffects_ThrowsInvalidData()
        {
            string json = ReadEmbeddedJson();
            JObject wrongSchema = JObject.Parse(json);
            wrongSchema["schemaVersion"] = GrowthContent.SUPPORTED_SCHEMA_VERSION + 1;
            Assert.Throws<InvalidDataException>(() => GrowthContent.Parse(wrongSchema.ToString()));

            JObject missingEffect = JObject.Parse(json);
            ((JArray)missingEffect["talismans"]!["effects"]!).RemoveAt(0);
            Assert.Throws<InvalidDataException>(() => GrowthContent.Parse(missingEffect.ToString()));

            JObject unknownField = JObject.Parse(json);
            unknownField["unexpected"] = 1;
            Assert.ThrowsAny<Exception>(() => GrowthContent.Parse(unknownField.ToString()));
        }

        [Fact]
        public void Training_CostsAndCaps_MatchFormula()
        {
            Assert.Equal(59500L, TrainingRules.InvestCost(G_Content, 0, 100));
            Assert.Equal(1090L, TrainingRules.StepCost(G_Content, 100));
            Assert.Equal(100L, TrainingRules.StepCost(G_Content, 1));

            // 누적 5N^2 + 95N
            Assert.Equal((5L * 30 * 30) + (95L * 30), TrainingRules.InvestCost(G_Content, 0, 30));
            Assert.Equal(TrainingRules.InvestCost(G_Content, 0, 40) - TrainingRules.InvestCost(G_Content, 0, 10),
                TrainingRules.InvestCost(G_Content, 10, 30));
            Assert.Equal(2200L, TrainingRules.RespecCost(G_Content, 100));
            Assert.Equal(0L, TrainingRules.RespecCost(G_Content, 0));

            int[] full = { 40, 40, 20, 0, 0 };
            Assert.Equal(ContractCodes.RejectReason.INVALID_TARGET, TrainingRules.CheckInvest(G_Content, full, 3, 1));
            Assert.Equal(ContractCodes.RejectReason.INVALID_TARGET,
                TrainingRules.CheckInvest(G_Content, new[] { 40, 0, 0, 0, 0 }, 0, 1));
            Assert.Equal(ContractCodes.RejectReason.NONE, TrainingRules.CheckInvest(G_Content, new[] { 39, 0, 0, 0, 0 }, 0, 1));
            Assert.Equal(ContractCodes.RejectReason.INVALID_TARGET,
                TrainingRules.CheckRespec(G_Content, full, new[] { 41, 39, 20, 0, 0 }));
            Assert.Equal(ContractCodes.RejectReason.INVALID_TARGET,
                TrainingRules.CheckRespec(G_Content, full, new[] { 40, 40, 19, 0, 0 }));
            Assert.Equal(ContractCodes.RejectReason.NONE, TrainingRules.CheckRespec(G_Content, full, new[] { 20, 20, 20, 20, 20 }));
        }

        /// <summary> int 끝 근처 steps는 덧셈이 넘쳐 상한 검사를 지나고 비용이 0이 될 수 있었다. 총 상한 초과로 비용 없이 거절한다 </summary>
        [Theory]
        [InlineData(int.MaxValue)]
        [InlineData(int.MaxValue - 1)]
        [InlineData(101)]
        public void Training_StepsBeyondTotalCapOrOverflow_RejectsAndCostThrows(int steps)
        {
            Assert.Equal(ContractCodes.RejectReason.INVALID_TARGET,
                TrainingRules.CheckInvest(G_Content, new[] { 1, 0, 0, 0, 0 }, 0, steps));
            Assert.Equal(ContractCodes.RejectReason.INVALID_TARGET,
                TrainingRules.CheckInvest(G_Content, new[] { 0, 0, 0, 0, 0 }, 1, steps));
            Assert.Throws<ArgumentOutOfRangeException>(() => TrainingRules.InvestCost(G_Content, 1, steps));
        }

        [Theory]
        [InlineData(0, 150, 2)]
        [InlineData(16, 950, 5)]
        [InlineData(24, 1350, 6)]
        [InlineData(5, 400, 3)]
        [InlineData(20, 1150, 6)]
        public void Enhancement_AttemptCost_IsHundredPlusFiftyNAndShardSteps(int current, long essence, long shards)
        {
            Assert.Equal((essence, shards), EnhancementRules.AttemptCost(G_Content, current));
            int target = current + 1;
            Assert.Equal(100 + (50 * target), essence);
            Assert.Equal(2 + ((target - 1) / 5), shards);
        }

        [Fact]
        public void Enhancement_SuccessRate_AddsTenPointsPerFailureUpToCertain()
        {
            Assert.Equal(new[] { 6500, 7500, 8500, 9500, 10000, 10000 },
                Enumerable.Range(0, 6).Select(failures => EnhancementRules.SuccessBasisPoints(G_Content, 10, failures)));
            Assert.Equal(10000, EnhancementRules.SuccessBasisPoints(G_Content, 0, 0));
            Assert.Equal(1000, EnhancementRules.SuccessBasisPoints(G_Content, 24, 0));
            Assert.Equal(10000, EnhancementRules.SuccessBasisPoints(G_Content, 24, 9));
            Assert.False(EnhancementRules.CanEnhance(GrowthContent.MAX_ENHANCEMENT));
            Assert.True(EnhancementRules.IsSuccess(5000, 4999));
            Assert.False(EnhancementRules.IsSuccess(5000, 5000));
            Assert.True(EnhancementRules.IsSuccess(10000, 9999));
        }

        [Theory]
        [InlineData(17, 10)]
        [InlineData(18, 11)]
        [InlineData(19, 12)]
        [InlineData(20, 13)]
        [InlineData(21, 14)]
        [InlineData(22, 15)]
        [InlineData(23, 16)]
        [InlineData(24, 18)]
        [InlineData(25, 20)]
        public void Inheritance_UpperGrade_UsesNineRowTable(int source, int result)
        {
            InheritanceDecision decision = InheritanceRules.Evaluate(G_Content, Item("equip_weapon_rare", source),
                Item("equip_weapon_epic", 0));
            Assert.True(decision.IsAccepted);
            Assert.Equal(result, decision.ResultLevel);
            Assert.Equal(1500, decision.EssenceCost);

            // 상위 계승 뒤 능력치 비율은 1.35 x (1 + 0.04 x 결과) / (1 + 0.04 x 원본) 이며 모두 1을 넘는다
            EquipmentStats before = EquipmentStatRules.Compute(G_Content, Definition("equip_weapon_rare"), source);
            EquipmentStats after = EquipmentStatRules.Compute(G_Content, Definition("equip_weapon_epic"), result);
            Assert.Equal(1.35m * (1m + (0.04m * result)) / (1m + (0.04m * source)), after.Attack / before.Attack);
            Assert.True(after.Attack > before.Attack);
        }

        [Fact]
        public void Inheritance_RejectsDropsSkipsAndUnmovableSources()
        {
            // 같은 등급은 단계를 그대로, 0강 원본은 거절
            InheritanceDecision same = InheritanceRules.Evaluate(G_Content, Item("equip_head_rare", 12), Item("equip_head_rare", 0));
            Assert.True(same.IsAccepted);
            Assert.Equal(12, same.ResultLevel);
            Assert.Equal(500, same.EssenceCost);
            AssertRejected(ContractCodes.RejectReason.INVALID_INHERITANCE, Item("equip_head_rare", 0), Item("equip_head_rare", 0));

            // 상위는 +17부터, 한 등급 위로만, 아래 등급과 전설 상위는 없다
            AssertRejected(ContractCodes.RejectReason.INVALID_INHERITANCE, Item("equip_head_rare", 16), Item("equip_head_epic", 0));
            AssertRejected(ContractCodes.RejectReason.INVALID_INHERITANCE, Item("equip_head_common", 25),
                Item("equip_head_epic", 0));
            AssertRejected(ContractCodes.RejectReason.INVALID_INHERITANCE, Item("equip_head_epic", 20), Item("equip_head_rare", 0));
            Assert.True(InheritanceRules.Evaluate(G_Content, Item("equip_head_legendary", 25),
                Item("equip_head_legendary", 0)).IsAccepted);

            // 대상은 같은 부위 +0 미착용
            AssertRejected(ContractCodes.RejectReason.INVALID_INHERITANCE, Item("equip_head_rare", 17), Item("equip_body_epic", 0));
            AssertRejected(ContractCodes.RejectReason.INVALID_INHERITANCE, Item("equip_head_rare", 17), Item("equip_head_epic", 1));
            AssertRejected(ContractCodes.RejectReason.INVALID_INHERITANCE, Item("equip_head_rare", 17),
                Item("equip_head_epic", 0, equipped: true));

            // 같은 등급이라도 제작 원본을 수련용 대상으로 옮기면 능력치가 내려가므로 거절
            AssertRejected(ContractCodes.RejectReason.INVALID_INHERITANCE, Item("equip_feet_common", 5),
                Item("equip_feet_training", 0));
            Assert.True(InheritanceRules.Evaluate(G_Content, Item("equip_feet_training", 5), Item("equip_feet_common", 0)).IsAccepted);

            // 착용, 잠금 원본은 묶인 장비
            AssertRejected(ContractCodes.RejectReason.BOUND_ITEM, Item("equip_head_rare", 17, equipped: true),
                Item("equip_head_epic", 0));
            AssertRejected(ContractCodes.RejectReason.BOUND_ITEM, Item("equip_head_rare", 17, locked: true),
                Item("equip_head_epic", 0));
        }

        [Fact]
        public void Equipment_Stats_ApplyGradeEnhancementAndTrainingRate()
        {
            // 정규 희귀 6부위 +17 장비 공격 합 544.32, 영웅 6부위 +10 612.36
            decimal rare = G_Content.Equipment.Definitions.Where(definition => !definition.IsTraining && (definition.Grade == 1))
                .Sum(definition => EquipmentStatRules.Compute(G_Content, definition, 17).Attack);
            decimal epic = G_Content.Equipment.Definitions.Where(definition => !definition.IsTraining && (definition.Grade == 2))
                .Sum(definition => EquipmentStatRules.Compute(G_Content, definition, 10).Attack);
            Assert.Equal(544.32m, rare);
            Assert.Equal(612.36m, epic);

            EquipmentStats training = EquipmentStatRules.Compute(G_Content, Definition("equip_accessory_training"), 0);
            EquipmentStats common = EquipmentStatRules.Compute(G_Content, Definition("equip_accessory_common"), 0);
            Assert.Equal(common.Attack / 2, training.Attack);
            Assert.Equal(0.01m, training.CritChance);
            Assert.Equal(0.02m, EquipmentStatRules.Compute(G_Content, Definition("equip_accessory_legendary"), 25).CritChance);
        }

        [Fact]
        public void Craft_CostAndRefund_FollowCatalog()
        {
            Assert.Equal(new[] { ("item_boss_material_common", 3L), ("item_auction_material_common", 1L) },
                CraftRules.CraftCost(Definition("equip_accessory_common")));
            Assert.Equal(new[] { ("item_boss_material_legendary", 10L) }, CraftRules.CraftCost(Definition("equip_weapon_legendary")));
            Assert.False(CraftRules.IsCraftable(Definition("equip_weapon_training")));
            Assert.Equal(1L, CraftRules.DismantleRefund(G_Content, Definition("equip_weapon_common")));
            Assert.Equal(0L, CraftRules.DismantleRefund(G_Content, Definition("equip_head_common")));
            Assert.Equal(2L, CraftRules.DismantleRefund(G_Content, Definition("equip_weapon_legendary")));
            Assert.Equal(0L, CraftRules.DismantleRefund(G_Content, Definition("equip_weapon_training")));
        }

        [Fact]
        public void Talisman_RegisterRatesAndLoadoutChecks()
        {
            Assert.Equal(new[] { 5000, 6500, 8000, 10000, 10000 },
                Enumerable.Range(0, 5).Select(failures => TalismanRules.RegisterSuccessBasisPoints(G_Content, failures)));

            Dictionary<string, int> learned = new Dictionary<string, int> { ["T01-1A"] = 1, ["T01-2B"] = 3, ["T02-1A"] = 0 };
            Assert.Equal(ContractCodes.RejectReason.NONE, TalismanRules.CheckLoadout(G_Content, ContractCodes.PlayerClass.WARRIOR,
                new[] { Entry("skill_warrior_wide_slash", 0, "T01-1A"), Entry("skill_warrior_wide_slash", 1, "T01-2B") }, learned));
            Assert.Equal(ContractCodes.RejectReason.MISSING_UNLOCK, TalismanRules.CheckLoadout(G_Content,
                ContractCodes.PlayerClass.WARRIOR, new[] { Entry("skill_warrior_dash", 0, "T02-1A") }, learned));
            Assert.Equal(ContractCodes.RejectReason.INVALID_TARGET, TalismanRules.CheckLoadout(G_Content,
                ContractCodes.PlayerClass.SORCERER, new[] { Entry("skill_warrior_wide_slash", 0, "T01-1A") }, learned));
            Assert.Equal(ContractCodes.RejectReason.INVALID_TARGET, TalismanRules.CheckLoadout(G_Content,
                ContractCodes.PlayerClass.WARRIOR, new[] { Entry("skill_warrior_wide_slash", 1, "T01-1A") }, learned));
            Assert.Equal(ContractCodes.RejectReason.INVALID_TARGET, TalismanRules.CheckLoadout(G_Content,
                ContractCodes.PlayerClass.WARRIOR, new[]
                {
                    Entry("skill_warrior_wide_slash", 0, "T01-1A"), Entry("skill_warrior_wide_slash", 0, "T01-1B")
                }, learned));
        }

        [Fact]
        public void Progression_TrialUnlocks_FollowCatalogFlags()
        {
            AccountProgressState fresh = new AccountProgressState(false, 0, 0,
                Array.Empty<(EAccountProgressKind, string)>());
            Dictionary<string, bool> regions = ProgressionRules.Regions(G_Content, EGrowthContentProfile.Trial, fresh)
                .ToDictionary(view => view.TargetId, view => view.Unlocked);
            Assert.False(regions["region_forest"]);
            Assert.True(regions["region_wolbaek_barrier"]);
            Assert.False(regions["region_ruins"]);
            Assert.True(ProgressionRules.BossBattles(G_Content, EGrowthContentProfile.Trial, fresh)
                .Single(view => view.TargetId == "battle_wolbaek_easy").Unlocked);

            AccountProgressState intro = new AccountProgressState(true, 0, 1,
                new[] { (EAccountProgressKind.RegionCleared, "region_forest") });
            ProgressTargetView forest = ProgressionRules.Regions(G_Content, EGrowthContentProfile.Trial, intro)
                .Single(view => view.TargetId == "region_forest");
            Assert.True(forest.Unlocked);
            Assert.True(forest.Cleared);

            // 정식판은 체험판 선해금이 없어 흔적 3개와 철각 하드 처치가 필요하다
            Assert.False(ProgressionRules.BossBattles(G_Content, EGrowthContentProfile.Standard, intro)
                .Single(view => view.TargetId == "battle_wolbaek_easy").Unlocked);
            AccountProgressState veteran = new AccountProgressState(true, 3, 2,
                new[] { (EAccountProgressKind.BattleCleared, "battle_cheolgak_hard") });
            Assert.True(ProgressionRules.BossBattles(G_Content, EGrowthContentProfile.Standard, veteran)
                .Single(view => view.TargetId == "battle_wolbaek_easy").Unlocked);
        }

        [Theory]
        [InlineData(null, EGrowthContentProfile.Trial)]
        [InlineData("", EGrowthContentProfile.Trial)]
        [InlineData("Trial", EGrowthContentProfile.Trial)]
        [InlineData("standard", EGrowthContentProfile.Standard)]
        public void GrowthOptions_ContentProfile_DefaultsToTrial(string? value, EGrowthContentProfile expected)
        {
            Assert.Equal(expected, new GrowthOptions { ContentProfile = value! }.ResolveProfile());
            Assert.Throws<InvalidOperationException>(() => new GrowthOptions { ContentProfile = "Beta" }.ResolveProfile());
            Assert.Throws<InvalidOperationException>(() => new GrowthOptions { ContentProfile = "7" }.ResolveProfile());
        }

        private static void AssertRejected(int reason, InheritanceItem source, InheritanceItem target)
        {
            InheritanceDecision decision = InheritanceRules.Evaluate(G_Content, source, target);
            Assert.False(decision.IsAccepted);
            Assert.Equal(reason, decision.ReasonCode);
        }

        private static InheritanceItem Item(string definitionId, int enhancement, bool equipped = false, bool locked = false)
        {
            return new InheritanceItem(Guid.NewGuid(), Definition(definitionId), enhancement, equipped, locked);
        }

        private static GrowthContent.EquipmentDefinition Definition(string definitionId)
        {
            Assert.True(G_Content.TryGetEquipment(definitionId, out GrowthContent.EquipmentDefinition definition));
            return definition;
        }

        private static TalismanLoadoutEntry Entry(string skillId, int tier, string effectId)
        {
            return new TalismanLoadoutEntry(skillId, tier, effectId);
        }

        private static string ReadEmbeddedJson()
        {
            using Stream stream = typeof(GrowthContent).Assembly.GetManifestResourceStream(GrowthContent.RESOURCE_NAME)!;
            using StreamReader reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
