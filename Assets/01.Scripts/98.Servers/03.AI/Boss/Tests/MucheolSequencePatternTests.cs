// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 무철 2페이즈 순차 타격(ph2_p01, p02, p04, p05 모양) 실행 검증. 수치는 모두 이 테스트가 주입하는 시험 데이터이며 밸런스 값이 아니다.
    // 경계(예고 끝, 실행 끝, 각도)는 부동소수 오차가 판정을 뒤집지 않게 경계에서 떨어진 시각과 각도로 검증한다
    public class MucheolSequencePatternTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float TEST_HREF   = 3541f; // 테스트가 명시적으로 주입하는 값이다
        private const float TOLERANCE   = 0.0001f;
        private const float ANGLE_TOLERANCE = 0.1f;
        private const float TIME_MARGIN = 0.05f; // 초, 경계 전후로 두는 여유
        private const int   FAR_ID      = 11;
        private const int   NEAR_ID     = 12;

        // 시험용 시간, 예고 0.9 -> 실행 1.0 -> 회복 1.5
        private const float TELEGRAPH  = 0.9f;
        private const float ACTIVE     = 1.0f;
        private const float RECOVERY   = 1.5f;
        private const float EXECUTE_AT = TELEGRAPH;
        private const float END_AT     = TELEGRAPH + ACTIVE + RECOVERY;

        // 시험용 형태
        private const float ZONE_RANGE   = 5f;
        private const float ZONE_ARC     = 60f;
        private const float ZONE_OFFSET  = 30f;
        private const float SLAM_ARC     = 70f;
        private const float LINE_LENGTH  = 6f;
        private const float LINE_WIDTH   = 3f;
        private const float SECOND_RADIUS = 6f;
        private const float STEP_DELAY   = 0.45f;
        private const float DAMAGE_A     = 0.16f;
        private const float DAMAGE_B     = 0.30f;

        /****************************************
        *               Helpers
        ****************************************/

        // 좌우 구역 둘, 0.45초 간격
        private static MucheolSequenceSpec TwoZones()
        {
            return new MucheolSequenceSpec(
                MucheolSequenceStep.Fan(ZONE_RANGE, ZONE_ARC, -ZONE_OFFSET, DAMAGE_A, 0f),
                MucheolSequenceStep.Fan(ZONE_RANGE, ZONE_ARC, ZONE_OFFSET, DAMAGE_A, STEP_DELAY));
        }

        // 구역 둘 뒤 정면 내려찍기
        private static MucheolSequenceSpec ZonesThenSlam()
        {
            return new MucheolSequenceSpec(
                MucheolSequenceStep.Fan(ZONE_RANGE, ZONE_ARC, -ZONE_OFFSET, DAMAGE_A, 0f),
                MucheolSequenceStep.Fan(ZONE_RANGE, ZONE_ARC, ZONE_OFFSET, DAMAGE_A, STEP_DELAY),
                MucheolSequenceStep.Fan(ZONE_RANGE, SLAM_ARC, 0f, DAMAGE_B, ACTIVE));
        }

        private static MucheolSequenceSpec SlamThenLine()
        {
            return new MucheolSequenceSpec(
                MucheolSequenceStep.Fan(ZONE_RANGE, SLAM_ARC, 0f, DAMAGE_B, 0f),
                MucheolSequenceStep.Line(LINE_LENGTH, LINE_WIDTH, 0f, DAMAGE_A, 0.5f));
        }

        private static MucheolSequenceSpec TwoCircles()
        {
            return new MucheolSequenceSpec(
                MucheolSequenceStep.Circle(ZONE_RANGE, DAMAGE_A, 0f),
                MucheolSequenceStep.Circle(SECOND_RADIUS, DAMAGE_A, 0.7f));
        }

        private static PatternDefinition MakeDefinition(float tracking, float turnRate, bool guardable)
        {
            return new PatternDefinition("mucheol_ph2_p01", MucheolPatternFactory.BOSS_ID, 2, 10f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                TELEGRAPH, tracking, turnRate, ACTIVE, RECOVERY, EnemyCatalogSO.AttackShapeSpec.Fan(ZONE_RANGE, 120f), 0.1f, guardable);
        }

        // 공격 FSM에 올린 최소 구성. 보스는 원점에서 +z를 보고, 추적 대상(10번)은 보스에서 멀리 둬 판정 대상과 섞이지 않게 한다
        private sealed class Rig
        {
            public FakeCombatTarget   Self       { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy };
            public FakeCombatTarget   Tracked    { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 40f) };
            public Phase2Query        Query      { get; } = new Phase2Query();
            public FakeDamageResolver Resolver   { get; } = new FakeDamageResolver();
            public SequenceRecorder   Listener   { get; } = new SequenceRecorder();
            public BossAttackFsm      Fsm        { get; } = new BossAttackFsm();
            public BossDamageGate     Gate       { get; }
            public MucheolSequencePattern Pattern { get; }
            public PatternDefinition  Definition { get; }
            public BossPatternContext Context    { get; }

            public Rig(MucheolSequenceSpec spec, float tracking = 0f, float turnRate = 0f, bool guardable = true)
            {
                Query.Players.Add(Tracked);
                Gate       = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = MakeDefinition(tracking, turnRate, guardable);
                Pattern    = new MucheolSequencePattern(TEST_HREF, spec, Listener);
                Context    = new BossPatternContext(Definition, Gate.IssueInstanceId(), Tracked.CombatId, Self, Query, Gate);
            }

            public FakeCombatTarget AddPlayer(int id, Vector3 position)
            {
                FakeCombatTarget player = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player, Position = position };
                Query.Players.Add(player);
                return player;
            }

            public void Start(double serverTime = 0.0)
            {
                BossPatternContext context = Context;
                Assert.IsTrue(Fsm.Start(Definition, Pattern, in context, serverTime));
            }

            // from 다음 틱부터 to까지 일정 간격으로 틱한다
            public void TickRange(double from, double to, double step)
            {
                int count = (int)Math.Round((to - from) / step);
                for (int i = 1; i <= count; ++i)
                {
                    Fsm.Tick(from + (i * step));
                }
            }

            public int Count(int targetId)
            {
                return Phase2Helpers.RequestsFor(Resolver, targetId).Count;
            }
        }

        /****************************************
        *            Spec Validation
        ****************************************/

        [Test]
        public void Spec_ValidSteps_KeepsOrderAndCopiesInput()
        {
            MucheolSequenceStep[] source =
            {
                MucheolSequenceStep.Fan(ZONE_RANGE, ZONE_ARC, -ZONE_OFFSET, DAMAGE_A, 0f),
                MucheolSequenceStep.Line(LINE_LENGTH, LINE_WIDTH, 0f, DAMAGE_B, 0.5f)
            };
            MucheolSequenceSpec spec = new MucheolSequenceSpec(source);

            source[1] = MucheolSequenceStep.Circle(1f, 9f, 9f); // 만든 뒤 원본 배열을 바꿔도 영향이 없어야 한다

            Assert.AreEqual(2, spec.StepCount);
            Assert.AreEqual(EMucheolSequenceShape.Line, spec.Steps[1].Shape);
            Assert.AreEqual(LINE_LENGTH, spec.Steps[1].Range, TOLERANCE);
            Assert.AreEqual(LINE_WIDTH, spec.Steps[1].Width, TOLERANCE);
            Assert.AreEqual(-ZONE_OFFSET, spec.Steps[0].AimOffsetDegrees, TOLERANCE);
        }

        [Test]
        public void Spec_PublicSteps_CannotBeMutatedThroughTheListOrACast()
        {
            MucheolSequenceSpec spec = TwoZones();
            IReadOnlyList<MucheolSequenceStep> steps = spec.Steps;

            Assert.IsNull(steps as MucheolSequenceStep[]);

            IList<MucheolSequenceStep> asList = steps as IList<MucheolSequenceStep>;
            if (asList != null)
            {
                Assert.IsTrue(asList.IsReadOnly);
                Assert.Throws<NotSupportedException>(() => asList[0] = MucheolSequenceStep.Circle(1f, 1f, 0f));
                Assert.Throws<NotSupportedException>(() => asList.Clear());
            }
        }

        [Test]
        public void Spec_InvalidValues_Throw()
        {
            Assert.Throws<ArgumentException>(() => new MucheolSequenceSpec());
            Assert.Throws<ArgumentException>(() => new MucheolSequenceSpec((MucheolSequenceStep[])null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolSequenceSpec(MucheolSequenceStep.Fan(0f, 60f, 0f, 0.1f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolSequenceSpec(MucheolSequenceStep.Fan(5f, 0f, 0f, 0.1f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolSequenceSpec(MucheolSequenceStep.Fan(5f, 361f, 0f, 0.1f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolSequenceSpec(MucheolSequenceStep.Fan(5f, 60f, 181f, 0.1f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolSequenceSpec(MucheolSequenceStep.Fan(5f, 60f, float.NaN, 0.1f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolSequenceSpec(MucheolSequenceStep.Line(6f, 0f, 0f, 0.1f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolSequenceSpec(MucheolSequenceStep.Line(0f, 3f, 0f, 0.1f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolSequenceSpec(MucheolSequenceStep.Circle(0f, 0.1f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolSequenceSpec(MucheolSequenceStep.Circle(5f, 0f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolSequenceSpec(MucheolSequenceStep.Circle(5f, 0.1f, -0.1f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolSequenceSpec(MucheolSequenceStep.Circle(5f, 0.1f, float.NaN)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolSequenceSpec(
                MucheolSequenceStep.Circle(5f, 0.1f, 0.5f), MucheolSequenceStep.Circle(5f, 0.1f, 0.25f)));
        }

        [Test]
        public void Constructor_NullSpec_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new MucheolSequencePattern(TEST_HREF, null));
        }

        /****************************************
        *       Left And Right Zones (P01 Shape)
        ****************************************/

        [Test]
        public void Begin_ReportsTelegraphSecondsOriginAndSteps()
        {
            Rig rig = new Rig(TwoZones());
            rig.Self.Position = new Vector3(2f, 0f, 3f);
            rig.Start();

            Assert.AreEqual(1, rig.Listener.StartCount);
            Assert.AreEqual(TELEGRAPH, rig.Listener.StartSeconds, TOLERANCE);
            Assert.AreEqual(2, rig.Listener.StartSteps.Count);
            Assert.AreEqual(2f, rig.Listener.StartOrigin.x, TOLERANCE);
            Assert.AreEqual(3f, rig.Listener.StartOrigin.z, TOLERANCE);
        }

        [Test]
        public void TwoZones_LeftHitsFirstThenRightAtItsOwnTime()
        {
            Rig rig = new Rig(TwoZones());
            rig.AddPlayer(FAR_ID, Phase2Helpers.Polar(-45f, 3f));
            rig.AddPlayer(NEAR_ID, Phase2Helpers.Polar(45f, 3f));
            rig.Start();

            rig.Fsm.Tick(EXECUTE_AT - TIME_MARGIN);
            Assert.AreEqual(0, rig.Resolver.Requests.Count, "예고 중에는 피해가 없다");

            rig.Fsm.Tick(EXECUTE_AT + TIME_MARGIN);
            Assert.AreEqual(1, rig.Count(FAR_ID), "왼쪽 구역 첫 타");
            Assert.AreEqual(0, rig.Count(NEAR_ID));

            rig.Fsm.Tick(EXECUTE_AT + STEP_DELAY - TIME_MARGIN);
            Assert.AreEqual(0, rig.Count(NEAR_ID), "오른쪽 구역은 간격이 지나기 전에는 나가지 않는다");

            rig.Fsm.Tick(EXECUTE_AT + STEP_DELAY + TIME_MARGIN);
            Assert.AreEqual(1, rig.Count(FAR_ID), "왼쪽 대상은 오른쪽 구역에 맞지 않는다");
            Assert.AreEqual(1, rig.Count(NEAR_ID), "오른쪽 구역 둘째 타");
            CollectionAssert.AreEqual(new[] { 0, 1 }, rig.Listener.HitIndices);
            CollectionAssert.AreEqual(new[] { 1, 1 }, rig.Listener.HitCounts);
        }

        [Test]
        public void TwoZones_RangeAndAngleEdges()
        {
            Rig rig = new Rig(TwoZones());
            int inside = 21, tooFar = 22, tooWide = 23, behind = 24;
            rig.AddPlayer(inside, Phase2Helpers.Polar(-45f, ZONE_RANGE - TIME_MARGIN));
            rig.AddPlayer(tooFar, Phase2Helpers.Polar(-45f, ZONE_RANGE + TIME_MARGIN));
            rig.AddPlayer(tooWide, Phase2Helpers.Polar(-65f, 3f));  // 왼쪽 구역은 -60도까지다
            rig.AddPlayer(behind, Phase2Helpers.Polar(180f, 2f));
            rig.Start();

            rig.Fsm.Tick(END_AT);

            Assert.AreEqual(1, rig.Count(inside));
            Assert.AreEqual(0, rig.Count(tooFar));
            Assert.AreEqual(0, rig.Count(tooWide));
            Assert.AreEqual(0, rig.Count(behind));
        }

        [Test]
        public void TwoZones_DamageIsStepDamageRefTimesHref()
        {
            Rig rig = new Rig(new MucheolSequenceSpec(
                MucheolSequenceStep.Fan(ZONE_RANGE, 360f, 0f, DAMAGE_A, 0f),
                MucheolSequenceStep.Fan(ZONE_RANGE, 360f, 0f, DAMAGE_B, STEP_DELAY)));
            rig.AddPlayer(FAR_ID, Phase2Helpers.Polar(0f, 2f));
            rig.Start();

            rig.Fsm.Tick(END_AT);

            List<DamageRequest> hits = Phase2Helpers.RequestsFor(rig.Resolver, FAR_ID);
            Assert.AreEqual(2, hits.Count);
            Assert.AreEqual(DAMAGE_A * TEST_HREF, hits[0].RawDamage, 0.001f);
            Assert.AreEqual(DAMAGE_B * TEST_HREF, hits[1].RawDamage, 0.001f);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Strike_GuardableFlagFollowsDefinition(bool guardable)
        {
            Rig rig = new Rig(TwoZones(), 0f, 0f, guardable);
            rig.AddPlayer(FAR_ID, Phase2Helpers.Polar(-45f, 3f));
            rig.Start();

            rig.Fsm.Tick(END_AT);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(!guardable, rig.Resolver.Requests[0].IsUnblockable);
        }

        /****************************************
        *      Slam Then Line (P02 Shape)
        ****************************************/

        [Test]
        public void SlamThenLine_FanAtFirstThenForwardLineHalfSecondLater()
        {
            Rig rig = new Rig(SlamThenLine());
            int inBoth = 21, lineOnly = 22, fanOnly = 23, beyond = 24, behind = 25;
            rig.AddPlayer(inBoth, new Vector3(0f, 0f, 4f));
            rig.AddPlayer(lineOnly, new Vector3(1.4f, 0f, 5.8f));  // 부채꼴 사거리(5m) 밖, 직사각형(길이 6m, 폭 3m) 안
            rig.AddPlayer(fanOnly, new Vector3(1.6f, 0f, 3f));     // 부채꼴 안, 직사각형 폭(좌우 1.5m) 밖
            rig.AddPlayer(beyond, new Vector3(0f, 0f, 6f + TIME_MARGIN));
            rig.AddPlayer(behind, new Vector3(0f, 0f, -1f));
            rig.Start();

            rig.Fsm.Tick(EXECUTE_AT + TIME_MARGIN);
            Assert.AreEqual(1, rig.Count(inBoth));
            Assert.AreEqual(0, rig.Count(lineOnly), "충격파는 0.5초 뒤에 나온다");
            Assert.AreEqual(1, rig.Count(fanOnly));

            rig.Fsm.Tick(EXECUTE_AT + 0.5f + TIME_MARGIN);
            Assert.AreEqual(2, rig.Count(inBoth));
            Assert.AreEqual(1, rig.Count(lineOnly));
            Assert.AreEqual(1, rig.Count(fanOnly));
            Assert.AreEqual(0, rig.Count(beyond));
            Assert.AreEqual(0, rig.Count(behind));

            List<DamageRequest> hits = Phase2Helpers.RequestsFor(rig.Resolver, inBoth);
            Assert.AreEqual(DAMAGE_B * TEST_HREF, hits[0].RawDamage, 0.001f);
            Assert.AreEqual(DAMAGE_A * TEST_HREF, hits[1].RawDamage, 0.001f);
        }

        [Test]
        public void SlamThenLine_LineFollowsTheLockedAimNotTheTargetsLaterPosition()
        {
            // 추적 0.4초, 회전 150도/초면 최대 60도까지 돈다. 대상이 오른쪽 90도에 있으면 방향은 60도에 고정된다
            Rig rig = new Rig(SlamThenLine(), 0.4f, 150f);
            rig.Tracked.Position = Phase2Helpers.Polar(90f, 3f);
            int onLockedAim = 21, onTarget = 22;
            rig.AddPlayer(onLockedAim, Phase2Helpers.Polar(60f, 5.5f));  // 고정된 방향의 직선 위, 부채꼴 사거리 밖
            rig.AddPlayer(onTarget, Phase2Helpers.Polar(90f, 5.5f));
            rig.Start();

            rig.Fsm.Tick(END_AT);

            Assert.AreEqual(60f, Vector3.Angle(Vector3.forward, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(1, rig.Count(onLockedAim));
            Assert.AreEqual(0, rig.Count(onTarget));
        }

        /****************************************
        *      Two Circles (P04 Shape)
        ****************************************/

        [Test]
        public void TwoCircles_SecondRadiusIsLargerAndComesLater()
        {
            Rig rig = new Rig(TwoCircles());
            int inner = 21, ring = 22, outside = 23, onEdge = 24;
            rig.AddPlayer(inner, Phase2Helpers.Polar(120f, 3f));
            rig.AddPlayer(ring, Phase2Helpers.Polar(-80f, ZONE_RANGE + 0.5f));  // 5m 밖, 6m 안
            rig.AddPlayer(outside, Phase2Helpers.Polar(10f, SECOND_RADIUS + 0.5f));
            rig.AddPlayer(onEdge, new Vector3(0f, 0f, ZONE_RANGE));              // 첫 원 경계 위, 경계는 안이다
            rig.Start();

            rig.Fsm.Tick(EXECUTE_AT + TIME_MARGIN);
            Assert.AreEqual(1, rig.Count(inner));
            Assert.AreEqual(0, rig.Count(ring), "첫 원은 반경 5m라 밖이다");
            Assert.AreEqual(1, rig.Count(onEdge));

            rig.Fsm.Tick(EXECUTE_AT + 0.7f + TIME_MARGIN);
            Assert.AreEqual(2, rig.Count(inner), "안쪽 대상은 두 원에 모두 한 번씩 맞는다");
            Assert.AreEqual(1, rig.Count(ring));
            Assert.AreEqual(0, rig.Count(outside));
            Assert.AreEqual(2, rig.Count(onEdge));
        }

        [Test]
        public void TwoCircles_ReHitAcrossStepsUsesDifferentActionIdsAndSameCast()
        {
            Rig rig = new Rig(TwoCircles());
            rig.AddPlayer(FAR_ID, Phase2Helpers.Polar(30f, 3f));
            rig.Start();

            rig.Fsm.Tick(END_AT);

            List<DamageRequest> hits = Phase2Helpers.RequestsFor(rig.Resolver, FAR_ID);
            Assert.AreEqual(2, hits.Count);
            Assert.AreEqual(rig.Context.CastInstanceId, hits[0].CastInstanceId);
            Assert.AreEqual(rig.Context.CastInstanceId, hits[1].CastInstanceId);
            Assert.AreNotEqual(hits[0].ActionInstanceId, hits[1].ActionInstanceId);
        }

        /****************************************
        *      Zones Then Slam (P05 Shape)
        ****************************************/

        [Test]
        public void ZonesThenSlam_EachHitHasItsOwnRangeAndOffset()
        {
            Rig rig = new Rig(ZonesThenSlam());
            int leftOnly = 21, rightOnly = 22, leftAndSlam = 23, beyondSlam = 24;
            rig.AddPlayer(leftOnly, Phase2Helpers.Polar(-45f, 3f));     // 왼쪽 구역만, 내려찍기(+-35도) 밖
            rig.AddPlayer(rightOnly, Phase2Helpers.Polar(45f, 3f));
            rig.AddPlayer(leftAndSlam, Phase2Helpers.Polar(-20f, 3f));  // 왼쪽 구역과 내려찍기
            rig.AddPlayer(beyondSlam, Phase2Helpers.Polar(-20f, 5f + TIME_MARGIN));
            rig.Start();

            rig.Fsm.Tick(END_AT);

            Assert.AreEqual(1, rig.Count(leftOnly));
            Assert.AreEqual(1, rig.Count(rightOnly));
            Assert.AreEqual(2, rig.Count(leftAndSlam));
            Assert.AreEqual(0, rig.Count(beyondSlam));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, rig.Listener.HitIndices);
        }

        [Test]
        public void ZonesThenSlam_SlamUsesItsOwnDamageAfterTwoZones()
        {
            Rig rig = new Rig(ZonesThenSlam());
            rig.AddPlayer(FAR_ID, Phase2Helpers.Polar(0f, 3f));  // 두 구역의 공유 경계, 두 구역 모두 안이다
            rig.Start();

            rig.Fsm.Tick(END_AT);

            List<DamageRequest> hits = Phase2Helpers.RequestsFor(rig.Resolver, FAR_ID);
            Assert.AreEqual(3, hits.Count, "한 시전 안에서 세 타");
            Assert.AreEqual(DAMAGE_B * TEST_HREF, hits[2].RawDamage, 0.001f);
            Assert.AreEqual(3, new HashSet<int>(new[] { hits[0].ActionInstanceId, hits[1].ActionInstanceId, hits[2].ActionInstanceId }).Count);
        }

        [Test]
        public void ZonesThenSlam_TargetMovedBetweenHits_IsJudgedAtItsPositionWhenEachHitResolves()
        {
            Rig rig = new Rig(ZonesThenSlam());
            FakeCombatTarget mover = rig.AddPlayer(FAR_ID, Phase2Helpers.Polar(-45f, 3f));
            FakeCombatTarget stayer = rig.AddPlayer(NEAR_ID, Phase2Helpers.Polar(-45f, 3f));
            rig.Start();

            rig.Fsm.Tick(EXECUTE_AT + TIME_MARGIN);
            Assert.AreEqual(1, rig.Count(FAR_ID));

            mover.Position = Phase2Helpers.Polar(120f, 3f);  // 둘째 구역과 내려찍기 모두 밖
            rig.Fsm.Tick(END_AT);

            Assert.AreEqual(1, rig.Count(FAR_ID), "벗어난 대상은 이후 타에 맞지 않는다");
            Assert.AreEqual(1, rig.Count(NEAR_ID), "제자리 대상은 왼쪽 구역에만 맞는다");
            Assert.IsNotNull(stayer);
        }

        [Test]
        public void ZonesThenSlam_TargetMovedIntoTheNextZone_IsHitByThatHitAndRehitIsAllowedAcrossHits()
        {
            Rig rig = new Rig(ZonesThenSlam());
            FakeCombatTarget mover = rig.AddPlayer(FAR_ID, Phase2Helpers.Polar(-45f, 3f));
            rig.Start();

            rig.Fsm.Tick(EXECUTE_AT + TIME_MARGIN);
            mover.Position = Phase2Helpers.Polar(45f, 3f);
            rig.Fsm.Tick(END_AT);

            Assert.AreEqual(2, rig.Count(FAR_ID), "왼쪽 구역에서 한 번, 옮긴 뒤 오른쪽 구역에서 한 번");
            Assert.AreEqual(0, rig.Resolver.Requests.FindAll(r => r.HitTargetId == FAR_ID && r.RawDamage > DAMAGE_A * TEST_HREF + 1f).Count);
        }

        /****************************************
        *      Duplicates, Cancel, Large Tick
        ****************************************/

        [Test]
        public void Tick_ManyFineTicks_EachTargetIsHitOncePerStep()
        {
            Rig rig = new Rig(ZonesThenSlam());
            rig.AddPlayer(FAR_ID, Phase2Helpers.Polar(-20f, 3f));  // 왼쪽 구역(0)과 내려찍기(2)
            rig.Start();

            rig.TickRange(0.0, END_AT + 1.0, 0.01);

            List<DamageRequest> hits = Phase2Helpers.RequestsFor(rig.Resolver, FAR_ID);
            Assert.AreEqual(2, hits.Count);
            Assert.AreNotEqual(hits[0].ActionInstanceId, hits[1].ActionInstanceId);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, rig.Listener.HitIndices);
            Assert.AreEqual(EBossAttackState.Ready, rig.Fsm.State);
        }

        [Test]
        public void Tick_SingleHugeTick_ResolvesEveryStepInOrderExactlyOnce()
        {
            Rig rig = new Rig(ZonesThenSlam());
            rig.AddPlayer(FAR_ID, Phase2Helpers.Polar(0f, 3f));
            rig.Start();

            rig.Fsm.Tick(100.0);

            Assert.AreEqual(3, rig.Count(FAR_ID));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, rig.Listener.HitIndices);
            Assert.AreEqual(3, rig.Pattern.ResolvedStepCount);
            Assert.IsTrue(rig.Pattern.IsFinished);
            Assert.AreEqual(EBossAttackState.Ready, rig.Fsm.State);

            rig.Fsm.Tick(101.0);
            Assert.AreEqual(3, rig.Count(FAR_ID), "끝난 시전은 더 치지 않는다");
        }

        [Test]
        public void Tick_CoarseAndFineTicks_ProduceTheSameHits()
        {
            Rig fine = new Rig(ZonesThenSlam());
            Rig coarse = new Rig(ZonesThenSlam());
            Rig skip = new Rig(ZonesThenSlam());
            foreach (Rig rig in new[] { fine, coarse, skip })
            {
                rig.AddPlayer(FAR_ID, Phase2Helpers.Polar(-20f, 3f));
                rig.AddPlayer(NEAR_ID, Phase2Helpers.Polar(45f, 3f));
                rig.Start();
            }

            fine.TickRange(0.0, END_AT + 1.0, 0.02);
            coarse.TickRange(0.0, END_AT + 1.0, 0.7);
            skip.Fsm.Tick(EXECUTE_AT + 0.6);  // 예고 끝과 둘째 타 사이로 한 번에 건너뛴다
            skip.Fsm.Tick(END_AT + 1.0);

            foreach (int id in new[] { FAR_ID, NEAR_ID })
            {
                Assert.AreEqual(fine.Count(id), coarse.Count(id), "coarse " + id);
                Assert.AreEqual(fine.Count(id), skip.Count(id), "skip " + id);
            }

            CollectionAssert.AreEqual(fine.Listener.HitIndices, coarse.Listener.HitIndices);
            CollectionAssert.AreEqual(fine.Listener.HitIndices, skip.Listener.HitIndices);
        }

        [Test]
        public void Cancel_BetweenHits_StopsLaterHitsAndNotifiesOnce()
        {
            Rig rig = new Rig(ZonesThenSlam());
            rig.AddPlayer(FAR_ID, Phase2Helpers.Polar(0f, 3f));
            rig.Start();

            rig.Fsm.Tick(EXECUTE_AT + TIME_MARGIN);
            Assert.AreEqual(1, rig.Count(FAR_ID));

            rig.Fsm.Cancel(EBossInterruptReason.DirectControl);
            rig.Fsm.Tick(END_AT + 1.0);

            Assert.AreEqual(1, rig.Count(FAR_ID), "취소 뒤에는 남은 타가 나가지 않는다");
            CollectionAssert.AreEqual(new[] { EBossInterruptReason.DirectControl }, rig.Listener.Cancels);
            Assert.IsTrue(rig.Pattern.IsCancelled);
        }

        [Test]
        public void Cancel_DuringTelegraph_NeverHits()
        {
            Rig rig = new Rig(ZonesThenSlam());
            rig.AddPlayer(FAR_ID, Phase2Helpers.Polar(0f, 3f));
            rig.Start();

            rig.Fsm.Tick(TELEGRAPH - TIME_MARGIN);
            rig.Fsm.Cancel(EBossInterruptReason.Death);
            rig.Fsm.Tick(END_AT + 1.0);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(0, rig.Listener.HitIndices.Count);
        }

        [Test]
        public void Gate_ClosedBeforeHit_NoDamageRequestReachesTheResolver()
        {
            Rig rig = new Rig(TwoZones());
            rig.AddPlayer(FAR_ID, Phase2Helpers.Polar(-45f, 3f));
            rig.Start();

            rig.Gate.CloseForGood();
            rig.Fsm.Tick(END_AT);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
        }

        /****************************************
        *      Schedule, Origin, Tracking
        ****************************************/

        [Test]
        public void ScheduledStepCount_ExcludesStepsLaterThanRecoveryEnd()
        {
            Rig rig = new Rig(new MucheolSequenceSpec(
                MucheolSequenceStep.Circle(ZONE_RANGE, DAMAGE_A, 0f),
                MucheolSequenceStep.Circle(ZONE_RANGE, DAMAGE_A, ACTIVE + RECOVERY + 0.5f)));
            rig.AddPlayer(FAR_ID, Phase2Helpers.Polar(0f, 2f));
            rig.Start();

            rig.Fsm.Tick(100.0);

            Assert.AreEqual(1, rig.Pattern.ScheduledStepCount);
            Assert.AreEqual(1, rig.Count(FAR_ID));
        }

        [Test]
        public void Strike_StepAtExactlyExecuteEnd_IsStillResolved()
        {
            Rig rig = new Rig(new MucheolSequenceSpec(
                MucheolSequenceStep.Circle(ZONE_RANGE, DAMAGE_A, 0f),
                MucheolSequenceStep.Circle(ZONE_RANGE, DAMAGE_A, ACTIVE)));
            rig.AddPlayer(FAR_ID, Phase2Helpers.Polar(0f, 2f));
            rig.Start();

            rig.TickRange(0.0, END_AT + 0.5, 0.05);

            Assert.AreEqual(2, rig.Count(FAR_ID));
        }

        [Test]
        public void Strike_BossMovedAfterBegin_JudgesAroundTheTelegraphOrigin()
        {
            Rig rig = new Rig(TwoCircles());
            rig.AddPlayer(FAR_ID, new Vector3(0f, 0f, 2f));       // 예고 기준점 근처
            rig.AddPlayer(NEAR_ID, new Vector3(30f, 0f, 2f));     // 보스가 옮겨 간 곳 근처
            rig.Start();

            rig.Self.Position = new Vector3(30f, 0f, 0f);
            rig.Fsm.Tick(END_AT);

            Assert.AreEqual(2, rig.Count(FAR_ID));
            Assert.AreEqual(0, rig.Count(NEAR_ID));
        }

        [Test]
        public void Tracking_RotationIsLimitedByTurnRateTimesTrackingSecondsEvenWithHugeTicks()
        {
            Rig rig = new Rig(TwoZones(), 0.4f, 150f);
            rig.Tracked.Position = Phase2Helpers.Polar(90f, 3f);
            rig.Start();

            rig.Fsm.Tick(100.0);

            Assert.AreEqual(60f, Vector3.Angle(Vector3.forward, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(1, rig.Listener.LockCount);
            Assert.IsTrue(rig.Pattern.IsDirectionLocked);
        }

        [Test]
        public void Tracking_StepOffsetIsAddedToTheLockedAim()
        {
            // 방향 60도에 오프셋 +30도면 둘째 구역은 90도를 중심으로 한다
            Rig rig = new Rig(TwoZones(), 0.4f, 150f);
            rig.Tracked.Position = Phase2Helpers.Polar(90f, 3f);
            rig.AddPlayer(FAR_ID, Phase2Helpers.Polar(90f, 3f));
            rig.AddPlayer(NEAR_ID, Phase2Helpers.Polar(-10f, 3f));
            rig.Start();

            rig.Fsm.Tick(END_AT);

            Assert.AreEqual(1, rig.Count(FAR_ID), "둘째 구역(90도 +-30도) 안");
            Assert.AreEqual(0, rig.Count(NEAR_ID), "-10도는 두 구역(30도와 90도 중심, 각각 +-30도) 모두 밖");
        }
    }
}
