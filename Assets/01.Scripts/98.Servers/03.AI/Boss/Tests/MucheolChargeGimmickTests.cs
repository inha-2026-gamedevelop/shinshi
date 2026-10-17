// System
using System;
using System.Collections.Generic;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

using BossDefinition    = Shinshi.Data.BossPatternCatalogSO.BossDefinition;
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 무철 기믹 1 "돌진 유도"의 AI 상태 관리와 P06 강제 선택을 가짜 체력, 카탈로그, 구조물 파괴 확인 입력으로 확인한다.
    // 발동 비율, 구조물 수, 그로기 시간, 시전 시간은 모두 이 테스트가 정한 시작값이며 운영 데이터가 아니다.
    //
    // 세 묶음으로 나뉜다.
    //  - 기믹 단위: 상태 관리 자체
    //  - 제안 연결 하네스: 공용 BossCombat에 아직 없는 세 연결(틱 갱신, 전환 보류, 선택 차단)이 있다고 가정하고 실제 선택 BT, 페이즈 FSM,
    //    공격 FSM을 같은 순서로 돌린다. 실제 BossCombat이 아니며, 연결 승인과 적용 뒤 같은 시나리오를 실제 BossCombat으로 돌려 통과해야 한다
    //  - 실제 BossBrain: 지금 있는 연결(선택 규칙)만으로 되는 것과 안 되는 것
    public class MucheolChargeGimmickTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  TRIGGER_RATE = 0.70f;
        private const int    REQUIRED     = 3;
        private const float  GROGGY       = 3f;
        private const float  PHASE_RATE   = 0.55f;

        private const int    STRUCTURE_A  = 101;
        private const int    STRUCTURE_B  = 102;
        private const int    STRUCTURE_C  = 103;
        private const int    UNREGISTERED = 999;

        // 시전 시간(초), 일반 패턴은 합계 2.0, P06은 합계 3.0이다
        private const float  NORMAL_TELEGRAPH = 1f;
        private const float  NORMAL_ACTIVE    = 0.5f;
        private const float  NORMAL_RECOVERY  = 0.5f;
        private const float  P06_TELEGRAPH    = 1f;
        private const float  P06_ACTIVE       = 0.5f;
        private const float  P06_RECOVERY     = 1.5f;

        private const string NORMAL_ID = "test_normal";
        private const double STEP      = 0.05;
        private const double MARGIN    = 0.001;

        /****************************************
        *               Helpers
        ****************************************/

        private static PatternDefinition MakeNormal()
        {
            return BossTestFixtures.MakePattern(NORMAL_ID, 10f, NORMAL_TELEGRAPH, NORMAL_ACTIVE, NORMAL_RECOVERY);
        }

        private static PatternDefinition MakeP06()
        {
            return BossTestFixtures.MakePattern(MucheolPatternFactory.P06_ID, 10f, P06_TELEGRAPH, P06_ACTIVE, P06_RECOVERY);
        }

        // FSM이 단계 경계를 만드는 식과 같다(앞 경계에 double로 더함)
        private static double P06FinishBoundary(double startTime)
        {
            return ((startTime + P06_TELEGRAPH) + P06_ACTIVE) + P06_RECOVERY;
        }

        // 실제 P06 패턴처럼 시전이 시작될 때와 취소될 때 시전 추적 수신자에게 알리는 가짜 패턴. 시작 알림은 패턴 시작 입력의 시전 ID를 싣는다
        private sealed class TrackedPattern : BossPatternBase
        {
            public IBossChargeCastTracker Tracker;

            protected override void OnBegin(double serverTime)
            {
                Tracker?.OnChargeCastStarted(Context.CastInstanceId);
            }

            protected override void OnCancelled(EBossInterruptReason reason)
            {
                Tracker?.OnChargeCastCancelled(Context.CastInstanceId, reason);
            }
        }

        private sealed class RecordingFactory : IBossPatternFactory, IBossPatternAvailability, IBossChargeCastTrackerSource
        {
            public bool                     CanCreateAll = true;
            public bool                     CreateReturnsNull;
            public IBossChargeCastTracker   Tracker;
            public List<string>             CreatedIds   = new List<string>();
            public List<BossPatternBase>    Created      = new List<BossPatternBase>();

            public IBossChargeCastTracker ChargeCastTracker => Tracker;

            public bool CanCreate(PatternDefinition definition)
            {
                return CanCreateAll;
            }

            // CreateReturnsNull은 선택 뒤 패턴 생성이 실패하는 경우를 모사한다
            public IBossPattern Create(PatternDefinition definition)
            {
                if (CreateReturnsNull)
                {
                    return null;
                }

                BossPatternBase pattern = (definition.DefinitionId == MucheolPatternFactory.P06_ID)
                    ? new TrackedPattern { Tracker = Tracker }
                    : (BossPatternBase)new ScheduledHitPattern();
                CreatedIds.Add(definition.DefinitionId);
                Created.Add(pattern);
                return pattern;
            }
        }

        // 그로기 알림을 온 순서대로 적는다
        private sealed class GroggyLog : IMucheolChargeGimmickListener
        {
            public List<string> Order     { get; } = new List<string>();
            public int          CastId    { get; private set; }
            public int          Structure { get; private set; }
            public double       Confirm   { get; private set; }
            public double       Start     { get; private set; }
            public double       End       { get; private set; }

            public void OnGroggyPending(int castInstanceId, int structureId, double confirmServerTime)
            {
                Order.Add("pending");
                CastId    = castInstanceId;
                Structure = structureId;
                Confirm   = confirmServerTime;
            }

            public void OnGroggyStarted(int castInstanceId, int structureId, double startServerTime, double endServerTime)
            {
                Order.Add("started");
                CastId    = castInstanceId;
                Structure = structureId;
                Start     = startServerTime;
                End       = endServerTime;
            }

            public void OnGroggyCancelled(int castInstanceId)
            {
                Order.Add("cancelled");
                CastId = castInstanceId;
            }
        }

        private sealed class Env
        {
            public GroggyLog        Groggy  { get; } = new GroggyLog();
            public FakeBossVitals   Vitals  { get; } = new FakeBossVitals();
            public FakeBossCatalog  Catalog { get; } = new FakeBossCatalog();
            public RecordingFactory Factory { get; } = new RecordingFactory();
            public MucheolChargeGimmick Gimmick { get; }

            public Env(bool withP06 = true, bool designate = true)
            {
                Catalog.Boss = BossTestFixtures.MakeBoss(PHASE_RATE);
                Catalog.Patterns.Add(MakeNormal());
                if (withP06)
                {
                    Catalog.Patterns.Add(MakeP06());
                }

                Gimmick = new MucheolChargeGimmick(Vitals, Catalog, Factory, BossTestFixtures.BOSS_ID,
                    new MucheolChargeGimmickSpec(TRIGGER_RATE, REQUIRED, GROGGY), Groggy);
                Factory.Tracker = Gimmick;

                if (designate)
                {
                    Assert.IsTrue(Gimmick.SetDesignatedStructures(new[] { STRUCTURE_A, STRUCTURE_B, STRUCTURE_C }));
                }
            }

            // 체력을 발동 기준 이하로 두고 틱해 기믹을 Active로 만든다
            public void Activate(double serverTime = 0.0)
            {
                Vitals.HealthRate = TRIGGER_RATE;
                Gimmick.Tick(serverTime, 1);
                Assert.AreEqual(EMucheolChargeGimmickState.Active, Gimmick.State);
            }

            // 기믹이 P06을 고르고 그 시전이 실제로 시작된 것으로 만든다. 실제로는 패턴이 시작되며 시전 ID와 함께 알린다
            public void SelectChargeCast(int castInstanceId = 1)
            {
                Assert.IsTrue(Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition pattern));
                Assert.AreEqual(MucheolPatternFactory.P06_ID, pattern.DefinitionId);
                Gimmick.OnChargeCastStarted(castInstanceId);
                Assert.AreEqual(castInstanceId, Gimmick.TrackedCastId);
            }
        }

        /****************************************
        *        Trigger And Availability
        ****************************************/

        [Test]
        public void Tick_HealthAboveTrigger_StaysDormantAndDoesNotInterfere()
        {
            Env env = new Env();
            env.Vitals.HealthRate = TRIGGER_RATE + 0.001f;

            env.Gimmick.Tick(0.0, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Dormant, env.Gimmick.State);
            Assert.IsFalse(env.Gimmick.DefersPhaseTransition);
            Assert.IsFalse(env.Gimmick.BlocksSelection);
            Assert.IsFalse(env.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition pattern));
            Assert.IsNull(pattern);
        }

        // 발동 기준과 같은 체력은 발동한다(경계 포함)
        [Test]
        public void Tick_HealthExactlyAtTrigger_Activates()
        {
            Env env = new Env();
            env.Vitals.HealthRate = TRIGGER_RATE;

            env.Gimmick.Tick(0.0, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);
            Assert.IsTrue(env.Gimmick.DefersPhaseTransition);
            Assert.IsFalse(env.Gimmick.BlocksSelection);
        }

        // 한 번의 피해로 발동 기준과 2페이즈 기준을 함께 넘어도 같은 틱에 발동한다
        [Test]
        public void Tick_SingleDamageBelowBothThresholds_ActivatesInThatTick()
        {
            Env env = new Env();
            env.Vitals.HealthRate = 0.80f;
            env.Gimmick.Tick(0.0, 1);
            Assert.AreEqual(EMucheolChargeGimmickState.Dormant, env.Gimmick.State);

            env.Vitals.HealthRate = PHASE_RATE - 0.05f;
            env.Gimmick.Tick(0.05, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);
            Assert.IsTrue(env.Gimmick.DefersPhaseTransition);
        }

        [Test]
        public void Tick_AfterCompletion_NeverActivatesAgain()
        {
            Env env = new Env();
            env.Activate();
            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 1.0));
            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 1.0));
            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 1.0));
            env.Gimmick.Tick(env.Gimmick.GroggyEndTime, 1);
            Assert.AreEqual(EMucheolChargeGimmickState.Completed, env.Gimmick.State);

            env.Vitals.HealthRate = 1f;
            env.Gimmick.Tick(100.0, 1);
            env.Vitals.HealthRate = 0.3f;
            env.Gimmick.Tick(101.0, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Completed, env.Gimmick.State);
            Assert.IsFalse(env.Gimmick.DefersPhaseTransition);
            Assert.IsFalse(env.Gimmick.BlocksSelection);
            Assert.IsFalse(env.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition _));
        }

        [Test]
        public void Tick_SecondPhase_NeverActivates()
        {
            Env env = new Env();
            env.Vitals.HealthRate = 0.40f;

            env.Gimmick.Tick(0.0, 2);

            Assert.AreEqual(EMucheolChargeGimmickState.Dormant, env.Gimmick.State);
        }

        // 운영 카탈로그에 P06 정의가 없으면 체력이 기준 이하여도 발동하지 않는다
        [Test]
        public void Tick_NoP06DefinitionInCatalog_StaysDormant()
        {
            Env env = new Env(withP06: false);
            env.Vitals.HealthRate = 0.10f;

            env.Gimmick.Tick(0.0, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Dormant, env.Gimmick.State);
            Assert.IsFalse(env.Gimmick.DefersPhaseTransition);
        }

        [Test]
        public void Tick_FactoryCannotCreateP06_StaysDormantUntilItCan()
        {
            Env env = new Env();
            env.Factory.CanCreateAll = false;
            env.Vitals.HealthRate = 0.50f;

            env.Gimmick.Tick(0.0, 1);
            Assert.AreEqual(EMucheolChargeGimmickState.Dormant, env.Gimmick.State);

            env.Factory.CanCreateAll = true;
            env.Gimmick.Tick(0.05, 1);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);
        }

        [Test]
        public void Tick_FewerDesignatedStructuresThanRequired_StaysDormant()
        {
            Env env = new Env(designate: false);
            Assert.IsTrue(env.Gimmick.SetDesignatedStructures(new[] { STRUCTURE_A, STRUCTURE_B }));
            env.Vitals.HealthRate = 0.50f;

            env.Gimmick.Tick(0.0, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Dormant, env.Gimmick.State);
        }

        [Test]
        public void SetDesignatedStructures_InvalidIdsOrAfterActivation_KeepsPreviousSet()
        {
            Env env = new Env(designate: false);
            Assert.IsTrue(env.Gimmick.SetDesignatedStructures(new[] { STRUCTURE_A, STRUCTURE_B, STRUCTURE_C }));

            Assert.IsFalse(env.Gimmick.SetDesignatedStructures(new[] { STRUCTURE_A, STRUCTURE_A }));
            Assert.IsFalse(env.Gimmick.SetDesignatedStructures(new[] { STRUCTURE_A, 0 }));
            Assert.IsFalse(env.Gimmick.SetDesignatedStructures(new[] { STRUCTURE_A, -5 }));
            Assert.IsFalse(env.Gimmick.SetDesignatedStructures(null));

            env.Activate();
            Assert.IsFalse(env.Gimmick.SetDesignatedStructures(new[] { 201, 202, 203 }));
            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 1.0));
            Assert.IsFalse(env.Gimmick.ConfirmStructureDestroyed(201, 1.0));
        }

        /****************************************
        *            Forced Selection
        ****************************************/

        // 일반 패턴이 카탈로그에 있고 가중치가 같아도 기믹 중에는 P06만 고른다
        [Test]
        public void TrySelect_WhenActive_ReturnsChargePatternIgnoringWeights()
        {
            Env env = new Env();
            env.Activate();

            bool selected = env.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition pattern);

            Assert.IsTrue(selected);
            Assert.AreEqual(MucheolPatternFactory.P06_ID, pattern.DefinitionId);

            // 고른 것만으로는 시전이 시작된 것이 아니다, 선택 대기일 뿐이다
            Assert.IsTrue(env.Gimmick.IsSelectionPending);
            Assert.IsFalse(env.Gimmick.IsCastInFlight);
        }

        [Test]
        public void TrySelect_ChargePatternBecomesUnavailable_StopsGimmickAndFallsBackToNormalSelection()
        {
            Env env = new Env();
            env.Activate();
            env.Factory.CanCreateAll = false;

            bool selected = env.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition pattern);

            Assert.IsFalse(selected);
            Assert.IsNull(pattern);
            Assert.AreEqual(EMucheolChargeGimmickState.Stopped, env.Gimmick.State);
            Assert.IsFalse(env.Gimmick.DefersPhaseTransition);
            Assert.IsFalse(env.Gimmick.BlocksSelection);
        }

        // 벽 충돌처럼 P06이 구조물을 부수지 못한 시전이 끝나도 진행도는 그대로이고 같은 규칙으로 P06을 다시 고른다
        [Test]
        public void OnCastFinished_WithoutConfirmedDestruction_RepeatsChargeWithoutProgress()
        {
            Env env = new Env();
            env.Activate();

            for (int i = 0; i < 6; ++i)
            {
                env.SelectChargeCast(i + 1);
                env.Gimmick.OnCastFinished(i + 1, 10.0 + i);

                Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);
                Assert.AreEqual(0, env.Gimmick.DestroyedCount);
                Assert.IsFalse(env.Gimmick.IsCastInFlight);
                Assert.IsFalse(env.Gimmick.BlocksSelection);
            }
        }

        // P06의 충돌 결과 알림은 받지 않는다, 충돌이 파괴를 뜻하지 않으므로 진행도의 입력이 될 수 없다
        [Test]
        public void Gimmick_DoesNotImplementChargeOutcomeListener()
        {
            Env env = new Env();

            Assert.IsFalse(env.Gimmick is IBossChargeOutcomeListener);
        }

        /****************************************
        *            Structure Progress
        ****************************************/

        [Test]
        public void ConfirmStructureDestroyed_DistinctRegisteredIds_AdvanceProgress()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast();

            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0));
            Assert.AreEqual(1, env.Gimmick.DestroyedCount);
            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.0));
            Assert.AreEqual(2, env.Gimmick.DestroyedCount);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);
        }

        [Test]
        public void ConfirmStructureDestroyed_DuplicateUnregisteredOrInvalidIds_DoNotAdvanceProgress()
        {
            Env env = new Env();
            env.Activate();
            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0));

            Assert.IsFalse(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.1));
            Assert.IsFalse(env.Gimmick.ConfirmStructureDestroyed(UNREGISTERED, 2.2));
            Assert.IsFalse(env.Gimmick.ConfirmStructureDestroyed(0, 2.3));
            Assert.IsFalse(env.Gimmick.ConfirmStructureDestroyed(-1, 2.4));

            Assert.AreEqual(1, env.Gimmick.DestroyedCount);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);
        }

        [Test]
        public void ConfirmStructureDestroyed_BeforeActivation_IsIgnored()
        {
            Env env = new Env();

            Assert.IsFalse(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 0.0));
            Assert.AreEqual(0, env.Gimmick.DestroyedCount);
        }

        // 같은 구조물이 두 번 확인돼도 세 개가 되지 않는다, 서로 다른 세 개여야 반복이 끝난다
        [Test]
        public void ConfirmStructureDestroyed_SameStructureRepeated_NeverCompletesRepeat()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast();

            for (int i = 0; i < 5; ++i)
            {
                env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0 + i);
            }

            Assert.AreEqual(1, env.Gimmick.DestroyedCount);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);
            Assert.IsFalse(env.Gimmick.BlocksSelection);
        }

        /****************************************
        *        Final Recovery And Groggy
        ****************************************/

        // 마지막 구조물이 시전 중에 부서지면 그 시전의 회복이 끝난 경계에서 그로기가 시작한다. 그 전에는 선택만 막는다
        [Test]
        public void OnCastFinished_AfterLastDestructionDuringCast_StartsGroggyAtThatBoundary()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast();
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.1);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 2.2);

            Assert.AreEqual(EMucheolChargeGimmickState.AwaitingFinalRecovery, env.Gimmick.State);
            Assert.IsTrue(env.Gimmick.BlocksSelection);
            Assert.IsTrue(env.Gimmick.DefersPhaseTransition);

            double boundary = 10.0;
            env.Gimmick.OnCastFinished(1, boundary);

            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
            Assert.AreEqual(boundary, env.Gimmick.GroggyStartTime);
            Assert.AreEqual(boundary + GROGGY, env.Gimmick.GroggyEndTime);
            Assert.IsTrue(env.Gimmick.BlocksSelection);
            Assert.IsTrue(env.Gimmick.DefersPhaseTransition);
        }

        // 틱 시각이 아니라 알림이 싣고 온 경계 시각이 그로기 시작이다, 틱이 한참 늦어도 같다
        [Test]
        public void Groggy_StartsAtBoundaryNotAtLaterTickTime()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast();
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 2.0);
            env.Gimmick.OnCastFinished(1, 10.0);

            env.Gimmick.Tick(11.0, 1);

            Assert.AreEqual(10.0, env.Gimmick.GroggyStartTime);
            Assert.AreEqual(13.0, env.Gimmick.GroggyEndTime);
            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
        }

        // 기믹이 고른 시전이 끝나도 아직 구조물이 모자라면 그로기가 아니다. 다른 시전의 종료 알림은 무시한다
        [Test]
        public void OnCastFinished_BeforeLastDestruction_OrForeignCast_DoesNotStartGroggy()
        {
            Env env = new Env();
            env.Activate();
            env.Gimmick.OnCastFinished(1, 5.0);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);

            env.SelectChargeCast();
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 6.0);
            env.Gimmick.OnCastFinished(2, 8.0);

            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);
            Assert.AreEqual(1, env.Gimmick.DestroyedCount);
        }

        // 마지막 파괴가 시전 사이에 확인되면 시전 중이 아니므로 확인 시각부터 그로기다
        [Test]
        public void ConfirmStructureDestroyed_LastOneBetweenCasts_StartsGroggyAtConfirmationTime()
        {
            Env env = new Env();
            env.Activate();
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 4.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 4.5);

            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 5.0));

            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
            Assert.AreEqual(5.0, env.Gimmick.GroggyStartTime);
            Assert.AreEqual(5.0 + GROGGY, env.Gimmick.GroggyEndTime);
            Assert.IsTrue(env.Gimmick.BlocksSelection);
        }

        // 그로기는 끝 경계 직전까지 선택을 막고 끝 경계 시각부터 끝난다
        [Test]
        public void Tick_GroggyEndBoundary_BlocksUntilEndAndNotAtEnd()
        {
            Env env = new Env();
            env.Activate();
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 4.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 4.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 4.0);
            double end = env.Gimmick.GroggyEndTime;

            env.Gimmick.Tick(end - MARGIN, 1);
            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
            Assert.IsTrue(env.Gimmick.BlocksSelection);
            Assert.IsTrue(env.Gimmick.DefersPhaseTransition);

            env.Gimmick.Tick(end, 1);
            Assert.AreEqual(EMucheolChargeGimmickState.Completed, env.Gimmick.State);
            Assert.IsFalse(env.Gimmick.BlocksSelection);
            Assert.IsFalse(env.Gimmick.DefersPhaseTransition);
        }

        [Test]
        public void Tick_HugeStepAcrossGroggy_CompletesInThatTick()
        {
            Env env = new Env();
            env.Activate();
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 4.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 4.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 4.0);

            env.Gimmick.Tick(10000.0, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Completed, env.Gimmick.State);
            Assert.IsFalse(env.Gimmick.BlocksSelection);
            Assert.IsFalse(env.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition _));
        }

        /****************************************
        *        Cast Tracking Boundaries
        ****************************************/

        // 고른 것만으로 시전이 시작된 것으로 보지 않는다. 시작 알림이 없으면 마지막 파괴가 와도 시전 종료를 기다리지 않는다
        [Test]
        public void TrySelect_WithoutStartNotification_DoesNotMakeFinalRecoveryWait()
        {
            Env env = new Env();
            env.Activate();
            Assert.IsTrue(env.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition _));

            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 6.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 6.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 6.5);

            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
            Assert.AreEqual(6.5, env.Gimmick.GroggyStartTime);
            Assert.IsFalse(env.Gimmick.IsCastInFlight);
        }

        // 선택 뒤 패턴 생성이나 시작이 실패해 시작 알림이 오지 않으면 다음 틱에서 선택 대기를 거두고 Active를 유지한다
        [Test]
        public void Tick_SelectionNeverStarted_ClearsPendingAndStaysActive()
        {
            Env env = new Env();
            env.Activate();
            Assert.IsTrue(env.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition _));
            Assert.IsTrue(env.Gimmick.IsSelectionPending);

            env.Gimmick.Tick(0.05, 1);

            Assert.IsFalse(env.Gimmick.IsSelectionPending);
            Assert.IsFalse(env.Gimmick.IsCastInFlight);
            Assert.AreEqual(1, env.Gimmick.FailedStartCount);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);
            Assert.IsFalse(env.Gimmick.BlocksSelection);

            // 다음 선택이 P06을 다시 고르고 이번에는 시작된다
            env.SelectChargeCast(2);
            env.Gimmick.Tick(0.1, 1);
            Assert.AreEqual(1, env.Gimmick.FailedStartCount);
            Assert.AreEqual(2, env.Gimmick.TrackedCastId);
        }

        // 시작에 실패한 선택 뒤에 마지막 파괴가 와도 AwaitingFinalRecovery에 갇히지 않는다
        [Test]
        public void Tick_AfterFailedStart_LastDestructionDoesNotDeadlock()
        {
            Env env = new Env();
            env.Activate();
            Assert.IsTrue(env.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition _));
            env.Gimmick.Tick(0.05, 1);

            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 1.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 1.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 1.0);

            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
            env.Gimmick.Tick(env.Gimmick.GroggyEndTime, 1);
            Assert.AreEqual(EMucheolChargeGimmickState.Completed, env.Gimmick.State);
        }

        // 선택 뒤 실제로 패턴 생성이 실패하는 공용 흐름: 시작 알림이 없고 다음 틱에 실패로 거둔다
        [Test]
        public void Harness_PatternCreationFails_GimmickRecoversAndRetries()
        {
            Env env = new Env();
            ProposedCombat combat = StartWithNormalCastThenDamage(env, TRIGGER_RATE, out double damageTime);
            combat.TickRange(damageTime, 1.95);
            env.Factory.CreateReturnsNull = true;

            combat.TickRange(1.95, 2.3);

            Assert.GreaterOrEqual(env.Gimmick.FailedStartCount, 1);
            Assert.IsFalse(env.Gimmick.IsCastInFlight);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);

            env.Factory.CreateReturnsNull = false;
            combat.TickRange(2.3, 2.6);
            Assert.IsTrue(env.Gimmick.IsCastInFlight);
        }

        [Test]
        public void OnChargeCastStarted_WithoutSelectionOrOutsideActive_IsIgnored()
        {
            Env dormant = new Env();
            dormant.Gimmick.OnChargeCastStarted(5);
            Assert.IsFalse(dormant.Gimmick.IsCastInFlight);

            Env active = new Env();
            active.Activate();
            active.Gimmick.OnChargeCastStarted(5);
            Assert.IsFalse(active.Gimmick.IsCastInFlight);
            Assert.AreEqual(0, active.Gimmick.TrackedCastId);
        }

        // 종료 알림을 받지 못한 채 새 시전이 시작되면 새 시전을 추적한다
        [Test]
        public void OnChargeCastStarted_WhileAnotherCastTracked_ReplacesIt()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);

            env.SelectChargeCast(8);

            Assert.AreEqual(8, env.Gimmick.TrackedCastId);
        }

        // 추적 중인 시전과 다른 시전 ID의 종료는 무시한다. 마지막 파괴를 기다리는 상태도 그대로다
        [Test]
        public void OnCastFinished_WithDifferentCastId_IsIgnoredWhileWaitingForFinalRecovery()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.0, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 2.0, 7);
            Assert.AreEqual(EMucheolChargeGimmickState.AwaitingFinalRecovery, env.Gimmick.State);

            env.Gimmick.OnCastFinished(6, 9.0);
            env.Gimmick.OnCastFinished(8, 9.5);
            env.Gimmick.OnCastFinished(0, 9.7);

            Assert.AreEqual(EMucheolChargeGimmickState.AwaitingFinalRecovery, env.Gimmick.State);
            Assert.AreEqual(7, env.Gimmick.TrackedCastId);
            Assert.IsTrue(env.Gimmick.BlocksSelection);

            env.Gimmick.OnCastFinished(7, 10.0);
            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
            Assert.AreEqual(10.0, env.Gimmick.GroggyStartTime);
        }

        [Test]
        public void OnCastFinished_WhenNothingTracked_IsIgnored()
        {
            Env env = new Env();
            env.Activate();

            env.Gimmick.OnCastFinished(3, 5.0);

            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);
            Assert.IsFalse(env.Gimmick.IsCastInFlight);
        }

        // 마지막 파괴를 기다리던 시전이 정상 종료 없이 취소되면 종료 경계가 없으므로 다음 틱 시각부터 그로기다. 영구히 기다리지 않는다
        [Test]
        public void OnChargeCastCancelled_WhileWaitingForFinalRecovery_StartsGroggyOnNextTick()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.0, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 2.0, 7);

            env.Gimmick.OnChargeCastCancelled(7, EBossInterruptReason.DirectControl);

            Assert.IsFalse(env.Gimmick.IsCastInFlight);
            Assert.AreEqual(EMucheolChargeGimmickState.AwaitingFinalRecovery, env.Gimmick.State);

            env.Gimmick.Tick(12.0, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
            Assert.AreEqual(12.0, env.Gimmick.GroggyStartTime);
            Assert.AreEqual(12.0 + GROGGY, env.Gimmick.GroggyEndTime);
        }

        // 구조물 파괴 전의 취소는 추적만 거두고 반복을 이어 간다
        [Test]
        public void OnChargeCastCancelled_BeforeFinalDestruction_ClearsTrackingAndKeepsRepeating()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0, 7);

            env.Gimmick.OnChargeCastCancelled(7, EBossInterruptReason.DirectControl);

            Assert.IsFalse(env.Gimmick.IsCastInFlight);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);
            Assert.AreEqual(1, env.Gimmick.DestroyedCount);
            env.SelectChargeCast(8);
            Assert.AreEqual(8, env.Gimmick.TrackedCastId);
        }

        [Test]
        public void OnChargeCastCancelled_WithDifferentCastId_IsIgnored()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);

            env.Gimmick.OnChargeCastCancelled(6, EBossInterruptReason.PhaseTransition);

            Assert.AreEqual(7, env.Gimmick.TrackedCastId);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);
        }

        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        public void OnChargeCastCancelled_ByDeathOrCombatEnd_StopsGimmick(EBossInterruptReason reason)
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);

            env.Gimmick.OnChargeCastCancelled(7, reason);

            Assert.AreEqual(EMucheolChargeGimmickState.Stopped, env.Gimmick.State);
            Assert.IsFalse(env.Gimmick.BlocksSelection);
            Assert.IsFalse(env.Gimmick.DefersPhaseTransition);
        }

        // 공용 흐름에서 마지막 파괴를 기다리던 시전이 취소돼도 그로기가 시작되고 선택 차단이 풀린다
        [Test]
        public void Harness_FinalCastCancelled_GroggyStillStartsAndEnds()
        {
            Env env = new Env();
            ProposedCombat combat = StartWithNormalCastThenDamage(env, TRIGGER_RATE, out double damageTime);
            combat.TickRange(damageTime, 2.1);
            double p06Start = combat.StartTimes[1];
            combat.TickRange(2.1, p06Start + 1.2);
            ConfirmAllStructures(env, p06Start + 1.2);
            Assert.AreEqual(EMucheolChargeGimmickState.AwaitingFinalRecovery, env.Gimmick.State);

            // 시전을 취소하는 공용 경로를 모사한다(직접 제어 등으로 정상 종료 없이 끊김)
            env.Factory.Created[1].Cancel(EBossInterruptReason.DirectControl);
            double cancelTick = p06Start + 1.25;
            combat.Tick(cancelTick);

            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
            Assert.AreEqual(cancelTick, env.Gimmick.GroggyStartTime);
            combat.Tick(env.Gimmick.GroggyEndTime);
            Assert.AreEqual(EMucheolChargeGimmickState.Completed, env.Gimmick.State);
            Assert.IsFalse(env.Gimmick.BlocksSelection);
        }

        /****************************************
        *          Late Destruction Confirmation
        ****************************************/

        // 파괴를 낸 시전이 이미 정상 종료한 뒤 확인이 늦게 와도 시전 ID가 그 시전이고 새 시전이 아직 없으면 그 시전의 종료 경계를 보존한다
        [Test]
        public void ConfirmStructureDestroyed_LateForJustFinishedCast_PreservesItsRecoveryBoundary()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.5, 7);
            env.Gimmick.OnCastFinished(7, 10.0);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);

            // 종료 경계 0.4초 뒤에 마지막 파괴 확인이 늦게 도착했다
            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 10.4, 7));

            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
            Assert.AreEqual(10.0, env.Gimmick.GroggyStartTime);
            Assert.AreEqual(10.0 + GROGGY, env.Gimmick.GroggyEndTime);
        }

        // 이미 다음 P06이 시작된 뒤에 늦게 오면 과거로 돌려 적용할 수 없다. 진행도는 오르고 그로기는 지금 진행 중인 시전의 종료 경계를 기다린다
        [Test]
        public void ConfirmStructureDestroyed_LateAfterNextCastStarted_WaitsForTheNewCastBoundary()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.5, 7);
            env.Gimmick.OnCastFinished(7, 10.0);
            env.SelectChargeCast(8);

            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 10.6, 7));

            Assert.AreEqual(3, env.Gimmick.DestroyedCount);
            Assert.AreEqual(EMucheolChargeGimmickState.AwaitingFinalRecovery, env.Gimmick.State);

            env.Gimmick.OnCastFinished(8, 13.0);
            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
            Assert.AreEqual(13.0, env.Gimmick.GroggyStartTime);
        }

        // 시전 ID를 모르거나 알려진 시전이 아니면 시전 사이로 보고 확인 시각부터 그로기다
        [TestCase(0)]
        [TestCase(99)]
        public void ConfirmStructureDestroyed_UnknownCastBetweenCasts_StartsGroggyAtConfirmationTime(int castInstanceId)
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);
            env.Gimmick.OnCastFinished(7, 10.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 10.1);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 10.2);

            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 10.4, castInstanceId));

            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
            Assert.AreEqual(10.4, env.Gimmick.GroggyStartTime);
        }

        // 파괴를 낸 시전이 진행 중이면 시전 ID를 알려도 알리지 않아도 그 시전의 종료 경계를 기다린다
        [TestCase(7)]
        [TestCase(0)]
        public void ConfirmStructureDestroyed_DuringCast_WaitsForThatCastBoundary(int castInstanceId)
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.0);

            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 2.5, castInstanceId);

            Assert.AreEqual(EMucheolChargeGimmickState.AwaitingFinalRecovery, env.Gimmick.State);
            env.Gimmick.OnCastFinished(7, 11.0);
            Assert.AreEqual(11.0, env.Gimmick.GroggyStartTime);
        }

        /****************************************
        *       No Retroactive Groggy
        ****************************************/

        // P06 7이 끝난 뒤 P06 8이 시작됐다가 취소되고 나서 7의 마지막 파괴 확인이 늦게 오면, 7의 종료 시각으로 그로기를 소급하지 않는다.
        // 그 사이 보스가 8로 다시 돌진했으므로 7의 종료 시각부터 그로기였다고 할 수 없다. 확인 시각부터 시작한다
        [Test]
        public void ConfirmStructureDestroyed_LateForCastBeforeCancelledNextCast_DoesNotBackdateToOldBoundary()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.5, 7);
            env.Gimmick.OnCastFinished(7, 10.0);
            env.SelectChargeCast(8);
            env.Gimmick.OnChargeCastCancelled(8, EBossInterruptReason.DirectControl);

            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 10.6, 7));

            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
            Assert.AreEqual(10.6, env.Gimmick.GroggyStartTime);
            Assert.AreEqual(10.6 + GROGGY, env.Gimmick.GroggyEndTime);
            Assert.AreNotEqual(10.0, env.Gimmick.GroggyStartTime);
        }

        // 8이 시작만 하고 아직 진행 중일 때는 8의 종료 경계를 기다린다(이미 있는 동작), 7의 종료 시각으로 소급하지 않는다
        [Test]
        public void ConfirmStructureDestroyed_LateForCastBeforeRunningNextCast_WaitsForNextCastBoundaryNotOldOne()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.5, 7);
            env.Gimmick.OnCastFinished(7, 10.0);
            env.SelectChargeCast(8);

            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 10.6, 7));
            Assert.AreEqual(EMucheolChargeGimmickState.AwaitingFinalRecovery, env.Gimmick.State);

            env.Gimmick.OnCastFinished(8, 13.0);

            Assert.AreEqual(13.0, env.Gimmick.GroggyStartTime);
        }

        // 8이 시작해서 정상 종료한 뒤에 7의 늦은 확인이 오면, 7의 경계도 8의 경계도 아니라 확인 시각부터다.
        // 8을 가리키는 늦은 확인은 8의 종료 경계를 보존한다
        [Test]
        public void ConfirmStructureDestroyed_LateAfterNextCastAlsoFinished_UsesOnlyMatchingCastBoundary()
        {
            Env forOldCast = new Env();
            forOldCast.Activate();
            forOldCast.SelectChargeCast(7);
            forOldCast.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0, 7);
            forOldCast.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.5, 7);
            forOldCast.Gimmick.OnCastFinished(7, 10.0);
            forOldCast.SelectChargeCast(8);
            forOldCast.Gimmick.OnCastFinished(8, 13.0);

            Assert.IsTrue(forOldCast.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 13.4, 7));
            Assert.AreEqual(13.4, forOldCast.Gimmick.GroggyStartTime);

            Env forNewCast = new Env();
            forNewCast.Activate();
            forNewCast.SelectChargeCast(7);
            forNewCast.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0, 7);
            forNewCast.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.5, 7);
            forNewCast.Gimmick.OnCastFinished(7, 10.0);
            forNewCast.SelectChargeCast(8);
            forNewCast.Gimmick.OnCastFinished(8, 13.0);

            Assert.IsTrue(forNewCast.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 13.4, 8));
            Assert.AreEqual(13.0, forNewCast.Gimmick.GroggyStartTime);
        }

        // 시작하지 못한 선택(생성 실패)은 시전이 아니므로 직전 시전의 종료 경계 보존을 무효로 만들지 않는다
        [Test]
        public void ConfirmStructureDestroyed_LateAfterFailedStartOnly_StillPreservesPreviousCastBoundary()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.5, 7);
            env.Gimmick.OnCastFinished(7, 10.0);
            Assert.IsTrue(env.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition _));
            env.Gimmick.Tick(10.05, 1);
            Assert.AreEqual(1, env.Gimmick.FailedStartCount);

            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 10.1, 7));

            Assert.AreEqual(10.0, env.Gimmick.GroggyStartTime);
        }

        /****************************************
        *        Cast Tracker Wiring Check
        ****************************************/

        // 팩토리가 이 기믹을 시전 추적 수신자로 연결하지 않았으면 체력이 기준 이하여도 발동하지 않는다.
        // 연결 없이 발동하면 시작 알림이 없어 마지막 파괴가 확인되는 순간 회복이 끝나기 전에 그로기가 시작된다
        [Test]
        public void Tick_TrackerNotWired_StaysDormant()
        {
            Env env = new Env();
            env.Factory.Tracker = null;
            env.Vitals.HealthRate = 0.10f;

            env.Gimmick.Tick(0.0, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Dormant, env.Gimmick.State);
            Assert.IsFalse(env.Gimmick.DefersPhaseTransition);
            Assert.IsFalse(env.Gimmick.BlocksSelection);
        }

        // 다른 수신자가 연결돼 있어도 이 기믹이 아니면 시작 알림이 닿지 않으므로 발동하지 않는다
        [Test]
        public void Tick_TrackerWiredToSomeoneElse_StaysDormant()
        {
            Env env = new Env();
            env.Factory.Tracker = new SpyTracker();
            env.Vitals.HealthRate = 0.10f;

            env.Gimmick.Tick(0.0, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Dormant, env.Gimmick.State);
        }

        private sealed class OpaqueFactory : IBossPatternFactory, IBossPatternAvailability
        {
            public bool CanCreate(PatternDefinition definition)
            {
                return true;
            }

            public IBossPattern Create(PatternDefinition definition)
            {
                return new ScheduledHitPattern();
            }
        }

        // 연결 정보를 밝히지 않는 팩토리(래퍼가 계약을 전달하지 않은 경우)에서는 연결을 확인할 수 없어 발동하지 않는다
        [Test]
        public void Tick_FactoryDoesNotExposeTrackerSource_StaysDormant()
        {
            FakeBossVitals vitals = new FakeBossVitals { HealthRate = 0.10f };
            FakeBossCatalog catalog = new FakeBossCatalog { Boss = BossTestFixtures.MakeBoss(PHASE_RATE) };
            catalog.Patterns.Add(MakeNormal());
            catalog.Patterns.Add(MakeP06());
            MucheolChargeGimmick gimmick = new MucheolChargeGimmick(vitals, catalog, new OpaqueFactory(),
                BossTestFixtures.BOSS_ID, new MucheolChargeGimmickSpec(TRIGGER_RATE, REQUIRED, GROGGY));
            Assert.IsTrue(gimmick.SetDesignatedStructures(new[] { STRUCTURE_A, STRUCTURE_B, STRUCTURE_C }));

            gimmick.Tick(0.0, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Dormant, gimmick.State);
        }

        // 발동 전에 연결하면 그때 발동한다, 연결 전에는 체력이 낮아도 대기만 한다
        [Test]
        public void Tick_TrackerWiredLater_ActivatesOnlyAfterWiring()
        {
            Env env = new Env();
            env.Factory.Tracker = null;
            env.Vitals.HealthRate = 0.50f;
            env.Gimmick.Tick(0.0, 1);
            Assert.AreEqual(EMucheolChargeGimmickState.Dormant, env.Gimmick.State);

            env.Factory.Tracker = env.Gimmick;
            env.Gimmick.Tick(0.05, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);
        }

        // 진행 중에 연결이 풀리면 기믹을 멈추고 일반 흐름으로 돌아간다. 전환 보류와 선택 차단도 풀린다
        [Test]
        public void Tick_TrackerUnwiredWhileActive_StopsGimmick()
        {
            Env env = new Env();
            env.Activate();
            env.Factory.Tracker = null;

            env.Gimmick.Tick(0.05, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Stopped, env.Gimmick.State);
            Assert.IsFalse(env.Gimmick.DefersPhaseTransition);
            Assert.IsFalse(env.Gimmick.BlocksSelection);
            Assert.IsFalse(env.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition _));
        }

        [Test]
        public void Tick_TrackerUnwiredWhileWaitingForFinalRecovery_StopsInsteadOfStartingGroggyEarly()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.0, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 2.0, 7);
            Assert.AreEqual(EMucheolChargeGimmickState.AwaitingFinalRecovery, env.Gimmick.State);

            env.Factory.Tracker = null;
            env.Gimmick.Tick(2.5, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Stopped, env.Gimmick.State);
            Assert.IsFalse(env.Gimmick.BlocksSelection);
        }

        // 이미 시작한 그로기는 연결이 풀려도 정당하게 시작한 것이라 끝까지 간다
        [Test]
        public void Tick_TrackerUnwiredDuringGroggy_GroggyStillRunsToItsEnd()
        {
            Env env = new Env();
            env.Activate();
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 4.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 4.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 4.0);
            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);

            env.Factory.Tracker = null;
            env.Gimmick.Tick(5.0, 1);
            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);

            env.Gimmick.Tick(env.Gimmick.GroggyEndTime, 1);
            Assert.AreEqual(EMucheolChargeGimmickState.Completed, env.Gimmick.State);
        }

        // 연결이 없는 상태로 선택 규칙이 불려도 P06을 강제하지 않는다
        [Test]
        public void TrySelect_TrackerNotWired_DoesNotForceChargeAndStops()
        {
            Env env = new Env();
            env.Activate();
            env.Factory.Tracker = null;

            bool selected = env.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition pattern);

            Assert.IsFalse(selected);
            Assert.IsNull(pattern);
            Assert.AreEqual(EMucheolChargeGimmickState.Stopped, env.Gimmick.State);
        }

        /****************************************
        *        Real Pattern And Real FSM
        ****************************************/

        private sealed class StubMovement : IBossMovement
        {
            public float GetClearDistance(UnityEngine.Vector3 origin, UnityEngine.Vector3 direction, float maxDistance, float bodyRadius)
            {
                return maxDistance;
            }

            public UnityEngine.Vector3 Move(UnityEngine.Vector3 from, UnityEngine.Vector3 to, float bodyRadius)
            {
                return to;
            }
        }

        private sealed class SpyTracker : IBossChargeCastTracker
        {
            public List<int> Started   = new List<int>();
            public List<int> Cancelled = new List<int>();

            public void OnChargeCastStarted(int castInstanceId)
            {
                Started.Add(castInstanceId);
            }

            public void OnChargeCastCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                Cancelled.Add(castInstanceId);
            }
        }

        // 실제 팩토리, 실제 돌진 패턴, 실제 공격 FSM으로 시전 ID와 종료 경계가 기믹까지 오는지 확인한다.
        // 지금 공격 FSM은 충돌 회복을 읽지 않아 종료 경계는 일반 회복(정의 값) 기준이다
        private sealed class RealPatternRig
        {
            public FakeBossVitals       Vitals  { get; } = new FakeBossVitals();
            public FakeBossCatalog      Catalog { get; } = new FakeBossCatalog();
            public MucheolPatternFactory Factory { get; }
            public MucheolChargeGimmick Gimmick { get; }
            public BossAttackFsm        Fsm     { get; }
            public PatternDefinition    Definition { get; }
            public FakeCombatTarget     Self    { get; } = new FakeCombatTarget { CombatId = BossHarness.SELF_ID, Faction = Shinshi.Common.ECombatFaction.Enemy };
            public FakeTargetQuery      Query   { get; } = new FakeTargetQuery();
            public BossDamageGate       Gate    { get; }

            public RealPatternRig(IBossChargeCastTracker extraTracker = null)
            {
                Definition = new PatternDefinition(MucheolPatternFactory.P06_ID, BossTestFixtures.BOSS_ID, 1, 10f,
                    new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                    P06_TELEGRAPH, 0f, 0f, P06_ACTIVE, P06_RECOVERY, Shinshi.Data.EnemyCatalogSO.AttackShapeSpec.Dash(18f, 3f), 0.4f, false);
                Catalog.Boss = BossTestFixtures.MakeBoss(PHASE_RATE);
                Catalog.Patterns.Add(Definition);

                Factory = new MucheolPatternFactory(3541f, null, new StubMovement(), null, null, null, null,
                    new MucheolChargeSpec(2.5f));
                Gimmick = new MucheolChargeGimmick(Vitals, Catalog, Factory, BossTestFixtures.BOSS_ID,
                    new MucheolChargeGimmickSpec(TRIGGER_RATE, REQUIRED, GROGGY));
                Factory.SetChargeCastTracker((extraTracker == null) ? (IBossChargeCastTracker)Gimmick : extraTracker);
                Assert.IsTrue(Gimmick.SetDesignatedStructures(new[] { STRUCTURE_A, STRUCTURE_B, STRUCTURE_C }));

                Query.Players.Add(new FakeCombatTarget
                {
                    CombatId = BossHarness.PLAYER_ID, Faction = Shinshi.Common.ECombatFaction.Player,
                    Position = new UnityEngine.Vector3(0f, 0f, 3f)
                });
                Gate = new BossDamageGate(new FakeDamageResolver());
                Gate.Open();
                Fsm = new BossAttackFsm(new BossCastListenerComposite(Gimmick));
            }

            public int StartCast(double serverTime)
            {
                Assert.IsTrue(Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition definition));
                IBossPattern pattern = Factory.Create(definition);
                Assert.IsNotNull(pattern);
                int castId = Gate.IssueInstanceId();
                BossPatternContext context = new BossPatternContext(definition, castId, BossHarness.PLAYER_ID, Self, Query, Gate);
                Assert.IsTrue(Fsm.Start(definition, pattern, in context, serverTime));
                return castId;
            }
        }

        // 패턴이 시작되며 실제 시전 ID를 알리고, 종료는 시전 알림의 경계 시각으로 오며, 마지막 파괴를 낸 시전의 경계에서 그로기가 시작한다
        [Test]
        public void RealPattern_StartAndFinish_DriveGimmickWithRealCastIdAndBoundary()
        {
            RealPatternRig rig = new RealPatternRig();
            rig.Vitals.HealthRate = TRIGGER_RATE;
            rig.Gimmick.Tick(0.0, 1);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, rig.Gimmick.State);

            int castId = rig.StartCast(4.0);

            Assert.AreEqual(castId, rig.Gimmick.TrackedCastId);
            Assert.IsFalse(rig.Gimmick.IsSelectionPending);

            rig.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 4.5, castId);
            rig.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 4.6, castId);
            rig.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 4.7, castId);
            Assert.AreEqual(EMucheolChargeGimmickState.AwaitingFinalRecovery, rig.Gimmick.State);

            rig.Fsm.Tick(1000.0);

            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, rig.Gimmick.State);
            Assert.AreEqual(P06FinishBoundary(4.0), rig.Gimmick.GroggyStartTime);
            Assert.IsFalse(rig.Gimmick.IsCastInFlight);
        }

        // 실제 패턴이 취소되면 취소가 알려지고 대기가 풀린다
        [Test]
        public void RealPattern_Cancel_NotifiesTrackerAndReleasesFinalRecoveryWait()
        {
            RealPatternRig rig = new RealPatternRig();
            rig.Vitals.HealthRate = TRIGGER_RATE;
            rig.Gimmick.Tick(0.0, 1);
            int castId = rig.StartCast(4.0);
            rig.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 4.5, castId);
            rig.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 4.5, castId);
            rig.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 4.5, castId);
            rig.Fsm.Tick(4.5);

            rig.Fsm.Cancel(EBossInterruptReason.DirectControl);

            Assert.IsFalse(rig.Gimmick.IsCastInFlight);
            Assert.AreEqual(EMucheolChargeGimmickState.AwaitingFinalRecovery, rig.Gimmick.State);

            rig.Gimmick.Tick(5.0, 1);
            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, rig.Gimmick.State);
            Assert.AreEqual(5.0, rig.Gimmick.GroggyStartTime);
        }

        // 패턴은 시작에 한 번, 취소에 한 번만 알리고 정상 종료한 뒤의 취소 호출은 알리지 않는다
        [Test]
        public void RealPattern_NotifiesOncePerLifecycleAndNotAfterFinish()
        {
            SpyTracker spy = new SpyTracker();
            RealPatternRig rig = new RealPatternRig(spy);
            PatternDefinition definition = rig.Definition;
            IBossPattern finished = rig.Factory.Create(definition);
            int firstId = rig.Gate.IssueInstanceId();
            BossPatternContext first = new BossPatternContext(definition, firstId, BossHarness.PLAYER_ID, rig.Self, rig.Query, rig.Gate);
            Assert.IsTrue(rig.Fsm.Start(definition, finished, in first, 1.0));
            rig.Fsm.Tick(100.0);
            finished.Cancel(EBossInterruptReason.None);

            Assert.AreEqual(1, spy.Started.Count);
            Assert.AreEqual(firstId, spy.Started[0]);
            Assert.AreEqual(0, spy.Cancelled.Count);

            IBossPattern cancelled = rig.Factory.Create(definition);
            int secondId = rig.Gate.IssueInstanceId();
            BossPatternContext second = new BossPatternContext(definition, secondId, BossHarness.PLAYER_ID, rig.Self, rig.Query, rig.Gate);
            Assert.IsTrue(rig.Fsm.Start(definition, cancelled, in second, 200.0));
            rig.Fsm.Cancel(EBossInterruptReason.DirectControl);
            cancelled.Cancel(EBossInterruptReason.DirectControl);

            CollectionAssert.AreEqual(new[] { firstId, secondId }, spy.Started);
            CollectionAssert.AreEqual(new[] { secondId }, spy.Cancelled);
        }

        // 팩토리가 이 기믹을 시전 추적 수신자로 연결하지 않았으면 발동하지 않는다. 연결 없이 발동하면 마지막 파괴가 확인되는 순간
        // 회복이 끝나기 전에 그로기가 시작된다
        [Test]
        public void RealPattern_WithoutTrackerWiring_NeverActivatesSoNoGroggyBeforeRecovery()
        {
            RealPatternRig rig = new RealPatternRig(new SpyTracker());
            rig.Vitals.HealthRate = TRIGGER_RATE;

            rig.Gimmick.Tick(0.0, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Dormant, rig.Gimmick.State);
            Assert.IsFalse(rig.Gimmick.DefersPhaseTransition);
            Assert.IsFalse(rig.Gimmick.BlocksSelection);
            Assert.IsFalse(rig.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition _));

            // 구조물 파괴가 확인돼도 기믹 밖이라 무시한다, 그로기도 시작하지 않는다
            Assert.IsFalse(rig.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 4.5));
            Assert.AreEqual(EMucheolChargeGimmickState.Dormant, rig.Gimmick.State);
        }

        // 연결하면 같은 구성이 발동하고, 팩토리 쪽 연결 정보가 이 기믹을 가리킨다
        [Test]
        public void RealPattern_WithTrackerWiring_Activates()
        {
            RealPatternRig rig = new RealPatternRig();
            rig.Vitals.HealthRate = TRIGGER_RATE;
            Assert.AreSame(rig.Gimmick, rig.Factory.ChargeCastTracker);

            rig.Gimmick.Tick(0.0, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Active, rig.Gimmick.State);
        }

        /****************************************
        *           Groggy Notifications
        ****************************************/

        // 마지막 파괴가 시전 중에 확인되면 대기를 먼저 알리고, 그 시전의 회복 끝 경계에서 시작과 끝을 채워 알린다
        [Test]
        public void GroggyListener_FinalDestructionDuringCast_PendingThenStartedAtBoundary()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.1, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 2.2, 7);

            CollectionAssert.AreEqual(new[] { "pending" }, env.Groggy.Order);
            Assert.AreEqual(7, env.Groggy.CastId);
            Assert.AreEqual(STRUCTURE_C, env.Groggy.Structure);
            Assert.AreEqual(2.2, env.Groggy.Confirm);

            double boundary = 10.0;
            env.Gimmick.OnCastFinished(7, boundary);

            CollectionAssert.AreEqual(new[] { "pending", "started" }, env.Groggy.Order);
            Assert.AreEqual(boundary, env.Groggy.Start);
            Assert.AreEqual(boundary + GROGGY, env.Groggy.End);
        }

        // 시전 사이에 확인되면 대기와 시작이 같은 순간에 이어서 온다
        [Test]
        public void GroggyListener_FinalDestructionBetweenCasts_PendingThenStartedAtConfirmation()
        {
            Env env = new Env();
            env.Activate();
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.1);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 2.2);

            CollectionAssert.AreEqual(new[] { "pending", "started" }, env.Groggy.Order);
            Assert.AreEqual(0, env.Groggy.CastId, "부순 시전을 모르면 0이다");
            Assert.AreEqual(2.2, env.Groggy.Start);
            Assert.AreEqual(2.2 + GROGGY, env.Groggy.End);
        }

        // 시전 추적 연결이 풀려 대기 중인 그로기를 접으면 취소를 알린다
        [Test]
        public void GroggyListener_TrackerUnwiredWhileAwaiting_SendsCancelled()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.0, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 2.0, 7);

            env.Factory.Tracker = null;
            env.Gimmick.Tick(2.5, 1);

            CollectionAssert.AreEqual(new[] { "pending", "cancelled" }, env.Groggy.Order);
            Assert.AreEqual(7, env.Groggy.CastId);
        }

        // 사망과 전투 종료로 멈추면 복제 상태가 기록을 비우므로 취소를 알리지 않는다
        [Test]
        public void GroggyListener_DeathWhileAwaiting_SendsNoCancel()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast(7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 2.0, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 2.0, 7);
            env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 2.0, 7);

            env.Gimmick.OnChargeCastCancelled(7, EBossInterruptReason.Death);
            env.Gimmick.Tick(2.5, 1);

            Assert.AreEqual(EMucheolChargeGimmickState.Stopped, env.Gimmick.State);
            CollectionAssert.AreEqual(new[] { "pending" }, env.Groggy.Order);
        }

        /****************************************
        *               Stop
        ****************************************/

        [Test]
        public void Stop_IgnoresLaterInputs()
        {
            Env env = new Env();
            env.Activate();
            env.SelectChargeCast();

            env.Gimmick.Stop();

            Assert.AreEqual(EMucheolChargeGimmickState.Stopped, env.Gimmick.State);
            Assert.IsFalse(env.Gimmick.DefersPhaseTransition);
            Assert.IsFalse(env.Gimmick.BlocksSelection);
            Assert.IsFalse(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 3.0));
            Assert.IsFalse(env.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition _));

            env.Vitals.HealthRate = 0.1f;
            env.Gimmick.Tick(4.0, 1);
            Assert.AreEqual(EMucheolChargeGimmickState.Stopped, env.Gimmick.State);
        }

        /****************************************
        *            Spec And Composite
        ****************************************/

        [Test]
        public void Spec_InvalidValues_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolChargeGimmickSpec(0f, REQUIRED, GROGGY));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolChargeGimmickSpec(1.01f, REQUIRED, GROGGY));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolChargeGimmickSpec(float.NaN, REQUIRED, GROGGY));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolChargeGimmickSpec(TRIGGER_RATE, 0, GROGGY));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolChargeGimmickSpec(TRIGGER_RATE, REQUIRED, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolChargeGimmickSpec(TRIGGER_RATE, REQUIRED, float.PositiveInfinity));

            MucheolChargeGimmickSpec valid = new MucheolChargeGimmickSpec(TRIGGER_RATE, REQUIRED, GROGGY);
            Assert.AreEqual(TRIGGER_RATE, valid.TriggerHealthRate, 0.0001f);
            Assert.AreEqual(REQUIRED, valid.RequiredStructureCount);
            Assert.AreEqual(GROGGY, valid.GroggySeconds, 0.0001f);
        }

        private sealed class CastSpy : IBossCastListener
        {
            public List<string> Log;
            public string       Name;

            public void OnCastExecuted(int castInstanceId, double serverTime)
            {
                Log.Add($"{Name}:exec:{castInstanceId}:{serverTime}");
            }

            public void OnCastFinished(int castInstanceId, double serverTime)
            {
                Log.Add($"{Name}:fin:{castInstanceId}:{serverTime}");
            }
        }

        [Test]
        public void CastListenerComposite_ForwardsInOrderAndSkipsNulls()
        {
            List<string> log = new List<string>();
            var composite = new BossCastListenerComposite(
                new CastSpy { Log = log, Name = "a" }, null, new CastSpy { Log = log, Name = "b" });

            composite.OnCastExecuted(3, 1.5);
            composite.OnCastFinished(3, 2.5);

            CollectionAssert.AreEqual(new[] { "a:exec:3:1.5", "b:exec:3:1.5", "a:fin:3:2.5", "b:fin:3:2.5" }, log);
            Assert.DoesNotThrow(() => new BossCastListenerComposite(null).OnCastFinished(1, 1.0));
            Assert.DoesNotThrow(() => new BossCastListenerComposite().OnCastExecuted(1, 1.0));
        }

        /****************************************
        *       Proposed Connection Harness
        ****************************************/

        // 공용 BossCombat.TickCombat의 순서를 따르되 기믹 연결 셋(틱 갱신, 전환 보류, 선택 차단)을 더한 가짜 전투. 실제 BossCombat이 아니다.
        // 사망과 전투 종료는 실제 BossBrain이 이 틱 앞에서 처리하므로 여기서는 다루지 않는다
        private sealed class ProposedCombat
        {
            private readonly BossPhaseFsm      _phase = new BossPhaseFsm();
            private readonly BossAttackFsm     _attack;
            private readonly BossSelectionTree _tree = new BossSelectionTree();
            private readonly BossBlackboard    _bb   = new BossBlackboard();
            private readonly BossDamageGate    _gate;
            private readonly Env               _env;
            private readonly FakeCombatTarget  _self   = new FakeCombatTarget { CombatId = BossHarness.SELF_ID, Faction = Shinshi.Common.ECombatFaction.Enemy };
            private readonly FakeTargetQuery   _query  = new FakeTargetQuery();
            private readonly FakeBossRandom    _random = new FakeBossRandom();
            private readonly FakeEffectLifetime _effects = new FakeEffectLifetime();
            private readonly BossDefinition    _boss;

            public List<string> Started       { get; } = new List<string>();
            public List<double> StartTimes    { get; } = new List<double>();
            public int          TransitionCount;
            public double       TransitionAt;

            public int Phase => _phase.Phase;

            public ProposedCombat(Env env)
            {
                _env  = env;
                _gate = new BossDamageGate(new FakeDamageResolver());
                _gate.Open();
                _boss = env.Catalog.Boss;

                _query.Players.Add(new FakeCombatTarget
                {
                    CombatId = BossHarness.PLAYER_ID, Faction = Shinshi.Common.ECombatFaction.Player,
                    Position = new UnityEngine.Vector3(0f, 0f, 3f)
                });

                // 시전 종료 알림을 기믹이 받도록 묶는다(제안하는 연결)
                _attack = new BossAttackFsm(new BossCastListenerComposite(env.Gimmick));
                _tree.AddRule(env.Gimmick);

                _bb.Self       = _self;
                _bb.Boss       = _boss;
                _bb.Phase      = _phase.Phase;
                _bb.Targets    = _query;
                _bb.Effects    = _effects;
                _bb.Catalog    = env.Catalog;
                _bb.Random     = _random;
                _bb.Vitals     = env.Vitals;
                _bb.Patterns   = env.Factory;
                _bb.NextSelectionTime = 0.0;
            }

            public void Tick(double serverTime)
            {
                _bb.ServerTime = serverTime;

                // 연결 1: 전환 판정보다 먼저 기믹을 갱신한다
                _env.Gimmick.Tick(serverTime, _phase.Phase);

                // 연결 2: 기믹이 보류 중이면 전환을 시작하지 않는다
                if ((!_env.Gimmick.DefersPhaseTransition) && (_phase.ShouldBeginTransition(_env.Vitals, _boss)))
                {
                    _phase.BeginTransition();
                    _attack.Cancel(EBossInterruptReason.PhaseTransition);
                    _phase.CompleteTransition();
                    _bb.ResetHistory();
                    _bb.Phase = _phase.Phase;
                    ++TransitionCount;
                    TransitionAt = serverTime;
                    return;
                }

                if (_attack.State != EBossAttackState.Ready)
                {
                    _attack.Tick(serverTime);
                    return;
                }

                if (!_phase.AllowsNewSelection)
                {
                    return;
                }

                // 연결 3: 기믹이 막으면 새 선택을 평가하지 않는다
                if (_env.Gimmick.BlocksSelection)
                {
                    return;
                }

                if (!_tree.Evaluate(_bb))
                {
                    return;
                }

                PatternDefinition definition = _bb.SelectedPattern;
                _bb.SelectedPattern = null;
                IBossPattern pattern = _env.Factory.Create(definition);
                if (pattern == null)
                {
                    // 실제 BossCombat처럼 시작하지 않고 재조회 시간 뒤로 미룬다(오류 로그는 이 시험에서 생략)
                    _bb.NextSelectionTime = serverTime + _boss.EmptyWeightRetrySeconds;
                    return;
                }

                BossPatternContext context = new BossPatternContext(definition, _gate.IssueInstanceId(), _bb.TargetCombatId,
                    _self, _query, _gate, _random);
                _attack.Start(definition, pattern, in context, serverTime);
                Started.Add(definition.DefinitionId);
                StartTimes.Add(serverTime);
                _bb.RecordUse(definition.DefinitionId, definition.Group);
            }

            public void TickRange(double from, double to)
            {
                int count = (int)Math.Round((to - from) / STEP);
                for (int i = 1; i <= count; ++i)
                {
                    Tick(from + (i * STEP));
                }
            }
        }

        // 일반 패턴을 먼저 시작시키고 그 시전 중에 체력을 내려 기믹을 발동한다. 일반 시전은 0.0에 시작해 2.0에 끝난다
        private static ProposedCombat StartWithNormalCastThenDamage(Env env, float healthAfterDamage, out double damageTime)
        {
            ProposedCombat combat = new ProposedCombat(env);
            env.Vitals.HealthRate = 0.80f;
            combat.Tick(0.0);
            Assert.AreEqual(1, combat.Started.Count);
            Assert.AreEqual(NORMAL_ID, combat.Started[0]);

            damageTime = 0.3;
            env.Vitals.HealthRate = healthAfterDamage;
            combat.Tick(damageTime);
            return combat;
        }

        // 마지막 구조물을 파괴하는 세 확인을 P06 시전 중에 낸다
        private static void ConfirmAllStructures(Env env, double serverTime)
        {
            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, serverTime));
            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, serverTime));
            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, serverTime));
        }

        // 진행 중인 시전은 정상적으로 마치고 그 뒤 첫 선택이 P06이다
        [Test]
        public void Harness_HealthAtTrigger_CurrentCastCompletesThenFirstSelectionIsCharge()
        {
            Env env = new Env();
            ProposedCombat combat = StartWithNormalCastThenDamage(env, TRIGGER_RATE, out double damageTime);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);

            combat.TickRange(damageTime, 1.95);
            CollectionAssert.AreEqual(new[] { NORMAL_ID }, combat.Started);
            Assert.IsFalse(env.Factory.Created[0].IsCancelled);

            combat.TickRange(1.95, 2.1);

            CollectionAssert.AreEqual(new[] { NORMAL_ID, MucheolPatternFactory.P06_ID }, combat.Started);
            Assert.IsTrue(env.Factory.Created[0].IsFinished);
            Assert.IsFalse(env.Factory.Created[0].IsCancelled);
            Assert.AreEqual(0, combat.TransitionCount);
        }

        // 발동 기준 바로 위 체력에서는 일반 선택이 그대로 이어진다
        [Test]
        public void Harness_HealthJustAboveTrigger_KeepsNormalSelection()
        {
            Env env = new Env();
            ProposedCombat combat = StartWithNormalCastThenDamage(env, TRIGGER_RATE + 0.01f, out double damageTime);

            combat.TickRange(damageTime, 8.0);

            Assert.AreEqual(EMucheolChargeGimmickState.Dormant, env.Gimmick.State);
            Assert.IsTrue(combat.Started.TrueForAll(id => id == NORMAL_ID));
            Assert.GreaterOrEqual(combat.Started.Count, 3);
        }

        // 벽 충돌 등으로 구조물이 부서지지 않으면 P06을 계속 반복한다, 일반 가중치는 쓰지 않는다
        [Test]
        public void Harness_NoConfirmedDestruction_RepeatsChargeUntilConfirmed()
        {
            Env env = new Env();
            ProposedCombat combat = StartWithNormalCastThenDamage(env, TRIGGER_RATE, out double damageTime);

            combat.TickRange(damageTime, 14.0);

            Assert.GreaterOrEqual(combat.Started.Count, 4);
            Assert.AreEqual(NORMAL_ID, combat.Started[0]);
            for (int i = 1; i < combat.Started.Count; ++i)
            {
                Assert.AreEqual(MucheolPatternFactory.P06_ID, combat.Started[i]);
            }

            Assert.AreEqual(0, env.Gimmick.DestroyedCount);
            Assert.AreEqual(EMucheolChargeGimmickState.Active, env.Gimmick.State);
        }

        // 70%에서 55% 이하로 한 번에 떨어져도 진행 중인 시전이 취소되지 않고, 구조물 파괴와 그로기를 마친 뒤에야 전환한다.
        // 전환은 그로기 끝 경계 틱에서 일반 공격보다 먼저 일어난다
        [Test]
        public void Harness_SingleDamageBelowPhaseThreshold_DefersTransitionUntilGroggyEnds()
        {
            Env env = new Env();
            ProposedCombat combat = StartWithNormalCastThenDamage(env, PHASE_RATE - 0.05f, out double damageTime);

            combat.TickRange(damageTime, 2.1);
            CollectionAssert.AreEqual(new[] { NORMAL_ID, MucheolPatternFactory.P06_ID }, combat.Started);
            Assert.IsFalse(env.Factory.Created[0].IsCancelled);
            Assert.AreEqual(0, combat.TransitionCount);

            double p06Start = combat.StartTimes[1];
            combat.TickRange(2.1, p06Start + 1.2);
            ConfirmAllStructures(env, p06Start + 1.2);
            Assert.AreEqual(EMucheolChargeGimmickState.AwaitingFinalRecovery, env.Gimmick.State);

            double boundary = P06FinishBoundary(p06Start);
            combat.TickRange(p06Start + 1.2, boundary + 2.9);

            // 마지막 시전이 끝난 경계부터 그로기라 새 선택도 전환도 없다
            Assert.AreEqual(boundary, env.Gimmick.GroggyStartTime);
            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
            CollectionAssert.AreEqual(new[] { NORMAL_ID, MucheolPatternFactory.P06_ID }, combat.Started);
            Assert.AreEqual(0, combat.TransitionCount);

            combat.Tick(env.Gimmick.GroggyEndTime - MARGIN);
            Assert.AreEqual(0, combat.TransitionCount);

            combat.Tick(env.Gimmick.GroggyEndTime);
            Assert.AreEqual(1, combat.TransitionCount);
            Assert.AreEqual(env.Gimmick.GroggyEndTime, combat.TransitionAt);
            Assert.AreEqual(2, combat.Phase);
            CollectionAssert.AreEqual(new[] { NORMAL_ID, MucheolPatternFactory.P06_ID }, combat.Started);
        }

        // 마지막 시전의 회복이 끝난 정확한 FSM 경계부터 3초 동안 새 공격을 고르지 않는다, 2페이즈 기준 위 체력이라 전환은 없다
        [Test]
        public void Harness_GroggyStartsAtExactRecoveryBoundaryAndBlocksSelectionForThreeSeconds()
        {
            Env env = new Env();
            ProposedCombat combat = StartWithNormalCastThenDamage(env, TRIGGER_RATE, out double damageTime);
            combat.TickRange(damageTime, 2.1);
            double p06Start = combat.StartTimes[1];
            combat.TickRange(2.1, p06Start + 1.2);
            ConfirmAllStructures(env, p06Start + 1.2);

            double boundary = P06FinishBoundary(p06Start);
            combat.TickRange(p06Start + 1.2, boundary + 0.5);

            Assert.AreEqual(boundary, env.Gimmick.GroggyStartTime);
            Assert.AreEqual(boundary + GROGGY, env.Gimmick.GroggyEndTime);

            // 그로기 중에는 피해를 받아 체력이 변해도 기믹이 이를 막거나 되돌리지 않는다
            env.Vitals.HealthRate = 0.62f;
            combat.TickRange(boundary + 0.5, boundary + 2.9);
            CollectionAssert.AreEqual(new[] { NORMAL_ID, MucheolPatternFactory.P06_ID }, combat.Started);
            Assert.AreEqual(0.62f, env.Vitals.HealthRate, 0.0001f);
            Assert.AreEqual(0, combat.TransitionCount);

            // 끝 경계 직전 틱까지 막히고, 끝 경계 시각의 틱에서 그로기가 끝나 같은 틱에 일반 선택이 다시 열린다(끝 경계 미포함)
            combat.Tick(env.Gimmick.GroggyEndTime - MARGIN);
            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
            Assert.AreEqual(2, combat.Started.Count);

            combat.Tick(env.Gimmick.GroggyEndTime);
            Assert.AreEqual(EMucheolChargeGimmickState.Completed, env.Gimmick.State);
            Assert.AreEqual(3, combat.Started.Count);
            Assert.AreEqual(NORMAL_ID, combat.Started[2]);
        }

        // 마지막 시전이 끝난 뒤 틱이 아주 커도 그로기는 끝 경계 기준으로 끝나고, 같은 틱에서 전환이 일반 선택보다 먼저 판정된다
        [Test]
        public void Harness_HugeTickPastGroggyEnd_TransitionHasPriorityOverNormalSelection()
        {
            Env env = new Env();
            ProposedCombat combat = StartWithNormalCastThenDamage(env, PHASE_RATE - 0.05f, out double damageTime);
            combat.TickRange(damageTime, 2.1);
            double p06Start = combat.StartTimes[1];
            combat.TickRange(2.1, p06Start + 1.2);
            ConfirmAllStructures(env, p06Start + 1.2);
            double boundary = P06FinishBoundary(p06Start);

            // 시전이 끝나는 틱이 한참 늦다, 이 틱에서는 시전 종료만 처리된다
            combat.Tick(boundary + 100.0);
            Assert.AreEqual(boundary, env.Gimmick.GroggyStartTime);
            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
            Assert.AreEqual(0, combat.TransitionCount);

            // 다음 틱에서 그로기가 이미 지나 있으므로 같은 틱에 전환이 일반 선택보다 먼저다
            combat.Tick(boundary + 100.05);
            Assert.AreEqual(EMucheolChargeGimmickState.Completed, env.Gimmick.State);
            Assert.AreEqual(1, combat.TransitionCount);
            CollectionAssert.AreEqual(new[] { NORMAL_ID, MucheolPatternFactory.P06_ID }, combat.Started);
        }

        /****************************************
        *          Real BossBrain (existing hook)
        ****************************************/

        private sealed class CountingRule : IBossSelectionRule
        {
            private readonly IBossSelectionRule _inner;

            public int Calls;

            public CountingRule(IBossSelectionRule inner)
            {
                _inner = inner;
            }

            public bool TrySelect(BossBlackboard blackboard, out PatternDefinition pattern)
            {
                ++Calls;
                return _inner.TrySelect(blackboard, out pattern);
            }
        }

        private sealed class RealBrainRig
        {
            public FakeCombatTarget Self   { get; } = new FakeCombatTarget { CombatId = BossHarness.SELF_ID, Faction = Shinshi.Common.ECombatFaction.Enemy };
            public FakeTargetQuery  Query  { get; } = new FakeTargetQuery();
            public BossBrain        Brain  { get; }
            public CountingRule     Rule   { get; }

            public RealBrainRig(Env env)
            {
                Query.Players.Add(new FakeCombatTarget
                {
                    CombatId = BossHarness.PLAYER_ID, Faction = Shinshi.Common.ECombatFaction.Player,
                    Position = new UnityEngine.Vector3(0f, 0f, 3f)
                });

                Brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
                {
                    Catalog  = env.Catalog,
                    Self     = Self,
                    Vitals   = env.Vitals,
                    Targets  = Query,
                    Damage   = new FakeDamageResolver(),
                    Patterns = env.Factory,
                    Effects  = new FakeEffectLifetime(),
                    Control  = new FakeControlSource(),
                    Random   = new FakeBossRandom()
                });

                Rule = new CountingRule(env.Gimmick);
                Assert.IsTrue(Brain.AddSelectionRule(Rule));
                Assert.IsTrue(Brain.Begin(0.0));
            }

            public void TickRange(double from, double to)
            {
                int count = (int)Math.Round((to - from) / STEP);
                for (int i = 1; i <= count; ++i)
                {
                    Brain.Tick(from + (i * STEP));
                }
            }
        }

        // 지금 있는 연결(선택 규칙)만으로 되는 것, 기믹 중에는 가중치와 상관없이 P06만 고른다
        [Test]
        public void RealBrain_ActiveGimmickRule_SelectsChargeEveryTimeIgnoringWeights()
        {
            Env env = new Env();
            env.Activate();
            RealBrainRig rig = new RealBrainRig(env);

            rig.TickRange(0.0, 14.0);

            Assert.GreaterOrEqual(env.Factory.CreatedIds.Count, 3);
            Assert.IsTrue(env.Factory.CreatedIds.TrueForAll(id => id == MucheolPatternFactory.P06_ID));
        }

        // 지금 공용 연결만으로는 안 되는 것, 그로기 중에도 가중 선택이 이어진다. 선택 차단은 BossCombat의 연결이 필요하다.
        // 연결을 승인받아 적용하면 이 시험의 기대를 "선택이 막힌다"로 바꿔야 한다
        [Test]
        public void RealBrain_GroggyCannotBlockSelectionWithoutSharedHook()
        {
            Env env = new Env();
            env.Activate();
            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_A, 0.0));
            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_B, 0.0));
            Assert.IsTrue(env.Gimmick.ConfirmStructureDestroyed(STRUCTURE_C, 0.0));
            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, env.Gimmick.State);
            RealBrainRig rig = new RealBrainRig(env);

            rig.TickRange(0.0, 2.0);

            Assert.GreaterOrEqual(env.Factory.CreatedIds.Count, 1);
            Assert.AreEqual(NORMAL_ID, env.Factory.CreatedIds[0]);
        }

        // 사망과 전투 종료는 기믹보다 앞선다, 보스 두뇌가 선택 규칙을 평가하기 전에 멈춘다
        [Test]
        public void RealBrain_CombatEnd_StopsBeforeTheRuleIsEvaluated()
        {
            Env env = new Env();
            env.Activate();
            RealBrainRig rig = new RealBrainRig(env);

            rig.Brain.RequestCombatEnd();
            rig.TickRange(0.0, 1.0);

            Assert.AreEqual(EBossFlowState.Ended, rig.Brain.FlowState);
            Assert.AreEqual(0, rig.Rule.Calls);
            Assert.AreEqual(0, env.Factory.CreatedIds.Count);
        }

        [Test]
        public void RealBrain_Death_StopsSelectionEvenWhileGimmickIsActive()
        {
            Env env = new Env();
            env.Activate();
            RealBrainRig rig = new RealBrainRig(env);

            rig.TickRange(0.0, 0.5);
            Assert.GreaterOrEqual(rig.Rule.Calls, 1);
            int callsBeforeDeath = rig.Rule.Calls;
            int castsBeforeDeath = env.Factory.CreatedIds.Count;

            env.Vitals.IsHealthZero = true;
            env.Vitals.HealthRate   = 0f;
            rig.TickRange(0.5, 10.0);

            Assert.AreEqual(EBossFlowState.Defeated, rig.Brain.FlowState);
            Assert.IsTrue(rig.Brain.IsBossDeathRecorded);
            Assert.AreEqual(callsBeforeDeath, rig.Rule.Calls);
            Assert.AreEqual(castsBeforeDeath, env.Factory.CreatedIds.Count);
        }
    }
}
