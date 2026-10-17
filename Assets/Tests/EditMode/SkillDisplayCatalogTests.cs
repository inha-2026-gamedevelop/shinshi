// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Tests
{
    public sealed class SkillDisplayCatalogTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ROOT_PATH       = "Assets/09.Data/Resources/GameDB.asset";
        private const string DISPLAY_PATH    = "Assets/09.Data/Combat/SkillDisplayCatalog.asset";
        private const int    NEW_EFFECTS     = 45;
        private const int    NEW_SKILLS      = 12;
        private const int    NO_EFFECT_COUNT = 13;

        // 2026-10-15 마검사 스택형, 궁수 속성형 개편으로 서버가 무시하는 효과
        private static readonly string[] G_NoEffect =
        {
            "T17-2A", "T17-3A", "T19-2A", "T19-3A",
            "T21-2A", "T21-3A", "T22-2A", "T22-3A", "T23-2A", "T23-3A", "T23-3B", "T24-2A", "T24-3A"
        };

        private GameDatabaseSO        _root;
        private SkillDisplayCatalogSO _display;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _root    = AssetDatabase.LoadAssetAtPath<GameDatabaseSO>(ROOT_PATH);
            _display = AssetDatabase.LoadAssetAtPath<SkillDisplayCatalogSO>(DISPLAY_PATH);
            Assert.That(_root, Is.Not.Null);
            Assert.That(_display, Is.Not.Null);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Root_DisplayCatalog_IsReferencedAndValid()
        {
            var errors = new List<string>();
            _root.Validate(errors);

            Assert.That(_root.SkillDisplay, Is.SameAs(_display));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [Test]
        public void Effects_NewClassEffects_AllHaveName()
        {
            int count = 0;
            IReadOnlyList<TalismanCatalogSO.TalismanEffectDefinition> effects = _root.Talismans.Effects;
            for (int i = 0; i < effects.Count; ++i)
            {
                TalismanCatalogSO.TalismanEffectDefinition effect = effects[i];
                if (effect.PlayerClass < EPlayerClass.Swordsman)
                {
                    continue;
                }

                ++count;
                Assert.That(_display.TryGetEffect(effect.DefinitionId, out SkillDisplayCatalogSO.DisplayEntry entry), Is.True,
                    effect.DefinitionId);
                Assert.That(entry.DisplayName, Is.Not.Empty, effect.DefinitionId);
                Assert.That(entry.UpgradeDescription, Is.Not.Empty, effect.DefinitionId);
            }

            Assert.That(count, Is.EqualTo(NEW_EFFECTS));
            Assert.That(_display.Effects.Count, Is.EqualTo(NEW_EFFECTS));
        }

        [Test]
        public void Skills_NewClassSkills_AllHaveName()
        {
            int count = 0;
            IReadOnlyList<SkillCatalogSO.SkillDefinition> skills = _root.Skills.Skills;
            for (int i = 0; i < skills.Count; ++i)
            {
                if (skills[i].PlayerClass < EPlayerClass.Swordsman)
                {
                    continue;
                }

                ++count;
                Assert.That(_display.TryGetSkill(skills[i].DefinitionId, out SkillDisplayCatalogSO.DisplayEntry entry), Is.True,
                    skills[i].DefinitionId);
                Assert.That(entry.DisplayName, Is.Not.Empty);
            }

            Assert.That(count, Is.EqualTo(NEW_SKILLS));
        }

        [Test]
        public void NoEffect_Flags_MatchRedesignList()
        {
            var flagged = new List<string>();
            for (int i = 0; i < _display.Effects.Count; ++i)
            {
                if (_display.Effects[i].NoEffect)
                {
                    flagged.Add(_display.Effects[i].DefinitionId);
                }
            }

            Assert.That(flagged.Count, Is.EqualTo(NO_EFFECT_COUNT));
            Assert.That(flagged, Is.EquivalentTo(G_NoEffect));
        }

        [Test]
        public void Classes_NewClasses_HaveBasicAttackAndStackRules()
        {
            Assert.That(_display.TryGetClass(EPlayerClass.Swordsman, out SkillDisplayCatalogSO.ClassDisplay swordsman), Is.True);
            Assert.That(swordsman.StackRule, Is.Empty);
            Assert.That(_display.TryGetClass(EPlayerClass.Mageknight, out SkillDisplayCatalogSO.ClassDisplay mageknight), Is.True);
            Assert.That(mageknight.StackRule, Is.Not.Empty);
            Assert.That(_display.TryGetClass(EPlayerClass.Archer, out SkillDisplayCatalogSO.ClassDisplay archer), Is.True);
            Assert.That(archer.StackRule, Is.Not.Empty);
        }

        [Test]
        public void DisplayNameOf_UnknownId_ReturnsId()
        {
            Assert.That(_display.DisplayNameOf("T99-1A"), Is.EqualTo("T99-1A"));
            Assert.That(_display.DisplayNameOf("T13-3B"), Is.EqualTo("회전 참격"));
        }

        [Test]
        public void ValidateReferences_UnknownEffect_ReportsError()
        {
            SkillDisplayCatalogSO copy = Object.Instantiate(_display);
            try
            {
                var serialized = new SerializedObject(copy);
                serialized.FindProperty("_effects").GetArrayElementAtIndex(0).FindPropertyRelative("_definitionId").stringValue =
                    "T99-1A";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var errors = new List<string>();
                copy.ValidateReferences(_root, errors);

                Assert.That(errors, Has.Some.Contains("T99-1A"));
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }
    }
}
