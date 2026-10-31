// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class DungeonSessionPrologueTests
    {
        // 프롤로그 출정은 웨이브도 보스 규칙도 없다: 보상 규칙 없이 시작하고, 시간 제한과 전멸 종료가 없으며, 마무리 보고로만 성공한다

        /****************************************
        *                Fields
        ****************************************/

        private const string SESSION_ID     = "prologue-session";
        private const int    QUEUE_CAPACITY = 64;
        private const int    PLAYER         = 1;
        private const double ONE_DAY        = 86400.0;

        private SessionConfigSO   _config;
        private ParticipantRoster _roster;
        private DungeonSession    _session;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<SessionConfigSO>();
            _roster = new ParticipantRoster(_config);
            _roster.TryAdd(PLAYER, "player");
            _session = new DungeonSession(SESSION_ID, _roster, new SessionEventQueue(SESSION_ID, new ResultSink(), QUEUE_CAPACITY,
                QUEUE_CAPACITY), _config, null, null, null, prologue: true);
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
        public void Begin_PrologueWithoutRewardRuleOrEffects_Starts()
        {
            Assert.IsTrue(_session.IsPrologue);
            Assert.IsFalse(_session.IsBossSession, "프롤로그는 보스전 규칙(공용 부활, 경매)을 쓰지 않는다");
            Assert.IsTrue(_session.Begin(0.0));
            Assert.IsTrue(_session.IsRunning);
        }

        [Test]
        public void CompleteTick_LongAfterStart_DoesNotTimeOut()
        {
            Assert.IsTrue(_session.Begin(0.0));

            Assert.IsTrue(_session.BeginTick(ONE_DAY));
            _session.CompleteTick();

            Assert.IsTrue(_session.IsRunning, "프롤로그는 제한 시간이 없다");
        }

        [Test]
        public void CompleteTick_ParticipantDead_DoesNotEndAsWipe()
        {
            Assert.IsTrue(_session.Begin(0.0));
            _roster.TrySetLifeState(PLAYER, EParticipantLifeState.Dead);

            Assert.IsTrue(_session.BeginTick(1.0));
            _session.CompleteTick();

            Assert.IsTrue(_session.IsRunning, "월백 교전 사망은 진행기가 끝내고 세션은 마무리까지 간다");
        }

        [Test]
        public void ReportPrologueCompleted_InTick_EndsSuccess()
        {
            Assert.IsTrue(_session.Begin(0.0));
            Assert.IsTrue(_session.BeginTick(1.0));
            Assert.IsTrue(_session.ReportPrologueCompleted());

            _session.CompleteTick();

            Assert.AreEqual(ESessionEndReason.Success, _session.EndReason);
        }

        [Test]
        public void ReportPrologueCompleted_OutsideTick_Rejected()
        {
            Assert.IsTrue(_session.Begin(0.0));

            Assert.IsFalse(_session.ReportPrologueCompleted());
        }

        [Test]
        public void ReportBossDefeated_Prologue_Rejected()
        {
            Assert.IsTrue(_session.Begin(0.0));
            Assert.IsTrue(_session.BeginTick(1.0));

            Assert.IsFalse(_session.ReportBossDefeated(), "프롤로그 월백 조우는 보스 처치로 기록하지 않는다");
            _session.CompleteTick();
            Assert.IsTrue(_session.IsRunning);
        }

        [Test]
        public void ReportPrologueCompleted_NormalBossSession_Rejected()
        {
            var roster = new ParticipantRoster(_config);
            roster.TryAdd(PLAYER, "player");
            var boss = new DungeonSession("boss", roster, new SessionEventQueue("boss", new ResultSink(), QUEUE_CAPACITY, QUEUE_CAPACITY),
                _config, null);
            Assert.IsTrue(boss.Begin(0.0));
            Assert.IsTrue(boss.BeginTick(1.0));

            Assert.IsFalse(boss.ReportPrologueCompleted());

            boss.CompleteTick();
            boss.Dispose();
        }

        [Test]
        public void GameSessionCodes_PrologueMode_DistinctFromNormalAndBoss()
        {
            Assert.AreNotEqual(GameSessionCodes.Mode.NORMAL, GameSessionCodes.Mode.PROLOGUE);
            Assert.AreNotEqual(GameSessionCodes.Mode.BOSS, GameSessionCodes.Mode.PROLOGUE);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private sealed class ResultSink : IBackendEventSink
        {
            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count,
                IBackendEventSinkListener listener)
            {
                listener.OnBatchCompleted(attemptId, EEventBatchResult.Committed);
                return true;
            }
        }
    }
}
