// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 실제 MucheolChargeGimmick, 실제 BossBrain과 BossCombat, 실제 MucheolPatternFactory와 P06(돌진 패턴)을 한 줄로 엮어 확인한다.
    // 체력, 카탈로그, 대상, 이동 창구, 피해 해석기, 구조물 파괴 확인 입력은 이 테스트 안의 가짜이며 맵이나 운영 구조물이 아니다.
    // 발동 비율, 구조물 수, 그로기 시간, 시전 시간, 충돌 회복은 모두 이 테스트가 정한 시작값이며 운영 데이터가 아니다.
    // 구조물 파괴는 ConfirmStructureDestroyed 입력으로만 표현한다. 돌진 충돌 자체는 파괴가 아니라는 점도 함께 본다
    public class MucheolChargeGimmickBrainTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  TRIGGER_RATE      = 0.70f;
        private const float  PHASE_RATE        = 0.55f;
        private const int    REQUIRED          = 3;
        private const float  GROGGY            = 3f;
        private const float  COLLISION_RECOVER = 2.5f;

        private const int    STRUCTURE_A       = 101;
        private const int    STRUCTURE_B       = 102;
        private const int    STRUCTURE_C       = 103;
        private const int    UNREGISTERED      = 999;

        // P06 합계 3.0초(예고 1, 실행 0.5, 일반 회복 1.5), 일반 P04 합계 2.0초
        private const float  P06_TELEGRAPH     = 1f;
        private const float  P06_ACTIVE        = 0.5f;
        private const float  P06_RECOVERY      = 1.5f;
        private const double P06_TOTAL         = 3.0;
        private const double STEP              = 0.05;

        /****************************************
        *               Helpers
        ****************************************/

        private sealed class SpyCasts : IBossCastListener
        {
            public List<KeyValuePair<int, double>> Executed = new List<KeyValuePair<int, double>>();
            public List<KeyValuePair<int, double>> Finished = new List<KeyValuePair<int, double>>();

            public void OnCastExecuted(int castInstanceId, double serverTime)
            {
                Executed.Add(new KeyValuePair<int, double>(castInstanceId, serverTime));
            }

            public void OnCastFinished(int castInstanceId, double serverTime)
            {
                Finished.Add(new KeyValuePair<int, double>(castInstanceId, serverTime));
            }
        }

        // 이동을 막지 않는 창구, 돌진이 정해진 거리를 모두 간다
        private sealed class OpenMovement : IBossMovement
        {
            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                return maxDistance;
            }

            public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
            {
                return to;
            }
        }

        // 일정 거리 앞에 벽이 있는 창구, 돌진이 도중에 벽에 막힌다
        private sealed class WallMovement : IBossMovement
        {
            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                return 4f;
            }

            public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
            {
                return to;
            }
        }

        private sealed class CountingGimmick : IBossGimmick
        {
            public int            Ticks;
            public List<int>      Executed = new List<int>();
            public List<int>      Finished = new List<int>();

            public bool DefersPhaseTransition => false;
            public bool BlocksSelection       => false;

            public void Tick(double serverTime, int phase)
            {
                ++Ticks;
            }

            public bool TrySelect(BossBlackboard blackboard, out PatternDefinition pattern)
            {
                pattern = null;
                return false;
            }

            public void OnCastExecuted(int castInstanceId, double serverTime)
            {
                Executed.Add(castInstanceId);
            }

            public void OnCastFinished(int castInstanceId, double serverTime)
            {
                Finished.Add(castInstanceId);
            }
        }

        private static PatternDefinition MakeP06(bool controlCancels = false)
        {
            return new PatternDefinition(MucheolPatternFactory.P06_ID, BossTestFixtures.BOSS_ID, 1, 10f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                P06_TELEGRAPH, 0f, 0f, P06_ACTIVE, P06_RECOVERY, EnemyCatalogSO.AttackShapeSpec.Dash(18f, 3f), 1f, true,
                EBossActionState.Telegraph, controlCancels ? 1f : 0f, controlCancels, 0f, 0f, 0);
        }

        private sealed class Rig
        {
            public FakeBossVitals        Vitals  { get; } = new FakeBossVitals();
            public FakeBossCatalog       Catalog { get; } = new FakeBossCatalog();
            public FakeControlSource     Control { get; } = new FakeControlSource();
            public FakeDeathPolicy       Death   { get; } = new FakeDeathPolicy();
            public SpyCasts              Body    { get; } = new SpyCasts();
            public MucheolPatternFactory Factory { get; }
            public MucheolChargeGimmick  Gimmick { get; }
            public BossBrain             Brain   { get; }

            public Rig(IBossMovement movement = null, bool controlCancels = false, IBossGimmick extraGimmick = null)
            {
                Catalog.Boss = BossTestFixtures.MakeBoss(PHASE_RATE);
                Catalog.Patterns.Add(BossTestFixtures.MakePattern(MucheolPatternFactory.P04_ID, 10f, 1f, 0.5f, 0.5f));
                Catalog.Patterns.Add(MakeP06(controlCancels));

                Factory = new MucheolPatternFactory(3541f, null, movement ?? new OpenMovement(), null, null, null, null,
                    new MucheolChargeSpec(COLLISION_RECOVER));
                Gimmick = new MucheolChargeGimmick(Vitals, Catalog, Factory, BossTestFixtures.BOSS_ID,
                    new MucheolChargeGimmickSpec(TRIGGER_RATE, REQUIRED, GROGGY));
                Factory.SetChargeCastTracker(Gimmick);
                Assert.IsTrue(Gimmick.SetDesignatedStructures(new[] { STRUCTURE_A, STRUCTURE_B, STRUCTURE_C }));

                FakeCombatTarget self = new FakeCombatTarget { CombatId = BossHarness.SELF_ID, Faction = ECombatFaction.Enemy };
                FakeTargetQuery query = new FakeTargetQuery();
                query.Players.Add(new FakeCombatTarget
                {
                    CombatId = BossHarness.PLAYER_ID, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f)
                });

                Brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
                {
                    Catalog     = Catalog,
                    Self        = self,
                    Vitals      = Vitals,
                    Targets     = query,
                    Damage      = new FakeDamageResolver(),
                    Patterns    = Factory,
                    Effects     = new FakeEffectLifetime(),
                    Control     = Control,
                    Random      = new FakeBossRandom(),
                    DeathPolicy = Death,
                    Casts       = Body
                });

                Assert.IsTrue(Brain.AddGimmick(Gimmick));
                if (extraGimmick != null)
                {
                    Assert.IsTrue(Brain.AddGimmick(extraGimmick));
                }

                Assert.IsTrue(Brain.Begin(0.0));
            }

            public double Now { get; private set; }

            // 시각을 STEP씩 올리며 조건이 참이 될 때까지 틱한다, 끝내 안 되면 실패한다
            public void RunUntil(System.Func<bool> condition, double limitSeconds = 30.0)
            {
                double end = Now + limitSeconds;
                while (!condition())
                {
                    Assert.Less(Now, end, "제한 시간 안에 조건이 참이 되지 않았다");
                    Now += STEP;
                    Brain.Tick(Now);
                }
            }

            public void Tick(double serverTime)
            {
                Now = serverTime;
                Brain.Tick(serverTime);
            }

            // 가중 선택으로 일반 패턴(P04)이 끝날 때까지 돌려 기믹이 없는 상태로 둔다
            public void SettleFirstNormalCast()
            {
                Tick(0.0);
                Assert.AreEqual(MucheolPatternFactory.P04_ID, Brain.ActivePatternId);
                RunUntil(() => Brain.AttackState == EBossAttackState.Ready);
            }

            // 기믹을 발동시키고 그 첫 P06 시전이 시작된 틱까지 진행한다
            public void ActivateAndStartP06()
            {
                Vitals.HealthRate = TRIGGER_RATE;
                RunUntil(() => Brain.ActivePatternId == MucheolPatternFactory.P06_ID);
            }

            public void DestroyAll(int castId)
            {
                Assert.IsTrue(Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, Now, castId));
                Assert.IsTrue(Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, Now, castId));
                Assert.IsTrue(Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, Now, castId));
            }

            public void FinishCurrentCast()
            {
                RunUntil(() => Brain.AttackState == EBossAttackState.Ready);
            }
        }

        /****************************************
        *            Cast Notification Wiring
        ****************************************/

        // 공용 BossCombat은 시전 알림을 몸체(Casts)와 등록된 기믹 모두에 전달해야 한다
        [Test]
        public void Brain_P06Cast_NotifiesGimmickAndBodyOfTheSameCast()
        {
            Rig rig = new Rig();
            rig.SettleFirstNormalCast();
            rig.ActivateAndStartP06();

            Assert.AreEqual(EMucheolChargeGimmickState.Active, rig.Gimmick.State);
            Assert.IsTrue(rig.Gimmick.IsCastInFlight);
            int castId = rig.Gimmick.TrackedCastId;
            Assert.AreNotEqual(0, castId);

            rig.FinishCurrentCast();

            Assert.IsFalse(rig.Gimmick.IsCastInFlight);
            Assert.AreEqual(1, rig.Body.Finished.FindAll(f => f.Key == castId).Count, "몸체도 같은 시전의 종료 알림을 한 번 받는다");
            Assert.AreEqual(1, rig.Body.Executed.FindAll(e => e.Key == castId).Count);
        }

        [Test]
        public void Brain_NoGimmick_BodyReceivesEachNotificationExactlyOncePerCast()
        {
            BossHarness h = new BossHarness(BossTestFixtures.MakeBoss(), null, BossTestFixtures.MakePattern("p1"));
            SpyCasts body = new SpyCasts();
            BossBrain brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
            {
                Catalog = h.Catalog, Self = h.Self, Vitals = h.Vitals, Targets = h.Query, Damage = h.Damage,
                Patterns = h.Factory, Effects = h.Effects, Control = h.Control, Random = h.Random, Casts = body
            });
            Assert.IsTrue(brain.Begin(0.0));

            for (double t = 0.0; t <= 6.0; t += STEP)
            {
                brain.Tick(t);
            }

            Assert.GreaterOrEqual(body.Finished.Count, 2);
            Assert.AreEqual(body.Executed.Count, body.Finished.Count + (brain.AttackState == EBossAttackState.Ready ? 0 : 1));
            for (int i = 0; i < body.Finished.Count; ++i)
            {
                Assert.AreEqual(body.Finished[i].Key, body.Executed[i].Key);
            }
        }

        [Test]
        public void Brain_SeveralGimmicks_AllReceiveCastNotificationsAndTicks()
        {
            CountingGimmick other = new CountingGimmick();
            Rig rig = new Rig(extraGimmick: other);
            rig.SettleFirstNormalCast();

            Assert.AreEqual(1, other.Executed.Count);
            Assert.AreEqual(1, other.Finished.Count);
            Assert.Greater(other.Ticks, 0);
            Assert.AreEqual(rig.Body.Executed[0].Key, other.Executed[0]);
        }

        [Test]
        public void Brain_SameGimmickAddedTwice_TicksAndNotifiesOnce()
        {
            CountingGimmick gimmick = new CountingGimmick();
            BossHarness h = new BossHarness(BossTestFixtures.MakeBoss(), null, BossTestFixtures.MakePattern("p1"));
            h.Factory.HitOffsets = new[] { 0.0 };
            Assert.IsTrue(h.Brain.AddGimmick(gimmick));
            Assert.IsTrue(h.Brain.AddGimmick(gimmick));
            Assert.IsTrue(h.Brain.Begin(0.0));

            h.Brain.Tick(0.0);
            h.Brain.Tick(0.25);
            Assert.AreEqual(2, gimmick.Ticks);

            h.Brain.Tick(100.0);
            Assert.AreEqual(1, gimmick.Executed.Count);
            Assert.AreEqual(1, gimmick.Finished.Count);
        }

        [Test]
        public void Brain_GimmickAlsoBodyListener_IsNotifiedOnlyOnce()
        {
            CountingGimmick gimmick = new CountingGimmick();
            BossHarness h = new BossHarness(BossTestFixtures.MakeBoss(), null, BossTestFixtures.MakePattern("p1"));
            h.Factory.HitOffsets = new[] { 0.0 };
            BossBrain brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
            {
                Catalog = h.Catalog, Self = h.Self, Vitals = h.Vitals, Targets = h.Query, Damage = h.Damage,
                Patterns = h.Factory, Effects = h.Effects, Control = h.Control, Random = h.Random, Casts = gimmick
            });
            Assert.IsTrue(brain.AddGimmick(gimmick));
            Assert.IsTrue(brain.Begin(0.0));

            brain.Tick(0.0);
            brain.Tick(100.0);

            Assert.AreEqual(1, gimmick.Executed.Count);
            Assert.AreEqual(1, gimmick.Finished.Count);
        }

        // 몸체가 기믹을 복합 수신자(중첩 포함)로 묶어 두었어도 기믹은 시전 알림을 한 번만 받고 몸체의 다른 수신자도 한 번씩 받는다
        [TestCase(false)]
        [TestCase(true)]
        public void Brain_GimmickInsideCompositeBody_IsNotifiedOnlyOnce(bool nested)
        {
            CountingGimmick gimmick = new CountingGimmick();
            SpyCasts spy = new SpyCasts();
            IBossCastListener body = new BossCastListenerComposite(spy, gimmick);
            if (nested)
            {
                body = new BossCastListenerComposite(new BossCastListenerComposite(body));
            }

            BossHarness h = new BossHarness(BossTestFixtures.MakeBoss(), null, BossTestFixtures.MakePattern("p1"));
            h.Factory.HitOffsets = new[] { 0.0 };
            BossBrain brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
            {
                Catalog = h.Catalog, Self = h.Self, Vitals = h.Vitals, Targets = h.Query, Damage = h.Damage,
                Patterns = h.Factory, Effects = h.Effects, Control = h.Control, Random = h.Random, Casts = body
            });
            Assert.IsTrue(brain.AddGimmick(gimmick));
            Assert.IsTrue(brain.Begin(0.0));

            brain.Tick(0.0);
            brain.Tick(100.0);

            Assert.AreEqual(1, gimmick.Executed.Count);
            Assert.AreEqual(1, gimmick.Finished.Count);
            Assert.AreEqual(1, spy.Executed.Count);
            Assert.AreEqual(1, spy.Finished.Count);
        }

        /****************************************
        *               Trigger And Repeat
        ****************************************/

        // 70% 바로 위에서는 발동하지 않고 정확히 70%에서 발동한다
        [Test]
        public void Trigger_JustAboveBoundary_DoesNotActivate_AtBoundary_Activates()
        {
            Rig rig = new Rig();
            rig.SettleFirstNormalCast();

            rig.Vitals.HealthRate = 0.7001f;
            rig.Tick(rig.Now + STEP);
            Assert.AreEqual(EMucheolChargeGimmickState.Dormant, rig.Gimmick.State);

            rig.Vitals.HealthRate = 0.70f;
            rig.Tick(rig.Now + STEP);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, rig.Gimmick.State);
        }

        // 발동 뒤에는 P06만 고르고, 구조물을 부수지 못해도 같은 시전이 반복된다
        [Test]
        public void Active_RepeatsP06_WithEachCastTrackedAndNoOtherPatternPicked()
        {
            Rig rig = new Rig();
            rig.SettleFirstNormalCast();
            rig.Vitals.HealthRate = TRIGGER_RATE;

            HashSet<int> trackedIds = new HashSet<int>();
            for (int cast = 0; cast < 4; ++cast)
            {
                rig.RunUntil(() => rig.Brain.ActivePatternId == MucheolPatternFactory.P06_ID);
                Assert.IsTrue(rig.Gimmick.IsCastInFlight);
                Assert.IsTrue(trackedIds.Add(rig.Gimmick.TrackedCastId), "시전마다 새 ID를 추적한다");
                rig.FinishCurrentCast();
                Assert.AreEqual(EMucheolChargeGimmickState.Active, rig.Gimmick.State);
            }

            Assert.AreEqual(0, rig.Gimmick.FailedStartCount);
            Assert.AreEqual(0, rig.Gimmick.DestroyedCount);
        }

        // 한 번의 피해로 70%와 55%를 함께 넘어도 같은 틱에 기믹이 먼저 전환을 보류한다
        [Test]
        public void Trigger_HealthDropsPastBothThresholdsAtOnce_DefersPhaseTransition()
        {
            Rig rig = new Rig();
            rig.SettleFirstNormalCast();

            rig.Vitals.HealthRate = 0.5f;
            rig.Tick(rig.Now + STEP);

            Assert.AreEqual(EMucheolChargeGimmickState.Active, rig.Gimmick.State);
            Assert.AreEqual(1, rig.Brain.Phase);
            Assert.AreEqual(EBossPhaseState.Active, rig.Brain.PhaseState);
        }

        /****************************************
        *            Destruction And Groggy
        ****************************************/

        // 서로 다른 지정 구조물 3개를 부수면 마지막 시전의 회복이 끝난 경계에서 그로기가 시작되고 그 전에는 시작하지 않는다
        [Test]
        public void ThreeDistinctStructures_GroggyStartsAtRecoveryEndBoundaryOfTheLastCast()
        {
            Rig rig = new Rig();
            rig.SettleFirstNormalCast();
            rig.ActivateAndStartP06();
            int castId = rig.Gimmick.TrackedCastId;
            double castStart = rig.Now;

            Assert.IsTrue(rig.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, rig.Now, castId));
            Assert.IsFalse(rig.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, rig.Now, castId), "같은 구조물은 다시 세지 않는다");
            Assert.IsFalse(rig.Gimmick.ConfirmStructureDestroyed(UNREGISTERED, rig.Now, castId), "지정되지 않은 구조물은 세지 않는다");
            Assert.IsTrue(rig.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, rig.Now, castId));
            Assert.AreEqual(2, rig.Gimmick.DestroyedCount);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, rig.Gimmick.State);

            Assert.IsTrue(rig.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, rig.Now, castId));
            Assert.AreEqual(EMucheolChargeGimmickState.AwaitingFinalRecovery, rig.Gimmick.State);

            // 실행이 끝난 뒤 회복 중에도 아직 그로기가 아니다
            rig.RunUntil(() => rig.Brain.AttackState == EBossAttackState.Recover);
            Assert.AreEqual(EMucheolChargeGimmickState.AwaitingFinalRecovery, rig.Gimmick.State);
            Assert.IsTrue(rig.Gimmick.BlocksSelection);

            rig.FinishCurrentCast();

            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, rig.Gimmick.State);
            double finishBoundary = rig.Body.Finished.Find(f => f.Key == castId).Value;
            Assert.AreEqual(finishBoundary, rig.Gimmick.GroggyStartTime);
            Assert.AreEqual(castStart + P06_TOTAL, finishBoundary, 0.0001, "충돌 없는 일반 종료는 정의의 일반 회복을 쓴다");
            Assert.AreEqual(finishBoundary + GROGGY, rig.Gimmick.GroggyEndTime, 0.0001);
        }

        // 서로 다른 시전에서 나눠 부숴도 세 번째 파괴를 낸 시전의 회복 뒤에 그로기가 시작된다
        [Test]
        public void StructuresDestroyedAcrossCasts_GroggyAfterTheCastThatMadeTheThirdDestruction()
        {
            Rig rig = new Rig();
            rig.SettleFirstNormalCast();
            rig.ActivateAndStartP06();

            Assert.IsTrue(rig.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, rig.Now, rig.Gimmick.TrackedCastId));
            rig.FinishCurrentCast();
            Assert.AreEqual(EMucheolChargeGimmickState.Active, rig.Gimmick.State);

            rig.RunUntil(() => rig.Brain.ActivePatternId == MucheolPatternFactory.P06_ID);
            Assert.IsTrue(rig.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, rig.Now, rig.Gimmick.TrackedCastId));
            rig.FinishCurrentCast();
            Assert.AreEqual(EMucheolChargeGimmickState.Active, rig.Gimmick.State);

            rig.RunUntil(() => rig.Brain.ActivePatternId == MucheolPatternFactory.P06_ID);
            int lastCast = rig.Gimmick.TrackedCastId;
            Assert.IsTrue(rig.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, rig.Now, lastCast));
            rig.FinishCurrentCast();

            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, rig.Gimmick.State);
            Assert.AreEqual(rig.Body.Finished.Find(f => f.Key == lastCast).Value, rig.Gimmick.GroggyStartTime);
        }

        // 돌진 충돌(벽)은 파괴가 아니다, 충돌 회복이 끝나도 진행도는 그대로이고 기믹은 계속된다
        [Test]
        public void WallCollision_IsNotAStructureDestruction()
        {
            Rig rig = new Rig(new WallMovement());
            rig.SettleFirstNormalCast();
            rig.ActivateAndStartP06();
            double castStart = rig.Now;
            int castId = rig.Gimmick.TrackedCastId;

            rig.FinishCurrentCast();

            Assert.AreEqual(0, rig.Gimmick.DestroyedCount);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, rig.Gimmick.State);

            // 막힌 돌진은 충돌 회복(시작값 2.5초)을 쓴다, 일반 회복(1.5초)이 아니다
            double finish = rig.Body.Finished.Find(f => f.Key == castId).Value;
            Assert.AreEqual(castStart + P06_TELEGRAPH + P06_ACTIVE + COLLISION_RECOVER, finish, 0.0001);
        }

        // 충돌 회복이 길어도 마지막 파괴를 낸 시전은 그 회복이 끝난 경계에서 그로기를 시작한다
        [Test]
        public void CollisionRecovery_LastCastGroggyStartsAfterTheLongRecovery()
        {
            Rig rig = new Rig(new WallMovement());
            rig.SettleFirstNormalCast();
            rig.ActivateAndStartP06();
            double castStart = rig.Now;
            rig.DestroyAll(rig.Gimmick.TrackedCastId);

            rig.FinishCurrentCast();

            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, rig.Gimmick.State);
            Assert.AreEqual(castStart + P06_TELEGRAPH + P06_ACTIVE + COLLISION_RECOVER, rig.Gimmick.GroggyStartTime, 0.0001);
        }

        // 그로기 동안 새 공격을 고르지 않고 끝 경계에서 일반 선택이 다시 열린다
        [Test]
        public void Groggy_BlocksNewAttacksForThreeSecondsThenSelectionResumes()
        {
            Rig rig = new Rig();
            rig.SettleFirstNormalCast();
            rig.ActivateAndStartP06();
            rig.DestroyAll(rig.Gimmick.TrackedCastId);
            rig.FinishCurrentCast();
            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, rig.Gimmick.State);

            int executedBefore = rig.Body.Executed.Count;
            double start = rig.Gimmick.GroggyStartTime;
            double end = rig.Gimmick.GroggyEndTime;
            Assert.AreEqual(GROGGY, end - start, 0.0001);

            while (rig.Now + STEP < end - 0.0001)
            {
                rig.Tick(rig.Now + STEP);
                Assert.AreEqual(EBossAttackState.Ready, rig.Brain.AttackState, "그로기 중에는 공격이 시작되지 않는다 t=" + rig.Now);
                Assert.AreEqual(string.Empty, rig.Brain.ActivePatternId);
            }

            Assert.AreEqual(executedBefore, rig.Body.Executed.Count);

            rig.Tick(end);
            Assert.AreEqual(EMucheolChargeGimmickState.Completed, rig.Gimmick.State);

            rig.RunUntil(() => rig.Brain.AttackState != EBossAttackState.Ready);
            Assert.AreNotEqual(string.Empty, rig.Brain.ActivePatternId, "그로기가 끝나면 일반 선택이 다시 열린다");
        }

        // 기믹 중 체력이 55% 아래로 내려가도 구조물 파괴와 그로기가 끝난 틱에 2페이즈로 전환한다
        [Test]
        public void PhaseTransition_DeferredUntilGroggyEnds_ThenReleasedOnThatTick()
        {
            Rig rig = new Rig();
            rig.SettleFirstNormalCast();
            rig.ActivateAndStartP06();
            rig.Vitals.HealthRate = 0.4f;

            rig.RunUntil(() => rig.Brain.AttackState == EBossAttackState.Ready);
            Assert.AreEqual(1, rig.Brain.Phase, "구조물을 부수기 전에는 전환하지 않는다");

            rig.RunUntil(() => rig.Brain.ActivePatternId == MucheolPatternFactory.P06_ID);
            rig.DestroyAll(rig.Gimmick.TrackedCastId);
            rig.FinishCurrentCast();
            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, rig.Gimmick.State);
            Assert.AreEqual(1, rig.Brain.Phase, "그로기 중에도 전환은 보류된다");

            rig.Tick(rig.Gimmick.GroggyEndTime - STEP);
            Assert.AreEqual(1, rig.Brain.Phase);

            rig.Tick(rig.Gimmick.GroggyEndTime);
            Assert.AreEqual(EMucheolChargeGimmickState.Completed, rig.Gimmick.State);
            Assert.AreEqual(2, rig.Brain.Phase, "그로기가 끝난 그 틱에 전환이 판정된다");
        }

        /****************************************
        *       Death, Combat End And Cancel
        ****************************************/

        [Test]
        public void Death_DuringP06Cast_StopsGimmickAndCastTracking()
        {
            Rig rig = new Rig();
            rig.SettleFirstNormalCast();
            rig.ActivateAndStartP06();

            rig.Vitals.IsHealthZero = true;
            rig.Death.Confirm = true;
            rig.Tick(rig.Now + STEP);

            Assert.AreEqual(EBossFlowState.Defeated, rig.Brain.FlowState);
            Assert.AreEqual(EMucheolChargeGimmickState.Stopped, rig.Gimmick.State);
            Assert.IsFalse(rig.Gimmick.IsCastInFlight);
            Assert.IsFalse(rig.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, rig.Now));
        }

        [Test]
        public void CombatEnd_DuringP06Cast_StopsGimmickAndCastTracking()
        {
            Rig rig = new Rig();
            rig.SettleFirstNormalCast();
            rig.ActivateAndStartP06();

            rig.Brain.RequestCombatEnd();
            rig.Tick(rig.Now + STEP);

            Assert.AreEqual(EBossFlowState.Ended, rig.Brain.FlowState);
            Assert.AreEqual(EMucheolChargeGimmickState.Stopped, rig.Gimmick.State);
            Assert.IsFalse(rig.Gimmick.IsCastInFlight);
        }

        // 마지막 파괴를 기다리는 중 사망하면 그로기를 시작하지 않고 멈춘다
        [Test]
        public void Death_WhileAwaitingFinalRecovery_StopsInsteadOfGroggy()
        {
            Rig rig = new Rig();
            rig.SettleFirstNormalCast();
            rig.ActivateAndStartP06();
            rig.DestroyAll(rig.Gimmick.TrackedCastId);

            rig.Vitals.IsHealthZero = true;
            rig.Death.Confirm = true;
            rig.Tick(rig.Now + STEP);
            rig.Tick(rig.Now + STEP);

            Assert.AreEqual(EMucheolChargeGimmickState.Stopped, rig.Gimmick.State);
        }

        // 구조물을 부수기 전에 시전이 취소되면 추적만 거두고 기믹은 계속되어 P06을 다시 고른다
        [Test]
        public void CastCancelledBeforeDestruction_KeepsGimmickActiveAndPicksP06Again()
        {
            Rig rig = new Rig(controlCancels: true);
            rig.SettleFirstNormalCast();
            rig.ActivateAndStartP06();
            int castId = rig.Gimmick.TrackedCastId;

            rig.Control.Pending = true;
            rig.Tick(rig.Now + STEP);

            Assert.AreEqual(EBossAttackState.Ready, rig.Brain.AttackState);
            Assert.IsFalse(rig.Gimmick.IsCastInFlight);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, rig.Gimmick.State);

            rig.RunUntil(() => rig.Brain.ActivePatternId == MucheolPatternFactory.P06_ID);
            Assert.AreNotEqual(castId, rig.Gimmick.TrackedCastId);
        }

        // 마지막 파괴를 기다리던 시전이 정상 종료 없이 취소되면 다음 틱 시각부터 그로기다
        [Test]
        public void CastCancelledWhileAwaitingFinalRecovery_GroggyStartsOnNextTick()
        {
            Rig rig = new Rig(controlCancels: true);
            rig.SettleFirstNormalCast();
            rig.ActivateAndStartP06();
            rig.DestroyAll(rig.Gimmick.TrackedCastId);
            Assert.AreEqual(EMucheolChargeGimmickState.AwaitingFinalRecovery, rig.Gimmick.State);

            rig.Control.Pending = true;
            rig.Tick(rig.Now + STEP);
            Assert.AreEqual(EBossAttackState.Ready, rig.Brain.AttackState);

            double nextTick = rig.Now + STEP;
            rig.Tick(nextTick);

            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, rig.Gimmick.State);
            Assert.AreEqual(nextTick, rig.Gimmick.GroggyStartTime);
            Assert.AreEqual(EBossAttackState.Ready, rig.Brain.AttackState, "그로기 시작 틱에도 새 공격은 없다");
        }

        /****************************************
        *           Coarse And Repeated Ticks
        ****************************************/

        // 큰 틱 하나로 시전 전체를 넘겨도 그로기 시작은 틱 시각이 아니라 회복 종료 경계다
        [Test]
        public void CoarseTick_GroggyStartsAtBoundaryNotAtTickTime()
        {
            Rig rig = new Rig();
            rig.SettleFirstNormalCast();
            rig.ActivateAndStartP06();
            double castStart = rig.Now;
            rig.DestroyAll(rig.Gimmick.TrackedCastId);

            rig.Tick(castStart + 100.0);

            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, rig.Gimmick.State);
            Assert.AreEqual(castStart + P06_TOTAL, rig.Gimmick.GroggyStartTime, 0.0001);

            // 다음 큰 틱은 그로기 끝 경계를 넘겼으므로 같은 틱에 끝난다
            rig.Tick(castStart + 200.0);
            Assert.AreEqual(EMucheolChargeGimmickState.Completed, rig.Gimmick.State);
        }

        // 같은 시각에 틱을 반복해도 진행도, 시전 수, 알림 횟수가 늘지 않는다
        [Test]
        public void RepeatedTicksAtTheSameTime_ChangeNothing()
        {
            Rig rig = new Rig();
            rig.SettleFirstNormalCast();
            rig.ActivateAndStartP06();
            Assert.IsTrue(rig.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, rig.Now, rig.Gimmick.TrackedCastId));

            int executed = rig.Body.Executed.Count;
            int finished = rig.Body.Finished.Count;
            int tracked  = rig.Gimmick.TrackedCastId;
            double now   = rig.Now;

            for (int i = 0; i < 5; ++i)
            {
                rig.Brain.Tick(now);
            }

            Assert.AreEqual(executed, rig.Body.Executed.Count);
            Assert.AreEqual(finished, rig.Body.Finished.Count);
            Assert.AreEqual(tracked, rig.Gimmick.TrackedCastId);
            Assert.AreEqual(1, rig.Gimmick.DestroyedCount);
            Assert.AreEqual(0, rig.Gimmick.FailedStartCount);
        }
    }
}
