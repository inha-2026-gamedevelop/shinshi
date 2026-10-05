// Unity
using UnityEngine;
using UnityEngine.TestTools.Constraints;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

using Is = UnityEngine.TestTools.Constraints.Is;

namespace Shinshi.Tests
{
    public class TalismanModifierResolverTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int   PARTICIPANT     = 7;
        private const int   OTHER           = 8;
        private const int   CAPACITY        = 2;
        private const int   LEVEL_MAX       = 5;
        private const int   RESOLVE_REPEATS = 1000;
        private const float TOLERANCE       = 0.0001f;

        private SkillCatalogSO           _skills;
        private TalismanCatalogSO        _talismans;
        private TalismanModifierResolver _resolver;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _skills    = ScriptableObject.CreateInstance<SkillCatalogSO>();
            _talismans = ScriptableObject.CreateInstance<TalismanCatalogSO>();
            _resolver  = new TalismanModifierResolver(_talismans, _skills, CAPACITY);
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

        [Test]
        public void TryRegister_WarriorLoadout_ResolvesEachSlot()
        {
            Assert.IsTrue(_resolver.TryRegister(PARTICIPANT, EPlayerClass.Warrior,
                new[] { Entry("T01-1A"), Entry("T01-3B"), Entry("T02-2A") }, out string error), error);

            SkillModifiers slash = _resolver.Resolve(PARTICIPANT, Skill(EPlayerClass.Warrior, Constants.Combat.SKILL_SLOT_Q));
            SkillModifiers dash  = _resolver.Resolve(PARTICIPANT, Skill(EPlayerClass.Warrior, Constants.Combat.SKILL_SLOT_W));
            SkillModifiers slam  = _resolver.Resolve(PARTICIPANT, Skill(EPlayerClass.Warrior, Constants.Combat.SKILL_SLOT_E));

            Assert.AreEqual(0.84f, slash.CooldownScale, TOLERANCE);
            Assert.IsTrue(slash.Core.Is(ETalismanEffectKind.CircleSweep));
            Assert.AreEqual(1.6f, dash.MoveDistanceDelta, TOLERANCE);
            Assert.AreEqual(1f, slam.CooldownScale);
            Assert.IsFalse(slam.Core.HasValue);
        }

        [Test]
        public void Resolve_UnknownParticipantOrForeignSkill_ReturnsIdentity()
        {
            Assert.IsTrue(_resolver.TryRegister(PARTICIPANT, EPlayerClass.Warrior, new[] { Entry("T01-1A") }, out _));

            SkillModifiers unknown = _resolver.Resolve(OTHER, Skill(EPlayerClass.Warrior, Constants.Combat.SKILL_SLOT_Q));
            SkillModifiers foreign = _resolver.Resolve(PARTICIPANT, Skill(EPlayerClass.Sorcerer, Constants.Combat.SKILL_SLOT_Q));

            Assert.AreEqual(1f, unknown.CooldownScale);
            Assert.AreEqual(1f, foreign.CooldownScale);
        }

        [Test]
        public void TryRegister_OtherClassTalisman_RejectsWithoutChangingPrevious()
        {
            Assert.IsTrue(_resolver.TryRegister(PARTICIPANT, EPlayerClass.Warrior, new[] { Entry("T01-1A") }, out _));

            Assert.IsFalse(_resolver.TryRegister(PARTICIPANT, EPlayerClass.Warrior, new[] { Entry("T05-1A") }, out string error));
            Assert.IsFalse(string.IsNullOrEmpty(error));

            SkillModifiers slash = _resolver.Resolve(PARTICIPANT, Skill(EPlayerClass.Warrior, Constants.Combat.SKILL_SLOT_Q));
            Assert.AreEqual(0.84f, slash.CooldownScale, TOLERANCE);
        }

        [Test]
        public void TryRegister_FullCapacity_RejectsNewParticipant()
        {
            Assert.IsTrue(_resolver.TryRegister(1, EPlayerClass.Warrior, null, out _));
            Assert.IsTrue(_resolver.TryRegister(2, EPlayerClass.Warrior, null, out _));

            Assert.IsFalse(_resolver.TryRegister(3, EPlayerClass.Warrior, null, out _));

            _resolver.Unregister(1);
            Assert.IsTrue(_resolver.TryRegister(3, EPlayerClass.Warrior, null, out _));
        }

        [Test]
        public void Resolve_RegisteredParticipant_DoesNotAllocate()
        {
            Assert.IsTrue(_resolver.TryRegister(PARTICIPANT, EPlayerClass.Warrior,
                new[] { Entry("T01-1A"), Entry("T01-2B"), Entry("T01-3A") }, out _));
            SkillCatalogSO.SkillDefinition skill = Skill(EPlayerClass.Warrior, Constants.Combat.SKILL_SLOT_Q);
            ITalismanModifierResolver resolver = _resolver;
            resolver.Resolve(PARTICIPANT, skill);

            Assert.That(() =>
            {
                for (int i = 0; i < RESOLVE_REPEATS; ++i)
                {
                    resolver.Resolve(PARTICIPANT, skill);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        /****************************************
        *                Helpers
        ****************************************/

        private CombatTalismanEntry Entry(string effectId)
        {
            Assert.IsTrue(_talismans.TryGetEffect(effectId, out TalismanCatalogSO.TalismanEffectDefinition effect), effectId);
            return new CombatTalismanEntry(effect.SkillId, (int)effect.Tier, effectId, LEVEL_MAX);
        }

        private SkillCatalogSO.SkillDefinition Skill(EPlayerClass playerClass, int slot)
        {
            Assert.IsTrue(_skills.TryGetSkillBySlot(playerClass, slot, out SkillCatalogSO.SkillDefinition skill));
            return skill;
        }
    }
}
