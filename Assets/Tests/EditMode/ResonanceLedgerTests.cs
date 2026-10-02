// System
using System;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class ResonanceLedgerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int   CAPACITY          = 16;
        private const int   SMALL_CAPACITY    = 2;
        private const int   SINGLE_CAPACITY   = 1;
        private const int   ATTACKER          = 1;
        private const int   TARGET            = 2;
        private const int   OTHER_TARGET      = 3;
        private const int   CAST              = 10;
        private const int   NEW_CAST          = 20;
        private const int   NEXT_CAST         = 30;
        private const int   ACTION            = 100;
        private const int   NEXT_ACTION       = 101;
        private const int   LAST_ACTION       = 102;
        private const int   NEGATIVE_ID       = -1;
        private const float DAMAGE            = 10f;
        private const float BASIC_GAIN        = 1f;
        private const float SKILL_GAIN        = 4f;
        private const float COMPANION_GAIN    = 0.5f;
        private const float SKILL_CAP         = 8f;
        private const float MAXIMUM           = 100f;
        private const float FRACTIONAL_BONUS  = 3.2f;
        private const float OVER_CAP_BONUS    = 10f;
        private const float RESTORED_FRACTION = 7.2f;
        private const float SMALL_SPEND       = 0.25f;
        private const float ALMOST_MAXIMUM    = MAXIMUM - SMALL_SPEND;
        private const float NEGATIVE_VALUE    = -1f;
        private const float TOLERANCE         = 0.0001f;

        private SkillCatalogSO _catalog;

        /****************************************
        *                Setup
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
        public void Record_BasicHitsMultipleTargets_OnlyFirstTargetGrantsOne()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);

            Assert.IsTrue(ledger.Record(Request(), Hit(), EResonanceSource.Basic));
            Assert.IsFalse(ledger.Record(Request(target: OTHER_TARGET), Hit(), EResonanceSource.Basic));
            Assert.AreEqual(BASIC_GAIN, ledger.Value);
        }

        [Test]
        public void Record_BasicNextAction_GrantsAgain()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);
            ledger.Record(Request(), Hit(), EResonanceSource.Basic);

            Assert.IsTrue(ledger.Record(Request(action: NEXT_ACTION), Hit(), EResonanceSource.Basic));
            Assert.AreEqual(BASIC_GAIN + BASIC_GAIN, ledger.Value);
        }

        [Test]
        public void Record_SkillHitsMultipleTargets_OnlyFirstTargetGrantsFour()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);

            Assert.IsTrue(ledger.Record(Request(), Hit(), EResonanceSource.Skill));
            Assert.IsFalse(ledger.Record(Request(target: OTHER_TARGET), Hit(), EResonanceSource.Skill));
            Assert.AreEqual(SKILL_GAIN, ledger.Value);
        }

        [Test]
        public void Record_SkillAreaNextTick_GrantsOncePerCast()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);
            ledger.Record(Request(), Hit(), EResonanceSource.Skill);

            Assert.IsFalse(ledger.Record(Request(action: NEXT_ACTION), Hit(), EResonanceSource.Skill));
            Assert.IsFalse(ledger.Record(Request(action: LAST_ACTION, target: OTHER_TARGET), Hit(), EResonanceSource.Skill));
            Assert.AreEqual(SKILL_GAIN, ledger.Value);
        }

        [Test]
        public void Record_CompanionMultipleHitsInOneAction_GrantsHalfOnce()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);

            Assert.IsTrue(ledger.Record(Request(), Hit(), EResonanceSource.Companion));
            Assert.IsFalse(ledger.Record(Request(), Hit(), EResonanceSource.Companion));
            Assert.IsFalse(ledger.Record(Request(target: OTHER_TARGET), Hit(), EResonanceSource.Companion));
            Assert.AreEqual(COMPANION_GAIN, ledger.Value);
        }

        [Test]
        public void Record_CompanionNextAction_PreservesFractionalAccumulation()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);
            ledger.Record(Request(), Hit(), EResonanceSource.Companion);

            Assert.IsTrue(ledger.Record(Request(action: NEXT_ACTION), Hit(), EResonanceSource.Companion));
            Assert.IsTrue(ledger.Record(Request(action: LAST_ACTION), Hit(), EResonanceSource.Companion));
            Assert.AreEqual(COMPANION_GAIN + COMPANION_GAIN + COMPANION_GAIN, ledger.Value);
        }

        [Test]
        public void Record_SameTripleWithDifferentSource_RejectsReplay()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);
            ledger.Record(Request(), Hit(), EResonanceSource.Basic);

            Assert.IsFalse(ledger.Record(Request(), Hit(), EResonanceSource.Skill));
            Assert.AreEqual(BASIC_GAIN, ledger.Value);
        }

        [Test]
        public void Record_SameActionAndTargetInDifferentCast_AllowsNewCast()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);
            ledger.Record(Request(), Hit(), EResonanceSource.Skill);

            Assert.IsTrue(ledger.Record(Request(cast: NEW_CAST), Hit(), EResonanceSource.Skill));
            Assert.AreEqual(SKILL_GAIN + SKILL_GAIN, ledger.Value);
        }

        [TestCase(CombatIds.NONE, CAST, ACTION, TARGET)]
        [TestCase(ATTACKER, CombatIds.NONE, ACTION, TARGET)]
        [TestCase(ATTACKER, CAST, CombatIds.NONE, TARGET)]
        [TestCase(ATTACKER, CAST, ACTION, CombatIds.NONE)]
        [TestCase(NEGATIVE_ID, CAST, ACTION, TARGET)]
        [TestCase(ATTACKER, NEGATIVE_ID, ACTION, TARGET)]
        [TestCase(ATTACKER, CAST, NEGATIVE_ID, TARGET)]
        [TestCase(ATTACKER, CAST, ACTION, NEGATIVE_ID)]
        public void Record_InvalidId_RejectsWithoutConsumingCapacity(int attacker, int cast, int action, int target)
        {
            var ledger = new ResonanceLedger(_catalog, SINGLE_CAPACITY);

            Assert.IsFalse(ledger.Record(Request(attacker, cast, action, target), Hit(), EResonanceSource.Skill));
            Assert.IsTrue(ledger.Record(Request(), Hit(), EResonanceSource.Skill));
            Assert.AreEqual(SKILL_GAIN, ledger.Value);
        }

        [Test]
        public void Record_InvulnerableGuardedAndNoDamage_GrantsNothingAndAllowsLaterHit()
        {
            var ledger = new ResonanceLedger(_catalog, SINGLE_CAPACITY);

            Assert.IsFalse(ledger.Record(Request(), DamageResult.Ignored(EDamageOutcome.Invulnerable), EResonanceSource.Skill));
            Assert.IsFalse(ledger.Record(Request(), DamageResult.Guarded(), EResonanceSource.Skill));
            Assert.IsFalse(ledger.Record(Request(), DamageResult.Applied(0f, false, false), EResonanceSource.Skill));
            Assert.IsTrue(ledger.Record(Request(), Hit(), EResonanceSource.Skill));
            Assert.AreEqual(SKILL_GAIN, ledger.Value);
        }

        [Test]
        public void Record_LethalZeroAppliedAmount_CountsOneFinishingHit()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);

            Assert.IsTrue(ledger.Record(Request(), DamageResult.Applied(0f, true, false), EResonanceSource.Skill));
            Assert.IsFalse(ledger.Record(Request(), DamageResult.Applied(0f, true, false), EResonanceSource.Skill));
            Assert.AreEqual(SKILL_GAIN, ledger.Value);
        }

        [Test]
        public void Record_NoResonanceOrAscension_GrantsNothing()
        {
            var ledger = new ResonanceLedger(_catalog, SINGLE_CAPACITY);

            Assert.IsFalse(ledger.Record(Request(flags: EDamageFlags.NoResonance), Hit(), EResonanceSource.Basic));
            Assert.IsFalse(ledger.Record(Request(), Hit(), EResonanceSource.Ascension));
            Assert.IsFalse(ledger.Record(Request(), Hit(), (EResonanceSource)int.MaxValue));
            Assert.IsTrue(ledger.Record(Request(), Hit(), EResonanceSource.Basic));
        }

        [Test]
        public void Record_SkillFractionalBonus_GrantsBonusOnceWithoutRounding()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);

            Assert.IsTrue(ledger.Record(Request(), Hit(), EResonanceSource.Skill, FRACTIONAL_BONUS));
            Assert.IsFalse(ledger.Record(Request(action: NEXT_ACTION), Hit(), EResonanceSource.Skill, FRACTIONAL_BONUS));
            Assert.AreEqual(SKILL_GAIN + FRACTIONAL_BONUS, ledger.Value, TOLERANCE);
        }

        [Test]
        public void Record_SkillExcessiveBonus_CapsGainAtEight()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);

            Assert.IsTrue(ledger.Record(Request(), Hit(), EResonanceSource.Skill, OVER_CAP_BONUS));
            Assert.AreEqual(SKILL_CAP, ledger.Value);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(NEGATIVE_VALUE)]
        public void Record_InvalidBonus_RejectsWithoutChangingValue(float bonus)
        {
            var ledger = new ResonanceLedger(_catalog, SINGLE_CAPACITY);

            Assert.IsFalse(ledger.Record(Request(), Hit(), EResonanceSource.Skill, bonus));
            Assert.IsTrue(ledger.Record(Request(), Hit(), EResonanceSource.Skill));
            Assert.AreEqual(SKILL_GAIN, ledger.Value);
        }

        [Test]
        public void Record_ValueReachesMaximum_KeepsReplayProtection()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);
            ledger.Restore(ALMOST_MAXIMUM);

            Assert.IsTrue(ledger.Record(Request(), Hit(), EResonanceSource.Companion));
            Assert.AreEqual(MAXIMUM, ledger.Value);
            Assert.IsTrue(ledger.TrySpend(SMALL_SPEND));
            Assert.IsFalse(ledger.Record(Request(), Hit(), EResonanceSource.Companion));
            Assert.AreEqual(ALMOST_MAXIMUM, ledger.Value);
        }

        [Test]
        public void TrySpend_FractionAndInsufficientValue_OnlyValidSpendChangesValue()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);
            ledger.Restore(RESTORED_FRACTION);

            Assert.IsTrue(ledger.TrySpend(SMALL_SPEND));
            Assert.AreEqual(RESTORED_FRACTION - SMALL_SPEND, ledger.Value, TOLERANCE);
            Assert.IsFalse(ledger.TrySpend(MAXIMUM));
            Assert.IsFalse(ledger.TrySpend(0f));
            Assert.IsFalse(ledger.TrySpend(NEGATIVE_VALUE));
            Assert.IsFalse(ledger.TrySpend(float.NaN));
            Assert.IsFalse(ledger.TrySpend(float.PositiveInfinity));
            Assert.AreEqual(RESTORED_FRACTION - SMALL_SPEND, ledger.Value, TOLERANCE);
        }

        [Test]
        public void Restore_ValidFractionAndOutOfRange_ClampsFiniteValues()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);
            ledger.Restore(RESTORED_FRACTION);
            Assert.AreEqual(RESTORED_FRACTION, ledger.Value);

            ledger.Restore(float.NaN);
            ledger.Restore(float.PositiveInfinity);
            Assert.AreEqual(RESTORED_FRACTION, ledger.Value);

            ledger.Restore(MAXIMUM + BASIC_GAIN);
            Assert.AreEqual(MAXIMUM, ledger.Value);

            ledger.Restore(NEGATIVE_VALUE);
            Assert.AreEqual(0f, ledger.Value);
        }

        [Test]
        public void Restore_AfterHit_DoesNotClearDuplicateRecord()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);
            ledger.Record(Request(), Hit(), EResonanceSource.Skill);
            ledger.Restore(0f);

            Assert.IsFalse(ledger.Record(Request(), Hit(), EResonanceSource.Skill));
            Assert.AreEqual(0f, ledger.Value);
        }

        [Test]
        public void OpenCast_BeforeNewerCastEnds_AllowsDelayedFirstHit()
        {
            var ledger = new ResonanceLedger(_catalog, SMALL_CAPACITY);
            Assert.IsTrue(ledger.OpenCast(CAST));
            Assert.IsTrue(ledger.Record(Request(cast: NEW_CAST), Hit(), EResonanceSource.Basic));
            ledger.ReleaseCast(NEW_CAST);

            Assert.IsTrue(ledger.Record(Request(), Hit(), EResonanceSource.Skill));
            Assert.IsFalse(ledger.Record(Request(action: NEXT_ACTION), Hit(), EResonanceSource.Skill));
            Assert.AreEqual(BASIC_GAIN + SKILL_GAIN, ledger.Value);
        }

        [Test]
        public void OpenCast_ReservationFillsCapacity_FirstHitReusesItsSlot()
        {
            var ledger = new ResonanceLedger(_catalog, SINGLE_CAPACITY);

            Assert.IsTrue(ledger.OpenCast(CAST));
            Assert.IsTrue(ledger.OpenCast(CAST));
            Assert.IsFalse(ledger.OpenCast(NEW_CAST));
            Assert.IsFalse(ledger.Record(Request(cast: NEW_CAST), Hit(), EResonanceSource.Skill));
            Assert.IsTrue(ledger.Record(Request(), Hit(), EResonanceSource.Skill));
            Assert.AreEqual(SKILL_GAIN, ledger.Value);
        }

        [Test]
        public void OpenCast_ReleasedOrInvalidCast_RejectsReservation()
        {
            var ledger = new ResonanceLedger(_catalog, SINGLE_CAPACITY);
            Assert.IsTrue(ledger.OpenCast(CAST));
            ledger.ReleaseCast(CAST);

            Assert.IsFalse(ledger.OpenCast(CAST));
            Assert.IsFalse(ledger.OpenCast(CombatIds.NONE));
            Assert.IsFalse(ledger.OpenCast(NEGATIVE_ID));
            Assert.IsTrue(ledger.OpenCast(NEW_CAST));
            Assert.IsTrue(ledger.Record(Request(cast: NEW_CAST), Hit(), EResonanceSource.Skill));
        }

        [Test]
        public void ReleaseCast_FullLedger_FreesOnlyEndedCastAndRejectsItsReplay()
        {
            var ledger = new ResonanceLedger(_catalog, SMALL_CAPACITY);
            ledger.Record(Request(), Hit(), EResonanceSource.Basic);
            ledger.Record(Request(cast: NEW_CAST), Hit(), EResonanceSource.Basic);
            Assert.IsFalse(ledger.Record(Request(cast: NEXT_CAST), Hit(), EResonanceSource.Basic));

            ledger.ReleaseCast(CAST);

            Assert.IsFalse(ledger.Record(Request(action: NEXT_ACTION), Hit(), EResonanceSource.Basic));
            Assert.IsFalse(ledger.Record(Request(cast: NEW_CAST), Hit(), EResonanceSource.Basic));
            Assert.IsTrue(ledger.Record(Request(cast: NEXT_CAST), Hit(), EResonanceSource.Basic));
            Assert.AreEqual(BASIC_GAIN + BASIC_GAIN + BASIC_GAIN, ledger.Value);
        }

        [Test]
        public void ReleaseCast_NewerCastEndsFirst_KeepsRecordedOlderCastAlive()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);
            ledger.Record(Request(), Hit(), EResonanceSource.Companion);
            ledger.Record(Request(cast: NEW_CAST), Hit(), EResonanceSource.Companion);
            ledger.ReleaseCast(NEW_CAST);

            Assert.IsTrue(ledger.Record(Request(action: NEXT_ACTION), Hit(), EResonanceSource.Companion));
            Assert.IsFalse(ledger.Record(Request(cast: NEW_CAST, action: NEXT_ACTION), Hit(), EResonanceSource.Companion));

            ledger.ReleaseCast(CAST);

            Assert.IsFalse(ledger.Record(Request(action: LAST_ACTION), Hit(), EResonanceSource.Companion));
            Assert.IsFalse(ledger.Record(Request(cast: NEW_CAST, action: LAST_ACTION), Hit(), EResonanceSource.Companion));
            Assert.AreEqual(COMPANION_GAIN + COMPANION_GAIN + COMPANION_GAIN, ledger.Value);
        }

        [Test]
        public void ReleaseCast_UnrecordedOldCastBelowReleasedId_RejectsConservatively()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);
            ledger.ReleaseCast(NEW_CAST);

            Assert.IsFalse(ledger.Record(Request(), Hit(), EResonanceSource.Skill));
            Assert.IsFalse(ledger.Record(Request(cast: NEW_CAST), Hit(), EResonanceSource.Skill));
            Assert.IsTrue(ledger.Record(Request(cast: NEXT_CAST), Hit(), EResonanceSource.Skill));
        }

        [Test]
        public void ReleaseCast_InvalidId_DoesNotCloseValidCastOrChangeValue()
        {
            var ledger = new ResonanceLedger(_catalog, CAPACITY);
            ledger.Record(Request(), Hit(), EResonanceSource.Companion);
            ledger.ReleaseCast(CombatIds.NONE);
            ledger.ReleaseCast(NEGATIVE_ID);

            Assert.IsTrue(ledger.Record(Request(action: NEXT_ACTION), Hit(), EResonanceSource.Companion));
            Assert.AreEqual(COMPANION_GAIN + COMPANION_GAIN, ledger.Value);
        }

        [Test]
        public void Constructor_NullCatalogOrInvalidCapacity_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ResonanceLedger(null, CAPACITY));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResonanceLedger(_catalog, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResonanceLedger(_catalog, NEGATIVE_ID));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static DamageRequest Request(int attacker = ATTACKER, int cast = CAST, int action = ACTION,
            int target = TARGET, EDamageFlags flags = EDamageFlags.None)
        {
            return new DamageRequest(attacker, target, cast, action, DAMAGE, flags, Vector3.zero, string.Empty);
        }

        private static DamageResult Hit()
        {
            return DamageResult.Applied(DAMAGE, false, false);
        }
    }
}
