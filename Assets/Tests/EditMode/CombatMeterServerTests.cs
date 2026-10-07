// System
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;
using Shinshi.Systems;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class CombatMeterServerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    PARTICIPANT      = 1;
        private const int    SECOND_PARTICIPANT = 2;
        private const int    PLAYER_ID        = 11;
        private const int    ENEMY_ID         = 12;
        private const int    SUMMON_ID        = 13;
        private const int    REPLACEMENT_ID   = 14;
        private const int    UNKNOWN_ID       = 99;
        private const int    SESSION_EPOCH    = 73;
        private const int    BASIC_SLOT       = (int)ECombatMeterSlot.BasicAttack;
        private const int    FIRST_SKILL_SLOT = (int)ECombatMeterSlot.Skill1;
        private const int    SUMMON_SLOT      = (int)ECombatMeterSlot.Summon;
        private const int    EXTRA_SLOT       = (int)ECombatMeterSlot.Extra;
        private const int    OTHER_SLOT       = (int)ECombatMeterSlot.Other;
        private const int    DEATH_RECAP_SIZE = Constants.CombatMeter.DEATH_RECAP_MAX_ENTRIES;
        private const int    REPEAT_COUNT     = 256;
        private const int    POTION_STOCK     = 2;
        private const uint   INPUT_SEQUENCE   = 41;
        private const float  MAX_HEALTH       = 1000000f;
        private const float  RAW_DAMAGE       = 100f;
        private const float  DEFENSE_CONSTANT = 1000f;
        private const float  REDUCTION_CAP    = 0.6f;
        private const float  TOLERANCE        = 0.001f;
        private const double START_TIME       = 0.0;
        private const double END_TIME         = 20.0;
        private const string SESSION_ID       = "meter-session";
        private const string PARTICIPANT_KEY  = "meter-player";
        private const string SECOND_PARTICIPANT_KEY = "meter-second";
        private const string BOSS_ID          = "boss_meter_test";
        private const string CAPTURE_FAILURE  = "meter-capture-failure";
        private const string CLOSE_FAILURE    = "meter-close-failure";
        private const string WARNING_FAILURE  = "meter-warning-failure";
        private const string UNKNOWN_SOURCE   = "meter-unregistered-source";
        private const string OTHER_UNKNOWN_SOURCE = "meter-another-unregistered-source";
        private const string UNKNOWN_WARNING  = "[CombatMeter] 등록되지 않은 피해 출처를 기타 슬롯에 기록했다";
        private const string TERMINAL_ERROR   = "[SessionEventQueue] 저장이 확정 거절돼 큐를 멈춘다. 출정은 서비스 장애 취소로 끝난다";

        private CombatTargetRegistry _registry;
        private DamageResolver       _inner;
        private CombatMeterResolver  _resolver;
        private CombatMeterRecorder  _meter;
        private SkillCatalogSO       _catalog;
        private FakeDamageable       _player;
        private FakeDamageable       _enemy;
        private FakeDamageable       _summon;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _catalog  = ScriptableObject.CreateInstance<SkillCatalogSO>();
            _registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY,
                Constants.Combat.HIT_LEDGER_RETENTION_SECONDS, Constants.Combat.LONGEST_ACTION_SECONDS);
            _inner = new DamageResolver(_registry, ledger, DEFENSE_CONSTANT, REDUCTION_CAP);
            _meter = new CombatMeterRecorder(_registry, _catalog, null, SESSION_EPOCH, SESSION_ID);
            _resolver = new CombatMeterResolver(_inner, _meter, _inner);
            _player = new FakeDamageable(PLAYER_ID, ECombatFaction.Player, MAX_HEALTH, 0f);
            _enemy  = new FakeDamageable(ENEMY_ID, ECombatFaction.Enemy, MAX_HEALTH, 0f);
            _summon = new FakeDamageable(SUMMON_ID, ECombatFaction.Player, MAX_HEALTH, 0f)
            {
                IsSummon      = true,
                OwnerCombatId = PLAYER_ID
            };
            Assert.IsTrue(_registry.Register(_player));
            Assert.IsTrue(_registry.Register(_enemy));
            Assert.IsTrue(_registry.Register(_summon));
            var stats = new CharacterStats(RAW_DAMAGE, MAX_HEALTH, 0f, 0f, RAW_DAMAGE, 5f);
            Assert.IsTrue(_meter.RegisterParticipant(PARTICIPANT, PLAYER_ID, PARTICIPANT_KEY,
                EPlayerClass.Warrior, in stats));
            _meter.Begin(START_TIME);
            _inner.BeginTick(START_TIME);
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
        public void Apply_LethalOverkill_PreservesResultAndRecordsRemainingHealth()
        {
            const float REMAINING_HEALTH = 35f;
            _enemy.Health = REMAINING_HEALTH;

            DamageResult result = Apply(PLAYER_ID, ENEMY_ID);
            CombatMeterSelfMessage self = Self();

            Assert.AreEqual(EDamageOutcome.Applied, result.Outcome);
            Assert.IsTrue(result.IsLethal);
            Assert.IsTrue(result.CountsAsHit);
            Assert.AreEqual(REMAINING_HEALTH, result.AppliedAmount, TOLERANCE);
            Assert.AreEqual(EParticipantLifeState.Dead, _enemy.LifeState);
            Assert.AreEqual(1, _enemy.DamageCalls);
            Assert.AreEqual(REMAINING_HEALTH, self.EncounterDamage, TOLERANCE);
            Assert.AreEqual(REMAINING_HEALTH, self.Slots[BASIC_SLOT].MaxHit, TOLERANCE);
            Assert.AreEqual(1, self.Slots[BASIC_SLOT].Hits);
        }

        [Test]
        public void Apply_CaptureThrows_PreservesCombatResultAndStopsMeterAfterOneException()
        {
            var targets = new ThrowingTargetQuery(_registry);
            ConfigureMeter(targets);
            Apply(PLAYER_ID, ENEMY_ID);
            targets.ThrowOnTryGet = true;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: " + CAPTURE_FAILURE));

            DamageResult failedCapture = Apply(PLAYER_ID, ENEMY_ID);
            int queriesAtFault = targets.TryGetCalls;
            DamageResult afterFault = Apply(PLAYER_ID, ENEMY_ID);

            Assert.AreEqual(EDamageOutcome.Applied, failedCapture.Outcome);
            Assert.AreEqual(RAW_DAMAGE, failedCapture.AppliedAmount, TOLERANCE);
            Assert.IsTrue(failedCapture.CountsAsHit);
            Assert.AreEqual(EDamageOutcome.Applied, afterFault.Outcome);
            Assert.AreEqual(3, _enemy.DamageCalls);
            Assert.AreEqual(MAX_HEALTH - (RAW_DAMAGE * 3f), _enemy.Health, TOLERANCE);
            Assert.AreEqual(queriesAtFault, targets.TryGetCalls);
            Assert.IsTrue(GetMeterField<bool>("_faulted"));
            Assert.AreEqual(RAW_DAMAGE, Self().EncounterDamage, TOLERANCE);
            Assert.AreEqual(1, Self().Slots[BASIC_SLOT].Hits);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Apply_RecordThrows_PreservesLethalResultAndStopsMeterAfterOneException()
        {
            const float REMAINING_HEALTH = 35f;
            object slots = GetParticipantField("Slots");
            _enemy.Health = REMAINING_HEALTH;
            SetParticipantField("Slots", null);
            try
            {
                LogAssert.Expect(LogType.Exception, new Regex("NullReferenceException"));

                DamageResult result = Apply(PLAYER_ID, ENEMY_ID);

                Assert.AreEqual(EDamageOutcome.Applied, result.Outcome);
                Assert.AreEqual(REMAINING_HEALTH, result.AppliedAmount, TOLERANCE);
                Assert.IsTrue(result.IsLethal);
                Assert.IsTrue(result.CountsAsHit);
                Assert.AreEqual(EParticipantLifeState.Dead, _enemy.LifeState);
                Assert.AreEqual(1, _enemy.DamageCalls);
            }
            finally
            {
                SetParticipantField("Slots", slots);
            }

            _enemy.Health    = MAX_HEALTH;
            _enemy.LifeState = EParticipantLifeState.Alive;
            Assert.AreEqual(EDamageOutcome.Applied, Apply(PLAYER_ID, ENEMY_ID).Outcome);
            Assert.IsTrue(GetMeterField<bool>("_faulted"));
            Assert.AreEqual(2, _enemy.DamageCalls);
            Assert.AreEqual(0f, Self().EncounterDamage);
            Assert.AreEqual(0, Self().Slots[BASIC_SLOT].Hits);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OnSessionClosed_FreezeOrBuildThrows_FreezesAndLeavesCombatWorking(bool buildFailure)
        {
            object sendSlots = GetParticipantField("SendSlots");
            int phaseCount = GetMeterField<int>("_phaseCount");
            if (buildFailure)
            {
                SetParticipantField("SendSlots", null);
            }
            else
            {
                SetMeterField("_phaseCount", 0);
            }
            try
            {
                LogAssert.Expect(LogType.Exception,
                    new Regex(buildFailure ? "NullReferenceException" : "IndexOutOfRangeException"));

                Assert.DoesNotThrow(() => _meter.OnSessionClosed(ESessionEndReason.Success, END_TIME));

                Assert.IsTrue(_meter.IsClosed);
                Assert.IsFalse(_meter.TryGetFinalSelf(PARTICIPANT, out _));
                Assert.IsFalse(_meter.TryGetResult(out _));
                Assert.DoesNotThrow(() => _meter.OnSessionClosed(ESessionEndReason.Timeout, END_TIME));
                Assert.AreEqual(EDamageOutcome.Applied, Apply(PLAYER_ID, ENEMY_ID).Outcome);
                Assert.AreEqual(1, _enemy.DamageCalls);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                SetParticipantField("SendSlots", sendSlots);
                SetMeterField("_phaseCount", phaseCount);
            }
        }

        [Test]
        public void OnSessionClosed_WarningLoggerThrows_FreezesAndSuppressesRepeatedFault()
        {
            Apply(PLAYER_ID, ENEMY_ID, UNKNOWN_SOURCE);
            ILogHandler original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new ThrowingWarningLogHandler(original);
            try
            {
                LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: " + WARNING_FAILURE));

                Assert.DoesNotThrow(() => _meter.OnSessionClosed(ESessionEndReason.Success, END_TIME));

                Assert.IsTrue(_meter.IsClosed);
                Assert.AreEqual(RAW_DAMAGE, FinalSelf().EncounterDamage, TOLERANCE);
                Assert.DoesNotThrow(() => _meter.OnSessionClosed(ESessionEndReason.Timeout, END_TIME));
                Assert.AreEqual(EDamageOutcome.Applied, Apply(PLAYER_ID, ENEMY_ID).Outcome);
                Assert.AreEqual(2, _enemy.DamageCalls);
                Assert.AreEqual(RAW_DAMAGE, FinalSelf().EncounterDamage, TOLERANCE);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }
        }

        [TestCase(EDamageOutcome.DuplicateHit)]
        [TestCase(EDamageOutcome.SameFaction)]
        [TestCase(EDamageOutcome.TargetNotFound)]
        [TestCase(EDamageOutcome.TargetNotAlive)]
        [TestCase(EDamageOutcome.Invulnerable)]
        public void Record_IgnoredOutcome_ChangesNeitherOutgoingNorIncomingTotals(EDamageOutcome outcome)
        {
            DamageRequest outgoing = Request(PLAYER_ID, ENEMY_ID);
            DamageRequest incoming = Request(ENEMY_ID, PLAYER_ID);
            DamageResult result = DamageResult.Ignored(outcome);
            _meter.Record(in outgoing, in result, START_TIME);
            _meter.Record(in incoming, in result, START_TIME);

            CombatMeterSelfMessage self = Self();
            Assert.AreEqual(0f, self.EncounterDamage);
            Assert.AreEqual(0f, self.DamageTaken);
            Assert.AreEqual(0, self.GuardedHits);
            Assert.AreEqual(0, self.Slots[BASIC_SLOT].Hits);
        }

        [TestCase(0f, false, 0)]
        [TestCase(0f, true, 1)]
        [TestCase(RAW_DAMAGE, false, 1)]
        public void Record_AppliedResult_UsesCountsAsHit(float amount, bool lethal, int expectedHits)
        {
            DamageRequest request = Request(PLAYER_ID, ENEMY_ID);
            DamageResult result = DamageResult.Applied(amount, lethal, false);

            _meter.Record(in request, in result, START_TIME);

            CombatMeterSelfMessage self = Self();
            Assert.AreEqual(amount, self.EncounterDamage, TOLERANCE);
            Assert.AreEqual(expectedHits, self.Slots[BASIC_SLOT].Hits);
            Assert.AreEqual(amount, self.Slots[BASIC_SLOT].MaxHit, TOLERANCE);
        }

        [Test]
        public void Apply_GuardedTwice_CountsVictimGuardHitsWithoutDamageOrOutgoingHits()
        {
            _player.IsGuardingFront = true;

            Assert.AreEqual(EDamageOutcome.Guarded, Apply(ENEMY_ID, PLAYER_ID).Outcome);
            Assert.AreEqual(EDamageOutcome.Guarded, Apply(ENEMY_ID, PLAYER_ID).Outcome);

            CombatMeterSelfMessage self = Self();
            Assert.AreEqual(2, self.GuardedHits);
            Assert.AreEqual(0f, self.DamageTaken);
            Assert.AreEqual(0f, self.EncounterDamage);
            Assert.AreEqual(0, self.Slots[BASIC_SLOT].Hits);
            Assert.AreEqual(0, _player.DamageCalls);
        }

        [Test]
        public void Apply_DuplicateAfterGuard_DoesNotCountSecondGuardHit()
        {
            _player.IsGuardingFront = true;
            DamageRequest request = Request(ENEMY_ID, PLAYER_ID);

            Assert.AreEqual(EDamageOutcome.Guarded, _resolver.Apply(in request).Outcome);
            Assert.AreEqual(EDamageOutcome.DuplicateHit, _resolver.Apply(in request).Outcome);

            Assert.AreEqual(1, Self().GuardedHits);
        }

        [Test]
        public void Apply_SummonUsingCatalogSkill_AttributesToOwnerAndPrefersSummonSlot()
        {
            Assert.IsTrue(_catalog.TryGetSkillBySlot(EPlayerClass.Warrior, 0,
                out SkillCatalogSO.SkillDefinition skill));

            Apply(SUMMON_ID, ENEMY_ID, skill.DefinitionId);

            CombatMeterSelfMessage self = Self();
            Assert.AreEqual(RAW_DAMAGE, self.EncounterDamage, TOLERANCE);
            Assert.AreEqual(RAW_DAMAGE, self.Slots[SUMMON_SLOT].Damage, TOLERANCE);
            Assert.AreEqual(0f, self.Slots[FIRST_SKILL_SLOT].Damage);
            Assert.AreEqual(1, self.Slots[SUMMON_SLOT].Hits);
        }

        [Test]
        public void Apply_UnregisteredOwnerAndResidualSummon_KeepsParticipantAttribution()
        {
            _registry.Unregister(PLAYER_ID);

            Assert.AreEqual(EDamageOutcome.Applied, Apply(SUMMON_ID, ENEMY_ID).Outcome);
            Assert.AreEqual(EDamageOutcome.Applied, Apply(PLAYER_ID, ENEMY_ID).Outcome);

            CombatMeterSelfMessage self = Self();
            Assert.AreEqual(RAW_DAMAGE * 2f, self.EncounterDamage, TOLERANCE);
            Assert.AreEqual(RAW_DAMAGE, self.Slots[SUMMON_SLOT].Damage, TOLERANCE);
            Assert.AreEqual(RAW_DAMAGE, self.Slots[BASIC_SLOT].Damage, TOLERANCE);
        }

        [Test]
        public void RegisterCombatId_ReplacementBody_KeepsOldBodyResidualDamageAttribution()
        {
            _registry.Unregister(PLAYER_ID);
            Assert.IsTrue(_registry.Register(new FakeDamageable(REPLACEMENT_ID, ECombatFaction.Player, MAX_HEALTH, 0f)));
            _meter.RegisterCombatId(PARTICIPANT, REPLACEMENT_ID);

            Apply(PLAYER_ID, ENEMY_ID);
            Apply(REPLACEMENT_ID, ENEMY_ID);

            Assert.AreEqual(RAW_DAMAGE * 2f, Self().EncounterDamage, TOLERANCE);
        }

        [Test]
        public void RegisterCombatId_MoreThanInitialCapacity_PreservesFirstAndLatestUntilParticipantClose()
        {
            const int COMBAT_ID_BASE = 1000;
            const int BODY_COUNT = Constants.Combat.MAX_COMBAT_TARGETS + 1;
            for (int i = 0; i < BODY_COUNT; ++i)
            {
                Assert.IsTrue(_meter.RegisterCombatId(PARTICIPANT, COMBAT_ID_BASE + i));
            }
            _registry.Unregister(PLAYER_ID);
            Apply(PLAYER_ID, ENEMY_ID);
            Apply(COMBAT_ID_BASE + BODY_COUNT - 1, ENEMY_ID);
            _meter.CloseParticipant(PARTICIPANT, 1.0);
            Apply(PLAYER_ID, ENEMY_ID);
            Apply(COMBAT_ID_BASE + BODY_COUNT - 1, ENEMY_ID);

            Assert.AreEqual(RAW_DAMAGE * 2f, Self().EncounterDamage, TOLERANCE);
            Assert.AreEqual(4, _enemy.DamageCalls);
        }

        [Test]
        public void Apply_RepeatedSummonRemoval_PrunesStaleCacheWithoutOverflowOrLosingBodies()
        {
            const int COMBAT_ID_BASE = 1000;
            const int SUMMON_COUNT = Constants.Combat.MAX_COMBAT_TARGETS + 1;
            for (int i = 0; i < SUMMON_COUNT; ++i)
            {
                int combatId = COMBAT_ID_BASE + i;
                var summon = new FakeDamageable(combatId, ECombatFaction.Player, MAX_HEALTH, 0f)
                {
                    IsSummon      = true,
                    OwnerCombatId = PLAYER_ID
                };
                Assert.IsTrue(_registry.Register(summon));
                Apply(combatId, ENEMY_ID);
                Apply(combatId, ENEMY_ID);
                Assert.IsTrue(_registry.Unregister(combatId));
            }
            Assert.IsTrue(_meter.RegisterCombatId(PARTICIPANT, REPLACEMENT_ID));
            Apply(REPLACEMENT_ID, ENEMY_ID);
            _meter.Tick(1.0);

            CombatMeterSelfMessage self = Self();
            Assert.AreEqual(SUMMON_COUNT * RAW_DAMAGE * 2f, self.Slots[SUMMON_SLOT].Damage, TOLERANCE);
            Assert.AreEqual(RAW_DAMAGE, self.Slots[BASIC_SLOT].Damage, TOLERANCE);
            Assert.LessOrEqual(GetMeterField<int>("_summonAttributionCount"), Constants.Combat.MAX_COMBAT_TARGETS);
            Assert.IsFalse(GetMeterField<bool>("_summonOverflow"));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Apply_RemovedCachedSummon_DropsAttributionAndReleasesCacheEntry()
        {
            Apply(SUMMON_ID, ENEMY_ID);
            Assert.AreEqual(1, GetMeterField<int>("_summonAttributionCount"));
            Assert.IsTrue(_registry.Unregister(SUMMON_ID));

            Assert.AreEqual(EDamageOutcome.Applied, Apply(SUMMON_ID, ENEMY_ID).Outcome);

            Assert.AreEqual(0, GetMeterField<int>("_summonAttributionCount"));
            Assert.AreEqual(RAW_DAMAGE, Self().Slots[SUMMON_SLOT].Damage, TOLERANCE);
            Assert.AreEqual(2, _enemy.DamageCalls);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Apply_ReusedSummonCombatId_AttributesToCurrentOwner(bool observeRemoval)
        {
            var stats = new CharacterStats(RAW_DAMAGE, MAX_HEALTH, 0f, 0f, RAW_DAMAGE, 5f);
            Assert.IsTrue(_meter.RegisterParticipant(SECOND_PARTICIPANT, REPLACEMENT_ID, SECOND_PARTICIPANT_KEY,
                EPlayerClass.Summoner, in stats));
            Assert.IsTrue(_registry.Register(new FakeDamageable(REPLACEMENT_ID, ECombatFaction.Player, MAX_HEALTH, 0f)));
            Apply(SUMMON_ID, ENEMY_ID);
            Assert.IsTrue(_registry.Unregister(SUMMON_ID));
            if (observeRemoval)
            {
                Apply(SUMMON_ID, ENEMY_ID);
            }
            var replacement = new FakeDamageable(SUMMON_ID, ECombatFaction.Player, MAX_HEALTH, 0f)
            {
                IsSummon      = true,
                OwnerCombatId = REPLACEMENT_ID
            };
            Assert.IsTrue(_registry.Register(replacement));

            Assert.AreEqual(EDamageOutcome.Applied, Apply(SUMMON_ID, ENEMY_ID).Outcome);

            Assert.AreEqual(RAW_DAMAGE, Self().EncounterDamage, TOLERANCE);
            Assert.IsTrue(_meter.TryGetSelf(SECOND_PARTICIPANT, END_TIME, out CombatMeterSelfMessage second));
            Assert.AreEqual(RAW_DAMAGE, second.EncounterDamage, TOLERANCE);
            Assert.AreEqual(RAW_DAMAGE, second.Slots[SUMMON_SLOT].Damage, TOLERANCE);
            Assert.AreEqual(1, GetMeterField<int>("_summonAttributionCount"));
        }

        [Test]
        public void Apply_WarmedSummonAttributionAndRemoval_AllocatesZeroBytes()
        {
            Apply(SUMMON_ID, ENEMY_ID);
            DamageRequest summonRequest = Request(SUMMON_ID, ENEMY_ID);
            DamageResult applied = DamageResult.Applied(RAW_DAMAGE, false, false);
            _meter.Record(in summonRequest, in applied, START_TIME);
            Assert.IsTrue(_registry.Unregister(SUMMON_ID));
            _meter.Record(in summonRequest, in applied, START_TIME);
            Assert.IsTrue(_registry.Register(_summon));
            _meter.Record(in summonRequest, in applied, START_TIME);
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < REPEAT_COUNT; ++i)
            {
                _meter.Record(in summonRequest, in applied, START_TIME);
            }
            _registry.Unregister(SUMMON_ID);
            _meter.Record(in summonRequest, in applied, START_TIME);
            for (int i = 0; i < REPEAT_COUNT; ++i)
            {
                _meter.Record(in summonRequest, in applied, START_TIME);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
            Assert.AreEqual((REPEAT_COUNT + 3) * RAW_DAMAGE, Self().Slots[SUMMON_SLOT].Damage, TOLERANCE);
            Assert.AreEqual(0, GetMeterField<int>("_summonAttributionCount"));
        }

        [Test]
        public void CloseParticipant_ResidualBodyAndSummonDamage_IsNotAttributedAfterClose()
        {
            Apply(PLAYER_ID, ENEMY_ID);
            _meter.CloseParticipant(PARTICIPANT, 1.0);

            Assert.AreEqual(EDamageOutcome.Applied, Apply(PLAYER_ID, ENEMY_ID).Outcome);
            Assert.AreEqual(EDamageOutcome.Applied, Apply(SUMMON_ID, ENEMY_ID).Outcome);

            Assert.AreEqual(3, _enemy.DamageCalls);
            Assert.AreEqual(RAW_DAMAGE, Self().EncounterDamage, TOLERANCE);
        }

        [Test]
        public void Apply_UnknownRemovedAttacker_RecordsVictimDamageWithoutOutgoingAttribution()
        {
            Assert.AreEqual(EDamageOutcome.Applied, Apply(UNKNOWN_ID, PLAYER_ID, UNKNOWN_SOURCE).Outcome);

            CombatMeterSelfMessage self = Self();
            Assert.AreEqual(RAW_DAMAGE, self.DamageTaken, TOLERANCE);
            Assert.AreEqual(0f, self.EncounterDamage);
            _meter.OnSessionClosed(ESessionEndReason.AllDead, END_TIME);
            CombatMeterFinalSelfMessage final = FinalSelf();
            Assert.AreEqual(UNKNOWN_SOURCE, final.TopDamageTaken[0].SourceId);
            Assert.AreEqual(RAW_DAMAGE, final.TopDamageTaken[0].Amount, TOLERANCE);
        }

        [Test]
        public void Apply_TargetUnregistersDuringDamage_KeepsCapturedVictimAttribution()
        {
            var unregistering = new UnregisteringResolver(_inner, _registry);
            var resolver = new CombatMeterResolver(unregistering, _meter, _inner);
            DamageRequest request = Request(ENEMY_ID, PLAYER_ID);

            DamageResult result = resolver.Apply(in request);

            Assert.AreEqual(EDamageOutcome.Applied, result.Outcome);
            Assert.IsFalse(_registry.TryGet(PLAYER_ID, out _));
            Assert.AreEqual(RAW_DAMAGE, Self().DamageTaken, TOLERANCE);
        }

        [Test]
        public void OnSessionClosed_DamageTakenSources_RanksAggregatedAmountsAndKeepsTopFive()
        {
            const int SOURCE_COUNT = 6;
            const int SMALL_HITS   = 8;
            const float SMALL_HIT = 10f;
            for (int i = 1; i <= SOURCE_COUNT; ++i)
            {
                DamageRequest request = Request(UNKNOWN_ID, PLAYER_ID, "source-" + i);
                DamageResult result = DamageResult.Applied(i * SMALL_HIT, false, false);
                _meter.Record(in request, in result, 1.0);
            }
            for (int i = 0; i < SMALL_HITS; ++i)
            {
                DamageRequest request = Request(UNKNOWN_ID, PLAYER_ID, "repeated-small-source");
                DamageResult result = DamageResult.Applied(SMALL_HIT, false, false);
                _meter.Record(in request, in result, 1.0);
            }
            _meter.OnSessionClosed(ESessionEndReason.Success, END_TIME);

            CombatMeterSourceAmount[] sources = FinalSelf().TopDamageTaken;
            Assert.AreEqual(Constants.CombatMeter.TOP_DAMAGE_TAKEN_SOURCES, sources.Length);
            Assert.AreEqual("repeated-small-source", sources[0].SourceId);
            Assert.AreEqual(SMALL_HITS * SMALL_HIT, sources[0].Amount, TOLERANCE);
            for (int i = 1; i < sources.Length; ++i)
            {
                Assert.AreEqual("source-" + (SOURCE_COUNT - i + 1), sources[i].SourceId);
                Assert.Greater(sources[i - 1].Amount, sources[i].Amount);
            }
        }

        [TestCase(EPlayerClass.Warrior, 0)]
        [TestCase(EPlayerClass.Warrior, 1)]
        [TestCase(EPlayerClass.Warrior, 2)]
        [TestCase(EPlayerClass.Warrior, 3)]
        [TestCase(EPlayerClass.Sorcerer, 0)]
        [TestCase(EPlayerClass.Sorcerer, 1)]
        [TestCase(EPlayerClass.Sorcerer, 2)]
        [TestCase(EPlayerClass.Sorcerer, 3)]
        [TestCase(EPlayerClass.Summoner, 0)]
        [TestCase(EPlayerClass.Summoner, 1)]
        [TestCase(EPlayerClass.Summoner, 2)]
        [TestCase(EPlayerClass.Summoner, 3)]
        public void Apply_CatalogSkill_MapsConfiguredSlotWithoutFixedSkillNames(EPlayerClass playerClass, int slot)
        {
            ConfigureMeter(playerClass);
            Assert.IsTrue(_catalog.TryGetSkillBySlot(playerClass, slot, out SkillCatalogSO.SkillDefinition skill));

            Apply(PLAYER_ID, ENEMY_ID, skill.DefinitionId);

            CombatMeterSelfMessage self = Self();
            Assert.AreEqual(RAW_DAMAGE, self.Slots[FIRST_SKILL_SLOT + slot].Damage, TOLERANCE);
            Assert.AreEqual(1, self.Slots[FIRST_SKILL_SLOT + slot].Hits);
            Assert.AreEqual(0f, self.Slots[OTHER_SLOT].Damage);
        }

        [Test]
        public void Apply_AscensionDefinition_UsesExtraSlot()
        {
            ConfigureMeter(EPlayerClass.Summoner);
            Apply(PLAYER_ID, ENEMY_ID, _catalog.Ascensions[0].DefinitionId);

            Assert.AreEqual(RAW_DAMAGE, Self().Slots[EXTRA_SLOT].Damage, TOLERANCE);
            Assert.AreEqual(0f, Self().Slots[OTHER_SLOT].Damage);
        }

        [Test]
        public void Apply_OtherClassDefinition_UsesOtherSlotForParticipant()
        {
            Assert.IsTrue(_catalog.TryGetSkillBySlot(EPlayerClass.Sorcerer, 0,
                out SkillCatalogSO.SkillDefinition skill));

            Apply(PLAYER_ID, ENEMY_ID, skill.DefinitionId);

            Assert.AreEqual(RAW_DAMAGE, Self().Slots[OTHER_SLOT].Damage, TOLERANCE);
            Assert.AreEqual(0f, Self().Slots[FIRST_SKILL_SLOT].Damage);
        }

        [Test]
        public void Tick_MultipleUnknownDefinitions_UsesOtherSlotAndWarnsOncePerSession()
        {
            Apply(PLAYER_ID, ENEMY_ID, UNKNOWN_SOURCE);
            Apply(PLAYER_ID, ENEMY_ID, OTHER_UNKNOWN_SOURCE);
            LogAssert.Expect(LogType.Warning, UNKNOWN_WARNING);

            _meter.Tick(1.0);
            Apply(PLAYER_ID, ENEMY_ID, UNKNOWN_SOURCE);
            _meter.Tick(2.0);

            Assert.AreEqual(RAW_DAMAGE * 3f, Self().Slots[OTHER_SLOT].Damage, TOLERANCE);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ObservePhase_TransitionAfterPlayerDamage_KeepsTransitionTickDamageInPreviousPhase()
        {
            const double TRANSITION_TIME = 10.0;
            const double WAIT_END_TIME   = 15.0;
            const double NEXT_HIT_TIME   = 12.0;
            _inner.BeginTick(1.0);
            Apply(PLAYER_ID, ENEMY_ID);
            _inner.BeginTick(TRANSITION_TIME);
            Apply(PLAYER_ID, ENEMY_ID);
            _meter.ObservePhase(2, true, TRANSITION_TIME);
            _inner.BeginTick(NEXT_HIT_TIME);
            Apply(PLAYER_ID, ENEMY_ID);

            CombatMeterSelfMessage waiting = Self(NEXT_HIT_TIME);
            Assert.AreEqual(2, waiting.Phase);
            Assert.AreEqual(NEXT_HIT_TIME - TRANSITION_TIME, waiting.PhaseElapsedSeconds, TOLERANCE);
            Assert.AreEqual(RAW_DAMAGE, waiting.PhaseDamage, TOLERANCE);
            _meter.ObservePhase(2, false, WAIT_END_TIME);
            _meter.OnSessionClosed(ESessionEndReason.Success, END_TIME);

            CombatMeterFinalSelfMessage final = FinalSelf();
            Assert.AreEqual(2, final.Phases.Length);
            Assert.AreEqual(RAW_DAMAGE * 2f, final.Phases[0].Damage, TOLERANCE);
            Assert.AreEqual(RAW_DAMAGE, final.Phases[1].Damage, TOLERANCE);
            Assert.AreEqual(TRANSITION_TIME, final.Phases[0].Seconds, TOLERANCE);
            Assert.AreEqual(END_TIME - TRANSITION_TIME, final.Phases[1].Seconds, TOLERANCE);
            Assert.AreEqual(END_TIME, final.EncounterSeconds, TOLERANCE);
            Assert.AreEqual(END_TIME, final.ActiveSeconds, TOLERANCE);
        }

        [Test]
        public void ObserveBossMeterPhase_NoWaitingStateObserved_UsesCurrentBossPhaseNumber()
        {
            const double TRANSITION_TIME = 10.0;
            const float HEALTH_RATE = 0.5f;
            var boss = new BossServer(BOSS_ID, UNKNOWN_ID, Vector3.zero, Vector3.forward, Vector3.zero,
                RAW_DAMAGE, MAX_HEALTH, 1f, new MeterBossCatalog(), _registry, _inner, null, new SeededBossRandom(1));
            var runtimeObject = new GameObject("MeterPhaseRuntimeTest");
            try
            {
                Assert.IsTrue(boss.Begin(START_TIME));
                var runtime = runtimeObject.AddComponent<CombatServerRuntime>();
                typeof(CombatServerRuntime).GetField("_combatMeter", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(runtime, _meter);
                typeof(CombatServerRuntime).GetField("_boss", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(runtime, boss);
                MethodInfo observe = typeof(CombatServerRuntime).GetMethod("ObserveBossMeterPhase",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(observe);
                observe.Invoke(runtime, new object[] { START_TIME });
                Apply(PLAYER_ID, ENEMY_ID);
                _inner.BeginTick(TRANSITION_TIME);
                Apply(PLAYER_ID, ENEMY_ID);
                boss.ApplyDamage(boss.MaxHealth * HEALTH_RATE, false, TRANSITION_TIME);
                boss.Tick(TRANSITION_TIME);
                Assert.AreEqual(2, boss.Phase);
                Assert.AreEqual(EBossPhaseState.AwaitingHandoff, boss.PhaseState);

                // 상태 관측 사이에 인계까지 끝나도 두뇌가 올린 단계 번호를 놓치면 안 된다
                var brain = (BossBrain)typeof(BossServer).GetField("_brain", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(boss);
                var combat = (BossCombat)typeof(BossBrain).GetField("_combat", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(brain);
                var phase = (BossPhaseFsm)typeof(BossCombat).GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(combat);
                Assert.IsTrue(phase.CompleteHandoff(true));
                Assert.AreEqual(EBossPhaseState.Active, boss.PhaseState);
                observe.Invoke(runtime, new object[] { TRANSITION_TIME });
                Assert.AreEqual(2, Self(TRANSITION_TIME).Phase);
                Apply(PLAYER_ID, ENEMY_ID);
                _meter.OnSessionClosed(ESessionEndReason.Success, END_TIME);

                CombatMeterFinalSelfMessage final = FinalSelf();
                Assert.AreEqual(2, final.Phases.Length);
                Assert.AreEqual(RAW_DAMAGE * 2f, final.Phases[0].Damage, TOLERANCE);
                Assert.AreEqual(RAW_DAMAGE, final.Phases[1].Damage, TOLERANCE);
                Assert.AreEqual(TRANSITION_TIME, final.Phases[0].Seconds, TOLERANCE);
                Assert.AreEqual(END_TIME - TRANSITION_TIME, final.Phases[1].Seconds, TOLERANCE);
            }
            finally
            {
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OnSessionClosed_ParticipantLog_UsesApiIdentityAndKeepsMeterCorrelationInData(bool apiIdentity)
        {
            const string API_PARTICIPANT_ID = "01111111-2222-3333-4444-555555555555";
            string participantKey = apiIdentity
                ? Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + API_PARTICIPANT_ID
                : PARTICIPANT_KEY;
            ConfigureMeter(_registry, participantKey);
            Apply(PLAYER_ID, ENEMY_ID);
            FieldInfo installed = typeof(ServerJsonLogHandler).GetField("_installed",
                BindingFlags.Static | BindingFlags.NonPublic);
            object original = installed.GetValue(null);
            using (var output = new System.IO.StringWriter())
            {
                var handler = new ServerJsonLogHandler(new ServerLogLine("dungeon", "meter-test", "meter-content"),
                    null, output, () => SESSION_ID);
                installed.SetValue(null, handler);
                try
                {
                    _meter.OnSessionClosed(ESessionEndReason.Success, END_TIME);

                    string line = output.ToString().TrimEnd('\r', '\n');
                    Assert.AreEqual(-1, line.IndexOf('\n'));
                    int dataStart = line.IndexOf(",\"data\":", StringComparison.Ordinal);
                    Assert.Greater(dataStart, 0);
                    string envelope = line.Substring(0, dataStart);
                    string data = line.Substring(dataStart);
                    StringAssert.Contains(apiIdentity ? "\"participantId\":\"" + API_PARTICIPANT_ID + "\""
                        : "\"participantId\":null", envelope);
                    StringAssert.Contains("\"sessionId\":\"" + SESSION_ID + "\"", envelope);
                    StringAssert.Contains("\"event\":\"combat.meter.closed\"", envelope);
                    StringAssert.Contains("\"participantId\":" + PARTICIPANT, data);
                    StringAssert.Contains("\"eventId\":\"" + SESSION_EPOCH + "\"", data);
                    StringAssert.Contains("\"reasonCode\":" + (int)ESessionEndReason.Success, data);
                    StringAssert.Contains("\"damage\":" + RAW_DAMAGE, data);
                }
                finally
                {
                    installed.SetValue(null, original);
                }
            }
        }

        [Test]
        public void OnSessionClosed_LastDamageAfterPeriodicTick_IsIncludedAndLaterDamageOnlyChangesCombat()
        {
            Apply(PLAYER_ID, ENEMY_ID);
            _meter.Tick(1.0);
            _inner.BeginTick(2.0);
            Apply(PLAYER_ID, ENEMY_ID);

            _meter.OnSessionClosed(ESessionEndReason.Timeout, 2.0);
            CombatMeterFinalSelfMessage before = FinalSelf();
            Assert.AreEqual(EDamageOutcome.Applied, Apply(PLAYER_ID, ENEMY_ID).Outcome);
            _meter.RecordPotion(PARTICIPANT);
            _meter.RecordRevive(PARTICIPANT, 3.0);
            _meter.ObserveLifeState(PARTICIPANT, EParticipantLifeState.Dead, 3.0);
            _meter.ObservePhase(2, false, 3.0);
            _meter.OnSessionClosed(ESessionEndReason.Success, 3.0);

            CombatMeterFinalSelfMessage after = FinalSelf();
            Assert.AreEqual(RAW_DAMAGE * 2f, before.EncounterDamage, TOLERANCE);
            Assert.AreEqual(before.EncounterDamage, after.EncounterDamage);
            Assert.AreEqual(before.EncounterSeconds, after.EncounterSeconds);
            Assert.AreEqual(before.Deaths, after.Deaths);
            Assert.AreEqual(before.Revives, after.Revives);
            Assert.AreEqual(before.PotionsUsed, after.PotionsUsed);
            Assert.AreEqual(3, _enemy.DamageCalls);
            Assert.IsTrue(_meter.TryGetResult(out CombatMeterResultMessage result));
            Assert.AreEqual(ESessionEndReason.Timeout, result.Reason);
            Assert.AreEqual(SESSION_EPOCH, result.SessionEpoch);
            Assert.AreEqual(RAW_DAMAGE * 2f, result.Entries[0].Damage, TOLERANCE);
        }

        [Test]
        public void Apply_LethalIncoming_RecapKeepsLatestFiveHitsWithinFiveSecondsIncludingLethalHit()
        {
            const int FIRST_RECENT_HIT = 5;
            const int DEATH_TIME       = 11;
            Apply(UNKNOWN_ID, PLAYER_ID, "old-hit");
            for (int time = FIRST_RECENT_HIT; time <= DEATH_TIME; ++time)
            {
                _inner.BeginTick(time);
                if (time == DEATH_TIME)
                {
                    _player.Health = RAW_DAMAGE;
                }

                Apply(UNKNOWN_ID, PLAYER_ID, "hit-" + time);
            }

            _meter.ObserveLifeState(PARTICIPANT, EParticipantLifeState.Dead, DEATH_TIME);
            _meter.OnSessionClosed(ESessionEndReason.AllDead, DEATH_TIME);

            CombatMeterFinalSelfMessage final = FinalSelf();
            Assert.AreEqual(1, final.Deaths);
            Assert.AreEqual(DEATH_RECAP_SIZE, final.LastDeathRecap.Length);
            for (int i = 0; i < DEATH_RECAP_SIZE; ++i)
            {
                int hitTime = DEATH_TIME - DEATH_RECAP_SIZE + 1 + i;
                Assert.AreEqual("hit-" + hitTime, final.LastDeathRecap[i].SourceId);
                Assert.AreEqual(RAW_DAMAGE, final.LastDeathRecap[i].Amount, TOLERANCE);
                Assert.AreEqual(DEATH_TIME - hitTime, final.LastDeathRecap[i].SecondsBeforeDeath, TOLERANCE);
            }
        }

        [Test]
        public void ObserveLifeState_DeathWithOldHit_RecapExcludesHitsOlderThanFiveSeconds()
        {
            _inner.BeginTick(1.0);
            Apply(UNKNOWN_ID, PLAYER_ID, "too-old");
            _inner.BeginTick(6.0);
            Apply(UNKNOWN_ID, PLAYER_ID, "at-window-boundary");
            _inner.BeginTick(11.0);
            _player.Health = RAW_DAMAGE;
            Apply(UNKNOWN_ID, PLAYER_ID, "lethal");
            _meter.OnSessionClosed(ESessionEndReason.AllDead, 11.0);

            CombatMeterDeathEntry[] recap = FinalSelf().LastDeathRecap;
            Assert.AreEqual(2, recap.Length);
            Assert.AreEqual("at-window-boundary", recap[0].SourceId);
            Assert.AreEqual(5f, recap[0].SecondsBeforeDeath, TOLERANCE);
            Assert.AreEqual("lethal", recap[1].SourceId);
        }

        [Test]
        public void ObserveLifeState_ReattachDoesNotCountRevive_ExplicitApprovedReviveDoes()
        {
            _meter.ObserveLifeState(PARTICIPANT, EParticipantLifeState.Dead, 2.0);
            _meter.ObserveLifeState(PARTICIPANT, EParticipantLifeState.Dead, 3.0);
            _meter.ObserveLifeState(PARTICIPANT, EParticipantLifeState.Alive, 7.0);
            _meter.OnSessionClosed(ESessionEndReason.Abandon, 10.0);

            CombatMeterFinalSelfMessage final = FinalSelf();
            Assert.AreEqual(1, final.Deaths);
            Assert.AreEqual(0, final.Revives);
            Assert.AreEqual(5f, final.ActiveSeconds, TOLERANCE);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SuspendActive_MissingCombatBody_ExcludesGapWithoutDeathOrRevive(bool reattach)
        {
            const double REMOVE_TIME = 2.0;
            const double REATTACH_TIME = 7.0;
            const double CLOSE_TIME = 10.0;
            Apply(PLAYER_ID, ENEMY_ID);
            _meter.SuspendActive(PARTICIPANT, REMOVE_TIME);
            _meter.SuspendActive(PARTICIPANT, REMOVE_TIME + 1.0);
            Assert.AreEqual(REMOVE_TIME, Self(REATTACH_TIME).ActiveSeconds, TOLERANCE);
            if (reattach)
            {
                Assert.IsTrue(_meter.RegisterCombatId(PARTICIPANT, REPLACEMENT_ID));
                _meter.ObserveLifeState(PARTICIPANT, EParticipantLifeState.Alive, REATTACH_TIME);
            }

            _meter.OnSessionClosed(ESessionEndReason.Success, CLOSE_TIME);

            CombatMeterFinalSelfMessage final = FinalSelf();
            double expected = reattach ? REMOVE_TIME + CLOSE_TIME - REATTACH_TIME : REMOVE_TIME;
            Assert.AreEqual(expected, final.ActiveSeconds, TOLERANCE);
            Assert.AreEqual(CLOSE_TIME, final.EncounterSeconds, TOLERANCE);
            Assert.AreEqual(0, final.Deaths);
            Assert.AreEqual(0, final.Revives);
            Assert.AreEqual(RAW_DAMAGE, final.EncounterDamage, TOLERANCE);
        }

        [Test]
        public void SuspendActive_ClosedWhileMissingBody_DoesNotResumeActivity()
        {
            const double REMOVE_TIME = 2.0;
            const double CLOSE_TIME = 7.0;
            _meter.SuspendActive(PARTICIPANT, REMOVE_TIME);
            _meter.CloseParticipant(PARTICIPANT, CLOSE_TIME);
            Assert.IsFalse(_meter.RegisterCombatId(PARTICIPANT, REPLACEMENT_ID));
            _meter.ObserveLifeState(PARTICIPANT, EParticipantLifeState.Alive, CLOSE_TIME);

            _meter.OnSessionClosed(ESessionEndReason.Abandon, END_TIME);

            Assert.AreEqual(REMOVE_TIME, FinalSelf().ActiveSeconds, TOLERANCE);
            Assert.AreEqual(0, FinalSelf().Deaths);
            Assert.AreEqual(0, FinalSelf().Revives);
        }

        [Test]
        public void RecordRevive_ApprovedParticipantOnly_IncrementsReviveAndResumesActivity()
        {
            _meter.ObserveLifeState(PARTICIPANT, EParticipantLifeState.Dead, 2.0);
            _meter.RecordRevive(UNKNOWN_ID, 6.0);
            _meter.RecordRevive(PARTICIPANT, 7.0);
            _meter.OnSessionClosed(ESessionEndReason.Success, 10.0);

            CombatMeterFinalSelfMessage final = FinalSelf();
            Assert.AreEqual(1, final.Revives);
            Assert.AreEqual(1, final.Deaths);
            Assert.AreEqual(5f, final.ActiveSeconds, TOLERANCE);
        }

        [Test]
        public void ApproveRevive_BeforeDelayThenSafePointApproval_CountsOnlySuccessfulRuntimeRevive()
        {
            const double TOO_EARLY_OFFSET = 0.1;
            var config = ScriptableObject.CreateInstance<SessionConfigSO>();
            var playerConfig = ScriptableObject.CreateInstance<PlayerConfigSO>();
            var sessions = new ServerSessionRegistry(Constants.Session.MAX_PARTY_SIZE);
            using (var host = new DungeonSessionHost(config, sessions, null, true, null, new CommittingSink()))
            {
                var runtimeObject = new GameObject("MeterReviveRuntimeTest");
                var playerObject = new GameObject("MeterRevivePlayerTest", typeof(NetworkIdentity));
                PlayerServer player = null;
                try
                {
                    var claims = new JoinTokenClaims(PARTICIPANT_KEY);
                    Assert.AreEqual(ERejectReason.None, sessions.TryAdmit(in claims, out SessionBinding binding));
                    Assert.AreEqual(PARTICIPANT, binding.ParticipantId);
                    Assert.AreEqual(ERejectReason.None, host.Admit(in claims, binding));
                    var runtime = runtimeObject.AddComponent<CombatServerRuntime>();
                    runtime.Initialize(playerConfig, config.ReconnectGraceSeconds, SESSION_EPOCH, host.Roster, _catalog);
                    runtime.BindSession(host);
                    typeof(CombatServerRuntime).GetField("_combatMeter", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(runtime, _meter);
                    host.CombatMeterSink = _meter;
                    Assert.IsFalse(host.BeginTick(START_TIME));
                    Assert.IsTrue(host.BeginTick(config.ReadyCountdownSeconds));
                    host.CompleteTick();
                    double deathTime = config.ReadyCountdownSeconds + 1.0;
                    double readyTime = deathTime + config.BossReviveDelay;
                    PlayerInputState input = playerObject.AddComponent<PlayerInputState>();
                    PlayerCombatState combat = playerObject.GetComponent<PlayerCombatState>();
                    NetworkIdentity identity = playerObject.GetComponent<NetworkIdentity>();
                    PropertyInfo identityProperty = typeof(NetworkBehaviour).GetProperty("netIdentity",
                        BindingFlags.Instance | BindingFlags.Public);
                    identityProperty.SetValue(input, identity);
                    identityProperty.SetValue(combat, identity);
                    var stats = new CharacterStats(RAW_DAMAGE, MAX_HEALTH, 0f, 0f, RAW_DAMAGE, 5f);
                    player = new PlayerServer(input, combat, EPlayerClass.Warrior, PLAYER_ID, PARTICIPANT, SESSION_EPOCH,
                        playerConfig, in stats, runtime.Resolver, runtime.Registry, new CombatLineOfSight(0, 1f), deathTime);
                    player.ApplyDamage(MAX_HEALTH, true, deathTime);
                    host.ReportLifeState(PARTICIPANT, EParticipantLifeState.Dead);
                    _meter.ObserveLifeState(PARTICIPANT, EParticipantLifeState.Dead, deathTime);
                    var selector = new RevivePointSelector(runtime.Registry, null, 0f);
                    selector.AddCandidate(Vector3.zero);
                    typeof(CombatServerRuntime).GetField("_revivePoints", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(runtime, selector);
                    MethodInfo approve = typeof(CombatServerRuntime).GetMethod("ApproveRevive",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                    approve.Invoke(runtime, new object[] { player, readyTime - TOO_EARLY_OFFSET });
                    Assert.AreEqual(EParticipantLifeState.Dead, player.LifeState);
                    Assert.AreEqual(config.BossSharedRevives, host.Session.RemainingRevives);
                    approve.Invoke(runtime, new object[] { player, readyTime });
                    approve.Invoke(runtime, new object[] { player, readyTime });

                    Assert.AreEqual(EParticipantLifeState.Alive, player.LifeState);
                    Assert.AreEqual(config.BossSharedRevives - 1, host.Session.RemainingRevives);
                    Assert.AreEqual((float)(MAX_HEALTH * config.BossReviveHealthRate), player.Health, TOLERANCE);
                    Assert.IsTrue(host.Session.TryCompleteBossForDevelopment(readyTime + 1.0));
                    Assert.AreEqual(1, FinalSelf().Revives);
                }
                finally
                {
                    player?.Shutdown();
                    Object.DestroyImmediate(playerObject);
                    Object.DestroyImmediate(runtimeObject);
                    Object.DestroyImmediate(config);
                    Object.DestroyImmediate(playerConfig);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PotionApplied_PendingCommitAndResolution_CountsOnlyActualHealing(bool fullAtCommit)
        {
            var playerConfig = ScriptableObject.CreateInstance<PlayerConfigSO>();
            var life = ScriptableObject.CreateInstance<LifeConfigSO>();
            var ledger = new PotionLedger();
            var user = new PotionUser();
            var potions = new PotionServer(playerConfig, life, ledger, user, Constants.Session.MAX_PARTY_SIZE);
            potions.PotionApplied += _meter.RecordPotion;
            try
            {
                Assert.IsTrue(potions.TryRegister(PARTICIPANT, PARTICIPANT_KEY, new[]
                {
                    new ReservationPotion(life.GetPotion(EPotionSize.Small).DefinitionId, "Account", POTION_STOCK)
                }));
                Assert.AreEqual(ERejectReason.None, potions.TryUse(user, EPotionSize.Small, INPUT_SEQUENCE, 1.0));
                potions.Tick(2.0);
                Assert.AreEqual(0, user.HealCalls);
                if (fullAtCommit)
                {
                    user.Health = user.MaxHealth;
                }

                ledger.Committed = true;
                potions.Tick(3.0);
                potions.Tick(4.0);
                _meter.OnSessionClosed(ESessionEndReason.Success, END_TIME);

                Assert.AreEqual(1, user.HealCalls);
                Assert.AreEqual(fullAtCommit ? 0 : 1, FinalSelf().PotionsUsed);
            }
            finally
            {
                potions.PotionApplied -= _meter.RecordPotion;
                Object.DestroyImmediate(playerConfig);
                Object.DestroyImmediate(life);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PotionApplied_DeathOrSessionEndBeforeResolution_DoesNotCountRevertedUse(bool sessionEnd)
        {
            var playerConfig = ScriptableObject.CreateInstance<PlayerConfigSO>();
            var life = ScriptableObject.CreateInstance<LifeConfigSO>();
            var ledger = new PotionLedger();
            var user = new PotionUser();
            var potions = new PotionServer(playerConfig, life, ledger, user, Constants.Session.MAX_PARTY_SIZE);
            potions.PotionApplied += _meter.RecordPotion;
            try
            {
                Assert.IsTrue(potions.TryRegister(PARTICIPANT, PARTICIPANT_KEY, new[]
                {
                    new ReservationPotion(life.GetPotion(EPotionSize.Small).DefinitionId, "Account", POTION_STOCK)
                }));
                Assert.AreEqual(ERejectReason.None, potions.TryUse(user, EPotionSize.Small, INPUT_SEQUENCE, 1.0));
                if (sessionEnd)
                {
                    potions.RevertUnapplied(2.0);
                    ledger.IsSessionRunning = false;
                }
                else
                {
                    user.LifeState = EParticipantLifeState.Dead;
                }

                ledger.Committed = true;
                potions.Tick(3.0);
                _meter.OnSessionClosed(ESessionEndReason.Abandon, END_TIME);

                Assert.AreEqual(1, ledger.Reverts);
                Assert.AreEqual(0, user.HealCalls);
                Assert.AreEqual(0, FinalSelf().PotionsUsed);
                Assert.AreEqual(0, potions.GetBossUses(PARTICIPANT));
            }
            finally
            {
                potions.PotionApplied -= _meter.RecordPotion;
                Object.DestroyImmediate(playerConfig);
                Object.DestroyImmediate(life);
            }
        }

        [Test]
        public void Apply_WarmedKnownDamagePath_AllocatesZeroBytes()
        {
            Apply(PLAYER_ID, ENEMY_ID);
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < REPEAT_COUNT; ++i)
            {
                DamageRequest request = Request(PLAYER_ID, ENEMY_ID);
                _resolver.Apply(in request);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
            Assert.AreEqual((REPEAT_COUNT + 1) * RAW_DAMAGE, Self().EncounterDamage, TOLERANCE);
        }

        [TestCase(ESessionEndReason.Success)]
        [TestCase(ESessionEndReason.Timeout)]
        [TestCase(ESessionEndReason.AllDead)]
        [TestCase(ESessionEndReason.Abandon)]
        [TestCase(ESessionEndReason.DisconnectExpired)]
        [TestCase(ESessionEndReason.ServiceCancelled)]
        public void SessionEnd_AllReasons_NotifiesAfterReasonAndPreservesExistingEndingListener(ESessionEndReason reason)
        {
            var config = ScriptableObject.CreateInstance<SessionConfigSO>();
            var roster = new ParticipantRoster(config);
            Assert.IsTrue(roster.TryAdd(PARTICIPANT, PARTICIPANT_KEY));
            Assert.IsTrue(roster.TryAdd(SECOND_PARTICIPANT, "meter-second"));
            var events = new SessionEventQueue(SESSION_ID, new CommittingSink());
            using (var session = new DungeonSession(SESSION_ID, roster, events, config, null))
            {
                var observer = new ClosingObserver(session);
                session.SetEndingListener(observer);
                session.SetCombatMeterSink(observer);
                try
                {
                    Assert.IsTrue(session.Begin(START_TIME));
                    EndSession(session, roster, config, reason);

                    Assert.IsTrue(session.IsEnded);
                    Assert.AreEqual(reason, session.EndReason);
                    Assert.AreEqual(1, observer.Starts);
                    Assert.AreEqual(1, observer.Endings);
                    Assert.AreEqual(1, observer.Closes);
                    Assert.AreEqual(1, observer.EndingOrder);
                    Assert.AreEqual(2, observer.ClosedOrder);
                    Assert.IsFalse(observer.EndedAtEnding);
                    Assert.IsTrue(observer.EndedAtClose);
                    Assert.AreEqual(reason, observer.ReasonAtClose);
                    Assert.AreEqual(reason, observer.ArgumentReason);
                    session.CancelService(END_TIME);
                    Assert.AreEqual(1, observer.Closes);
                }
                finally
                {
                    Object.DestroyImmediate(config);
                }
            }
        }

        [Test]
        public void SessionEnd_MeterSinkThrows_StillGrantsRewardClosesRosterAndStoresSessionEnded()
        {
            const string MATERIAL_ID = "item_boss_material_common";
            const int ESSENCE = 600;
            const int CURRENCY = 120;
            const int MATERIAL_QUANTITY = 3;
            var config = ScriptableObject.CreateInstance<SessionConfigSO>();
            var roster = new ParticipantRoster(config);
            Assert.IsTrue(roster.TryAdd(PARTICIPANT, PARTICIPANT_KEY));
            Assert.IsTrue(roster.TryAdd(SECOND_PARTICIPANT, SECOND_PARTICIPANT_KEY));
            var sink = new CommittingSink();
            var events = new SessionEventQueue(SESSION_ID, sink);
            using (var session = new DungeonSession(SESSION_ID, roster, events, config, null))
            {
                var meter = new ThrowingClosingSink();
                session.SetCombatMeterSink(meter);
                try
                {
                    var reward = new RewardCatalogSO.BossRewardSpec(BOSS_ID, EBossDifficulty.Easy, ESSENCE, CURRENCY,
                        MATERIAL_ID, 1, 1f, MATERIAL_QUANTITY, null, 0f, 0);
                    Assert.IsTrue(session.RegisterBossReward(reward));
                    Assert.IsTrue(session.Begin(START_TIME));
                    Assert.IsTrue(session.BeginTick(1.0));
                    Assert.IsTrue(session.ReportBossDefeated());
                    LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: " + CLOSE_FAILURE));

                    Assert.DoesNotThrow(session.CompleteTick);

                    Assert.IsTrue(session.IsEnded);
                    Assert.AreEqual(ESessionEndReason.Success, session.EndReason);
                    Assert.AreEqual(1, meter.Closes);
                    Assert.AreEqual(0, roster.ActiveCount);
                    Assert.Greater(session.EndEventSequence, 0);
                    for (int i = 0; i < roster.Count; ++i)
                    {
                        Assert.AreEqual(EParticipantConnectionState.Departed, roster.GetAt(i).ConnectionState);
                        Assert.AreEqual(ESSENCE, session.Ledger.GetBossEssence(i));
                        Assert.AreEqual(CURRENCY, session.Ledger.GetBossCurrency(i));
                        Assert.AreEqual(MATERIAL_QUANTITY, session.Ledger.GetBossMaterials(i));
                    }
                    events.Tick(END_TIME);
                    events.Tick(END_TIME + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS);
                    Assert.AreEqual(1, sink.SessionEndCount);
                    Assert.AreEqual(session.EndEventSequence, sink.LastSessionEnd.Seq);
                    Assert.AreEqual(ESessionEndReason.Success, sink.LastSessionEnd.Data.Reason);
                    Assert.AreEqual(roster.Count, sink.LastSessionEnd.Data.Grants.Length);
                    for (int i = 0; i < sink.LastSessionEnd.Data.Grants.Length; ++i)
                    {
                        SettlementGrant grant = sink.LastSessionEnd.Data.Grants[i];
                        Assert.AreEqual(ESSENCE, grant.Essence);
                        Assert.AreEqual(CURRENCY, grant.Currency);
                        Assert.AreEqual(1, grant.Items.Length);
                        Assert.AreEqual(MATERIAL_ID, grant.Items[0].DefinitionId);
                        Assert.AreEqual(MATERIAL_QUANTITY, grant.Items[0].Quantity);
                    }
                    Assert.IsFalse(session.CancelService(END_TIME));
                    Assert.AreEqual(1, meter.Closes);
                    LogAssert.NoUnexpectedReceived();
                }
                finally
                {
                    Object.DestroyImmediate(config);
                }
            }
        }

        [Test]
        public void SessionEnd_TerminalStorage_StillNotifiesMeterWithoutCallingPotionEndingListener()
        {
            var config = ScriptableObject.CreateInstance<SessionConfigSO>();
            var roster = new ParticipantRoster(config);
            Assert.IsTrue(roster.TryAdd(PARTICIPANT, PARTICIPANT_KEY));
            var sink = new CommittingSink { Result = EEventBatchResult.Terminal };
            var events = new SessionEventQueue(SESSION_ID, sink);
            using (var session = new DungeonSession(SESSION_ID, roster, events, config, null))
            {
                var observer = new ClosingObserver(session);
                session.SetEndingListener(observer);
                session.SetCombatMeterSink(observer);
                try
                {
                    Assert.IsTrue(session.Begin(START_TIME));
                    LogAssert.Expect(LogType.Error, TERMINAL_ERROR);
                    Assert.IsTrue(session.BeginTick(Constants.Session.EVENT_BATCH_INTERVAL_SECONDS));
                    session.CompleteTick();
                    Assert.IsTrue(session.HasTerminalStorage);
                    Assert.IsTrue(session.BeginTick(2.0));
                    session.CompleteTick();

                    Assert.AreEqual(ESessionEndReason.ServiceCancelled, session.EndReason);
                    Assert.AreEqual(0, observer.Endings);
                    Assert.AreEqual(1, observer.Closes);
                    Assert.IsTrue(observer.EndedAtClose);
                }
                finally
                {
                    Object.DestroyImmediate(config);
                }
            }
        }

        [Test]
        public void EnsureCombatMeter_NormalSortie_DoesNotCreateRecorderOrDecorateResolver()
        {
            var config = ScriptableObject.CreateInstance<SessionConfigSO>();
            var playerConfig = ScriptableObject.CreateInstance<PlayerConfigSO>();
            var sessions = new ServerSessionRegistry(Constants.Session.MAX_PARTY_SIZE);
            using (var host = new DungeonSessionHost(config, sessions, null, true, null, new CommittingSink()))
            {
                var runtimeObject = new GameObject("MeterNormalRuntimeTest");
                try
                {
                    var descriptor = new SortieDescriptor(GameSessionCodes.Mode.NORMAL, "map-test", "battle-test", null,
                        GameSessionCodes.Difficulty.NONE);
                    var claims = new JoinTokenClaims(PARTICIPANT_KEY, null, null, null).WithSortie(in descriptor);
                    Assert.AreEqual(ERejectReason.None, sessions.TryAdmit(in claims, out SessionBinding binding));
                    Assert.AreEqual(ERejectReason.None, host.Admit(in claims, binding));
                    var runtime = runtimeObject.AddComponent<CombatServerRuntime>();
                    runtime.Initialize(playerConfig, config.ReconnectGraceSeconds, SESSION_EPOCH, host.Roster, _catalog);
                    runtime.BindSession(host);
                    IDamageResolver original = runtime.Resolver;

                    typeof(CombatServerRuntime).GetMethod("EnsureCombatMeter", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(runtime, null);

                    Assert.IsNull(runtime.CombatMeter);
                    Assert.IsNull(host.CombatMeterSink);
                    Assert.AreSame(original, runtime.Resolver);
                }
                finally
                {
                    Object.DestroyImmediate(runtimeObject);
                    Object.DestroyImmediate(config);
                    Object.DestroyImmediate(playerConfig);
                }
            }
        }

        /****************************************
        *                Helpers
        ****************************************/

        private DamageRequest Request(int attackerId, int targetId,
            string sourceId = Constants.Combat.BASIC_ATTACK_SOURCE_ID)
        {
            int actionId = _inner.IssueInstanceId();
            return new DamageRequest(attackerId, targetId, actionId, actionId, RAW_DAMAGE, EDamageFlags.None,
                Vector3.forward, sourceId);
        }

        private DamageResult Apply(int attackerId, int targetId,
            string sourceId = Constants.Combat.BASIC_ATTACK_SOURCE_ID)
        {
            DamageRequest request = Request(attackerId, targetId, sourceId);
            return _resolver.Apply(in request);
        }

        private CombatMeterSelfMessage Self(double serverTime = END_TIME)
        {
            Assert.IsTrue(_meter.TryGetSelf(PARTICIPANT, serverTime, out CombatMeterSelfMessage self));
            return self;
        }

        private void ConfigureMeter(EPlayerClass playerClass)
        {
            var stats = new CharacterStats(RAW_DAMAGE, MAX_HEALTH, 0f, 0f, RAW_DAMAGE, 5f);
            _meter = new CombatMeterRecorder(_registry, _catalog, null, SESSION_EPOCH, SESSION_ID);
            Assert.IsTrue(_meter.RegisterParticipant(PARTICIPANT, PLAYER_ID, PARTICIPANT_KEY, playerClass, in stats));
            _meter.Begin(START_TIME);
            _resolver = new CombatMeterResolver(_inner, _meter, _inner);
        }

        private void ConfigureMeter(ICombatTargetQuery targets, string participantKey = null)
        {
            var stats = new CharacterStats(RAW_DAMAGE, MAX_HEALTH, 0f, 0f, RAW_DAMAGE, 5f);
            _meter = new CombatMeterRecorder(targets, _catalog, null, SESSION_EPOCH, SESSION_ID);
            Assert.IsTrue(_meter.RegisterParticipant(PARTICIPANT, PLAYER_ID, PARTICIPANT_KEY,
                EPlayerClass.Warrior, in stats, participantKey: participantKey));
            _meter.Begin(START_TIME);
            _resolver = new CombatMeterResolver(_inner, _meter, _inner);
        }

        private T GetMeterField<T>(string name)
        {
            FieldInfo field = typeof(CombatMeterRecorder).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            return (T)field.GetValue(_meter);
        }

        private void SetMeterField(string name, object value)
        {
            FieldInfo field = typeof(CombatMeterRecorder).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(_meter, value);
        }

        private object GetParticipantField(string name)
        {
            Array participants = GetMeterField<Array>("_participants");
            object participant = participants.GetValue(0);
            FieldInfo field = participant.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(field);
            return field.GetValue(participant);
        }

        private void SetParticipantField(string name, object value)
        {
            Array participants = GetMeterField<Array>("_participants");
            object participant = participants.GetValue(0);
            FieldInfo field = participant.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(field);
            field.SetValue(participant, value);
            participants.SetValue(participant, 0);
        }

        private CombatMeterFinalSelfMessage FinalSelf()
        {
            Assert.IsTrue(_meter.TryGetFinalSelf(PARTICIPANT, out CombatMeterFinalSelfMessage final));
            return final;
        }

        private static void EndSession(DungeonSession session, ParticipantRoster roster, SessionConfigSO config,
            ESessionEndReason reason)
        {
            double time = 1.0;
            switch (reason)
            {
                case ESessionEndReason.ServiceCancelled:
                    Assert.IsTrue(session.CancelService(time));
                    return;
                case ESessionEndReason.Abandon:
                    Assert.IsTrue(session.TryAbandon(PARTICIPANT, time));
                    Assert.IsTrue(session.TryAbandon(SECOND_PARTICIPANT, time));
                    break;
                case ESessionEndReason.DisconnectExpired:
                    Assert.IsTrue(session.TryDisconnect(PARTICIPANT, time));
                    Assert.IsTrue(session.TryDisconnect(SECOND_PARTICIPANT, time));
                    time += config.ReconnectGraceSeconds;
                    break;
                case ESessionEndReason.AllDead:
                    Assert.IsTrue(roster.TrySetLifeState(PARTICIPANT, EParticipantLifeState.Dead));
                    Assert.IsTrue(roster.TrySetLifeState(SECOND_PARTICIPANT, EParticipantLifeState.Dead));
                    break;
            }

            Assert.IsTrue(session.BeginTick(time));
            if (reason == ESessionEndReason.Success)
            {
                Assert.IsTrue(session.ReportBossDefeated());
            }
            else if (reason == ESessionEndReason.Timeout)
            {
                var outcome = new WaveSessionOutcome(reason, 0, 0, time);
                session.OnWaveSessionEnded(in outcome);
            }

            session.CompleteTick();
        }

        private sealed class CommittingSink : IBackendEventSink
        {
            public EEventBatchResult Result { get; set; } = EEventBatchResult.Committed;
            public int SessionEndCount { get; private set; }
            public SessionEvent LastSessionEnd { get; private set; }

            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count,
                IBackendEventSinkListener listener)
            {
                for (int i = 0; i < count; ++i)
                {
                    if (events[i].Data.Type == ESessionEventType.SessionEnded)
                    {
                        ++SessionEndCount;
                        LastSessionEnd = events[i];
                    }
                }
                listener.OnBatchCompleted(attemptId, Result);
                return true;
            }
        }

        private sealed class ThrowingTargetQuery : ICombatTargetQuery
        {
            private readonly ICombatTargetQuery _inner;

            public bool ThrowOnTryGet { get; set; }
            public int TryGetCalls { get; private set; }

            public ThrowingTargetQuery(ICombatTargetQuery inner) { _inner = inner; }

            public bool TryGet(int combatId, out ICombatTarget target)
            {
                ++TryGetCalls;
                if (ThrowOnTryGet)
                {
                    throw new InvalidOperationException(CAPTURE_FAILURE);
                }
                return _inner.TryGet(combatId, out target);
            }

            public int GetLivingPlayers(List<ICombatTarget> results) => _inner.GetLivingPlayers(results);

            public int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
                List<ICombatTarget> results)
            {
                return _inner.GetLivingInRadius(center, radius, faction, includeSummons, results);
            }

            public int CountLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons)
            {
                return _inner.CountLivingInRadius(center, radius, faction, includeSummons);
            }
        }

        private sealed class ThrowingWarningLogHandler : ILogHandler
        {
            private readonly ILogHandler _inner;

            public ThrowingWarningLogHandler(ILogHandler inner) { _inner = inner; }

            public void LogFormat(LogType logType, Object context, string format, params object[] args)
            {
                if (logType == LogType.Warning)
                {
                    throw new InvalidOperationException(WARNING_FAILURE);
                }
                _inner.LogFormat(logType, context, format, args);
            }

            public void LogException(Exception exception, Object context) => _inner.LogException(exception, context);
        }

        private sealed class ThrowingClosingSink : ICombatMeterSink
        {
            public int Closes { get; private set; }

            public void OnSessionStarted(double serverTime) { }
            public void ResendFinal(int participantId) { }

            public void OnSessionClosed(ESessionEndReason reason, double serverTime)
            {
                ++Closes;
                throw new InvalidOperationException(CLOSE_FAILURE);
            }
        }

        private sealed class MeterBossCatalog : IBossPatternCatalog
        {
            private const float ARENA_DIAMETER = 36f;
            private const float HEALTH_REF = 700f;
            private const float TRANSITION_RATE = 0.5f;
            private const float NEAR_DISTANCE = 5f;
            private const float MID_DISTANCE = 12f;
            private const float FRONT_ANGLE = 60f;
            private const float SIDE_ANGLE = 120f;
            private const float RETRY_SECONDS = 0.25f;
            private const float CONTROL_MAX_SECONDS = 1f;
            private const float CONTROL_IMMUNE_SECONDS = 2f;

            private readonly BossPatternCatalogSO.BossDefinition _boss = new BossPatternCatalogSO.BossDefinition(
                BOSS_ID, ARENA_DIAMETER, HEALTH_REF, 0f, TRANSITION_RATE, 0f, NEAR_DISTANCE, MID_DISTANCE,
                FRONT_ANGLE, SIDE_ANGLE, NEAR_DISTANCE, RETRY_SECONDS, CONTROL_MAX_SECONDS, CONTROL_IMMUNE_SECONDS);

            public bool TryGetBoss(string bossId, out BossPatternCatalogSO.BossDefinition boss)
            {
                boss = (bossId == BOSS_ID) ? _boss : null;
                return boss != null;
            }

            public bool IsTuned(string bossId) => bossId == BOSS_ID;

            public void CollectPatterns(string bossId, int phase, List<BossPatternCatalogSO.PatternDefinition> results)
            {
                results.Clear();
            }
        }

        private sealed class UnregisteringResolver : IDamageResolver
        {
            private readonly IDamageResolver _inner;
            private readonly CombatTargetRegistry _registry;

            public UnregisteringResolver(IDamageResolver inner, CombatTargetRegistry registry)
            {
                _inner    = inner;
                _registry = registry;
            }

            public int IssueInstanceId() { return _inner.IssueInstanceId(); }

            public DamageResult Apply(in DamageRequest request)
            {
                DamageResult result = _inner.Apply(in request);
                _registry.Unregister(request.HitTargetId);
                return result;
            }
        }

        private sealed class ClosingObserver : ICombatMeterSink, ISessionEndingListener
        {
            private readonly DungeonSession _session;
            private int _order;

            public int Starts { get; private set; }
            public int Endings { get; private set; }
            public int Closes { get; private set; }
            public int EndingOrder { get; private set; }
            public int ClosedOrder { get; private set; }
            public bool EndedAtEnding { get; private set; }
            public bool EndedAtClose { get; private set; }
            public ESessionEndReason ReasonAtClose { get; private set; }
            public ESessionEndReason ArgumentReason { get; private set; }

            public ClosingObserver(DungeonSession session) { _session = session; }
            public void OnSessionStarted(double serverTime) { ++Starts; }
            public void ResendFinal(int participantId) { }

            public void OnSessionEnding(double serverTime)
            {
                ++Endings;
                EndingOrder   = ++_order;
                EndedAtEnding = _session.IsEnded;
            }

            public void OnSessionClosed(ESessionEndReason reason, double serverTime)
            {
                ++Closes;
                ClosedOrder    = ++_order;
                EndedAtClose   = _session.IsEnded;
                ReasonAtClose  = _session.EndReason;
                ArgumentReason = reason;
            }
        }

        private sealed class PotionLedger : IPotionEventLedger
        {
            private long _sequence;
            public bool IsSessionRunning { get; set; } = true;
            public bool IsBossSession => true;
            public bool IsStorageStopped => false;
            public bool Committed { get; set; }
            public int Reverts { get; private set; }

            public bool TryRecordUse(string participantKey, string definitionId, string bindingKind, double serverTime,
                out long sequence)
            {
                sequence = ++_sequence;
                return true;
            }

            public bool TryRecordRevert(string participantKey, long useSequence, double serverTime, out long sequence)
            {
                sequence = ++_sequence;
                ++Reverts;
                return true;
            }

            public bool IsCommitted(long sequence) { return Committed; }
        }

        private sealed class PotionUser : IPotionUser, IPotionHost
        {
            public int ParticipantId => PARTICIPANT;
            public EParticipantLifeState LifeState { get; set; } = EParticipantLifeState.Alive;
            public float Health { get; set; } = RAW_DAMAGE;
            public float MaxHealth => MAX_HEALTH;
            public bool IsControlLocked => false;
            public int HealCalls { get; private set; }

            public void Heal(float amount)
            {
                ++HealCalls;
                Health = Mathf.Min(MaxHealth, Health + amount);
            }

            public bool TryGetPotionUser(int participantId, out IPotionUser user)
            {
                user = (participantId == PARTICIPANT) ? this : null;
                return user != null;
            }

            public void OnPotionState(int participantId, uint sequence, ERejectReason reason) { }
        }
    }
}
