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
    // 무철 방향 봉인의 상태 관리, 타수 집계, 방향 구역 경계를 가짜 체력과 직접 만든 타격으로 확인한다.
    // 마지막 묶음은 실제 BossBrain에 올려 진행 중인 패턴이 끝난 뒤에야 표식이 나오는지 본다.
    // 발동 비율, 인원별 타수, 구역 각도는 기획 확정값을 따른 이 테스트의 시작값이며 운영 데이터는 아니다.
    // 하한은 발동 전에는 없고 살아서 발동 비율 이하에 도착한 순간 남은 실제 체력으로 정해진다.
    // 체력 하한을 실제로 적용하는 서버 피해 판정기는 이 범위가 아니라 판정기 시험은 따로 필요하다
    public class MucheolSealGimmickTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float TRIGGER_RATE = 0.30f;
        private const int   SOLO_HITS    = 5;
        private const int   GROUP_HITS   = 10;
        private const float FRONT_HALF   = 45f;
        private const float SIDE_LIMIT   = 135f;

        // 보스 정면을 +Z로 둔 기준에서 각 구역 한가운데로 향하는 각도(도)
        private const float RIGHT_ANGLE = 90f;
        private const float LEFT_ANGLE  = -90f;
        private const float REAR_ANGLE  = 180f;
        private const float FRONT_ANGLE = 0f;

        private const int   FIRST_ACTION = 1;

        /****************************************
        *               Helpers
        ****************************************/

        private static MucheolSealGimmickSpec MakeSpec()
        {
            return new MucheolSealGimmickSpec(TRIGGER_RATE, SOLO_HITS, GROUP_HITS, FRONT_HALF, SIDE_LIMIT);
        }

        // 정면 기준 각도만큼 오른쪽(양수)으로 돌린 방향을 만든다
        private static Vector3 Incoming(float angleFromForward, Vector3 forward)
        {
            return Quaternion.AngleAxis(angleFromForward, Vector3.up) * forward;
        }

        private static MucheolSealHit MakeHit(int actionId, float angle, bool isDot = false, bool isSummon = false)
        {
            return new MucheolSealHit(actionId, isDot, isSummon, Vector3.forward, Incoming(angle, Vector3.forward));
        }

        private sealed class Env
        {
            public FakeBossVitals Vitals { get; } = new FakeBossVitals();
            public MucheolSealGimmick Gimmick { get; }
            public int NextAction { get; set; } = FIRST_ACTION;

            public Env(int startingCount = 1)
            {
                Gimmick = new MucheolSealGimmick(Vitals, MakeSpec(), startingCount);
            }

            // 체력을 발동 비율에 두고 틱한 뒤 공격이 비어 있는 선택 평가로 표식을 낸다
            public void Activate()
            {
                Vitals.HealthRate = TRIGGER_RATE;
                Gimmick.Tick(0.0, 2);
                Assert.AreEqual(EMucheolSealState.Pending, Gimmick.State);
                Assert.IsFalse(Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition pattern));
                Assert.IsNull(pattern);
                Assert.AreEqual(EMucheolSealState.Active, Gimmick.State);
            }

            public EMucheolSealHitResult Hit(float angle)
            {
                MucheolSealHit hit = MakeHit(NextAction++, angle);
                return Gimmick.ReportHit(in hit).Result;
            }

            public void Fill(float angle, int count)
            {
                for (int i = 0; i < count; ++i)
                {
                    Hit(angle);
                }
            }
        }

        /****************************************
        *        Trigger And Floor
        ****************************************/

        [Test]
        public void Tick_HealthAboveTrigger_StaysDormantWithoutFloor()
        {
            Env env = new Env();
            env.Vitals.HealthRate = TRIGGER_RATE + 0.05f;

            env.Gimmick.Tick(0.0, 2);

            Assert.AreEqual(EMucheolSealState.Dormant, env.Gimmick.State);
            Assert.AreEqual(0f, env.Gimmick.HealthFloorRate, 0f, "발동 전에는 하한이 없다");
            Assert.IsFalse(env.Gimmick.IsMarkerVisible);
        }

        [Test]
        public void ReportHit_BeforeTrigger_ReturnsNoFloor()
        {
            Env env = new Env();
            env.Vitals.HealthRate = 0.35f;

            MucheolSealHit hit = MakeHit(FIRST_ACTION, RIGHT_ANGLE);
            MucheolSealVerdict verdict = env.Gimmick.ReportHit(in hit);

            Assert.AreEqual(EMucheolSealHitResult.NotActive, verdict.Result);
            Assert.AreEqual(0f, verdict.HealthFloorRate, 0f, "경계를 넘기는 피해도 평소대로 들어간다");
            Assert.AreEqual(EMucheolSealState.Dormant, env.Gimmick.State);
        }

        [Test]
        public void Tick_HealthExactlyAtTrigger_StartsPendingWithoutMarker()
        {
            Env env = new Env();
            env.Vitals.HealthRate = TRIGGER_RATE;

            env.Gimmick.Tick(0.0, 2);

            Assert.AreEqual(EMucheolSealState.Pending, env.Gimmick.State);
            Assert.AreEqual(EMucheolSealDirection.None, env.Gimmick.CurrentDirection);
            Assert.IsFalse(env.Gimmick.IsMarkerVisible);
        }

        // 35%에서 한 번에 20%로 내려왔으면 하한은 30%가 아니라 실제 남은 20%다
        [Test]
        public void Tick_ArrivesBelowTrigger_FreezesActualRemainingHealth()
        {
            Env env = new Env();
            env.Vitals.HealthRate = 0.35f;
            env.Gimmick.Tick(0.0, 2);
            Assert.AreEqual(0f, env.Gimmick.HealthFloorRate, 0f);

            env.Vitals.HealthRate = 0.20f;
            env.Gimmick.Tick(0.05, 2);

            Assert.AreEqual(EMucheolSealState.Pending, env.Gimmick.State);
            Assert.AreEqual(0.20f, env.Gimmick.HealthFloorRate, 0f);

            // 이후 체력이 더 낮게 읽혀도 도착 순간 값을 유지한다
            env.Vitals.HealthRate = 0.10f;
            env.Gimmick.Tick(0.10, 2);
            Assert.AreEqual(0.20f, env.Gimmick.HealthFloorRate, 0f);
        }

        // 틱이 읽기 전에 같은 틱의 다음 타격이 먼저 오면 그 타격이 도착을 알아채 같은 하한을 받는다
        [Test]
        public void ReportHit_AfterArrivalBeforeTick_ArmsAndReturnsActualFloor()
        {
            Env env = new Env();
            env.Vitals.HealthRate = 0.35f;
            MucheolSealHit first = MakeHit(1, RIGHT_ANGLE);
            Assert.AreEqual(0f, env.Gimmick.ReportHit(in first).HealthFloorRate, 0f);

            env.Vitals.HealthRate = 0.20f;
            MucheolSealHit second = MakeHit(2, RIGHT_ANGLE);
            MucheolSealVerdict verdict = env.Gimmick.ReportHit(in second);

            Assert.AreEqual(0.20f, verdict.HealthFloorRate, 0f);
            Assert.AreEqual(EMucheolSealState.Pending, env.Gimmick.State);
        }

        // 정상 피해로 체력이 0이 되면 기믹 없이 기존 처치 경로를 따른다
        [Test]
        public void Tick_HealthZero_NeverArms()
        {
            Env env = new Env();
            env.Vitals.HealthRate = 0f;
            env.Vitals.IsHealthZero = true;

            env.Gimmick.Tick(0.0, 2);
            MucheolSealHit hit = MakeHit(FIRST_ACTION, RIGHT_ANGLE);
            MucheolSealVerdict verdict = env.Gimmick.ReportHit(in hit);

            Assert.AreEqual(EMucheolSealState.Dormant, env.Gimmick.State);
            Assert.AreEqual(0f, env.Gimmick.HealthFloorRate, 0f);
            Assert.AreEqual(0f, verdict.HealthFloorRate, 0f);
            Assert.IsFalse(verdict.ConsumesHit);
        }

        [Test]
        public void Tick_RateZeroEvenIfFlagMissing_NeverArms()
        {
            Env env = new Env();
            env.Vitals.HealthRate = 0f;

            env.Gimmick.Tick(0.0, 2);

            Assert.AreEqual(EMucheolSealState.Dormant, env.Gimmick.State);
        }

        // 55% 전환, 30% 봉인, 25% 강화가 한 타격에 겹쳐도 같은 틱에 맞게 처리된다
        [Test]
        public void Overlap_OneHitCrossesTransitionSealAndEnhancedBands()
        {
            MucheolDevCatalog dev = new MucheolDevCatalog(3541f, true);
            Assert.IsTrue(dev.TryGetBoss(MucheolPatternFactory.BOSS_ID, out BossPatternCatalogSO.BossDefinition boss));

            FakeBossVitals vitals = new FakeBossVitals { HealthRate = 0.60f };
            MucheolSealGimmick gimmick = new MucheolSealGimmick(vitals, MakeSpec(), 1);

            // 타격이 보고되는 순간은 아직 피해 전이라 하한이 없다
            MucheolSealHit hit = MakeHit(FIRST_ACTION, RIGHT_ANGLE);
            Assert.AreEqual(0f, gimmick.ReportHit(in hit).HealthFloorRate, 0f);

            vitals.HealthRate = 0.20f;
            gimmick.Tick(0.05, 1);

            Assert.AreEqual(EMucheolSealState.Pending, gimmick.State);
            Assert.AreEqual(0.20f, gimmick.HealthFloorRate, 0f);
            Assert.IsFalse(gimmick.DefersPhaseTransition, "55% 전환을 보류하지 않는다");
            Assert.Less(vitals.HealthRate, boss.PhaseTransitionHealthRate, "전환 기준을 넘었다");
            Assert.IsTrue(boss.IsEnhanced(gimmick.HealthFloorRate), "동결 체력이 25% 강화 구간에 있다");
        }

        // 실제 두뇌에서 같은 타격이 전환과 봉인을 같은 틱에 일으키고, 전환은 보류되지 않는다
        [Test]
        public void Brain_OneHitStartsPhaseTransitionAndSealInTheSameTick()
        {
            FakeBossVitals vitals = new FakeBossVitals { HealthRate = 0.60f };
            FakeBossCatalog catalog = new FakeBossCatalog();
            catalog.Boss = BossTestFixtures.MakeBoss(0.55f);
            catalog.Patterns.Add(BossTestFixtures.MakePattern(MucheolPatternFactory.P04_ID, 10f, 1f, 0.5f, 0.5f));
            MucheolPatternFactory factory = new MucheolPatternFactory(3541f, null, null, null, null, null, null,
                new MucheolChargeSpec(2.5f));
            MucheolSealGimmick gimmick = new MucheolSealGimmick(vitals, MakeSpec(), 1);

            FakeTargetQuery query = new FakeTargetQuery();
            query.Players.Add(new FakeCombatTarget
            {
                CombatId = BossHarness.PLAYER_ID, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f)
            });
            BossBrain brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
            {
                Catalog     = catalog,
                Self        = new FakeCombatTarget { CombatId = BossHarness.SELF_ID, Faction = ECombatFaction.Enemy },
                Vitals      = vitals,
                Targets     = query,
                Damage      = new FakeDamageResolver(),
                Patterns    = factory,
                Effects     = new FakeEffectLifetime(),
                Control     = new FakeControlSource(),
                Random      = new FakeBossRandom(),
                DeathPolicy = new FakeDeathPolicy(),
                Casts       = new SpyCasts()
            });
            Assert.IsTrue(brain.AddGimmick(gimmick));
            Assert.IsTrue(brain.Begin(0.0));
            brain.Tick(0.0);
            Assert.AreEqual(1, brain.Phase);

            MucheolSealHit hit = MakeHit(FIRST_ACTION, RIGHT_ANGLE);
            Assert.AreEqual(0f, gimmick.ReportHit(in hit).HealthFloorRate, 0f);
            vitals.HealthRate = 0.20f;
            brain.Tick(0.05);

            Assert.AreEqual(2, brain.Phase, "55% 전환이 같은 틱에 시작된다");
            Assert.AreEqual(EMucheolSealState.Pending, gimmick.State);
            Assert.AreEqual(0.20f, gimmick.HealthFloorRate, 0f);
        }

        [Test]
        public void Gimmick_NeverDefersTransitionOrBlocksSelection()
        {
            Env env = new Env();
            Assert.IsFalse(env.Gimmick.DefersPhaseTransition);
            Assert.IsFalse(env.Gimmick.BlocksSelection);

            env.Activate();

            Assert.IsFalse(env.Gimmick.DefersPhaseTransition);
            Assert.IsFalse(env.Gimmick.BlocksSelection);
        }

        // 대기 전에는 선택 평가가 불려도 표식이 나오지 않는다
        [Test]
        public void TrySelect_BeforeTrigger_DoesNothing()
        {
            Env env = new Env();

            Assert.IsFalse(env.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition pattern));

            Assert.IsNull(pattern);
            Assert.AreEqual(EMucheolSealState.Dormant, env.Gimmick.State);
        }

        [Test]
        public void Hit_WhilePending_IsNotCountedAndFloorStillApplies()
        {
            Env env = new Env();
            env.Vitals.HealthRate = TRIGGER_RATE;
            env.Gimmick.Tick(0.0, 2);

            MucheolSealHit hit = MakeHit(FIRST_ACTION, RIGHT_ANGLE);
            MucheolSealVerdict verdict = env.Gimmick.ReportHit(in hit);

            Assert.AreEqual(EMucheolSealHitResult.NotActive, verdict.Result);
            Assert.AreEqual(TRIGGER_RATE, verdict.HealthFloorRate, 0f);
            Assert.IsFalse(verdict.ConsumesHit);

            env.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition _);
            Assert.AreEqual(0, env.Gimmick.HitCount, "표식이 나오기 전의 타격은 0타다");
        }

        /****************************************
        *        Direction Order And Counting
        ****************************************/

        [Test]
        public void Activation_StartsAtRightWithZeroHits()
        {
            Env env = new Env();
            env.Activate();

            Assert.AreEqual(EMucheolSealDirection.Right, env.Gimmick.CurrentDirection);
            Assert.AreEqual(0, env.Gimmick.HitCount);
            Assert.IsTrue(env.Gimmick.IsMarkerVisible);
        }

        [Test]
        public void Hits_FollowRightThenLeftThenRearOnly()
        {
            Env env = new Env();
            env.Activate();

            // 왼쪽과 후방을 먼저 쳐도 오른쪽이 채워지기 전에는 오르지 않는다
            Assert.AreEqual(EMucheolSealHitResult.WrongDirection, env.Hit(LEFT_ANGLE));
            Assert.AreEqual(EMucheolSealHitResult.WrongDirection, env.Hit(REAR_ANGLE));
            Assert.AreEqual(0, env.Gimmick.HitCount);

            env.Fill(RIGHT_ANGLE, SOLO_HITS - 1);
            Assert.AreEqual(EMucheolSealHitResult.DirectionCleared, env.Hit(RIGHT_ANGLE));
            Assert.AreEqual(EMucheolSealDirection.Left, env.Gimmick.CurrentDirection);
            Assert.AreEqual(0, env.Gimmick.HitCount);

            Assert.AreEqual(EMucheolSealHitResult.WrongDirection, env.Hit(RIGHT_ANGLE));
            env.Fill(LEFT_ANGLE, SOLO_HITS - 1);
            Assert.AreEqual(EMucheolSealHitResult.DirectionCleared, env.Hit(LEFT_ANGLE));
            Assert.AreEqual(EMucheolSealDirection.Rear, env.Gimmick.CurrentDirection);

            env.Fill(REAR_ANGLE, SOLO_HITS - 1);
            Assert.AreEqual(EMucheolSealHitResult.Broken, env.Hit(REAR_ANGLE));
            Assert.AreEqual(EMucheolSealState.Broken, env.Gimmick.State);
        }

        [Test]
        public void Hit_FromFront_IsNotCounted()
        {
            Env env = new Env();
            env.Activate();

            Assert.AreEqual(EMucheolSealHitResult.WrongDirection, env.Hit(FRONT_ANGLE));
            Assert.AreEqual(0, env.Gimmick.HitCount);
        }

        [Test]
        public void Hit_WrongDirection_HasNoPenaltyAndKeepsProgress()
        {
            Env env = new Env();
            env.Activate();
            env.Fill(RIGHT_ANGLE, 3);

            env.Hit(LEFT_ANGLE);
            env.Hit(REAR_ANGLE);
            env.Hit(FRONT_ANGLE);

            Assert.AreEqual(3, env.Gimmick.HitCount);
            Assert.AreEqual(EMucheolSealDirection.Right, env.Gimmick.CurrentDirection);
        }

        [Test]
        public void Hit_DamageOverTimeAndSummon_AreExcluded()
        {
            Env env = new Env();
            env.Activate();

            MucheolSealHit dot    = MakeHit(1, RIGHT_ANGLE, isDot: true);
            MucheolSealHit summon = MakeHit(2, RIGHT_ANGLE, isSummon: true);

            Assert.AreEqual(EMucheolSealHitResult.Excluded, env.Gimmick.ReportHit(in dot).Result);
            Assert.AreEqual(EMucheolSealHitResult.Excluded, env.Gimmick.ReportHit(in summon).Result);
            Assert.AreEqual(0, env.Gimmick.HitCount);
        }

        [Test]
        public void Hit_UndeterminableDirectionOrInvalidAction_IsNotCounted()
        {
            Env env = new Env();
            env.Activate();

            MucheolSealHit noDirection = new MucheolSealHit(1, false, false, Vector3.forward, Vector3.zero);
            MucheolSealHit vertical    = new MucheolSealHit(2, false, false, Vector3.forward, Vector3.up);
            MucheolSealHit noForward   = new MucheolSealHit(3, false, false, Vector3.zero, Vector3.right);
            MucheolSealHit noAction    = MakeHit(0, RIGHT_ANGLE);

            Assert.AreEqual(EMucheolSealHitResult.Unclassifiable, env.Gimmick.ReportHit(in noDirection).Result);
            Assert.AreEqual(EMucheolSealHitResult.Unclassifiable, env.Gimmick.ReportHit(in vertical).Result);
            Assert.AreEqual(EMucheolSealHitResult.Unclassifiable, env.Gimmick.ReportHit(in noForward).Result);
            Assert.AreEqual(EMucheolSealHitResult.Unclassifiable, env.Gimmick.ReportHit(in noAction).Result);
            Assert.AreEqual(0, env.Gimmick.HitCount);
        }

        [Test]
        public void Hit_SameActionReportedTwice_CountsOnce()
        {
            Env env = new Env();
            env.Activate();
            MucheolSealHit hit = MakeHit(7, RIGHT_ANGLE);

            Assert.AreEqual(EMucheolSealHitResult.Counted, env.Gimmick.ReportHit(in hit).Result);
            Assert.AreEqual(EMucheolSealHitResult.Duplicate, env.Gimmick.ReportHit(in hit).Result);
            Assert.AreEqual(EMucheolSealHitResult.Duplicate, env.Gimmick.ReportHit(in hit).Result);

            Assert.AreEqual(1, env.Gimmick.HitCount);
        }

        [Test]
        public void Hit_DifferentActions_EachCountOnce()
        {
            Env env = new Env();
            env.Activate();

            env.Hit(RIGHT_ANGLE);
            env.Hit(RIGHT_ANGLE);
            env.Hit(RIGHT_ANGLE);

            Assert.AreEqual(3, env.Gimmick.HitCount);
        }

        [Test]
        public void Hit_CountedSerialRisesOnlyOnCountedHits()
        {
            Env env = new Env();
            env.Activate();
            int before = env.Gimmick.CountedHitSerial;

            env.Hit(LEFT_ANGLE);
            Assert.AreEqual(before, env.Gimmick.CountedHitSerial);

            env.Hit(RIGHT_ANGLE);
            Assert.AreEqual(before + 1, env.Gimmick.CountedHitSerial);
        }

        /****************************************
        *        Required Hits By Starting Count
        ****************************************/

        [TestCase(1, SOLO_HITS)]
        [TestCase(2, GROUP_HITS)]
        [TestCase(3, GROUP_HITS)]
        [TestCase(4, GROUP_HITS)]
        public void RequiredHits_ComesFromStartingCount(int startingCount, int expected)
        {
            Env env = new Env(startingCount);
            env.Activate();

            Assert.AreEqual(expected, env.Gimmick.RequiredHits);

            env.Fill(RIGHT_ANGLE, expected - 1);
            Assert.AreEqual(EMucheolSealDirection.Right, env.Gimmick.CurrentDirection);
            Assert.AreEqual(EMucheolSealHitResult.DirectionCleared, env.Hit(RIGHT_ANGLE));
            Assert.AreEqual(EMucheolSealDirection.Left, env.Gimmick.CurrentDirection);
        }

        [Test]
        public void Spec_InvalidStartingCountOrValues_Throw()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => MakeSpec().GetHitsPerDirection(0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new MucheolSealGimmickSpec(0f, SOLO_HITS, GROUP_HITS, FRONT_HALF, SIDE_LIMIT));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new MucheolSealGimmickSpec(TRIGGER_RATE, 0, GROUP_HITS, FRONT_HALF, SIDE_LIMIT));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new MucheolSealGimmickSpec(TRIGGER_RATE, SOLO_HITS, GROUP_HITS, SIDE_LIMIT, FRONT_HALF));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new Env(0));
        }

        /****************************************
        *        Time, Break And Stop
        ****************************************/

        [Test]
        public void Progress_SurvivesLongTimeAndRepeatedTicks()
        {
            Env env = new Env();
            env.Activate();
            env.Fill(RIGHT_ANGLE, 4);

            for (int i = 1; i <= 1000; ++i)
            {
                env.Gimmick.Tick(i * 10.0, 2);
                env.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition _);
            }

            Assert.AreEqual(4, env.Gimmick.HitCount);
            Assert.AreEqual(EMucheolSealDirection.Right, env.Gimmick.CurrentDirection);
        }

        [Test]
        public void Progress_DoesNotRestartWhenHealthRecoversOrRepeatsTrigger()
        {
            Env env = new Env();
            env.Activate();
            env.Fill(RIGHT_ANGLE, 2);

            env.Vitals.HealthRate = 1f;
            env.Gimmick.Tick(1.0, 2);
            env.Vitals.HealthRate = TRIGGER_RATE;
            env.Gimmick.Tick(2.0, 2);

            Assert.AreEqual(2, env.Gimmick.HitCount);
            Assert.AreEqual(EMucheolSealState.Active, env.Gimmick.State);
        }

        [Test]
        public void FinalRearHit_BreaksSealWithoutDamageAndNextHitIsFree()
        {
            Env env = new Env();
            env.Activate();
            env.Fill(RIGHT_ANGLE, SOLO_HITS);
            env.Fill(LEFT_ANGLE, SOLO_HITS);
            env.Fill(REAR_ANGLE, SOLO_HITS - 1);

            MucheolSealHit last = MakeHit(env.NextAction++, REAR_ANGLE);
            MucheolSealVerdict breaking = env.Gimmick.ReportHit(in last);

            Assert.AreEqual(EMucheolSealHitResult.Broken, breaking.Result);
            Assert.AreEqual(TRIGGER_RATE, breaking.HealthFloorRate, 0f, "깨는 타격에도 하한이 적용돼 체력 피해가 없다");
            Assert.IsTrue(breaking.ConsumesHit);

            MucheolSealHit next = MakeHit(env.NextAction++, REAR_ANGLE);
            MucheolSealVerdict after = env.Gimmick.ReportHit(in next);

            Assert.AreEqual(EMucheolSealHitResult.NotActive, after.Result);
            Assert.AreEqual(0f, after.HealthFloorRate, 0f, "다음 유효 공격부터 체력 피해가 다시 들어간다");
            Assert.IsFalse(after.ConsumesHit);
            Assert.AreEqual(0f, env.Gimmick.HealthFloorRate, 0f);
            Assert.AreEqual(EMucheolSealDirection.None, env.Gimmick.CurrentDirection);
        }

        [Test]
        public void Broken_NeverRearmsEvenWhenHealthStaysLow()
        {
            Env env = new Env();
            env.Activate();
            env.Fill(RIGHT_ANGLE, SOLO_HITS);
            env.Fill(LEFT_ANGLE, SOLO_HITS);
            env.Fill(REAR_ANGLE, SOLO_HITS);

            env.Vitals.HealthRate = 0.2f;
            env.Gimmick.Tick(5.0, 2);
            env.Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition _);

            Assert.AreEqual(EMucheolSealState.Broken, env.Gimmick.State);
            Assert.AreEqual(0f, env.Gimmick.HealthFloorRate, 0f);
        }

        [Test]
        public void ConsumedHits_AreReportedAsConsumedButOtherBlockedHitsAreNot()
        {
            Env env = new Env();
            env.Activate();

            MucheolSealHit counted = MakeHit(1, RIGHT_ANGLE);
            MucheolSealHit wrong   = MakeHit(2, LEFT_ANGLE);

            Assert.IsTrue(env.Gimmick.ReportHit(in counted).ConsumesHit);
            Assert.IsFalse(env.Gimmick.ReportHit(in wrong).ConsumesHit);
            Assert.AreEqual(TRIGGER_RATE, env.Gimmick.ReportHit(in wrong).HealthFloorRate, 0f);
        }

        [Test]
        public void Stop_FreezesStateAndDropsFloor()
        {
            Env env = new Env();
            env.Activate();
            env.Fill(RIGHT_ANGLE, 2);

            env.Gimmick.Stop(EBossInterruptReason.CombatEnd);

            Assert.AreEqual(EMucheolSealState.Stopped, env.Gimmick.State);
            Assert.AreEqual(0f, env.Gimmick.HealthFloorRate, 0f);
            Assert.AreEqual(EMucheolSealHitResult.NotActive, env.Hit(RIGHT_ANGLE));
        }

        /****************************************
        *        Geometry Boundaries
        ****************************************/

        [TestCase(0f,      EMucheolSealDirection.Front)]
        [TestCase(44.99f,  EMucheolSealDirection.Front)]
        [TestCase(45f,     EMucheolSealDirection.Front)]
        [TestCase(-45f,    EMucheolSealDirection.Front)]
        [TestCase(45.1f,   EMucheolSealDirection.Right)]
        [TestCase(90f,     EMucheolSealDirection.Right)]
        [TestCase(135f,    EMucheolSealDirection.Right)]
        [TestCase(-45.1f,  EMucheolSealDirection.Left)]
        [TestCase(-90f,    EMucheolSealDirection.Left)]
        [TestCase(-135f,   EMucheolSealDirection.Left)]
        [TestCase(135.1f,  EMucheolSealDirection.Rear)]
        [TestCase(-135.1f, EMucheolSealDirection.Rear)]
        [TestCase(180f,    EMucheolSealDirection.Rear)]
        public void Classify_UsesBoundariesOwnedByOneZone(float angle, EMucheolSealDirection expected)
        {
            EMucheolSealDirection actual = MucheolSealGeometry.Classify(Vector3.forward, Incoming(angle, Vector3.forward),
                FRONT_HALF, SIDE_LIMIT);

            Assert.AreEqual(expected, actual);
        }

        // 보스가 회전하면 같은 월드 방향도 다른 구역이 된다, 적중 확정 순간의 시선을 쓴다
        [Test]
        public void Classify_FollowsBossRotation()
        {
            Vector3 fromEast = Vector3.right;

            Assert.AreEqual(EMucheolSealDirection.Right, MucheolSealGeometry.Classify(Vector3.forward, fromEast, FRONT_HALF, SIDE_LIMIT));
            Assert.AreEqual(EMucheolSealDirection.Front, MucheolSealGeometry.Classify(Vector3.right, fromEast, FRONT_HALF, SIDE_LIMIT));
            Assert.AreEqual(EMucheolSealDirection.Left, MucheolSealGeometry.Classify(Vector3.back, fromEast, FRONT_HALF, SIDE_LIMIT));
            Assert.AreEqual(EMucheolSealDirection.Rear, MucheolSealGeometry.Classify(Vector3.left, fromEast, FRONT_HALF, SIDE_LIMIT));
        }

        [Test]
        public void Hit_UsesTheBossForwardCarriedByTheHit()
        {
            Env env = new Env();
            env.Activate();

            // 보스가 동쪽을 보고 있으면 북쪽에서 온 타격은 왼쪽이고 남쪽에서 온 타격은 오른쪽이다
            MucheolSealHit fromSouth = new MucheolSealHit(1, false, false, Vector3.right, Vector3.back);
            MucheolSealHit fromNorth = new MucheolSealHit(2, false, false, Vector3.right, Vector3.forward);

            Assert.AreEqual(EMucheolSealHitResult.Counted, env.Gimmick.ReportHit(in fromSouth).Result);
            Assert.AreEqual(EMucheolSealHitResult.WrongDirection, env.Gimmick.ReportHit(in fromNorth).Result);
        }

        // 투사체는 발사자가 아니라 탄이 들어온 방향(진행 방향의 반대)으로 가른다
        [Test]
        public void Hit_ProjectileUsesArrivalDirectionNotShooterPosition()
        {
            Env env = new Env();
            env.Activate();

            Vector3 shooterBehindBoss = Vector3.back * 20f;
            Vector3 projectileVelocity = Vector3.left;
            Vector3 arrival = -projectileVelocity;

            MucheolSealHit viaShooter = new MucheolSealHit(1, false, false, Vector3.forward, shooterBehindBoss);
            MucheolSealHit viaArrival = new MucheolSealHit(2, false, false, Vector3.forward, arrival);

            Assert.AreEqual(EMucheolSealHitResult.WrongDirection, env.Gimmick.ReportHit(in viaShooter).Result);
            Assert.AreEqual(EMucheolSealHitResult.Counted, env.Gimmick.ReportHit(in viaArrival).Result);
        }

        /****************************************
        *        Real Boss Brain
        ****************************************/

        // 진행 중인 패턴은 취소되지 않고 끝나며, 표식은 그 뒤에 나온다. 가짜 체력이라 체력을 직접 낮춘다
        [Test]
        public void Brain_PatternInProgressFinishesBeforeMarkerAppears()
        {
            FakeBossVitals vitals  = new FakeBossVitals();
            FakeBossCatalog catalog = new FakeBossCatalog();
            FakeControlSource control = new FakeControlSource();
            FakeDeathPolicy death = new FakeDeathPolicy();
            SpyCasts body = new SpyCasts();

            catalog.Boss = BossTestFixtures.MakeBoss(0.1f);
            catalog.Patterns.Add(BossTestFixtures.MakePattern(MucheolPatternFactory.P04_ID, 10f, 1f, 0.5f, 0.5f));

            MucheolPatternFactory factory = new MucheolPatternFactory(3541f, null, null, null, null, null, null,
                new MucheolChargeSpec(2.5f));
            MucheolSealGimmick gimmick = new MucheolSealGimmick(vitals, MakeSpec(), 1);

            FakeCombatTarget self = new FakeCombatTarget { CombatId = BossHarness.SELF_ID, Faction = ECombatFaction.Enemy };
            FakeTargetQuery query = new FakeTargetQuery();
            query.Players.Add(new FakeCombatTarget
            {
                CombatId = BossHarness.PLAYER_ID, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f)
            });

            BossBrain brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
            {
                Catalog     = catalog,
                Self        = self,
                Vitals      = vitals,
                Targets     = query,
                Damage      = new FakeDamageResolver(),
                Patterns    = factory,
                Effects     = new FakeEffectLifetime(),
                Control     = control,
                Random      = new FakeBossRandom(),
                DeathPolicy = death,
                Casts       = body
            });
            Assert.IsTrue(brain.AddGimmick(gimmick));
            Assert.IsTrue(brain.Begin(0.0));

            brain.Tick(0.0);
            Assert.AreEqual(MucheolPatternFactory.P04_ID, brain.ActivePatternId);
            Assert.AreNotEqual(EBossAttackState.Ready, brain.AttackState);

            // 패턴이 한창일 때 체력이 발동 비율에 닿는다
            vitals.HealthRate = TRIGGER_RATE;
            double now = 0.0;
            int executedBefore = body.Executed.Count;
            while (brain.AttackState != EBossAttackState.Ready)
            {
                now += 0.05;
                brain.Tick(now);

                if (brain.AttackState != EBossAttackState.Ready)
                {
                    Assert.AreEqual(EMucheolSealState.Pending, gimmick.State, "패턴이 끝나기 전에는 표식이 없다");
                    Assert.AreEqual(MucheolPatternFactory.P04_ID, brain.ActivePatternId, "진행 중인 패턴은 취소되지 않는다");
                }

                Assert.Less(now, 30.0);
            }

            Assert.AreEqual(1, body.Finished.Count, "진행 중이던 패턴은 정상 종료한다");
            Assert.AreEqual(EMucheolSealState.Pending, gimmick.State);

            now += 0.05;
            brain.Tick(now);

            Assert.AreEqual(EMucheolSealState.Active, gimmick.State, "공격이 비면 표식이 나온다");
            Assert.AreEqual(EMucheolSealDirection.Right, gimmick.CurrentDirection);
            Assert.AreEqual(0, gimmick.HitCount);
            Assert.GreaterOrEqual(body.Executed.Count, executedBefore);

            // 표식이 나온 뒤에도 보스는 기존 패턴을 계속 쓴다
            brain.Tick(now + 0.05);
            Assert.AreNotEqual(string.Empty, brain.ActivePatternId);
        }

        private sealed class SpyCasts : IBossCastListener
        {
            public List<int> Executed = new List<int>();
            public List<int> Finished = new List<int>();

            public void OnCastExecuted(int castInstanceId, double serverTime)
            {
                Executed.Add(castInstanceId);
            }

            public void OnCastFinished(int castInstanceId, double serverTime)
            {
                Finished.Add(castInstanceId);
            }
        }
    }
}
