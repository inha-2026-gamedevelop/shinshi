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
    public class ReviveBudgetTests
    {
        /****************************************
        *                Fields
        ****************************************/

        // 기획이 정한 협동 보스 부활 규칙, SessionConfigSO 초안 값이 이와 같아야 한다
        private const int    DOCUMENTED_SHARED_REVIVES = 3;
        private const float  DOCUMENTED_BOSS_DELAY     = 10f;
        private const float  DOCUMENTED_BOSS_RATE      = 0.3f;
        private const float  MAX_HEALTH                = 1500f;
        private const double DEATH_TIME                = 20.0;
        private const double JUST_BEFORE               = 0.001;
        private const float  TOLERANCE                 = 0.0001f;
        private const int    TWO_ALIVE                 = 2;
        private const int    ONE_ALIVE                 = 1;
        private const int    NONE_ALIVE                = 0;

        private SessionConfigSO _session;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _session = ScriptableObject.CreateInstance<SessionConfigSO>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_session);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void SessionConfig_BossRevive_MatchesDocumentedRule()
        {
            Assert.AreEqual(DOCUMENTED_SHARED_REVIVES, _session.BossSharedRevives);
            Assert.AreEqual(DOCUMENTED_BOSS_DELAY, _session.BossReviveDelay);
            Assert.AreEqual(DOCUMENTED_BOSS_RATE, _session.BossReviveHealthRate);
        }

        [Test]
        public void TryCommitRevive_SharedThreeTimes_FourthIsNoRevives()
        {
            var budget = new ReviveBudget(_session.BossSharedRevives);

            for (int i = 0; i < DOCUMENTED_SHARED_REVIVES; ++i)
            {
                Assert.AreEqual(ERejectReason.None, budget.TryCommitRevive());
            }

            Assert.AreEqual(0, budget.Remaining);
            Assert.AreEqual(ERejectReason.NoRevives, budget.TryCommitRevive());
            Assert.AreEqual(ERejectReason.NoRevives, budget.CanRevive(DEATH_TIME, DEATH_TIME + _session.BossReviveDelay, _session.BossReviveDelay));
        }

        [Test]
        public void CanRevive_BeforeBossDelay_TooEarlyAndKeepsCount()
        {
            var    budget = new ReviveBudget(_session.BossSharedRevives);
            double now    = DEATH_TIME + _session.BossReviveDelay - JUST_BEFORE;

            Assert.AreEqual(ERejectReason.TooEarly, budget.CanRevive(DEATH_TIME, now, _session.BossReviveDelay));
            Assert.AreEqual(DOCUMENTED_SHARED_REVIVES, budget.Remaining);
        }

        [Test]
        public void CanRevive_AtBossDelay_AllowedWithoutConsuming()
        {
            var    budget = new ReviveBudget(_session.BossSharedRevives);
            double now    = DEATH_TIME + _session.BossReviveDelay;

            Assert.AreEqual(ERejectReason.None, budget.CanRevive(DEATH_TIME, now, _session.BossReviveDelay));
            Assert.AreEqual(DOCUMENTED_SHARED_REVIVES, budget.Remaining);
        }

        [Test]
        public void ReviveHealth_Boss_Is30PercentOfMax()
        {
            Assert.AreEqual(MAX_HEALTH * DOCUMENTED_BOSS_RATE, PlayerLifeRules.ReviveHealth(MAX_HEALTH, _session.BossReviveHealthRate), TOLERANCE);
        }

        [Test]
        public void EvaluateWipe_LastSurvivorDiesSameTickAsRevive_WipeWinsAndKeepsCount()
        {
            // A는 이미 쓰러져 부활 대기 시간이 막 끝났고, 같은 틱에 마지막 생존자 B가 쓰러진다
            var    budget = new ReviveBudget(_session.BossSharedRevives);
            double tick   = DEATH_TIME + _session.BossReviveDelay;

            bool wiped = budget.EvaluateWipe(NONE_ALIVE);

            Assert.IsTrue(wiped);
            Assert.IsTrue(budget.IsTerminal);
            Assert.AreEqual(ERejectReason.SessionTerminal, budget.CanRevive(DEATH_TIME, tick, _session.BossReviveDelay));
            Assert.AreEqual(ERejectReason.SessionTerminal, budget.TryCommitRevive());
            Assert.AreEqual(DOCUMENTED_SHARED_REVIVES, budget.Remaining);
        }

        [Test]
        public void EvaluateWipe_OneSurvivorLeft_ReviveStillAllowed()
        {
            var    budget = new ReviveBudget(_session.BossSharedRevives);
            double tick   = DEATH_TIME + _session.BossReviveDelay;

            Assert.IsFalse(budget.EvaluateWipe(ONE_ALIVE));
            Assert.AreEqual(ERejectReason.None, budget.CanRevive(DEATH_TIME, tick, _session.BossReviveDelay));
            Assert.AreEqual(ERejectReason.None, budget.TryCommitRevive());
            Assert.AreEqual(DOCUMENTED_SHARED_REVIVES - 1, budget.Remaining);
        }

        [Test]
        public void EvaluateWipe_SoloDeath_FailsImmediatelyWithRevivesLeft()
        {
            var budget = new ReviveBudget(_session.BossSharedRevives);

            Assert.IsTrue(budget.EvaluateWipe(NONE_ALIVE));
            Assert.AreEqual(DOCUMENTED_SHARED_REVIVES, budget.Remaining);
        }

        [Test]
        public void EvaluateWipe_AfterTerminal_StaysTerminalEvenIfSomeoneAlive()
        {
            var budget = new ReviveBudget(_session.BossSharedRevives);
            budget.EvaluateWipe(NONE_ALIVE);

            Assert.IsTrue(budget.EvaluateWipe(TWO_ALIVE));
        }

        [Test]
        public void LifeStateOnDeath_RevivesLeft_DeadThenSpectatingWhenExhausted()
        {
            var budget = new ReviveBudget(_session.BossSharedRevives);

            Assert.AreEqual(EParticipantLifeState.Dead, budget.LifeStateOnDeath());

            for (int i = 0; i < DOCUMENTED_SHARED_REVIVES; ++i)
            {
                budget.TryCommitRevive();
            }

            Assert.AreEqual(EParticipantLifeState.Spectating, budget.LifeStateOnDeath());
        }

        [Test]
        public void MarkTerminal_AvailableRevives_RejectsLaterRevives()
        {
            var budget = new ReviveBudget(_session.BossSharedRevives);
            budget.MarkTerminal();

            Assert.AreEqual(ERejectReason.SessionTerminal, budget.TryCommitRevive());
        }
    }
}
