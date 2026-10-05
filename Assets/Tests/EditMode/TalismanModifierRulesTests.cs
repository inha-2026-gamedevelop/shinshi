// System
using System.Collections.Generic;

// Unity
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
    public class TalismanModifierRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float TOLERANCE = 0.0001f;
        private const int   LEVEL_ONE = 1;
        private const int   LEVEL_MAX = 5;

        private const string WIDE_SLASH     = "skill_warrior_wide_slash";
        private const string DASH           = "skill_warrior_dash";
        private const string GROUND_SLAM    = "skill_warrior_ground_slam";
        private const string COUNTER        = "skill_warrior_counter";
        private const string SPELL_BOLT     = "skill_sorcerer_spell_bolt";
        private const string FLAME_FIELD    = "skill_sorcerer_flame_field";
        private const string BINDING_FIELD  = "skill_sorcerer_binding_field";
        private const string LIGHTNING      = "skill_sorcerer_lightning";
        private const string SUMMON_BEAST   = "skill_summoner_summon_beast";
        private const string SUMMON_SPIRIT  = "skill_summoner_summon_spirit";
        private const string SPIRIT_STRIKE  = "skill_summoner_spirit_strike";
        private const string TARGET_BARRIER = "skill_summoner_target_barrier";

        private SkillCatalogSO    _skills;
        private TalismanCatalogSO _talismans;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _skills    = ScriptableObject.CreateInstance<SkillCatalogSO>();
            _talismans = ScriptableObject.CreateInstance<TalismanCatalogSO>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_skills);
            Object.DestroyImmediate(_talismans);
        }

        /****************************************
        *                Tests
        ****************************************/

        // 전투 기준안 "부적 45개 수치 연결" 표의 레벨 1과 레벨 5 값
        [TestCase("T01-1A", WIDE_SLASH, "cooldown", 5.4f, 5.04f)]
        [TestCase("T01-1B", WIDE_SLASH, "energy", 16f, 13.6f)]
        [TestCase("T01-2A", WIDE_SLASH, "arc", 140f, 152f)]
        [TestCase("T01-2B", WIDE_SLASH, "arc", 80f, 80f)]
        [TestCase("T01-2B", WIDE_SLASH, "damage", 1.2f, 1.32f)]
        [TestCase("T01-3A", WIDE_SLASH, "added", 0.6f, 0.96f)]
        [TestCase("T01-3A", WIDE_SLASH, "primary", 2.2f, 2.2f)]
        [TestCase("T01-3A", WIDE_SLASH, "delay", 0.15f, 0.15f)]
        [TestCase("T01-3B", WIDE_SLASH, "primary", 2.4f, 2.52f)]
        [TestCase("T01-3B", WIDE_SLASH, "radius", 3f, 3f)]
        [TestCase("T02-1A", DASH, "energy", 16f, 13.6f)]
        [TestCase("T02-2A", DASH, "range", 5f, 5.6f)]
        [TestCase("T02-3A", DASH, "primary", 2.0f, 2.12f)]
        [TestCase("T03-1A", GROUND_SLAM, "cooldown", 10.8f, 10.08f)]
        [TestCase("T03-2A", GROUND_SLAM, "damage", 1.15f, 1.24f)]
        [TestCase("T03-3A", GROUND_SLAM, "added", 1.2f, 1.92f)]
        [TestCase("T03-3A", GROUND_SLAM, "coreRadius", 4f, 4f)]
        [TestCase("T03-3A", GROUND_SLAM, "delay", 0.2f, 0.2f)]
        [TestCase("T04-1A", COUNTER, "energy", 20f, 17f)]
        [TestCase("T04-2A", COUNTER, "active", 0.6f, 0.66f)]
        [TestCase("T04-3A", COUNTER, "primary", 4.0f, 4.3f)]
        [TestCase("T04-3A", COUNTER, "length", 8f, 8f)]
        [TestCase("T05-1A", SPELL_BOLT, "energy", 12f, 10.2f)]
        [TestCase("T05-2A", SPELL_BOLT, "range", 12f, 13.2f)]
        [TestCase("T05-3A", SPELL_BOLT, "primary", 1.4f, 1.52f)]
        [TestCase("T05-3A", SPELL_BOLT, "count", 3f, 3f)]
        [TestCase("T06-1A", FLAME_FIELD, "cooldown", 10.8f, 10.08f)]
        [TestCase("T06-1B", FLAME_FIELD, "energy", 24f, 20.4f)]
        [TestCase("T06-2A", FLAME_FIELD, "radius", 3f, 3.3f)]
        [TestCase("T06-2B", FLAME_FIELD, "radius", 2f, 2f)]
        [TestCase("T06-2B", FLAME_FIELD, "damage", 1.2f, 1.32f)]
        [TestCase("T06-3A", FLAME_FIELD, "primary", 5.5f, 6.1f)]
        [TestCase("T06-3A", FLAME_FIELD, "delay", 0.8f, 0.8f)]
        [TestCase("T06-3B", FLAME_FIELD, "added", 1.5f, 2.4f)]
        [TestCase("T07-1A", BINDING_FIELD, "energy", 20f, 17f)]
        [TestCase("T07-2A", BINDING_FIELD, "radius", 3.5f, 3.8f)]
        [TestCase("T07-3A", BINDING_FIELD, "root", 0.2f, 0.32f)]
        [TestCase("T08-1A", LIGHTNING, "cooldown", 9f, 8.4f)]
        [TestCase("T08-2A", LIGHTNING, "radius", 2.5f, 2.8f)]
        [TestCase("T08-3A", LIGHTNING, "added", 2f, 3.2f)]
        [TestCase("T08-3A", LIGHTNING, "delay", 0.35f, 0.35f)]
        [TestCase("T09-1A", SUMMON_BEAST, "duration", 24f, 26.4f)]
        [TestCase("T09-1B", SUMMON_BEAST, "energy", 24f, 20.4f)]
        [TestCase("T09-2A", SUMMON_BEAST, "interval", 0.675f, 0.63f)]
        [TestCase("T09-2B", SUMMON_BEAST, "marked", 0.1f, 0.16f)]
        [TestCase("T09-3A", SUMMON_BEAST, "added", 0.15f, 0.24f)]
        [TestCase("T09-3A", SUMMON_BEAST, "delay", 0.12f, 0.12f)]
        [TestCase("T09-3B", SUMMON_BEAST, "primary", 0.3f, 0.33f)]
        [TestCase("T09-3B", SUMMON_BEAST, "coreRadius", 2.5f, 2.5f)]
        [TestCase("T10-1A", SUMMON_SPIRIT, "duration", 24f, 26.4f)]
        [TestCase("T10-2A", SUMMON_SPIRIT, "companionRange", 10f, 11.2f)]
        [TestCase("T10-3A", SUMMON_SPIRIT, "primary", 0.35f, 0.38f)]
        [TestCase("T10-3A", SUMMON_SPIRIT, "count", 2f, 2f)]
        [TestCase("T11-1A", SPIRIT_STRIKE, "energy", 12f, 10.2f)]
        [TestCase("T11-2A", SPIRIT_STRIKE, "resonance", 2f, 3.2f)]
        [TestCase("T11-3A", SPIRIT_STRIKE, "added", 1.2f, 1.92f)]
        [TestCase("T11-3A", SPIRIT_STRIKE, "extra", 2f, 2f)]
        [TestCase("T12-1A", TARGET_BARRIER, "cooldown", 10.8f, 10.08f)]
        [TestCase("T12-2A", TARGET_BARRIER, "duration", 8f, 9.2f)]
        public void Compile_SingleEffect_MatchesLevelOneAndFiveTable(string effectId, string skillId, string measure,
            float levelOne, float levelFive)
        {
            SkillCatalogSO.SkillDefinition skill = Skill(skillId);

            Assert.AreEqual(levelOne, Measure(skill, Compile(skill, Entry(effectId, LEVEL_ONE)), measure), TOLERANCE,
                $"{effectId} {measure} 레벨 1");
            Assert.AreEqual(levelFive, Measure(skill, Compile(skill, Entry(effectId, LEVEL_MAX)), measure), TOLERANCE,
                $"{effectId} {measure} 레벨 5");
        }

        [Test]
        public void Compile_CircleSweepWithWideArc_GrowsRadiusInsteadOfArc()
        {
            SkillCatalogSO.SkillDefinition skill = Skill(WIDE_SLASH);

            SkillModifiers levelOne = Compile(skill, Entry("T01-3B", LEVEL_ONE), Entry("T01-2A", LEVEL_ONE));
            SkillModifiers levelMax = Compile(skill, Entry("T01-3B", LEVEL_ONE), Entry("T01-2A", LEVEL_MAX));

            Assert.AreEqual(ESkillShape.Circle, TalismanModifierRules.GetShape(skill, in levelOne));
            Assert.AreEqual(3.5f, TalismanModifierRules.GetRadius(skill, in levelOne), TOLERANCE);
            Assert.AreEqual(3.8f, TalismanModifierRules.GetRadius(skill, in levelMax), TOLERANCE);
            Assert.AreEqual(0f, levelMax.ArcAngleDelta, TOLERANCE);
        }

        [Test]
        public void Compile_CircleSweepWithFocusArc_ShrinksRadiusAndAddsDamage()
        {
            SkillCatalogSO.SkillDefinition skill = Skill(WIDE_SLASH);

            SkillModifiers modifiers = Compile(skill, Entry("T01-3B", LEVEL_ONE), Entry("T01-2B", LEVEL_MAX));

            Assert.AreEqual(2.5f, TalismanModifierRules.GetRadius(skill, in modifiers), TOLERANCE);
            Assert.AreEqual(1.32f, modifiers.DamageScale, TOLERANCE);
        }

        [Test]
        public void Compile_FollowUpWithFocusArc_NarrowsBothStrikes()
        {
            SkillCatalogSO.SkillDefinition skill = Skill(WIDE_SLASH);

            SkillModifiers modifiers = Compile(skill, Entry("T01-3A", LEVEL_ONE), Entry("T01-2B", LEVEL_ONE),
                Entry("T01-1B", LEVEL_ONE));

            Assert.AreEqual(ESkillShape.Cone, TalismanModifierRules.GetShape(skill, in modifiers));
            Assert.AreEqual(80f, TalismanModifierRules.GetArcAngle(skill, in modifiers), TOLERANCE);
            Assert.AreEqual(1.2f, modifiers.DamageScale, TOLERANCE);
            Assert.AreEqual(16f, TalismanModifierRules.GetEnergyCost(skill, in modifiers), TOLERANCE);
        }

        [Test]
        public void Compile_DetonateWithFocusRadius_ExplosionUsesFixedRadiusAndDamage()
        {
            SkillCatalogSO.SkillDefinition skill = Skill(FLAME_FIELD);

            SkillModifiers modifiers = Compile(skill, Entry("T06-3A", LEVEL_ONE), Entry("T06-2B", LEVEL_ONE));

            Assert.AreEqual(2f, TalismanModifierRules.GetRadius(skill, in modifiers), TOLERANCE);
            Assert.AreEqual(5.5f, TalismanModifierRules.GetPrimaryCoefficient(skill, in modifiers), TOLERANCE);
            Assert.AreEqual(1.2f, modifiers.DamageScale, TOLERANCE);
        }

        [Test]
        public void Compile_SignatureSkillCombinations_KeepPositiveDisplayRange()
        {
            string[] signatures = { WIDE_SLASH, FLAME_FIELD, SUMMON_BEAST };
            for (int s = 0; s < signatures.Length; ++s)
            {
                SkillCatalogSO.SkillDefinition skill = Skill(signatures[s]);
                List<string>[] tiers = EffectsByTier(skill.DefinitionId);
                for (int a = -1; a < tiers[0].Count; ++a)
                {
                    for (int b = -1; b < tiers[1].Count; ++b)
                    {
                        for (int c = -1; c < tiers[2].Count; ++c)
                        {
                            var entries = new List<CombatTalismanEntry>();
                            AddIfChosen(entries, tiers[0], a);
                            AddIfChosen(entries, tiers[1], b);
                            AddIfChosen(entries, tiers[2], c);
                            Assert.IsTrue(TalismanModifierRules.TryCompile(_talismans, skill, entries, out SkillModifiers modifiers,
                                out string error), error);

                            string label = $"{skill.DefinitionId} {a},{b},{c}";
                            ESkillShape shape = TalismanModifierRules.GetShape(skill, in modifiers);
                            if ((shape == ESkillShape.Cone) || (shape == ESkillShape.Circle))
                            {
                                Assert.Greater(TalismanModifierRules.GetRadius(skill, in modifiers), 0f, label);
                            }

                            if (shape == ESkillShape.Cone)
                            {
                                Assert.Greater(TalismanModifierRules.GetArcAngle(skill, in modifiers), 0f, label);
                            }

                            if (shape == ESkillShape.Summon)
                            {
                                Assert.Greater(TalismanModifierRules.GetCompanionAttackInterval(skill, in modifiers), 0f, label);
                                Assert.Greater(TalismanModifierRules.GetDuration(skill, in modifiers), 0f, label);
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void Compile_EveryEffectAtMaxLevel_KeepsCostAndCooldownPositive()
        {
            for (int i = 0; i < _talismans.Effects.Count; ++i)
            {
                TalismanCatalogSO.TalismanEffectDefinition effect = _talismans.Effects[i];
                SkillCatalogSO.SkillDefinition skill = Skill(effect.SkillId);

                SkillModifiers modifiers = Compile(skill, Entry(effect.DefinitionId, LEVEL_MAX));

                Assert.Greater(TalismanModifierRules.GetEnergyCost(skill, in modifiers), 0f, effect.DefinitionId);
                Assert.Greater(TalismanModifierRules.GetCooldown(skill, in modifiers), 0f, effect.DefinitionId);
            }
        }

        [Test]
        public void Compile_AttachBarrier_RecordsKindWithoutBonus()
        {
            SkillCatalogSO.SkillDefinition skill = Skill(TARGET_BARRIER);

            SkillModifiers modifiers = Compile(skill, Entry("T12-3A", LEVEL_MAX));

            Assert.IsTrue(modifiers.Core.Is(ETalismanEffectKind.AttachBarrier));
            Assert.AreEqual(0f, modifiers.Core.AddedCoefficient);
            Assert.AreEqual(0f, modifiers.MarkedDamageBonus);
        }

        [Test]
        public void TryCompile_OtherSkillEntries_AreIgnored()
        {
            SkillCatalogSO.SkillDefinition skill = Skill(WIDE_SLASH);
            var entries = new[] { Entry("T02-2A", LEVEL_MAX) };

            Assert.IsTrue(TalismanModifierRules.TryCompile(_talismans, skill, entries, out SkillModifiers modifiers, out _));
            Assert.AreEqual(1f, modifiers.CooldownScale);
            Assert.IsFalse(modifiers.Core.HasValue);
        }

        [TestCase("unknown")]
        [TestCase("duplicateTier")]
        [TestCase("levelZero")]
        [TestCase("levelSix")]
        [TestCase("wrongTier")]
        public void TryCompile_InvalidEntry_ReturnsFalse(string kind)
        {
            SkillCatalogSO.SkillDefinition skill = Skill(WIDE_SLASH);
            CombatTalismanEntry[] entries;
            switch (kind)
            {
                case "unknown":
                    entries = new[] { new CombatTalismanEntry(WIDE_SLASH, 0, "T99-1A", LEVEL_ONE) };
                    break;
                case "duplicateTier":
                    entries = new[] { Entry("T01-1A", LEVEL_ONE), Entry("T01-1B", LEVEL_ONE) };
                    break;
                case "levelZero":
                    entries = new[] { Entry("T01-1A", 0) };
                    break;
                case "levelSix":
                    entries = new[] { Entry("T01-1A", LEVEL_MAX + 1) };
                    break;
                default:
                    entries = new[] { new CombatTalismanEntry(WIDE_SLASH, (int)ETalismanTier.Core, "T01-1A", LEVEL_ONE) };
                    break;
            }

            Assert.IsFalse(TalismanModifierRules.TryCompile(_talismans, skill, entries, out _, out string error));
            Assert.IsFalse(string.IsNullOrEmpty(error));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private float Measure(SkillCatalogSO.SkillDefinition skill, SkillModifiers modifiers, string measure)
        {
            switch (measure)
            {
                case "cooldown":
                    return TalismanModifierRules.GetCooldown(skill, in modifiers);
                case "energy":
                    return TalismanModifierRules.GetEnergyCost(skill, in modifiers);
                case "arc":
                    return TalismanModifierRules.GetArcAngle(skill, in modifiers);
                case "damage":
                    return modifiers.DamageScale;
                case "added":
                    return modifiers.Core.AddedCoefficient;
                case "primary":
                    return TalismanModifierRules.GetPrimaryCoefficient(skill, in modifiers);
                case "delay":
                    return modifiers.Core.Delay;
                case "radius":
                    return TalismanModifierRules.GetRadius(skill, in modifiers);
                case "coreRadius":
                    return modifiers.Core.Radius;
                case "range":
                    return TalismanModifierRules.GetCastRange(skill, in modifiers);
                case "active":
                    return TalismanModifierRules.GetActiveSeconds(skill, in modifiers);
                case "length":
                    return modifiers.Core.Length;
                case "count":
                    return modifiers.Core.Count;
                case "root":
                    return modifiers.Core.RootBonus;
                case "duration":
                    return TalismanModifierRules.GetDuration(skill, in modifiers);
                case "interval":
                    return TalismanModifierRules.GetCompanionAttackInterval(skill, in modifiers);
                case "marked":
                    return modifiers.MarkedDamageBonus;
                case "companionRange":
                    return TalismanModifierRules.GetCompanionAttackRange(skill, in modifiers);
                case "resonance":
                    return modifiers.ResonanceBonus;
                case "extra":
                    return modifiers.Core.ExtraTargets;
                default:
                    Assert.Fail("모르는 측정 " + measure);
                    return 0f;
            }
        }

        private SkillModifiers Compile(SkillCatalogSO.SkillDefinition skill, params CombatTalismanEntry[] entries)
        {
            Assert.IsTrue(TalismanModifierRules.TryCompile(_talismans, skill, entries, out SkillModifiers modifiers,
                out string error), error);
            return modifiers;
        }

        private CombatTalismanEntry Entry(string effectId, int level)
        {
            Assert.IsTrue(_talismans.TryGetEffect(effectId, out TalismanCatalogSO.TalismanEffectDefinition effect), effectId);
            return new CombatTalismanEntry(effect.SkillId, (int)effect.Tier, effectId, level);
        }

        private SkillCatalogSO.SkillDefinition Skill(string skillId)
        {
            Assert.IsTrue(_skills.TryGetSkill(skillId, out SkillCatalogSO.SkillDefinition skill), skillId);
            return skill;
        }

        private List<string>[] EffectsByTier(string skillId)
        {
            var tiers = new List<string>[Constants.Combat.TALISMAN_TIERS_PER_SKILL];
            for (int i = 0; i < tiers.Length; ++i)
            {
                tiers[i] = new List<string>();
            }

            for (int i = 0; i < _talismans.Effects.Count; ++i)
            {
                TalismanCatalogSO.TalismanEffectDefinition effect = _talismans.Effects[i];
                if (effect.SkillId == skillId)
                {
                    tiers[(int)effect.Tier].Add(effect.DefinitionId);
                }
            }

            return tiers;
        }

        private void AddIfChosen(List<CombatTalismanEntry> entries, List<string> tier, int index)
        {
            if (index >= 0)
            {
                entries.Add(Entry(tier[index], LEVEL_MAX));
            }
        }
    }
}
