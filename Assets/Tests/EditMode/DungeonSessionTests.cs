// System
using System;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;
using Shinshi.Systems;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class DungeonSessionTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const double LIMIT          = 420.0;
        private const int    QUEUE_CAPACITY = 64;
        private const int    REPEAT_COUNT   = 1000;
        private SessionConfigSO   _config;
        private WaveConfigSO      _waves;
        private RewardCatalogSO   _rewards;
        private ParticipantRoster _roster;
        private DungeonSession    _session;
        private RecordingSink     _sink;
        private FakeWave          _wave;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config  = ScriptableObject.CreateInstance<SessionConfigSO>();
            _waves   = ScriptableObject.CreateInstance<WaveConfigSO>();
            _rewards = ScriptableObject.CreateInstance<RewardCatalogSO>();
            typeof(WaveConfigSO).GetField("_waves", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_waves,
                new[]
                {
                    new WaveConfigSO.WaveDefinition("one", new[] { new WaveConfigSO.WaveEnemyEntry("enemy_melee", 1) },
                        new[] { new WaveConfigSO.SpawnGroup(0f, 1) }),
                    new WaveConfigSO.WaveDefinition("two", new[] { new WaveConfigSO.WaveEnemyEntry("enemy_melee", 1) },
                        new[] { new WaveConfigSO.SpawnGroup(0f, 1) })
                });
            _roster = new ParticipantRoster(_config);
            _roster.TryAdd(1, "player_one");
            _roster.TryAdd(2, "player_two");
            _sink = new RecordingSink();
            _wave = new FakeWave();
            var queue = new SessionEventQueue("session", _sink, QUEUE_CAPACITY, QUEUE_CAPACITY);
            _session = new DungeonSession("session", _roster, queue, _config, _waves, _wave);
            _session.RegisterRewards(_rewards);
            var drop = new RewardCatalogSO.EnemyDropSpec("enemy_melee", 5, 1f, 1, 1f, 1);
            _session.RegisterEnemySource(0, "source_one", drop);
            _session.RegisterEnemySource(1, "source_two", drop);
            _session.RegisterParticipantLoot(1, new[] { "T01-1A" });
            _session.RegisterParticipantLoot(2, new[] { "T02-1A" });
        }

        [TearDown]
        public void TearDown()
        {
            _session.Dispose();
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_waves);
            Object.DestroyImmediate(_rewards);
        }

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(true)]
        [TestCase(false)]
        public void CompleteTick_DeadlineAndFinalKillSameTick_SuccessWins(bool timeoutFirst)
        {
            Assert.IsTrue(_session.Begin(0.0));
            KillFirstWave();
            Assert.IsTrue(_session.BeginTick(LIMIT));
            var timeout = new WaveSessionOutcome(ESessionEndReason.Timeout, 1, 1, LIMIT);
            if (timeoutFirst)
            {
                _session.OnWaveSessionEnded(in timeout);
            }

            _session.OnEnemyDefeated(1, "source_two", "player_two");
            _session.OnWaveCleared(1);
            if (!timeoutFirst)
            {
                _session.OnWaveSessionEnded(in timeout);
            }

            _session.CompleteTick();
            Assert.IsTrue(_session.IsEnded);
            Assert.AreEqual(ESessionEndReason.Success, _session.EndReason);
            Assert.IsTrue(_session.CanReturn);
            Assert.AreEqual(1, _sink.Count(ESessionEventType.SessionEnded));
            Assert.AreEqual(4, _sink.Count(ESessionEventType.EnemyKilled));
            Assert.Less(_sink.LastIndexOf(ESessionEventType.EnemyKilled), _sink.LastIndexOf(ESessionEventType.SessionEnded));
        }

        [Test]
        public void CompleteTick_DeadlineWithoutFinalClear_TimesOut()
        {
            _session.Begin(0.0);
            KillFirstWave();
            _session.BeginTick(LIMIT);
            _session.CompleteTick();
            Assert.AreEqual(ESessionEndReason.Timeout, _session.EndReason);
        }

        [Test]
        public void CompleteTick_NormalAllDead_KeepsRunningWithUnlimitedRevives()
        {
            _session.Begin(0.0);
            _roster.TrySetLifeState(1, EParticipantLifeState.Dead);
            _roster.TrySetLifeState(2, EParticipantLifeState.Dead);
            _session.BeginTick(1.0);
            _session.CompleteTick();

            Assert.IsTrue(_session.IsRunning, "일반전은 전원 사망 중에도 시간과 적 상태가 이어진다");
            Assert.AreEqual(-1, _session.RemainingRevives);
            Assert.AreEqual(EParticipantLifeState.Dead, _session.LifeStateOnDeath());
        }

        [Test]
        public void CommitRevive_NormalAfterDelay_RevivesRepeatedlyAtNormalRate()
        {
            const double DEATH_TIME = 1.0;
            const int    REVIVES    = 5;
            _session.Begin(0.0);
            double ready = DEATH_TIME + _config.NormalReviveDelay;
            for (int i = 0; i < REVIVES; ++i)
            {
                _roster.TrySetLifeState(1, EParticipantLifeState.Dead);
                Assert.AreEqual(ERejectReason.TooEarly, _session.CheckRevive(1, DEATH_TIME, ready - 0.1));
                Assert.AreEqual(ERejectReason.None, _session.CommitRevive(1, DEATH_TIME, ready, out float rate));
                Assert.AreEqual(_config.NormalReviveHealthRate, rate);
            }
        }

        [Test]
        public void Constructor_WrongSaltLength_Throws()
        {
            var queue = new SessionEventQueue("other", _sink, QUEUE_CAPACITY, QUEUE_CAPACITY);
            Assert.Throws<ArgumentException>(() => new DungeonSession("other", _roster, queue, _config, null, null,
                new byte[Constants.Session.LOOT_SALT_BYTES - 1]));
        }

        [Test]
        public void BeginTick_RosterAlreadyObservedLaterTime_RejectsWithoutLosingLoot()
        {
            _session.Begin(0.0);
            _roster.Tick(100.0);
            Assert.IsFalse(_session.BeginTick(1.0));
            Assert.IsTrue(_session.BeginTick(100.0));
            _session.OnEnemyDefeated(0, "source_one", "player_one");
            _session.CompleteTick();
            Assert.AreEqual(2, _sink.Count(ESessionEventType.EnemyKilled));
        }

        [Test]
        public void TryAbandon_OneParticipantLeaves_StartingScaleAndDeadGraceLootRemain()
        {
            _session.Begin(0.0);
            float initialScale = _session.EnemyHealthScale;
            _roster.TrySetLifeState(2, EParticipantLifeState.Dead);
            _session.TryDisconnect(2, 0.0);
            _session.TryAbandon(1, 0.0);
            KillFirstWave();
            Assert.AreEqual(2, _session.StartingCount);
            Assert.AreEqual(initialScale, _session.EnemyHealthScale);
            Assert.IsTrue(_session.IsRunning);
            Assert.AreEqual(1, _sink.Count(ESessionEventType.EnemyKilled));
            Assert.AreEqual("player_two", _sink.Last(ESessionEventType.EnemyKilled).Data.ParticipantId);
            Assert.AreEqual(1, _sink.Count(ESessionEventType.ParticipantSettled));
        }

        [Test]
        public void CompleteTick_AllParticipantsGraceExpires_PreservesDepartureReason()
        {
            _session.Begin(0.0);
            _session.TryDisconnect(1, 0.0);
            _session.TryDisconnect(2, 0.0);
            _session.BeginTick(_config.ReconnectGraceSeconds);
            _session.CompleteTick();
            Assert.AreEqual(ESessionEndReason.DisconnectExpired, _session.EndReason);
            Assert.AreEqual(2, _sink.Count(ESessionEventType.ParticipantSettled));
        }

        [Test]
        public void OnEnemyDefeated_DuplicateSource_ProducesOnlyOneRollPerRecipient()
        {
            _session.Begin(0.0);
            _session.BeginTick(1.0);
            _session.OnEnemyDefeated(0, "source_one", "player_one");
            _session.OnEnemyDefeated(0, "source_one", "player_one");
            _session.CompleteTick();
            Assert.AreEqual(1, _session.KillSequence);
            Assert.AreEqual(2, _sink.Count(ESessionEventType.EnemyKilled));
        }

        [Test]
        public void CompleteTick_FullQueueAndFourDisconnectAbandons_PreservesLootAndEndsWithoutOverflow()
        {
            _session.Dispose();
            _roster.TryAdd(3, "player_three");
            _roster.TryAdd(4, "player_four");
            var queue = new SessionEventQueue("session", _sink, Constants.Session.MAX_PARTY_SIZE,
                Constants.Session.MAX_PARTY_SIZE);
            _session = new DungeonSession("session", _roster, queue, _config, _waves, _wave);
            _session.RegisterRewards(_rewards);
            var drop = new RewardCatalogSO.EnemyDropSpec("enemy_melee", 5, 1f, 1, 1f, 1);
            _session.RegisterEnemySource(0, "source_one", drop);
            _session.RegisterEnemySource(1, "source_two", drop);
            for (int i = 1; i <= Constants.Session.MAX_PARTY_SIZE; ++i)
            {
                _session.RegisterParticipantLoot(i, new[] { "T01-1A" });
            }

            _session.Begin(0.0);
            _session.BeginTick(1.0);
            _session.OnEnemyDefeated(0, "source_one", "player_one");
            for (int i = 1; i <= Constants.Session.MAX_PARTY_SIZE; ++i)
            {
                _session.TryDisconnect(i, 1.0);
                _session.TryAbandon(i, 1.0);
            }

            LogAssert.Expect(LogType.Warning,
                "[SessionEventQueue] 이벤트 저장 큐 한계에 도달했다. 저장 대기 이벤트는 보존한다");
            Assert.DoesNotThrow(() => _session.CompleteTick());
            Assert.AreEqual(ESessionEndReason.ServiceCancelled, _session.EndReason);
            const int DRAIN_TICKS = 8;
            for (int i = 2; i <= DRAIN_TICKS; ++i)
            {
                _session.BeginTick(i);
                _session.CompleteTick();
            }

            Assert.AreEqual(4, _sink.Count(ESessionEventType.EnemyKilled));
            Assert.AreEqual(4, _sink.Count(ESessionEventType.ParticipantSettled));
            Assert.AreEqual(1, _sink.Count(ESessionEventType.SessionEnded));
            Assert.AreEqual(0, _session.PendingEventCount);
            Assert.IsTrue(_session.CanReturn);
        }

        [Test]
        public void CompleteTick_IdleWithNonAllocatingSink_AllocatesZeroBytes()
        {
            _session.Dispose();
            var queue = new SessionEventQueue("session", new NonAllocatingSink());
            _session = new DungeonSession("session", _roster, queue, _config, null);
            _session.Begin(0.0);
            _session.BeginTick(1.0);
            _session.CompleteTick();
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 2; i < REPEAT_COUNT; ++i)
            {
                _session.BeginTick(i);
                _session.CompleteTick();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Test]
        public void TryCompleteBossForDevelopment_ExpiryCreatesStorageFault_RejectsSuccess()
        {
            _session.Dispose();
            var queue = new SessionEventQueue("boss", _sink, 3, 3);
            _session = new DungeonSession("boss", _roster, queue, _config, null);
            _session.Begin(0.0);
            _session.TryDisconnect(1, 0.0);
            Assert.IsFalse(_session.TryCompleteBossForDevelopment(_config.ReconnectGraceSeconds));
            Assert.IsTrue(_session.HasStorageFault);
            LogAssert.Expect(LogType.Warning,
                "[SessionEventQueue] 이벤트 저장 큐 한계에 도달했다. 저장 대기 이벤트는 보존한다");
            _session.BeginTick(_config.ReconnectGraceSeconds);
            _session.CompleteTick();
            Assert.AreEqual(ESessionEndReason.ServiceCancelled, _session.EndReason);
        }

        [Test]
        public void TryCompleteBossForDevelopment_BossSession_WritesBossSuccessOnce()
        {
            _session.Dispose();
            var queue = new SessionEventQueue("boss", _sink, QUEUE_CAPACITY, QUEUE_CAPACITY);
            _session = new DungeonSession("boss", _roster, queue, _config, null);
            _session.Begin(0.0);
            Assert.IsTrue(_session.TryCompleteBossForDevelopment(0.0));
            Assert.IsFalse(_session.TryCompleteBossForDevelopment(0.0));
            _session.BeginTick(1.0);
            _session.CompleteTick();
            Assert.AreEqual(ESessionEndReason.Success, _session.EndReason);
            Assert.IsTrue(_sink.Last(ESessionEventType.SessionEnded).Data.IsBossSession);
        }
