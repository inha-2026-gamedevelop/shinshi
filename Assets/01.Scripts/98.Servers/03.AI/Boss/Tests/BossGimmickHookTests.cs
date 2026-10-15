// System
using System.Collections.Generic;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 두뇌가 기믹 계약(IBossGimmick, IBossGimmickCleanup)을 어떤 순서로 읽는지만 본다. 기믹의 내용은 다른 시험이 확인한다
    public class BossGimmickHookTests
    {
        /****************************************
        *               Helpers
        ****************************************/

        private sealed class FakeGimmick : IBossGimmick, IBossGimmickCleanup
        {
            public List<int>  Phases       { get; } = new List<int>();
            public List<EBossInterruptReason> Stops { get; } = new List<EBossInterruptReason>();
            public bool       Defers       { get; set; }
            public bool       Blocks       { get; set; }
            public bool       CleanupAsked { get; set; }

            public bool DefersPhaseTransition => Defers;
            public bool BlocksSelection       => Blocks;

            public void Tick(double serverTime, int phase)
            {
                Phases.Add(phase);
            }

            public bool TrySelect(BossBlackboard blackboard, out PatternDefinition pattern)
            {
                pattern = null;
                return false;
            }

            public void OnCastExecuted(int castInstanceId, double serverTime)
            {
            }

            public void OnCastFinished(int castInstanceId, double serverTime)
            {
            }

            public bool ConsumeCleanupRequest()
            {
                bool asked = CleanupAsked;
                CleanupAsked = false;
                return asked;
            }

            public void Stop(EBossInterruptReason reason)
            {
                Stops.Add(reason);
            }
        }

        private static BossHarness Make(FakeGimmick gimmick)
        {
            BossHarness h = new BossHarness(BossTestFixtures.MakeBoss(), null, BossTestFixtures.MakePattern("p1"));
            h.Factory.HitOffsets = new[] { 0.0 };
            Assert.IsTrue(h.Brain.AddGimmick(gimmick));
            Assert.IsTrue(h.Brain.Begin(0.0));
            return h;
        }

        /****************************************
        *               Registration
        ****************************************/

        [Test]
        public void AddGimmick_AfterBegin_IsRejected()
        {
            BossHarness h = new BossHarness(BossTestFixtures.MakeBoss(), null, BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);

            Assert.IsFalse(h.Brain.AddGimmick(new FakeGimmick()));
        }

        [Test]
        public void AddGimmick_Null_IsRejected()
        {
            BossHarness h = new BossHarness(BossTestFixtures.MakeBoss(), null, BossTestFixtures.MakePattern("p1"));

            Assert.IsFalse(h.Brain.AddGimmick(null));
        }

        /****************************************
        *               Tick Order
        ****************************************/

        [Test]
        public void Tick_CombatTick_CallsGimmickWithCurrentPhase()
        {
            FakeGimmick gimmick = new FakeGimmick();
            BossHarness h = Make(gimmick);

            h.Brain.Tick(0.0);
            h.Brain.Tick(0.25);

            CollectionAssert.AreEqual(new[] { 1, 1 }, gimmick.Phases);
        }

        [Test]
        public void Tick_DefersPhaseTransition_HoldsPhaseUntilReleased()
        {
            FakeGimmick gimmick = new FakeGimmick { Defers = true, Blocks = true };
            BossHarness h = Make(gimmick);
            h.Vitals.HealthRate = 0.4f;

            h.Brain.Tick(0.0);
            Assert.AreEqual(1, h.Brain.Phase);
            Assert.AreEqual(EBossPhaseState.Active, h.Brain.PhaseState);

            gimmick.Defers = false;
            h.Brain.Tick(0.25);

            Assert.AreEqual(2, h.Brain.Phase);
            Assert.AreEqual(EBossPhaseState.AwaitingHandoff, h.Brain.PhaseState);
        }

        /****************************************
        *               Selection
        ****************************************/

        [Test]
        public void Tick_BlocksSelection_CreatesNoPatternUntilReleased()
        {
            FakeGimmick gimmick = new FakeGimmick { Blocks = true };
            BossHarness h = Make(gimmick);

            h.Brain.Tick(0.0);
            h.Brain.Tick(0.25);
            Assert.AreEqual(0, h.Factory.CreateCount);
            Assert.AreEqual(EBossAttackState.Ready, h.Brain.AttackState);

            gimmick.Blocks = false;
            h.Brain.Tick(0.5);

            Assert.AreEqual(1, h.Factory.CreateCount);
        }

        /****************************************
        *               Cleanup
        ****************************************/

        [Test]
        public void Tick_CleanupRequest_CancelsRunningPatternAndEffectsWithGimmickEntry()
        {
            FakeGimmick gimmick = new FakeGimmick();
            BossHarness h = Make(gimmick);
            h.Brain.Tick(0.0);
            Assert.AreEqual(EBossAttackState.Telegraph, h.Brain.AttackState);

            gimmick.CleanupAsked = true;
            gimmick.Blocks       = true;
            h.Brain.Tick(0.25);

            Assert.AreEqual(EBossAttackState.Ready, h.Brain.AttackState);
            Assert.IsTrue(h.Factory.Last.IsCancelled);
            Assert.AreEqual(EBossInterruptReason.GimmickEntry, h.Factory.Last.CancelReason);
            Assert.AreEqual(1, h.Effects.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.GimmickEntry, h.Effects.Cancels[0].Key);
            Assert.AreEqual(CombatIds.NONE, h.Effects.Cancels[0].Value);
        }

        [Test]
        public void Tick_CleanupRequestIsConsumedOnce_DoesNotCancelAgain()
        {
            FakeGimmick gimmick = new FakeGimmick { Blocks = true, CleanupAsked = true };
            BossHarness h = Make(gimmick);

            h.Brain.Tick(0.0);
            h.Brain.Tick(0.25);
            h.Brain.Tick(0.5);

            Assert.AreEqual(1, h.Effects.Cancels.Count);
        }

        /****************************************
        *               Stop
        ****************************************/

        [Test]
        public void Tick_BossDeath_StopsGimmickWithDeathReason()
        {
            FakeGimmick gimmick = new FakeGimmick();
            BossHarness h = Make(gimmick);
            h.Brain.Tick(0.0);

            h.Vitals.IsHealthZero = true;
            h.Brain.Tick(0.25);

            CollectionAssert.AreEqual(new[] { EBossInterruptReason.Death }, gimmick.Stops);
        }

        [Test]
        public void Tick_CombatEnd_StopsGimmickWithCombatEndReason()
        {
            FakeGimmick gimmick = new FakeGimmick();
            BossHarness h = Make(gimmick);
            h.Brain.Tick(0.0);

            h.Brain.RequestCombatEnd();
            h.Brain.Tick(0.25);

            CollectionAssert.AreEqual(new[] { EBossInterruptReason.CombatEnd }, gimmick.Stops);
        }
    }
}
