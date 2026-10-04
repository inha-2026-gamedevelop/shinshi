// System
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class SortieCheckpointTests
    {
        // 30초 진단 체크포인트(형식 왕복, 주기, 보내는 중 건너뜀), 개인 이탈 이벤트 매핑, 일반 적 인원 배율을 본다

        /****************************************
        *                Fields
        ****************************************/

        private const string SORTIE_ID      = "6f1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const string API_ONE        = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const int    STATUS_OK      = 200;
        private const float  VITAL_HEALTH   = 512.5f;
        private const float  VITAL_MAX      = 1000f;
        private const float  VITAL_ENERGY   = 42f;
        private const float  VITAL_RESONANT = 7.5f;
        private const float  BOSS_RATE      = 0.625f;
        private const int    BOSS_PHASE     = 1;
        private const int    ENEMY_ID       = 9;
        private const float  REFERENCE      = 100f;
        private const float  BODY_RADIUS    = 0.5f;
        private const float  SCALE_EPSILON  = 0.0001f;

        private SessionConfigSO           _config;
        private ServerSessionRegistry     _registry;
        private FakeBackendTransport      _transport;
        private BackendGateway            _gateway;
        private DungeonSessionHost        _host;
        private ConcurrentQueue<TimeSpan> _delays;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config    = ScriptableObject.CreateInstance<SessionConfigSO>();
            _registry  = new ServerSessionRegistry(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _delays    = new ConcurrentQueue<TimeSpan>();
            _transport = new FakeBackendTransport((request, token) => Task.FromResult(Respond(request)));
            _gateway   = new BackendGateway(_transport, ZeroClock, FakeBackendTransport.ImmediateDelay(_delays), null, null);
            _host      = new DungeonSessionHost(_config, _registry, _gateway, false, null, new CommittingSink());
        }

        [TearDown]
        public void TearDown()
        {
            _host.Dispose();
            _gateway.Dispose();
            Object.DestroyImmediate(_config);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Codec_WriteThenRead_RoundTripsEveryField()
        {
            var state = new SessionCheckpointState(Constants.Session.MAX_PARTY_SIZE)
            {
                ServerTime       = 123.5,
                ElapsedSeconds   = 30.25,
                StartingCount    = 2,
                KillSequence     = 17,
                RemainingRevives = 2,
                HasWave          = true,
                WaveIndex        = 3,
                WaveCount        = 6,
                RemainingInWave  = 11,
                WaveElapsed      = 12.5,
                HasBoss          = true,
                BossHealthRate   = BOSS_RATE,
                BossPhase        = BOSS_PHASE
            };
            var vitals = new CheckpointVitals(VITAL_HEALTH, VITAL_MAX, VITAL_ENERGY, VITAL_RESONANT, 99.0, true);
            Assert.IsTrue(state.TryAdd(new CheckpointParticipant("p:one", EParticipantConnectionState.ReconnectGrace,
                EParticipantLifeState.Dead, ESessionEndReason.Success, 150.0, true, in vitals)));
            Assert.IsTrue(state.TryAdd(new CheckpointParticipant("p:two", EParticipantConnectionState.Departed,
                EParticipantLifeState.Alive, ESessionEndReason.Abandon, 0.0, false, default)));

            byte[] bytes = Write(state);
            var    read  = new SessionCheckpointState(Constants.Session.MAX_PARTY_SIZE);

            Assert.IsTrue(SessionCheckpointCodec.TryRead(bytes, read));
            Assert.AreEqual(state.ServerTime, read.ServerTime);
            Assert.AreEqual(state.ElapsedSeconds, read.ElapsedSeconds);
            Assert.AreEqual(state.KillSequence, read.KillSequence);
            Assert.AreEqual(state.RemainingRevives, read.RemainingRevives);
            Assert.AreEqual(state.WaveIndex, read.WaveIndex);
            Assert.AreEqual(state.RemainingInWave, read.RemainingInWave);
            Assert.AreEqual(BOSS_RATE, read.BossHealthRate);
            Assert.AreEqual(BOSS_PHASE, read.BossPhase);
            Assert.AreEqual(2, read.ParticipantCount);
            CheckpointParticipant first = read.GetAt(0);
            Assert.AreEqual("p:one", first.ParticipantKey);
            Assert.AreEqual(EParticipantConnectionState.ReconnectGrace, first.ConnectionState);
            Assert.AreEqual(EParticipantLifeState.Dead, first.LifeState);
            Assert.AreEqual(150.0, first.GraceUntil);
            Assert.IsTrue(first.HasVitals);
            Assert.AreEqual(VITAL_HEALTH, first.Vitals.Health);
            Assert.AreEqual(VITAL_ENERGY, first.Vitals.Energy);
            Assert.AreEqual(VITAL_RESONANT, first.Vitals.Resonance);
            Assert.AreEqual(99.0, first.Vitals.DeathTime);
            Assert.IsTrue(first.Vitals.IsDetached);
            Assert.AreEqual(ESessionEndReason.Abandon, read.GetAt(1).CloseReason);
            Assert.IsFalse(read.GetAt(1).HasVitals);

            byte[] truncated = new byte[bytes.Length - 1];
            Array.Copy(bytes, truncated, truncated.Length);
            Assert.IsFalse(SessionCheckpointCodec.TryRead(truncated, new SessionCheckpointState(Constants.Session.MAX_PARTY_SIZE)));
        }

        [Test]
        public void Checkpoint_EveryThirtySeconds_SkipsWhileInFlightAndCarriesVitals()
        {
            _host.CheckpointSource = new FakeCheckpointSource();
            double start = StartBackendSession();
            int    calls = _transport.Calls;

            Tick(start + Constants.Network.CHECKPOINT_INTERVAL_SECONDS - 1.0);
            Assert.AreEqual(0, _host.CheckpointSequence, "30초 전에는 보내지 않는다");

            Tick(start + Constants.Network.CHECKPOINT_INTERVAL_SECONDS);
            Assert.AreEqual(1, _host.CheckpointSequence);
            Assert.IsTrue(_host.IsCheckpointInFlight);
            FakeBackendTransport.WaitForCompleted(_gateway, 1);
            Assert.AreEqual(calls + 1, _transport.Calls);

            // 응답을 Pump하지 않아 보내는 중이다. 이번 주기는 건너뛴다(재시도 없음)
            Tick(start + (2.0 * Constants.Network.CHECKPOINT_INTERVAL_SECONDS));
            Assert.AreEqual(1, _host.CheckpointSequence);

            _gateway.Pump();
            Assert.IsFalse(_host.IsCheckpointInFlight);
            Tick(start + (3.0 * Constants.Network.CHECKPOINT_INTERVAL_SECONDS));
            Assert.AreEqual(2, _host.CheckpointSequence);

            BackendRequest request = LastCheckpointRequest();
            Assert.AreEqual(string.Format(GameServerRoutes.CHECKPOINTS_FORMAT, SORTIE_ID), request.Path);
            Assert.IsFalse(request.IsRetryable, "진단용이라 재시도하지 않는다");
            var body  = BackendJson.Deserialize<SessionCheckpointRequest>(request.BodyJson);
            var state = new SessionCheckpointState(Constants.Session.MAX_PARTY_SIZE);
            Assert.AreEqual(SessionCheckpointCodec.SCHEMA_VERSION, body.SchemaVersion);
            Assert.AreEqual(2, body.CheckpointSeq);
            Assert.LessOrEqual(body.LastEventSeq, _host.Session.Events.LastAcknowledgedSequence);
            Assert.IsTrue(SessionCheckpointCodec.TryRead(Convert.FromBase64String(body.StateBase64), state));
            Assert.AreEqual(1, state.ParticipantCount);
            Assert.AreEqual(Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + API_ONE, state.GetAt(0).ParticipantKey);
            Assert.IsTrue(state.GetAt(0).HasVitals);
            Assert.AreEqual(VITAL_HEALTH, state.GetAt(0).Vitals.Health);
            Assert.IsTrue(state.HasBoss);
            Assert.AreEqual(BOSS_RATE, state.BossHealthRate);
            Assert.AreEqual(_config.BossSharedRevives, state.RemainingRevives);
        }

        [TestCase(ESessionEndReason.Abandon, GameSessionCodes.EndReason.ABANDON)]
        [TestCase(ESessionEndReason.DisconnectExpired, GameSessionCodes.EndReason.DISCONNECT_EXPIRED)]
        public void EventSink_ParticipantSettledDeparture_MapsToParticipantDeparted(ESessionEndReason reason, int expected)
        {
            string key    = Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + API_ONE;
            var    events = new[]
            {
                new SessionEvent(1, new SessionEventData(ESessionEventType.ParticipantLeft, 1.0, participantId: key, reason: reason)),
                new SessionEvent(2, new SessionEventData(ESessionEventType.ParticipantSettled, 1.0, participantId: key, reason: reason)),
                new SessionEvent(3, new SessionEventData(ESessionEventType.ParticipantSettled, 1.0, participantId: "player_one",
                    reason: reason))
            };

            var body = BackendJson.Deserialize<SessionEventBatchRequest>(
                HttpBackendEventSink.CreateRequest(SORTIE_ID, events, events.Length).BodyJson);

            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, body.Events[0].EventKind, "연결 이탈은 진단 기록이다");
            Assert.AreEqual(GameSessionCodes.EventKind.PARTICIPANT_DEPARTED, body.Events[1].EventKind);
            Assert.AreEqual(API_ONE, body.Events[1].ParticipantId);
            Assert.AreEqual(expected, body.Events[1].EndReason);
            Assert.IsTrue(string.IsNullOrEmpty(body.Events[1].PayloadJson));
            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, body.Events[2].EventKind, "API 참가자 ID가 아니면 진단 기록으로 남긴다");
        }

        [Test]
        public void EventSink_SettledWithEndReason_StaysDiagnostic()
        {
            var events = new[]
            {
                new SessionEvent(1, new SessionEventData(ESessionEventType.ParticipantSettled, 1.0,
                    participantId: Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + API_ONE, reason: ESessionEndReason.Success))
            };

            var body = BackendJson.Deserialize<SessionEventBatchRequest>(
                HttpBackendEventSink.CreateRequest(SORTIE_ID, events, events.Length).BodyJson);

            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, body.Events[0].EventKind);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void EnemyServer_StartingCountScale_MultipliesMaxHealth(int startingCount)
        {
            var enemies = ScriptableObject.CreateInstance<EnemyCatalogSO>();
            try
            {
                EnemyCatalogSO.EnemyDefinition definition = enemies.Enemies[0];
                float scale = _config.GetEnemyHealthScale(startingCount);
                var baseline = new EnemyServer(definition, ENEMY_ID, Vector3.zero, Vector3.forward, REFERENCE, BODY_RADIUS);
                var scaled   = new EnemyServer(definition, ENEMY_ID, Vector3.zero, Vector3.forward, REFERENCE, BODY_RADIUS, scale);

                Assert.AreEqual(baseline.MaxHealth * scale, scaled.MaxHealth, baseline.MaxHealth * SCALE_EPSILON);
                Assert.AreEqual(scaled.MaxHealth, scaled.Health);
                if (startingCount == 1)
                {
                    Assert.AreEqual(baseline.MaxHealth, scaled.MaxHealth, "1인은 배율 1이다");
                }

                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    new EnemyServer(definition, ENEMY_ID, Vector3.zero, Vector3.forward, REFERENCE, BODY_RADIUS, 0f));
            }
            finally
            {
                Object.DestroyImmediate(enemies);
            }
        }

        [Test]
        public void CreateSessionEpoch_SameSortie_SameNonZeroNumber()
        {
            int first  = CombatServerInstaller.CreateSessionEpoch(SORTIE_ID);
            int second = CombatServerInstaller.CreateSessionEpoch(SORTIE_ID);
            int other  = CombatServerInstaller.CreateSessionEpoch(API_ONE);

            Assert.AreEqual(first, second);
            Assert.Greater(first, CombatIds.NONE);
            Assert.AreNotEqual(first, other);
        }

        /****************************************
        *                Helpers
        ****************************************/

        // 임대 이관 -> start -> 세션 시작까지 진행하고 시작 서버 시각을 돌려준다
        private double StartBackendSession()
        {
            var claims = new JoinTokenClaims(Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + API_ONE, null, null, SORTIE_ID);
            Assert.AreEqual(ERejectReason.None, _registry.TryAdmit(in claims, out SessionBinding binding));
            Assert.AreEqual(ERejectReason.None, _host.Admit(in claims, binding));

            Tick(1.0);
            Complete();
            Tick(2.0);
            Complete();
            Tick(3.0);
            Assert.IsTrue(_host.IsSessionRunning);
            return 3.0;
        }

        private void Tick(double serverTime)
        {
            if (_host.BeginTick(serverTime))
            {
                _host.CompleteTick();
            }
        }

        // 보낸 요청 하나의 결과가 Pump 대기 큐에 들어온 뒤 전달한다
        private void Complete()
        {
            FakeBackendTransport.WaitForCompleted(_gateway, 1);
            _gateway.Pump();
        }

        private BackendRequest LastCheckpointRequest()
        {
            FakeBackendTransport.WaitForCompleted(_gateway, 1);
            BackendRequest last = null;
            foreach (BackendRequest request in _transport.Requests)
            {
                if (request.Path.EndsWith("/checkpoints", StringComparison.Ordinal))
                {
                    last = request;
                }
            }

            Assert.IsNotNull(last);
            return last;
        }

        private static byte[] Write(SessionCheckpointState state)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                SessionCheckpointCodec.Write(writer, state);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static BackendTransportResult Respond(BackendRequest request)
        {
            if (request.Path.EndsWith("/lease-transfer", StringComparison.Ordinal))
            {
                return new BackendTransportResult(STATUS_OK, BackendJson.Serialize(new LeaseTransferResponse(API_ONE, SORTIE_ID, 0L,
                    ContractCodes.RejectReason.NONE, 0L)));
            }

            return new BackendTransportResult(STATUS_OK, BackendJson.Serialize(new OperationResult(SORTIE_ID,
                ContractCodes.OperationStatus.COMMITTED, 0, 0L, 0L, string.Empty)));
        }

        private static double ZeroClock()
        {
            return 0.0;
        }

        private sealed class CommittingSink : IBackendEventSink
        {
            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count,
                IBackendEventSinkListener listener)
            {
                listener.OnBatchCompleted(attemptId, EEventBatchResult.Committed);
                return true;
            }
        }

        private sealed class FakeCheckpointSource : ICheckpointSource
        {
            public bool TryGetParticipantVitals(int participantId, out CheckpointVitals vitals)
            {
                vitals = new CheckpointVitals(VITAL_HEALTH, VITAL_MAX, VITAL_ENERGY, VITAL_RESONANT, 0.0, false);
                return true;
            }

            public bool TryGetBossVitals(out float healthRate, out int phase)
            {
                healthRate = BOSS_RATE;
                phase      = BOSS_PHASE;
                return true;
            }
        }
    }
}