#endif

        private void KillFirstWave()
        {
            _session.BeginTick(1.0);
            _session.OnEnemyDefeated(0, "source_one", "player_one");
            _session.OnWaveCleared(0);
            _session.CompleteTick();
        }

        private sealed class FakeWave : IWaveSession
        {
            public bool                 IsRunning  { get; private set; }
            public bool                 IsFinished => !IsRunning;
            public WaveProgressSnapshot Progress   => default;
            public void SetListener(IWaveSessionListener listener) { }
            public void Begin(double serverTime) { IsRunning = true; }
            public void Tick(double serverTime) { }
            public void Abort() { IsRunning = false; }
        }

        private sealed class NonAllocatingSink : IBackendEventSink
        {
            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count,
                IBackendEventSinkListener listener)
            {
                listener.OnBatchCompleted(attemptId, EEventBatchResult.Committed);
                return true;
            }
        }

        private sealed class RecordingSink : IBackendEventSink
        {
            private readonly List<SessionEvent> _received = new List<SessionEvent>(QUEUE_CAPACITY);

            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count,
                IBackendEventSinkListener listener)
            {
                for (int i = 0; i < count; ++i)
                {
                    _received.Add(events[i]);
                }

                listener.OnBatchCompleted(attemptId, EEventBatchResult.Committed);
                return true;
            }

            public int Count(ESessionEventType type)
            {
                int count = 0;
                for (int i = 0; i < _received.Count; ++i)
                {
                    if (_received[i].Data.Type == type)
                    {
                        ++count;
                    }
                }

                return count;
            }

            public int LastIndexOf(ESessionEventType type)
            {
                for (int i = _received.Count - 1; i >= 0; --i)
                {
                    if (_received[i].Data.Type == type)
                    {
                        return i;
                    }
                }

                return -1;
            }

            public SessionEvent Last(ESessionEventType type)
            {
                return _received[LastIndexOf(type)];
            }
        }
    }
}
