// System
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// Third Party
using NUnit.Framework;

// Project
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;
using Shinshi.Systems;

namespace Shinshi.Tests
{
    public sealed class DungeonSessionExperienceTests
    {
        private SessionConfigSO _config;
        private WaveConfigSO _waves;
        private RewardCatalogSO _rewards;
        private CharacterLevelConfigSO _levels;
        private ParticipantRoster _roster;
        private DungeonSession _session;
        private SessionEventQueue _queue;
        private DelayedSink _sink;
        private CharacterLevelSettings _settings;
        private readonly List<SessionEvent> _committed = new List<SessionEvent>();

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<SessionConfigSO>();
            _waves = ScriptableObject.CreateInstance<WaveConfigSO>();
            _rewards = ScriptableObject.CreateInstance<RewardCatalogSO>();
            _levels = ScriptableObject.CreateInstance<CharacterLevelConfigSO>();
            _settings = _levels.ToSettings();
            typeof(WaveConfigSO).GetField("_waves", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_waves,
                new[] { new WaveConfigSO.WaveDefinition("one", new[] { new WaveConfigSO.WaveEnemyEntry("enemy_melee", 2) },
                    new[] { new WaveConfigSO.SpawnGroup(0f, 2) }) });
            _roster = new ParticipantRoster(_config);
            for (int i = 1; i <= 4; ++i)
            {
                Assert.IsTrue(_roster.TryAdd(i, "player-" + i));
            }
            _sink = new DelayedSink();
            _queue = new SessionEventQueue("xp-session", _sink, 128, 128);
            _queue.Committed += entry => _committed.Add(entry);
            _committed.Clear();
            _session = new DungeonSession("xp-session", _roster, _queue, _config, _waves, new FakeWave());
            _session.RegisterRewards(_rewards);
            for (int i = 1; i <= 4; ++i)
            {
                Assert.IsTrue(_session.RegisterParticipantLoot(i, new[] { "T01-1A" }));
            }
        }

