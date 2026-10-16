// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Tests
{
    public class SkillInputRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  DISTANCE_TOLERANCE = 0.0001f;
        private const float  CAST_RANGE         = 8f;
        private const double BUFFER_SECONDS     = 0.15;
        private const double NOW                = 100.0;

        private static readonly EPlayerClass[] G_Classes =
        {
            EPlayerClass.Warrior,
            EPlayerClass.Sorcerer,
            EPlayerClass.Summoner
        };

        private SkillCatalogSO _catalog;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _catalog = ScriptableObject.CreateInstance<SkillCatalogSO>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_catalog);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryMapKey_RegularKeys_MapToSlotsForEveryClass()
        {
            for (int i = 0; i < G_Classes.Length; ++i)
            {
                AssertMapped(G_Classes[i], ESkillInputKey.Q, ESkillRequestKind.Regular, Constants.Combat.SKILL_SLOT_Q);
                AssertMapped(G_Classes[i], ESkillInputKey.W, ESkillRequestKind.Regular, Constants.Combat.SKILL_SLOT_W);
                AssertMapped(G_Classes[i], ESkillInputKey.E, ESkillRequestKind.Regular, Constants.Combat.SKILL_SLOT_E);
                AssertMapped(G_Classes[i], ESkillInputKey.R, ESkillRequestKind.Regular, Constants.Combat.SKILL_SLOT_R);
            }
        }

        [Test]
        public void TryMapKey_AscensionKeysAsSummoner_MapToAscensionSlots()
        {
            AssertMapped(EPlayerClass.Summoner, ESkillInputKey.Z, ESkillRequestKind.Ascension, 0);
            AssertMapped(EPlayerClass.Summoner, ESkillInputKey.X, ESkillRequestKind.Ascension, 1);
            AssertMapped(EPlayerClass.Summoner, ESkillInputKey.C, ESkillRequestKind.Ascension, 2);
        }

        [Test]
        public void TryMapKey_AscensionKeysAsOtherClass_False()
        {
            Assert.IsFalse(SkillInputRules.TryMapKey(EPlayerClass.Warrior, ESkillInputKey.Z, out _, out _));
            Assert.IsFalse(SkillInputRules.TryMapKey(EPlayerClass.Sorcerer, ESkillInputKey.X, out _, out _));
            Assert.IsFalse(SkillInputRules.TryMapKey(EPlayerClass.Warrior, ESkillInputKey.C, out _, out _));
        }

        [Test]
        public void TryMapKey_None_False()
        {
            Assert.IsFalse(SkillInputRules.TryMapKey(EPlayerClass.Summoner, ESkillInputKey.None, out _, out _));
        }

        [Test]
        public void TryMapKey_AscensionSlots_ExistInCatalog()
        {
            ESkillInputKey[] keys = { ESkillInputKey.Z, ESkillInputKey.X, ESkillInputKey.C };
            for (int i = 0; i < keys.Length; ++i)
            {
                Assert.IsTrue(SkillInputRules.TryMapKey(EPlayerClass.Summoner, keys[i], out ESkillRequestKind kind, out byte slot));
                Assert.IsTrue(SkillInputRules.TryGetAim(_catalog, EPlayerClass.Summoner, kind, slot, out ESkillAimMode aimMode,
                    out float castRange, out float radius));
                Assert.AreEqual(ESkillAimMode.Point, aimMode);
                Assert.Greater(castRange, 0f);
                Assert.Greater(radius, 0f);
            }
        }

        [Test]
        public void TryGetAim_RegularSkills_ReadCatalogArea()
        {
            for (int i = 0; i < G_Classes.Length; ++i)
            {
                for (int slot = 0; slot < Constants.Player.SKILL_SLOT_COUNT; ++slot)
                {
                    Assert.IsTrue(_catalog.TryGetSkillBySlot(G_Classes[i], slot, out SkillCatalogSO.SkillDefinition skill));
                    Assert.IsTrue(SkillInputRules.TryGetAim(_catalog, G_Classes[i], ESkillRequestKind.Regular, slot,
                        out ESkillAimMode aimMode, out float castRange, out float radius));
                    Assert.AreEqual(skill.Area.AimMode, aimMode);
                    Assert.AreEqual(skill.Area.CastRange, castRange);
                    Assert.AreEqual(skill.Area.Radius, radius);
                }
            }
        }

        [Test]
        public void TryGetAim_AscensionAsOtherClass_False()
        {
            Assert.IsFalse(SkillInputRules.TryGetAim(_catalog, EPlayerClass.Warrior, ESkillRequestKind.Ascension, 0,
                out _, out _, out _));
        }

        [Test]
        public void TryGetAim_UnknownSlotOrNoCatalog_False()
        {
            Assert.IsFalse(SkillInputRules.TryGetAim(_catalog, EPlayerClass.Sorcerer, ESkillRequestKind.Regular,
                Constants.Player.SKILL_SLOT_COUNT, out _, out _, out _));
            Assert.IsFalse(SkillInputRules.TryGetAim(_catalog, EPlayerClass.Summoner, ESkillRequestKind.Ascension,
                Constants.Player.SUMMON_SLOT_COUNT, out _, out _, out _));
            Assert.IsFalse(SkillInputRules.TryGetAim(null, EPlayerClass.Sorcerer, ESkillRequestKind.Regular, 0,
                out _, out _, out _));
        }

        [Test]
        public void IsReady_WithinBuffer_True()
        {
            Assert.IsTrue(SkillInputRules.IsReady(NOW, NOW, BUFFER_SECONDS));
            Assert.IsTrue(SkillInputRules.IsReady(NOW + BUFFER_SECONDS, NOW, BUFFER_SECONDS));
            Assert.IsTrue(SkillInputRules.IsReady(0.0, NOW, BUFFER_SECONDS));
        }

        [Test]
        public void IsReady_EarlierThanBuffer_False()
        {
            Assert.IsFalse(SkillInputRules.IsReady(NOW + (BUFFER_SECONDS * 2.0), NOW, BUFFER_SECONDS));
        }

        [Test]
        public void DecideKey_NotReady_Ignore()
        {
            Assert.AreEqual(ESkillInputDecision.Ignore, SkillInputRules.DecideKey(ESkillAimMode.Point, false, false,
                ESkillRequestKind.Regular, 0, ESkillRequestKind.Regular, 1));
            Assert.AreEqual(ESkillInputDecision.Ignore, SkillInputRules.DecideKey(ESkillAimMode.Direction, false, true,
                ESkillRequestKind.Regular, 1, ESkillRequestKind.Regular, 0));
        }

        [Test]
        public void DecideKey_DirectionOrSelf_CastNow()
        {
            Assert.AreEqual(ESkillInputDecision.CastNow, SkillInputRules.DecideKey(ESkillAimMode.Direction, true, false,
                ESkillRequestKind.Regular, 0, ESkillRequestKind.Regular, 0));
            Assert.AreEqual(ESkillInputDecision.CastNow, SkillInputRules.DecideKey(ESkillAimMode.Self, true, true,
                ESkillRequestKind.Regular, 3, ESkillRequestKind.Regular, 0));
        }

        [Test]
        public void DecideKey_PointWhileIdle_BeginTargeting()
        {
            Assert.AreEqual(ESkillInputDecision.BeginTargeting, SkillInputRules.DecideKey(ESkillAimMode.Point, true, false,
                ESkillRequestKind.Regular, 0, ESkillRequestKind.Regular, 1));
        }

        [Test]
        public void DecideKey_OtherPointWhileTargeting_SwitchesTargeting()
        {
            Assert.AreEqual(ESkillInputDecision.BeginTargeting, SkillInputRules.DecideKey(ESkillAimMode.Point, true, true,
                ESkillRequestKind.Regular, 1, ESkillRequestKind.Regular, 2));
            Assert.AreEqual(ESkillInputDecision.BeginTargeting, SkillInputRules.DecideKey(ESkillAimMode.Point, true, true,
                ESkillRequestKind.Regular, 1, ESkillRequestKind.Ascension, 1));
        }

        [Test]
        public void DecideKey_SamePointWhileTargeting_Ignore()
        {
            Assert.AreEqual(ESkillInputDecision.Ignore, SkillInputRules.DecideKey(ESkillAimMode.Point, true, true,
                ESkillRequestKind.Ascension, 2, ESkillRequestKind.Ascension, 2));
        }

        [Test]
        public void ClampToRange_Inside_Unchanged()
        {
            Vector3 origin = new Vector3(1f, 0f, 1f);
            Vector3 target = new Vector3(3f, 0.5f, 4f);

            Assert.AreEqual(target, SkillInputRules.ClampToRange(origin, target, CAST_RANGE));
        }

        [Test]
        public void ClampToRange_Outside_ClampsHorizontalDistanceAndKeepsHeight()
        {
            Vector3 origin = new Vector3(1f, 0f, 1f);
            Vector3 target = new Vector3(1f, 0.5f, 21f);

            Vector3 clamped = SkillInputRules.ClampToRange(origin, target, CAST_RANGE);

            Assert.AreEqual(CAST_RANGE, HorizontalDistance(origin, clamped), DISTANCE_TOLERANCE);
            Assert.AreEqual(target.y, clamped.y);
            Assert.AreEqual(origin.x, clamped.x, DISTANCE_TOLERANCE);
            Assert.Greater(clamped.z, origin.z);
        }

        [Test]
        public void ClampToRange_NegativeLimit_ReturnsOriginHorizontally()
        {
            Vector3 origin = new Vector3(1f, 0f, 1f);
            Vector3 target = new Vector3(5f, 0f, 5f);

            Vector3 clamped = SkillInputRules.ClampToRange(origin, target, -1f);

            Assert.AreEqual(0f, HorizontalDistance(origin, clamped), DISTANCE_TOLERANCE);
        }

        [Test]
        public void ClampTargetPoint_FarCursor_StaysInsideServerRangeByMargin()
        {
            Vector3 origin = Vector3.zero;
            Vector3 cursor = new Vector3(30f, 0f, 40f);

            Vector3 clamped = SkillInputRules.ClampTargetPoint(origin, cursor, CAST_RANGE);

            Assert.AreEqual(CAST_RANGE - Constants.Player.SKILL_POINT_RANGE_MARGIN, HorizontalDistance(origin, clamped),
                DISTANCE_TOLERANCE);
            Assert.Less(HorizontalDistance(origin, clamped), CAST_RANGE);
        }

        [Test]
        public void IsRoutineRejection_CombatReasonsTrue_ConnectionReasonsFalse()
        {
            Assert.IsTrue(SkillInputRules.IsRoutineRejection(ERejectReason.Cooldown));
            Assert.IsTrue(SkillInputRules.IsRoutineRejection(ERejectReason.InsufficientEnergy));
            Assert.IsTrue(SkillInputRules.IsRoutineRejection(ERejectReason.OutOfRange));
            Assert.IsFalse(SkillInputRules.IsRoutineRejection(ERejectReason.SessionMismatch));
            Assert.IsFalse(SkillInputRules.IsRoutineRejection(ERejectReason.VersionMismatch));
            Assert.IsFalse(SkillInputRules.IsRoutineRejection(ERejectReason.Disabled));
        }

        [Test]
        public void GetRequiredStacks_ArcherBurst_RequiresFullStacks()
        {
            int required = SkillInputRules.GetRequiredStacks(_catalog, EPlayerClass.Archer, ESkillRequestKind.Regular,
                Constants.Combat.SKILL_SLOT_R);

            Assert.AreEqual(_catalog.ClassStackMax, required);
            Assert.Greater(required, 0);
        }

        [Test]
        public void GetRequiredStacks_SkillsWithoutRequirement_Zero()
        {
            Assert.AreEqual(0, SkillInputRules.GetRequiredStacks(_catalog, EPlayerClass.Archer, ESkillRequestKind.Regular,
                Constants.Combat.SKILL_SLOT_Q));
            Assert.AreEqual(0, SkillInputRules.GetRequiredStacks(_catalog, EPlayerClass.Mageknight, ESkillRequestKind.Regular,
                Constants.Combat.SKILL_SLOT_R));
            Assert.AreEqual(0, SkillInputRules.GetRequiredStacks(_catalog, EPlayerClass.Summoner, ESkillRequestKind.Ascension, 0));
            Assert.AreEqual(0, SkillInputRules.GetRequiredStacks(null, EPlayerClass.Archer, ESkillRequestKind.Regular,
                Constants.Combat.SKILL_SLOT_R));
        }

        [Test]
        public void HasRequiredStacks_BelowRequirementFalse_OtherwiseTrue()
        {
            int max = _catalog.ClassStackMax;

            Assert.IsFalse(SkillInputRules.HasRequiredStacks(max, max - 1));
            Assert.IsFalse(SkillInputRules.HasRequiredStacks(max, 0));
            Assert.IsTrue(SkillInputRules.HasRequiredStacks(max, max));
            Assert.IsTrue(SkillInputRules.HasRequiredStacks(0, 0));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static void AssertMapped(EPlayerClass playerClass, ESkillInputKey key, ESkillRequestKind expectedKind, int expectedSlot)
        {
            Assert.IsTrue(SkillInputRules.TryMapKey(playerClass, key, out ESkillRequestKind kind, out byte slot));
            Assert.AreEqual(expectedKind, kind);
            Assert.AreEqual(expectedSlot, slot);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float deltaX = b.x - a.x;
            float deltaZ = b.z - a.z;
            return Mathf.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
        }
    }
}
