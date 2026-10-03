// System
using System.Collections.Generic;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    public class BossCastListenerTests
    {
        // 발동과 정상 종료 알림이 틱 시각이 아니라 FSM 단계 경계 시각으로 한 번씩 오고, 취소된 시전에는 종료가 오지 않는지 본다

        /****************************************
        *                Fields
        ****************************************/

        private const int    CAST_ID      = 7;
        private const float  TELEGRAPH    = 1f;
        private const float  ACTIVE       = 0.5f;
        private const float  RECOVERY     = 0.5f;
        private const double TIME_EPSILON = 0.000001;
        private const double COARSE_TICK  = 5.0;
        private const double HALF         = 0.5;
        private const double SMALL_STEP   = 0.1;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Tick_PastTelegraphEnd_NotifiesExecutedAtBoundary()
        {
            RecordingCastListener casts = new RecordingCastListener();
            BossAttackFsm fsm = StartFsm(casts);

            fsm.Tick(TELEGRAPH * HALF);
            Assert.AreEqual(0, casts.Executed.Count);

            fsm.Tick(TELEGRAPH + SMALL_STEP);

            Assert.AreEqual(1, casts.Executed.Count);
            Assert.AreEqual(CAST_ID, casts.Executed[0].CastId);
            Assert.AreEqual(TELEGRAPH, casts.Executed[0].Time, TIME_EPSILON);
            Assert.AreEqual(0, casts.Finished.Count);
        }

        [Test]
        public void Tick_PastRecoveryEnd_NotifiesFinishedAtBoundary()
        {
            RecordingCastListener casts = new RecordingCastListener();
            BossAttackFsm fsm = StartFsm(casts);

            fsm.Tick(TELEGRAPH + SMALL_STEP);
            fsm.Tick(TELEGRAPH + ACTIVE + RECOVERY + SMALL_STEP);

            Assert.AreEqual(1, casts.Finished.Count);
            Assert.AreEqual(CAST_ID, casts.Finished[0].CastId);
            Assert.AreEqual(TELEGRAPH + ACTIVE + RECOVERY, casts.Finished[0].Time, TIME_EPSILON);
            Assert.AreEqual(EBossAttackState.Ready, fsm.State);
        }

        [Test]
        public void Tick_CoarseTickSkipsAllPhases_NotifiesBothInOrder()
        {
            RecordingCastListener casts = new RecordingCastListener();
            BossAttackFsm fsm = StartFsm(casts);

            fsm.Tick(COARSE_TICK);

            Assert.AreEqual(1, casts.Executed.Count);
            Assert.AreEqual(1, casts.Finished.Count);
            CollectionAssert.AreEqual(new[] { RecordingCastListener.EXECUTED, RecordingCastListener.FINISHED }, casts.Order);
        }

        [Test]
        public void Cancel_DuringTelegraph_NotifiesNothing()
        {
            RecordingCastListener casts = new RecordingCastListener();
            BossAttackFsm fsm = StartFsm(casts);

            fsm.Cancel(EBossInterruptReason.Death);
            fsm.Tick(COARSE_TICK);

            Assert.AreEqual(0, casts.Executed.Count);
            Assert.AreEqual(0, casts.Finished.Count);
        }

        [Test]
        public void Cancel_DuringRecover_DoesNotNotifyFinished()
        {
            RecordingCastListener casts = new RecordingCastListener();
            BossAttackFsm fsm = StartFsm(casts);

            fsm.Tick(TELEGRAPH + ACTIVE + SMALL_STEP);
            Assert.AreEqual(EBossAttackState.Recover, fsm.State);

            fsm.Cancel(EBossInterruptReason.PhaseTransition);

            Assert.AreEqual(1, casts.Executed.Count);
            Assert.AreEqual(0, casts.Finished.Count);
        }

        [Test]
        public void Constructor_WithoutListener_RunsWithoutNotifying()
        {
            BossAttackFsm fsm = StartFsm(null);

            fsm.Tick(COARSE_TICK);

            Assert.AreEqual(EBossAttackState.Ready, fsm.State);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static BossAttackFsm StartFsm(IBossCastListener casts)
        {
            FakeDamageResolver resolver = new FakeDamageResolver();
            BossDamageGate gate = new BossDamageGate(resolver);
            gate.Open();

            PatternDefinition definition = BossTestFixtures.MakePattern("cast_test", telegraph: TELEGRAPH, active: ACTIVE,
                recovery: RECOVERY);
            FakeCombatTarget self = new FakeCombatTarget { CombatId = BossHarness.SELF_ID, Faction = ECombatFaction.Enemy };
            BossPatternContext context = new BossPatternContext(definition, CAST_ID, BossHarness.PLAYER_ID, self,
                new FakeTargetQuery(), gate);

            BossAttackFsm fsm = new BossAttackFsm(casts);
            Assert.IsTrue(fsm.Start(definition, new ScheduledHitPattern(), in context, 0.0));
            return fsm;
        }

        private struct CastEvent
        {
            public int    CastId;
            public double Time;
        }

        private sealed class RecordingCastListener : IBossCastListener
        {
            public const string EXECUTED = "executed";
            public const string FINISHED = "finished";

            public List<CastEvent> Executed { get; } = new List<CastEvent>();
            public List<CastEvent> Finished { get; } = new List<CastEvent>();
            public List<string>    Order    { get; } = new List<string>();

            public void OnCastExecuted(int castInstanceId, double serverTime)
            {
                Executed.Add(new CastEvent { CastId = castInstanceId, Time = serverTime });
                Order.Add(EXECUTED);
            }

            public void OnCastFinished(int castInstanceId, double serverTime)
            {
                Finished.Add(new CastEvent { CastId = castInstanceId, Time = serverTime });
                Order.Add(FINISHED);
            }
        }
    }
}