        [TearDown]
        public void TearDown()
        {
            _session.Dispose();
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_waves);
            Object.DestroyImmediate(_rewards);
            Object.DestroyImmediate(_levels);
        }

        [Test]
        public void KillAndWave_GrantsNormalEliteAndWaveOncePerParticipantOnlyAfterAck()
        {
            Configure();
            Assert.IsTrue(_session.Begin(0));
            Assert.IsTrue(_session.BeginTick(1));
            _session.OnEnemyDefeated(0, "normal", "player-1");
            _session.OnEnemyDefeated(0, "normal", "player-1");
            _session.OnEnemyDefeated(0, "elite", "player-1");
            _session.OnWaveCleared(0);
            _session.OnWaveCleared(0);
            _session.CompleteTick();
            Assert.IsEmpty(_committed);
            SessionEvent[] experience = _sink.Received.Where(entry => entry.Data.Type == ESessionEventType.ExperienceGranted).ToArray();
            Assert.AreEqual(12, experience.Length);
            foreach (var group in experience.GroupBy(entry => entry.Data.ParticipantId))
            {
                Assert.AreEqual(3, group.Count());
                foreach (SessionEvent entry in group)
                {
                    Assert.AreEqual(_settings.GetGrant(_settings.Regions[0].RegionId, entry.Data.ExperienceSourceKind), entry.Data.Amount);
                    Assert.AreEqual(CharacterLevelRules.SettingsHash(_settings), entry.Data.ExperienceSettingsHash);
                }
            }
            _sink.Commit();
            _sink.Commit();
            Assert.AreEqual(12, _committed.Count(entry => entry.Data.Type == ESessionEventType.ExperienceGranted));
        }

        [Test]
        public void Kill_DeadAndGraceEligible_DepartedAndExpiredExcluded()
        {
            Configure();
            _session.Begin(0);
            _roster.TrySetLifeState(1, EParticipantLifeState.Dead);

            _session.TryDisconnect(3, 0);
            _session.TryAbandon(4, 0);
            double time = _config.ReconnectGraceSeconds;
            // 2번의 유예를 늦게 시작해 3번만 만료되는 시각을 만든다.

            _session.TryDisconnect(2, time - 1);
            _session.BeginTick(time);
            _session.OnEnemyDefeated(0, "normal", "player-1");
            _session.CompleteTick();
            Assert.AreEqual(new[] { "player-1", "player-2" }, _sink.Received
                .Where(entry => entry.Data.Type == ESessionEventType.ExperienceGranted)
                .Select(entry => entry.Data.ParticipantId).OrderBy(id => id).ToArray());
        }

        [Test]
        public void Configure_UnclassifiedExistingSourceOrNewSource_IsRejected()
        {
            var drop = new RewardCatalogSO.EnemyDropSpec("enemy_melee", 5, 1f, 1, 1f, 1);
            Assert.IsTrue(_session.RegisterEnemySource(0, "unknown", drop));
            Assert.IsFalse(_session.ConfigureExperience(_settings, _settings.Regions[0].RegionId));
            _session.Dispose();
            _session = new DungeonSession("xp-session", _roster, _queue, _config, _waves, new FakeWave());
            Assert.IsTrue(_session.ConfigureExperience(_settings, _settings.Regions[0].RegionId));
            Assert.IsFalse(_session.RegisterEnemySource(0, "unknown", drop));
            Assert.IsFalse(_session.RegisterEnemySource(0, "wave-kind", drop, CharacterExperienceSource.WAVE_CLEARED));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FullQueue_FourPlayerRewardAndDepartures_PreservesExperienceUntilCommitted(bool saturateOnWave)
        {
            const int QUEUE_CAPACITY = 32;
            const int MAX_DRAIN_TICKS = 20;
            _session.Dispose();
            _queue = new SessionEventQueue("xp-session", _sink, QUEUE_CAPACITY, QUEUE_CAPACITY);
            _queue.Committed += entry => _committed.Add(entry);
            _session = new DungeonSession("xp-session", _roster, _queue, _config, _waves, new FakeWave());
            _session.RegisterRewards(_rewards);
            for (int i = 1; i <= 4; ++i)
            {
                Assert.IsTrue(_session.RegisterParticipantLoot(i, new[] { "T01-1A" }));
            }
            Configure();
            Assert.IsTrue(_session.Begin(0));
            Assert.IsTrue(_session.BeginTick(1));
            if (!saturateOnWave)
            {
                // 이미 저장을 기다리는 배치로 큐를 채워 다음 처치의 네 사람 보상 전체가 pending에 가게 한다.
                int remaining = _queue.Capacity - _queue.QueuedCount;
                for (int i = 0; i < remaining; ++i)
                {
                    Assert.IsTrue(_queue.TryEnqueue(new SessionEventData(ESessionEventType.WaveCleared, 0), out _));
                }
            }
            Assert.DoesNotThrow(() =>
            {
                _session.OnEnemyDefeated(0, "normal", "player-1");
                if (saturateOnWave)
                {
                    _session.OnEnemyDefeated(0, "elite", "player-1");
                    _session.OnWaveCleared(0);
                }
                for (int i = 1; i <= 4; ++i)
                {
                    _session.TryDisconnect(i, 1);
                    _session.TryAbandon(i, 1);
                }
            });
            Assert.IsTrue(_session.HasStorageFault);
            Assert.Greater(_session.PendingEventCount, 0);
            Assert.LessOrEqual(_session.PendingEventCount, (4 * 7) + 1);
            LogAssert.Expect(LogType.Warning,
                "[SessionEventQueue] 이벤트 저장 큐 한계에 도달했다. 저장 대기 이벤트는 보존한다");
            Assert.DoesNotThrow(() => _session.CompleteTick());
            Assert.AreEqual(ESessionEndReason.ServiceCancelled, _session.EndReason);
            for (int tick = 2; tick <= MAX_DRAIN_TICKS && !_session.CanReturn; ++tick)
            {
                _sink.Commit();
                _session.BeginTick(tick);
                _session.CompleteTick();
            }
            _sink.Commit();
            Assert.AreEqual(0, _session.PendingEventCount);
            Assert.IsTrue(_session.CanReturn);
            SessionEvent[] experience = _committed.Where(entry => entry.Data.Type == ESessionEventType.ExperienceGranted).ToArray();
            Assert.AreEqual(saturateOnWave ? 12 : 4, experience.Length);
            Assert.AreEqual(experience.Length, experience.Select(entry => entry.Seq).Distinct().Count());
            Assert.AreEqual(4, _committed.Count(entry => entry.Data.Type == ESessionEventType.ParticipantSettled));
            Assert.AreEqual(1, _committed.Count(entry => entry.Data.Type == ESessionEventType.SessionEnded));
            foreach (var participant in experience.GroupBy(entry => entry.Data.ParticipantId))
            {
                Assert.AreEqual(saturateOnWave ? 3 : 1, participant.Count());
            }
        }

        [Test]
        public void BossCompletion_NeverGrantsExperience()
        {
            _session.Dispose();
            _session = new DungeonSession("xp-session", _roster, _queue, _config, null);
            Assert.IsTrue(_session.ConfigureExperience(_settings, _settings.Regions[0].RegionId));
            _session.Begin(0);
            Assert.IsTrue(_session.TryCompleteBossForDevelopment(0));
            _session.BeginTick(1);
            _session.CompleteTick();
            Assert.IsFalse(_sink.Received.Any(entry => entry.Data.Type == ESessionEventType.ExperienceGranted));
        }

        private void Configure()
        {
            Assert.IsTrue(_session.ConfigureExperience(_settings, _settings.Regions[0].RegionId));
            var drop = new RewardCatalogSO.EnemyDropSpec("enemy_melee", 5, 1f, 1, 1f, 1);
            Assert.IsTrue(_session.RegisterEnemySource(0, "normal", drop, CharacterExperienceSource.NORMAL_ENEMY));
            Assert.IsTrue(_session.RegisterEnemySource(0, "elite", drop, CharacterExperienceSource.ELITE_ENEMY));
        }

        private sealed class FakeWave : IWaveSession
        {
            public bool IsRunning { get; private set; }
            public bool IsFinished => !IsRunning;
            public WaveProgressSnapshot Progress => default;
            public void SetListener(IWaveSessionListener listener) { }
            public void Begin(double serverTime) { IsRunning = true; }
            public void Tick(double serverTime) { }
            public void Abort() { IsRunning = false; }
        }

        private sealed class DelayedSink : IBackendEventSink
        {
            public readonly List<SessionEvent> Received = new List<SessionEvent>();
            private IBackendEventSinkListener _listener;
            private long _attempt;
            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count, IBackendEventSinkListener listener)
            {
                _attempt = attemptId;
                _listener = listener;
                for (int i = 0; i < count; ++i) { Received.Add(events[i]); }
                return true;
            }
            public void Commit() { _listener.OnBatchCompleted(_attempt, EEventBatchResult.Committed); }
        }
    }
}
