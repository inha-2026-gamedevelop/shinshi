// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;
using Shinshi.UI;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public sealed class SkillWindowViewRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SKILL_A    = "skill_swordsman_slash";
        private const string SKILL_B    = "skill_swordsman_dash";
        private const string EFFECT_1A  = "T13-1A";
        private const string EFFECT_1B  = "T13-1B";
        private const string EFFECT_2A  = "T13-2A";
        private const int    CARD_COST  = 1;
        private const int    ESSENCE    = 150;
        private const int    MAX_LEVEL  = 5;

        private TalismanCatalogSO _talismans;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _talismans = ScriptableObject.CreateInstance<TalismanCatalogSO>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_talismans);
        }

        /****************************************
        *            Slot state tests
        ****************************************/

        [Test]
        public void OwnState_LevelZeroFailureRow_IsNotRegistered()
        {
            var rows = new[] { new TownTalismanEntry(EFFECT_1A, 0, 2) };

            Assert.That(SkillWindowViewRules.TryFindTalisman(rows, EFFECT_1A, out TownTalismanEntry row), Is.True);
            Assert.That(SkillWindowViewRules.IsRegistered(row.Level), Is.False);
            Assert.That(SkillWindowViewRules.OwnState(row.Level, 0), Is.EqualTo(SkillWindowViewRules.EOwnState.Unowned));
            Assert.That(SkillWindowViewRules.OwnState(row.Level, 1), Is.EqualTo(SkillWindowViewRules.EOwnState.CardOnly));
            Assert.That(SkillWindowViewRules.OwnState(1, 0), Is.EqualTo(SkillWindowViewRules.EOwnState.Registered));
        }

        [Test]
        public void AvailableCards_ReservedQuantity_IsExcluded()
        {
            var items = new[]
            {
                new TownItemEntry(EFFECT_1A, "account", 5, 2),
                new TownItemEntry(EFFECT_1B, "account", 9, 0),
                new TownItemEntry(EFFECT_1A, "bound", 1, 3)
            };

            Assert.That(SkillWindowViewRules.AvailableCards(items, EFFECT_1A), Is.EqualTo(3));
            Assert.That(SkillWindowViewRules.AvailableCards(null, EFFECT_1A), Is.EqualTo(0));
        }

        [Test]
        public void CanRegister_Conditions_RequireCardEssenceAndUnregistered()
        {
            Assert.That(SkillWindowViewRules.CanRegister(0, 1, ESSENCE, CARD_COST, ESSENCE), Is.True);
            Assert.That(SkillWindowViewRules.CanRegister(0, 0, ESSENCE, CARD_COST, ESSENCE), Is.False);
            Assert.That(SkillWindowViewRules.CanRegister(0, 1, ESSENCE - 1, CARD_COST, ESSENCE), Is.False);
            Assert.That(SkillWindowViewRules.CanRegister(1, 1, ESSENCE, CARD_COST, ESSENCE), Is.False);
        }

        [Test]
        public void CanUpgrade_MaxLevelOrMissingCost_IsFalse()
        {
            Assert.That(SkillWindowViewRules.CanUpgrade(1, MAX_LEVEL, 1, 300, 1, 300), Is.True);
            Assert.That(SkillWindowViewRules.CanUpgrade(0, MAX_LEVEL, 1, 300, 1, 300), Is.False);
            Assert.That(SkillWindowViewRules.CanUpgrade(MAX_LEVEL, MAX_LEVEL, 9, 9999, 1, 300), Is.False);
            Assert.That(SkillWindowViewRules.CanUpgrade(3, MAX_LEVEL, 1, 9999, 2, 1000), Is.False);
        }

        [Test]
        public void Disassemble_Quantity_ClampedAndEssencePreviewed()
        {
            Assert.That(SkillWindowViewRules.ClampDisassembleQuantity(5, 3), Is.EqualTo(3));
            Assert.That(SkillWindowViewRules.ClampDisassembleQuantity(0, 3), Is.EqualTo(1));
            Assert.That(SkillWindowViewRules.ClampDisassembleQuantity(2, 0), Is.EqualTo(0));
            Assert.That(SkillWindowViewRules.DisassembleEssence(3, 50), Is.EqualTo(150));
        }

        [Test]
        public void TryFindEffect_Choices_SignatureHasTwoOthersOne()
        {
            Assert.That(SkillWindowViewRules.TryFindEffect(_talismans, SKILL_A, 0, 1, out var signature), Is.True);
            Assert.That(signature.DefinitionId, Is.EqualTo(EFFECT_1B));
            Assert.That(SkillWindowViewRules.TryFindEffect(_talismans, SKILL_B, 0, 0, out _), Is.True);
            Assert.That(SkillWindowViewRules.TryFindEffect(_talismans, SKILL_B, 0, 1, out _), Is.False);
        }

        /****************************************
        *              Loadout tests
        ****************************************/

        [Test]
        public void ToggleEquip_SameTier_ReplacesAndSecondToggleRemoves()
        {
            var draft = new List<TownLoadoutEntry>();

            Assert.That(SkillWindowViewRules.ToggleEquip(draft, SKILL_A, 0, EFFECT_1A), Is.True);
            Assert.That(SkillWindowViewRules.ToggleEquip(draft, SKILL_A, 0, EFFECT_1B), Is.True);
            Assert.That(draft.Count, Is.EqualTo(1));
            Assert.That(draft[0].EffectId, Is.EqualTo(EFFECT_1B));
            Assert.That(SkillWindowViewRules.ToggleEquip(draft, SKILL_A, 0, EFFECT_1B), Is.False);
            Assert.That(draft, Is.Empty);
        }

        [Test]
        public void ValidateLoadout_DuplicateTier_IsRejected()
        {
            var draft = new List<TownLoadoutEntry>
            {
                new TownLoadoutEntry(SKILL_A, 0, EFFECT_1A),
                new TownLoadoutEntry(SKILL_A, 0, EFFECT_1B)
            };

            Assert.That(SkillWindowViewRules.ValidateLoadout(draft, Registered),
                Is.EqualTo(SkillWindowViewRules.ELoadoutError.DuplicateTier));
        }

        [Test]
        public void ValidateLoadout_MoreThanTwelve_IsRejected()
        {
            var draft = new List<TownLoadoutEntry>();
            for (int i = 0; i <= Constants.Town.MAX_GROWTH_LOADOUT_SLOTS; ++i)
            {
                draft.Add(new TownLoadoutEntry("skill_" + i, 0, EFFECT_1A));
            }

            Assert.That(SkillWindowViewRules.ValidateLoadout(draft, Registered),
                Is.EqualTo(SkillWindowViewRules.ELoadoutError.TooMany));
        }

        [Test]
        public void ValidateLoadout_InvalidTier_IsRejected()
        {
            var draft = new List<TownLoadoutEntry> { new TownLoadoutEntry(SKILL_A, SkillWindowViewRules.TIERS_PER_SKILL, EFFECT_1A) };

            Assert.That(SkillWindowViewRules.ValidateLoadout(draft, Registered),
                Is.EqualTo(SkillWindowViewRules.ELoadoutError.InvalidEntry));
        }

        [Test]
        public void ValidateLoadout_Unregistered_IsRejectedAndFullSkillPasses()
        {
            var draft = new List<TownLoadoutEntry>
            {
                new TownLoadoutEntry(SKILL_A, 0, EFFECT_1A),
                new TownLoadoutEntry(SKILL_A, 1, EFFECT_2A),
                new TownLoadoutEntry(SKILL_A, 2, "T13-3A")
            };

            Assert.That(SkillWindowViewRules.ValidateLoadout(draft, Registered), Is.EqualTo(SkillWindowViewRules.ELoadoutError.None));
            Assert.That(SkillWindowViewRules.ValidateLoadout(draft, id => (id == EFFECT_2A) ? 0 : 1),
                Is.EqualTo(SkillWindowViewRules.ELoadoutError.Unregistered));
        }

        [Test]
        public void SameLoadout_DifferentOrder_IsEqual()
        {
            var left  = new[] { new TownLoadoutEntry(SKILL_A, 0, EFFECT_1A), new TownLoadoutEntry(SKILL_B, 0, "T14-1A") };
            var right = new[] { new TownLoadoutEntry(SKILL_B, 0, "T14-1A"), new TownLoadoutEntry(SKILL_A, 0, EFFECT_1A) };

            Assert.That(SkillWindowViewRules.SameLoadout(left, right), Is.True);
            Assert.That(SkillWindowViewRules.SameLoadout(left, new[] { left[0] }), Is.False);
        }

        /****************************************
        *               Mode tests
        ****************************************/

        [TestCase(true, false, false, EPartyPhase.Open, false, SkillWindowViewRules.EMode.Writable)]
        [TestCase(true, false, true, EPartyPhase.Open, false, SkillWindowViewRules.EMode.Writable)]
        [TestCase(true, false, true, EPartyPhase.Countdown, false, SkillWindowViewRules.EMode.SortieLocked)]
        [TestCase(true, false, true, EPartyPhase.Queued, false, SkillWindowViewRules.EMode.SortieLocked)]
        [TestCase(true, false, true, EPartyPhase.Departing, false, SkillWindowViewRules.EMode.SortieLocked)]
        [TestCase(true, false, false, EPartyPhase.Open, true, SkillWindowViewRules.EMode.SortieLocked)]
        [TestCase(false, true, false, EPartyPhase.Open, false, SkillWindowViewRules.EMode.Dungeon)]
        [TestCase(false, false, false, EPartyPhase.Open, false, SkillWindowViewRules.EMode.Offline)]
        public void ResolveMode_PartyPhaseAndRole_MatchesTable(bool isTown, bool isDungeon, bool inRoom, EPartyPhase phase,
            bool transferring, SkillWindowViewRules.EMode expected)
        {
            Assert.That(SkillWindowViewRules.ResolveMode(isTown, isDungeon, inRoom, phase, transferring), Is.EqualTo(expected));
        }

        [Test]
        public void ShowsTalismans_DungeonAndOffline_Hidden()
        {
            Assert.That(SkillWindowViewRules.ShowsTalismans(SkillWindowViewRules.EMode.Dungeon), Is.False);
            Assert.That(SkillWindowViewRules.ShowsTalismans(SkillWindowViewRules.EMode.Offline), Is.False);
            Assert.That(SkillWindowViewRules.ShowsTalismans(SkillWindowViewRules.EMode.SortieLocked), Is.True);
        }

        /****************************************
        *              Text tests
        ****************************************/

        [Test]
        public void BonusText_RateAndUnitStats_Formatted()
        {
            Assert.That(SkillWindowViewRules.BonusText(ETalismanUpgradeStat.CooldownRate, 0.1f), Is.EqualTo("-10%"));
            Assert.That(SkillWindowViewRules.BonusText(ETalismanUpgradeStat.DamageRate, 0.32f), Is.EqualTo("+32%"));
            Assert.That(SkillWindowViewRules.BonusText(ETalismanUpgradeStat.ArcAngle, 20f), Is.EqualTo("+20도"));
            Assert.That(SkillWindowViewRules.BonusText(ETalismanUpgradeStat.AddedCoefficient, 0.6f), Is.EqualTo("+0.6A"));
        }

        [Test]
        public void SlotOfTab_BasicTab_HasNoSkillSlot()
        {
            Assert.That(SkillWindowViewRules.SlotOfTab(SkillWindowViewRules.BASIC_TAB), Is.EqualTo(-1));
            Assert.That(SkillWindowViewRules.SlotOfTab(1), Is.EqualTo(0));
            Assert.That(SkillWindowViewRules.SlotOfTab(4), Is.EqualTo(3));
        }

        /****************************************
        *         Shared write lock tests
        ****************************************/

        [Test]
        public void WriteLock_Snapshot_DoesNotLockAndSecondWriteIsRefused()
        {
            var  writeLock = new GrowthWriteLock();
            Guid first     = Guid.NewGuid();

            Assert.That(writeLock.TryBegin(Guid.NewGuid(), EGrowthAction.Snapshot), Is.False);
            Assert.That(writeLock.TryBegin(first, EGrowthAction.TalismanRegister), Is.True);
            Assert.That(writeLock.TryBegin(Guid.NewGuid(), EGrowthAction.EquipmentEnhance), Is.False);
            Assert.That(writeLock.Action, Is.EqualTo(EGrowthAction.TalismanRegister));
        }

        [Test]
        public void WriteLock_BackendUnavailable_KeepsLockAndConfirmedResultReleases()
        {
            var  writeLock = new GrowthWriteLock();
            Guid operation = Guid.NewGuid();
            writeLock.TryBegin(operation, EGrowthAction.TalismanUpgrade);

            Assert.That(writeLock.Resolve(Guid.NewGuid(), ERejectReason.None), Is.False);
            Assert.That(writeLock.Resolve(operation, ERejectReason.BackendUnavailable), Is.True);
            Assert.That(writeLock.IsHeld, Is.True);
            Assert.That(writeLock.IsOutcomeUnknown, Is.True);
            Assert.That(writeLock.MarkRetrying(operation), Is.True);
            Assert.That(writeLock.Resolve(operation, ERejectReason.InsufficientItem), Is.True);
            Assert.That(writeLock.IsHeld, Is.False);
        }

        [Test]
        public void WriteLock_Release_OnlyByOwner()
        {
            var  writeLock = new GrowthWriteLock();
            Guid operation = Guid.NewGuid();
            writeLock.TryBegin(operation, EGrowthAction.Training);

            Assert.That(writeLock.Release(Guid.NewGuid()), Is.False);
            Assert.That(writeLock.Release(Guid.Empty), Is.False);
            Assert.That(writeLock.Release(operation), Is.True);
            Assert.That(writeLock.IsHeld, Is.False);
        }

        [Test]
        public void CanRetry_OtherWindowRequest_IsRefused()
        {
            Guid enhance = Guid.NewGuid();

            Assert.That(GrowthWriteGuard.CanRetry(true, true, EGrowthAction.EquipmentEnhance, enhance, Guid.Empty,
                SkillWindowViewRules.G_OwnActions), Is.False);
            Assert.That(GrowthWriteGuard.IsBlockedByOther(true, enhance, Guid.Empty), Is.True);
            Assert.That(GrowthWriteGuard.IsBlockedByOther(true, enhance, enhance), Is.False);
            Assert.That(GrowthWriteGuard.IsBlockedByOther(false, Guid.Empty, Guid.Empty), Is.False);
        }

        [Test]
        public void CanRetry_OwnUnknownRequest_IsAllowed()
        {
            Guid register = Guid.NewGuid();

            Assert.That(GrowthWriteGuard.CanRetry(true, true, EGrowthAction.TalismanRegister, register, register,
                SkillWindowViewRules.G_OwnActions), Is.True);
            Assert.That(GrowthWriteGuard.CanRetry(true, false, EGrowthAction.TalismanRegister, register, register,
                SkillWindowViewRules.G_OwnActions), Is.False);
            Assert.That(GrowthWriteGuard.CanRetry(true, true, EGrowthAction.TalismanRegister, register, Guid.NewGuid(),
                SkillWindowViewRules.G_OwnActions), Is.False);
        }

        private static int Registered(string effectId)
        {
            return 1;
        }
    }
}
