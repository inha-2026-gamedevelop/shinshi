// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class DungeonSessionLifeTests
    {
        // 보스전 전멸 판정, 부활 승인, 저장 확정 거절 뒤 장애 취소를 웨이브 없는 보스 세션으로 본다

        /****************************************
        *                Fields
        ****************************************/

        private const string SESSION_ID     = "boss-session";
        private const int    QUEUE_CAPACITY = 64;
        private const int    PLAYER_ONE     = 1;
        private const int    PLAYER_TWO     = 2;
        private const double DEATH_TIME     = 2.0;
        private const float  RATE_EPSILON   = 0.0001f;
        private const string TERMINAL_ERROR = "[SessionEventQueue] 저장이 확정 거절돼 큐를 멈춘다. 출정은 서비스 장애 취소로 끝난다";

        private SessionConfigSO   _config;
        private ParticipantRoster _roster;
        private ResultSink        _sink;
        private DungeonSession    _session;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<SessionConfigSO>();
            _roster = new ParticipantRoster(_config);
            _roster.TryAdd(PLAYER_ONE, "player_one");
            _roster.TryAdd(PLAYER_TWO, "player_two");
            _sink    = new ResultSink();
            _session = new DungeonSession(SESSION_ID, _roster, new SessionEventQueue(SESSION_ID, _sink, QUEUE_CAPACITY,
                QUEUE_CAPACITY), _config, null);
            Assert.IsTrue(_session.Begin(0.0));
        }

        [TearDown]
        public void TearDown()
        {
            _session.Dispose();
            Object.DestroyImmediate(_config);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void CompleteTick_EveryActiveParticipantDead_EndsAllDeadDespiteRevivesLeft()
        {
            _roster.TrySetLifeState(PLAYER_ONE, EParticipantLifeState.Dead);
            _roster.TrySetLifeState(PLAYER_TWO, EParticipantLifeState.Dead);

            Tick(1.0);

            Assert.IsTrue(_session.IsEnded);
            Assert.AreEqual(ESessionEndReason.AllDead, _session.EndReason);
            Assert.AreEqual(_config.BossSharedRevives, _session.RemainingRevives, "남은 부활은 전멸을 막지 않는다");
            Assert.AreEqual(ERejectReason.SessionTerminal, _session.CheckRevive(PLAYER_ONE, DEATH_TIME, 100.0));
        }

        [Test]
        public void CompleteTick_AliveCharacterInReconnectGrace_IsNotWipe()
        {
            _roster.TrySetLifeState(PLAYER_ONE, EParticipantLifeState.Dead);
            Assert.IsTrue(_session.TryDisconnect(PLAYER_TWO, 0.5));

            Tick(1.0);

            Assert.IsTrue(_session.IsRunning, "유예 중에도 서버에 남아 피격되는 캐릭터는 생존자로 센다");
        }

        [Test]
        public void CompleteTick_SurvivorAbandons_RemainingDeadPartyWipes()
        {
            _roster.TrySetLifeState(PLAYER_ONE, EParticipantLifeState.Dead);
            Assert.IsTrue(_session.TryAbandon(PLAYER_TWO, 0.5));

            Tick(1.0);

            Assert.AreEqual(ESessionEndReason.AllDead, _session.EndReason, "접속 중 생존자가 0이면 전원 이탈 전이라도 끝난다");
        }

        [Test]
        public void CheckRevive_BossDelayAndSharedBudget_CommitsOnlyOnSuccess()
        {
            _roster.TrySetLifeState(PLAYER_ONE, EParticipantLifeState.Dead);
            double ready = DEATH_TIME + _config.BossReviveDelay;

            Assert.AreEqual(ERejectReason.TooEarly, _session.CheckRevive(PLAYER_ONE, DEATH_TIME, ready - 0.1));
            Assert.AreEqual(ERejectReason.None, _session.CheckRevive(PLAYER_ONE, DEATH_TIME, ready));
            Assert.AreEqual(_config.BossSharedRevives, _session.RemainingRevives, "확인만으로는 횟수를 쓰지 않는다");

            Assert.AreEqual(ERejectReason.None, _session.CommitRevive(PLAYER_ONE, DEATH_TIME, ready, out float rate));
            Assert.AreEqual(_config.BossReviveHealthRate, rate, RATE_EPSILON);
            Assert.AreEqual(_config.BossSharedRevives - 1, _session.RemainingRevives);
            Assert.IsTrue(_roster.TryGet(PLAYER_ONE, out ParticipantSnapshot snapshot));
            Assert.AreEqual(EParticipantLifeState.Alive, snapshot.LifeState);
            Assert.AreEqual(ERejectReason.WrongPhase, _session.CheckRevive(PLAYER_ONE, DEATH_TIME, ready), "살아 있으면 부활이 없다");
        }

        [Test]
        public void CommitRevive_BudgetExhausted_RejectsAndDeathBecomesSpectating()
        {
            double ready = DEATH_TIME + _config.BossReviveDelay;
            for (int i = 0; i < _config.BossSharedRevives; ++i)
            {
                _roster.TrySetLifeState(PLAYER_ONE, EParticipantLifeState.Dead);
                Assert.AreEqual(ERejectReason.None, _session.CommitRevive(PLAYER_ONE, DEATH_TIME, ready, out _));
            }

            _roster.TrySetLifeState(PLAYER_ONE, EParticipantLifeState.Dead);

            Assert.AreEqual(ERejectReason.NoRevives, _session.CommitRevive(PLAYER_ONE, DEATH_TIME, ready, out _));
            Assert.AreEqual(EParticipantLifeState.Spectating, _session.LifeStateOnDeath());
        }

        [Test]
        public void CompleteTick_StorageTerminal_CancelsServiceWithoutOverflow()
        {
            _sink.Result = EEventBatchResult.Terminal;
            LogAssert.Expect(LogType.Error, TERMINAL_ERROR);
            Tick(Constants.Session.EVENT_BATCH_INTERVAL_SECONDS);
            Assert.IsTrue(_session.HasTerminalStorage);

            _session.TryAbandon(PLAYER_ONE, 1.5);
            Assert.DoesNotThrow(() => Tick(2.0));

            Assert.IsTrue(_session.IsEnded);
            Assert.AreEqual(ESessionEndReason.ServiceCancelled, _session.EndReason);
            Assert.AreEqual(0, _session.PendingEventCount, "멈춘 큐에 넣을 수 없는 이벤트를 쌓지 않는다");
            Assert.IsFalse(_session.CanReturn, "종료 이벤트가 저장되지 않았으므로 API 장애 취소가 맡는다");
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void Tick(double serverTime)
        {
            Assert.IsTrue(_session.BeginTick(serverTime));
            _session.CompleteTick();
        }

        private sealed class ResultSink : IBackendEventSink
        {
            public EEventBatchResult Result { get; set; } = EEventBatchResult.Committed;

            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count,
                IBackendEventSinkListener listener)
            {
                listener.OnBatchCompleted(attemptId, Result);
                return true;
            }
        }
    }
}
