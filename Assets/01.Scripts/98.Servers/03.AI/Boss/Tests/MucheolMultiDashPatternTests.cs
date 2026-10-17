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
    // 무철 2페이즈 연속 돌진(ph2_p03, p06 모양) 실행 검증. 수치는 모두 이 테스트가 주입하는 시험 데이터이며 밸런스 값이 아니다.
    // 시간표(시험 데이터): 예고 1.0 -> 돌진 0.75초, 재조준 0.6초, 돌진 0.75초(, 재조준 0.6초, 돌진 0.75초). 속도는 18m / 0.75초 = 24m/s다.
    // 공용 공격 FSM은 실행 시간을 정의 하나로 고정하므로, 세 번 달리는 시전을 정의 2.1초에 올리는 경우(잘림)와
    // 실행 시간을 패턴에서 읽는 제안 FSM(ProposedExecuteFsm)에 올리는 경우를 따로 시험한다
    public class MucheolMultiDashPatternTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float TEST_HREF   = 3541f; // 테스트가 명시적으로 주입하는 값이다
        private const float TOLERANCE   = 0.0001f;
        private const float POSITION_TOLERANCE = 0.05f;
        private const float ANGLE_TOLERANCE = 0.1f;
        private const double TIME_TOLERANCE = 0.001;
        private const float BODY_RADIUS = 0.5f;
        private const int   PLAYER_A    = 21;
        private const int   PLAYER_B    = 22;
        private const int   PLAYER_C    = 23;

        private const float RANGE        = 18f;
        private const float WIDTH        = 3f;
        private const float DAMAGE       = 0.32f;
        private const float DASH_SECONDS = 0.75f;
        private const float REAIM_SECONDS = 0.6f;
        private const float TELEGRAPH    = 1.0f;
        private const float RECOVERY     = 1.8f;
        private const float ACTIVE_TWO   = 2.1f;   // 두 번: 0.75 + 0.6 + 0.75
        private const float ACTIVE_THREE = 3.45f;  // 세 번: 0.75 x 3 + 0.6 x 2

        private const double DASH0_AT = TELEGRAPH;
        private const double DASH1_AT = TELEGRAPH + DASH_SECONDS + REAIM_SECONDS;

        /****************************************
        *               Helpers
        ****************************************/

        private static MucheolMultiDashSpec Spec(int count = 2, int enhancedCount = 3)
        {
            return new MucheolMultiDashSpec(count, enhancedCount, DASH_SECONDS, REAIM_SECONDS);
        }

        // 정의의 회전 속도는 일부러 0이다. 이지 조준은 정의의 회전 속도와 상관없이 각도 제한이 없어야 한다
        private static PatternDefinition MakeDefinition(float active, float tracking = 0f)
        {
            return new PatternDefinition("mucheol_ph2_p06", MucheolPatternFactory.BOSS_ID, 2, 10f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                TELEGRAPH, tracking, 0f, active, RECOVERY, EnemyCatalogSO.AttackShapeSpec.Dash(RANGE, WIDTH), DAMAGE, false);
        }

        private static void AssertFacing(Vector3 expected, Vector3 actual, string message)
        {
            Assert.AreEqual(0f, Vector3.Angle(expected, actual), ANGLE_TOLERANCE, message);
        }

        private static Vector3 RightOf(Vector3 direction)
        {
            return new Vector3(direction.z, 0f, -direction.x);
        }

        private static void AssertInOrder(List<string> log, params string[] expected)
        {
            int from = 0;
            for (int i = 0; i < expected.Length; ++i)
            {
                int index = log.IndexOf(expected[i], from);
                Assert.GreaterOrEqual(index, 0, $"'{expected[i]}'가 순서대로 나오지 않았다: {string.Join(",", log)}");
                from = index + 1;
            }
        }

        // 공격 FSM에 올린 최소 구성. 보스는 원점에서 +z를 보고, 추적 대상(10번)은 정면 멀리에 선다. 예고 추적 시간은 기본 0이라 방향은 시작 때 정해져 고정된다
        private sealed class Rig
        {
            public FakeCombatTarget   Self       { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, BodyRadius = BODY_RADIUS };
            public FakeCombatTarget   Tracked    { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 200f) };
            public Phase2Query        Query      { get; } = new Phase2Query();
            public FakeDamageResolver Resolver   { get; } = new FakeDamageResolver();
            public DashRecorder       Listener   { get; } = new DashRecorder();
            public Phase2Movement     Movement   { get; } = new Phase2Movement();
            public BossAttackFsm      Fsm        { get; } = new BossAttackFsm();
            public BossDamageGate     Gate       { get; }
            public MucheolMultiDashPattern Pattern { get; }
            public PatternDefinition  Definition { get; }
            public BossPatternContext Context    { get; }

            public Rig(MucheolMultiDashSpec spec, int dashCount, float active, float wallZ = float.PositiveInfinity, float tracking = 0f)
            {
                Query.Players.Add(Tracked);
                Movement.Self = Self;
                Movement.WallZ = wallZ;
                Gate       = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = MakeDefinition(active, tracking);
                Pattern    = new MucheolMultiDashPattern(TEST_HREF, Movement, spec, dashCount, Listener);
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

            // 제안 FSM에 올려 시작한다
            public ProposedExecuteFsm StartProposed()
            {
                ProposedExecuteFsm fsm = new ProposedExecuteFsm();
                BossPatternContext context = Context;
                fsm.Start(Definition, Pattern, in context, 0.0);
                return fsm;
            }
        }

        private static void TickProposed(ProposedExecuteFsm fsm, double from, double to, double step)
        {
            int count = (int)Math.Round((to - from) / step);
            for (int i = 1; i <= count; ++i)
            {
                fsm.Tick(from + (i * step));
            }
        }

        /****************************************
        *            Spec Validation
        ****************************************/

        [Test]
        public void Spec_ExecuteSecondsMatchTheTimetable()
        {
            MucheolMultiDashSpec spec = Spec();

            Assert.AreEqual(ACTIVE_TWO, spec.GetExecuteSeconds(2), TOLERANCE);
            Assert.AreEqual(ACTIVE_THREE, spec.GetExecuteSeconds(3), TOLERANCE);
            Assert.AreEqual(DASH_SECONDS, spec.GetExecuteSeconds(1), TOLERANCE);
            Assert.AreEqual(2, spec.GetDashCount(false));
            Assert.AreEqual(3, spec.GetDashCount(true));
        }

        [Test]
        public void Spec_InvalidValues_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolMultiDashSpec(0, 1, DASH_SECONDS, REAIM_SECONDS));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolMultiDashSpec(3, 2, DASH_SECONDS, REAIM_SECONDS));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolMultiDashSpec(2, 3, 0f, REAIM_SECONDS));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolMultiDashSpec(2, 3, float.NaN, REAIM_SECONDS));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolMultiDashSpec(2, 3, DASH_SECONDS, -0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolMultiDashSpec(2, 3, DASH_SECONDS, float.PositiveInfinity));
        }

        [Test]
        public void Constructor_InvalidArguments_Throw()
        {
            Phase2Movement movement = new Phase2Movement();

            Assert.Throws<ArgumentNullException>(() => new MucheolMultiDashPattern(TEST_HREF, null, Spec(), 2));
            Assert.Throws<ArgumentNullException>(() => new MucheolMultiDashPattern(TEST_HREF, movement, null, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolMultiDashPattern(TEST_HREF, movement, Spec(), 0));
        }

        /****************************************
        *       Two Dashes In One Cast
        ****************************************/

        [Test]
        public void Begin_ReportsFirstDashLengthAndPlannedCount()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            rig.Start();

            Assert.AreEqual(2, rig.Listener.TelegraphDashCount);
            Assert.AreEqual(RANGE, rig.Listener.TelegraphLength, TOLERANCE);
            Assert.AreEqual(2, rig.Pattern.PlannedDashCount);
        }

        [Test]
        public void TwoDashes_EachDashHitsItsOwnPathOnceInOrder()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 9f));    // 첫 돌진 구간 0~18m
            rig.AddPlayer(PLAYER_B, new Vector3(0f, 0f, 27f));   // 둘째 돌진 구간 18~36m
            rig.Start();

            rig.TickRange(0.0, DASH1_AT + DASH_SECONDS + RECOVERY + 0.5, 0.02);

            Assert.AreEqual(1, rig.Count(PLAYER_A));
            Assert.AreEqual(1, rig.Count(PLAYER_B));
            Assert.AreEqual(2, rig.Pattern.CompletedDashCount);
            Assert.AreEqual(2, rig.Listener.StartedCount);
            AssertInOrder(rig.Listener.Log, "telegraph", "lock0", "start0", "hit0x1", "stop0", "reaim1", "lock1", "start1", "hit1x1", "stop1");
            Assert.AreEqual(-1, rig.Listener.TruncatedCompleted, "다 달렸으니 잘림 알림이 없다");
            Assert.AreEqual(EBossAttackState.Ready, rig.Fsm.State);
        }

        [Test]
        public void TwoDashes_StartTimesAreComputedBoundariesNotTickTimes()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            rig.Start();

            rig.Fsm.Tick(100.0);

            Assert.AreEqual(DASH0_AT, rig.Listener.StartTimes[0], TIME_TOLERANCE);
            Assert.AreEqual(DASH1_AT, rig.Listener.StartTimes[1], TIME_TOLERANCE);
            Assert.AreEqual(DASH_SECONDS, rig.Listener.StartSeconds[0], TOLERANCE);
            Assert.AreEqual(DASH_SECONDS, rig.Listener.StartSeconds[1], TOLERANCE);
        }

        [Test]
        public void TwoDashes_HitHappensWhenTheDashReachesTheTargetAtConstantSpeed()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 9f));
            rig.AddPlayer(PLAYER_B, new Vector3(0f, 0f, 27f));
            rig.Start();

            // 속도 24m/s, 9m 지점에는 0.375초 뒤, 둘째 돌진의 27m 지점(둘째 시작 뒤 9m)에도 0.375초 뒤다
            rig.TickRange(0.0, DASH0_AT + 0.30, 0.01);
            Assert.AreEqual(0, rig.Count(PLAYER_A));
            rig.TickRange(DASH0_AT + 0.30, DASH0_AT + 0.45, 0.01);
            Assert.AreEqual(1, rig.Count(PLAYER_A));

            rig.TickRange(DASH0_AT + 0.45, DASH1_AT + 0.30, 0.01);
            Assert.AreEqual(0, rig.Count(PLAYER_B));
            rig.TickRange(DASH1_AT + 0.30, DASH1_AT + 0.45, 0.01);
            Assert.AreEqual(1, rig.Count(PLAYER_B));
        }

        [Test]
        public void TwoDashes_DamageIsDefinitionDamageTimesHrefAndUnblockable()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 9f));
            rig.Start();

            rig.Fsm.Tick(100.0);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(DAMAGE * TEST_HREF, rig.Resolver.Requests[0].RawDamage, 0.001f);
            Assert.IsTrue(rig.Resolver.Requests[0].IsUnblockable);
        }

        [Test]
        public void TwoDashes_PathWidthIsDefinitionWidth()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            rig.AddPlayer(PLAYER_A, new Vector3(WIDTH * 0.5f - 0.05f, 0f, 9f));
            rig.AddPlayer(PLAYER_B, new Vector3(WIDTH * 0.5f + 0.05f, 0f, 9f));
            rig.Start();

            rig.Fsm.Tick(100.0);

            Assert.AreEqual(1, rig.Count(PLAYER_A));
            Assert.AreEqual(0, rig.Count(PLAYER_B));
        }

        /****************************************
        *   Same Cast, Separate Action Per Dash
        ****************************************/

        [Test]
        public void ReHit_SameTargetInBothDashes_SharesCastAndUsesSeparateActionIds()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            FakeCombatTarget mover = rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 9f));
            rig.Start();

            rig.TickRange(0.0, DASH0_AT + DASH_SECONDS + 0.1, 0.02);
            Assert.AreEqual(1, rig.Count(PLAYER_A));

            mover.Position = new Vector3(0f, 0f, 27f);  // 첫 돌진이 끝난 뒤 둘째 돌진 경로로 옮겨 간다
            rig.TickRange(DASH0_AT + DASH_SECONDS + 0.1, DASH1_AT + DASH_SECONDS + RECOVERY + 0.5, 0.02);

            List<DamageRequest> hits = Phase2Helpers.RequestsFor(rig.Resolver, PLAYER_A);
            Assert.AreEqual(2, hits.Count);
            Assert.AreEqual(rig.Context.CastInstanceId, hits[0].CastInstanceId);
            Assert.AreEqual(rig.Context.CastInstanceId, hits[1].CastInstanceId);
            Assert.AreNotEqual(hits[0].ActionInstanceId, hits[1].ActionInstanceId);
        }

        [Test]
        public void Duplicate_FineTicksInsideOneDash_HitTheSameTargetOnce()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 9f));
            rig.Start();

            rig.TickRange(0.0, DASH1_AT + DASH_SECONDS + RECOVERY, 0.005);

            Assert.AreEqual(1, rig.Count(PLAYER_A));
        }

        [Test]
        public void MovedTarget_LeavesThePathBeforeTheDashArrives_IsNotHit_AndOneEnteringInTimeIs()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            FakeCombatTarget leaver = rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 14f));
            FakeCombatTarget enterer = rig.AddPlayer(PLAYER_B, new Vector3(6f, 0f, 14f));
            rig.Start();

            rig.TickRange(0.0, DASH0_AT + 0.10, 0.01);  // 돌진은 아직 2.4m 지점
            leaver.Position  = new Vector3(6f, 0f, 14f);
            enterer.Position = new Vector3(0f, 0f, 14f);
            rig.TickRange(DASH0_AT + 0.10, DASH0_AT + DASH_SECONDS + 0.1, 0.01);

            Assert.AreEqual(0, rig.Count(PLAYER_A));
            Assert.AreEqual(1, rig.Count(PLAYER_B));
        }

        /****************************************
        *            Re-aim Limit
        ****************************************/

        // 이지 조준: 돌진마다 대상 쪽으로 각도 제한 없이 새로 잡고, 고정한 뒤에는 꺾이지 않는다

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(-90f)]
        [TestCase(135f)]
        [TestCase(180f)]
        [TestCase(-150f)]
        public void FirstDash_PlayerAnywhere_TelegraphLockMovementAndHitsFollowTheSameDirection(float degrees)
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            Vector3 direction = Phase2Helpers.Polar(degrees, 1f);
            Vector3 right = RightOf(direction);
            rig.Tracked.Position = direction * 30f;
            rig.AddPlayer(PLAYER_A, (direction * 9f) + (right * 1.4f));  // 폭의 절반(1.5m) 안
            rig.AddPlayer(PLAYER_B, (direction * 9f) + (right * 1.6f));  // 폭의 절반 밖
            rig.Start();

            AssertFacing(direction, rig.Listener.TelegraphDirection, "예고 시작 방향");

            rig.TickRange(0.0, DASH0_AT + DASH_SECONDS + 0.01, 0.01);

            AssertFacing(direction, rig.Listener.LockDirections[0], "고정 방향");
            AssertFacing(direction, rig.Listener.StartDirections[0], "돌진 시작 방향");
            Assert.AreEqual(0f, Vector3.Angle(direction, rig.Self.Position), ANGLE_TOLERANCE, "실제 이동 방향");
            Assert.AreEqual(RANGE, rig.Self.Position.magnitude, POSITION_TOLERANCE);
            Assert.AreEqual(1, rig.Count(PLAYER_A));
            Assert.AreEqual(0, rig.Count(PLAYER_B));
        }

        [Test]
        public void FirstDash_PlayerBehind_HugeTick_MovesAndHitsTowardThePlayer()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            rig.Tracked.Position = new Vector3(0f, 0f, -30f);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, -9f));
            rig.AddPlayer(PLAYER_B, new Vector3(0f, 0f, 9f));  // 보스가 원래 보던 쪽
            rig.Start();

            rig.Fsm.Tick(100.0);

            Assert.AreEqual(1, rig.Count(PLAYER_A));
            Assert.AreEqual(0, rig.Count(PLAYER_B));
            Assert.AreEqual(-RANGE * 2f, rig.Self.Position.z, POSITION_TOLERANCE, "두 번 모두 대상 쪽으로 간다");
        }

        [Test]
        public void TwoDashes_FirstStopsAtWall_SecondAimsAtThePlayerOnTheOppositeSide()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO, 12f);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, -3f));  // 둘째 돌진 경로(11.5m에서 -6.5m)에만 있다
            rig.Start();

            rig.TickRange(0.0, DASH0_AT + DASH_SECONDS - 0.05, 0.01);
            Assert.IsTrue(rig.Pattern.IsDashing);
            Assert.AreEqual(12f - BODY_RADIUS, rig.Self.Position.z, POSITION_TOLERANCE, "첫 돌진은 벽 앞에서 멈춰 있다");

            rig.Tracked.Position = new Vector3(0f, 0f, -40f);  // 벽에 멈춘 보스의 반대편
            rig.TickRange(DASH0_AT + DASH_SECONDS - 0.05, DASH1_AT + DASH_SECONDS + RECOVERY + 0.5, 0.01);

            Assert.IsTrue(rig.Listener.StoppedByWall[0]);
            Assert.IsFalse(rig.Listener.StoppedByWall[1]);
            AssertFacing(Vector3.back, rig.Listener.ReaimDirections[0], "재조준 예고 방향");
            AssertFacing(Vector3.back, rig.Listener.LockDirections[1], "둘째 고정 방향");
            AssertFacing(Vector3.back, rig.Listener.StartDirections[1], "둘째 돌진 시작 방향");
            Assert.AreEqual(1, rig.Count(PLAYER_A));
            Assert.AreEqual((12f - BODY_RADIUS) - RANGE, rig.Self.Position.z, POSITION_TOLERANCE, "멈춘 자리에서 반대편으로 한 번 더 간다");
        }

        [Test]
        public void ThreeDashes_EachDashAfterAWallStopAimsAgainFromWhereTheBossStands()
        {
            Rig rig = new Rig(Spec(3), 3, ACTIVE_THREE, 12f);
            Vector3 wallStop = new Vector3(0f, 0f, 12f - BODY_RADIUS);
            Vector3 secondEnd = wallStop + (Vector3.left * RANGE);
            Vector3 thirdTarget = new Vector3(20f, 0f, -40f);
            Vector3 thirdDirection = (thirdTarget - secondEnd).normalized;
            rig.AddPlayer(PLAYER_A, wallStop + (Vector3.left * 9f));
            rig.AddPlayer(PLAYER_B, secondEnd + (thirdDirection * 9f));
            rig.Start();

            double dash2At = DASH1_AT + DASH_SECONDS + REAIM_SECONDS;

            rig.TickRange(0.0, DASH0_AT + DASH_SECONDS - 0.05, 0.01);
            rig.Tracked.Position = wallStop + (Vector3.left * 40f);
            rig.TickRange(DASH0_AT + DASH_SECONDS - 0.05, DASH1_AT + 0.35, 0.01);

            rig.Tracked.Position = new Vector3(-40f, 0f, 40f);  // 둘째 돌진이 고정된 뒤에 움직여도 꺾이지 않는다
            rig.TickRange(DASH1_AT + 0.35, DASH1_AT + DASH_SECONDS - 0.05, 0.01);
            Assert.AreEqual(wallStop.z, rig.Self.Position.z, POSITION_TOLERANCE, "둘째 돌진은 -x 직선이다");

            rig.Tracked.Position = thirdTarget;
            rig.TickRange(DASH1_AT + DASH_SECONDS - 0.05, dash2At + DASH_SECONDS + RECOVERY + 0.5, 0.01);

            Assert.IsTrue(rig.Listener.StoppedByWall[0]);
            Assert.IsFalse(rig.Listener.StoppedByWall[1]);
            Assert.IsFalse(rig.Listener.StoppedByWall[2]);
            AssertFacing(Vector3.forward, rig.Listener.StartDirections[0], "첫 돌진");
            AssertFacing(Vector3.left, rig.Listener.StartDirections[1], "둘째 돌진");
            AssertFacing(thirdDirection, rig.Listener.StartDirections[2], "셋째 돌진");
            AssertFacing(Vector3.left, rig.Listener.LockDirections[1], "둘째 고정 방향");
            AssertFacing(thirdDirection, rig.Listener.LockDirections[2], "셋째 고정 방향");
            Assert.AreEqual(1, rig.Count(PLAYER_A));
            Assert.AreEqual(1, rig.Count(PLAYER_B));
            Assert.AreEqual(3, rig.Pattern.CompletedDashCount);
            Assert.AreEqual(0f, Vector3.Distance(secondEnd + (thirdDirection * RANGE), rig.Self.Position), POSITION_TOLERANCE);
        }

        [Test]
        public void DirectionLocked_PlayerMovesAfterwards_DashDoesNotBend()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO, float.PositiveInfinity, 0.4f);
            rig.Tracked.Position = new Vector3(0f, 0f, -30f);
            rig.Start();

            rig.TickRange(0.0, 0.3, 0.01);
            rig.Tracked.Position = new Vector3(30f, 0f, 0f);  // 고정 전에는 따라간다
            rig.TickRange(0.3, 0.5, 0.01);
            AssertFacing(Vector3.right, rig.Listener.LockDirections[0], "고정 방향은 고정 시각의 대상 위치다");

            int aimEventsAtLock = rig.Listener.Log.FindAll(entry => entry == "aim0").Count;
            rig.Tracked.Position = new Vector3(-30f, 0f, 0f);  // 고정한 뒤에는 따라가지 않는다
            rig.TickRange(0.5, DASH0_AT + DASH_SECONDS + 0.01, 0.01);

            Assert.AreEqual(aimEventsAtLock, rig.Listener.Log.FindAll(entry => entry == "aim0").Count, "고정 뒤에 방향 변경 알림이 없다");
            AssertFacing(Vector3.right, rig.Listener.StartDirections[0], "돌진 시작 방향");
            Assert.AreEqual(0f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.Greater(rig.Self.Position.x, RANGE - 1f);
        }

        [Test]
        public void TargetDead_AtStart_KeepsFacingAndNeverSwitchesToAnotherPlayer()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            rig.Tracked.Position = new Vector3(0f, 0f, -30f);
            rig.Tracked.LifeState = EParticipantLifeState.Dead;
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, -9f));
            rig.AddPlayer(PLAYER_B, new Vector3(0f, 0f, 9f));
            rig.Start();

            rig.Fsm.Tick(100.0);

            AssertFacing(Vector3.forward, rig.Listener.StartDirections[0], "죽은 대상 쪽으로도 다른 플레이어 쪽으로도 바꾸지 않는다");
            Assert.AreEqual(0, rig.Count(PLAYER_A));
            Assert.AreEqual(1, rig.Count(PLAYER_B));
        }

        [Test]
        public void TargetGone_BetweenDashes_KeepsTheLastValidDirection()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            rig.Tracked.Position = new Vector3(0f, 0f, -40f);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 5f));  // 첫 돌진이 가는 반대편에 남은 다른 플레이어
            rig.Start();

            rig.TickRange(0.0, DASH0_AT + DASH_SECONDS - 0.05, 0.01);
            rig.Query.Players.Remove(rig.Tracked);
            rig.TickRange(DASH0_AT + DASH_SECONDS - 0.05, DASH1_AT + DASH_SECONDS + RECOVERY + 0.5, 0.01);

            AssertFacing(Vector3.back, rig.Listener.StartDirections[1], "사라진 대상의 마지막 방향을 유지한다");
            Assert.AreEqual(0, rig.Count(PLAYER_A));
            Assert.AreEqual(-RANGE * 2f, rig.Self.Position.z, POSITION_TOLERANCE);
        }

        [Test]
        public void Cancel_DuringReaim_DoesNotAimOrMoveTowardThePlayerAfterwards()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            rig.Tracked.Position = new Vector3(0f, 0f, -40f);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, -27f));
            rig.Start();

            rig.TickRange(0.0, DASH0_AT + DASH_SECONDS + 0.2, 0.01);
            Vector3 stopped = rig.Self.Position;
            int aimEvents = rig.Listener.Log.FindAll(entry => entry.StartsWith("aim")).Count;
            rig.Fsm.Cancel(EBossInterruptReason.DirectControl);
            rig.Tracked.Position = new Vector3(40f, 0f, 0f);
            rig.Fsm.Tick(100.0);

            Assert.AreEqual(0f, Vector3.Distance(stopped, rig.Self.Position), TOLERANCE);
            Assert.AreEqual(aimEvents, rig.Listener.Log.FindAll(entry => entry.StartsWith("aim")).Count);
            Assert.AreEqual(1, rig.Listener.StartedCount);
            Assert.AreEqual(0, rig.Count(PLAYER_A), "취소된 시전은 더 치지 않는다");
        }

        /****************************************
        *               Wall Stop
        ****************************************/

        [Test]
        public void Wall_StopsTheDashInFrontOfItAndLaterDashesAreMeasuredAgain()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO, 12f);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 9f));
            rig.AddPlayer(PLAYER_B, new Vector3(0f, 0f, 11.4f));  // 벽 앞 정지 지점(12m - 몸체 반경 0.5m = 11.5m)보다 안쪽
            rig.AddPlayer(PLAYER_C, new Vector3(0f, 0f, 11.9f));  // 정지 지점 너머, 벽 앞이라도 돌진은 거기까지 가지 않는다
            rig.Start();

            rig.Fsm.Tick(100.0);

            Assert.AreEqual(1, rig.Count(PLAYER_A));
            Assert.AreEqual(1, rig.Count(PLAYER_B));
            Assert.AreEqual(0, rig.Count(PLAYER_C));
            Assert.AreEqual(12f - BODY_RADIUS, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.IsTrue(rig.Listener.StoppedByWall[0]);
            Assert.AreEqual(2, rig.Pattern.CompletedDashCount, "벽에 막힌 돌진도 끝난 것으로 센다");
            Assert.IsTrue(rig.Listener.StoppedByWall[1], "벽 앞에서 시작한 둘째 돌진은 갈 거리가 없다");
        }

        [Test]
        public void Wall_OnlyTheSecondDashHitsTheWall_FirstDashRunsItsFullRange()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO, 30f);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 27f));
            rig.AddPlayer(PLAYER_B, new Vector3(0f, 0f, 33f));
            rig.Start();

            rig.Fsm.Tick(100.0);

            Assert.IsFalse(rig.Listener.StoppedByWall[0]);
            Assert.IsTrue(rig.Listener.StoppedByWall[1]);
            Assert.AreEqual(1, rig.Count(PLAYER_A));
            Assert.AreEqual(0, rig.Count(PLAYER_B));
            Assert.AreEqual(30f - BODY_RADIUS, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(RANGE, rig.Listener.StartDistances[0], POSITION_TOLERANCE);
            Assert.AreEqual(30f - BODY_RADIUS - RANGE, rig.Listener.StartDistances[1], POSITION_TOLERANCE, "둘째 돌진의 예고 길이는 벽 앞까지다");
        }

        /****************************************
        *                Cancel
        ****************************************/

        [Test]
        public void Cancel_MidDash_StopsMovementAndLaterDashesAndHits()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 14f));
            rig.AddPlayer(PLAYER_B, new Vector3(0f, 0f, 27f));
            rig.Start();

            rig.TickRange(0.0, DASH0_AT + 0.20, 0.01);  // 4.8m 지점
            float zAtCancel = rig.Self.Position.z;
            rig.Fsm.Cancel(EBossInterruptReason.DirectControl);
            rig.TickRange(DASH0_AT + 0.20, DASH1_AT + DASH_SECONDS + RECOVERY + 1.0, 0.02);

            Assert.AreEqual(zAtCancel, rig.Self.Position.z, TOLERANCE);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(1, rig.Listener.StartedCount);
            CollectionAssert.AreEqual(new[] { EBossInterruptReason.DirectControl }, rig.Listener.Cancels);
            Assert.IsTrue(rig.Pattern.IsCancelled);
        }

        [Test]
        public void Cancel_DuringReaim_NeverStartsTheSecondDash()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            rig.AddPlayer(PLAYER_B, new Vector3(0f, 0f, 27f));
            rig.Start();

            rig.TickRange(0.0, DASH0_AT + DASH_SECONDS + 0.2, 0.01);
            rig.Fsm.Cancel(EBossInterruptReason.PhaseTransition);
            rig.Fsm.Tick(100.0);

            Assert.AreEqual(1, rig.Listener.StartedCount);
            Assert.AreEqual(0, rig.Count(PLAYER_B));
            Assert.AreEqual(1, rig.Pattern.CompletedDashCount);
        }

        [Test]
        public void Cancel_DuringTelegraph_NeverMoves()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 9f));
            rig.Start();

            rig.Fsm.Tick(TELEGRAPH - 0.1);
            rig.Fsm.Cancel(EBossInterruptReason.Death);
            rig.Fsm.Tick(100.0);

            Assert.AreEqual(0f, rig.Self.Position.z, TOLERANCE);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(0, rig.Listener.StartedCount);
        }

        /****************************************
        *               Large Tick
        ****************************************/

        [Test]
        public void HugeTick_RunsBothDashesInOrderOnceAndFinishes()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_TWO);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 9f));
            rig.AddPlayer(PLAYER_B, new Vector3(0f, 0f, 27f));
            rig.Start();

            rig.Fsm.Tick(100.0);

            Assert.AreEqual(1, rig.Count(PLAYER_A));
            Assert.AreEqual(1, rig.Count(PLAYER_B));
            AssertInOrder(rig.Listener.Log, "start0", "stop0", "reaim1", "start1", "stop1");
            Assert.AreEqual(2, rig.Pattern.CompletedDashCount);
            Assert.IsTrue(rig.Pattern.IsFinished);
            Assert.AreEqual(EBossAttackState.Ready, rig.Fsm.State);
            Assert.AreEqual(36f, rig.Self.Position.z, POSITION_TOLERANCE);

            rig.Fsm.Tick(101.0);
            Assert.AreEqual(2, rig.Resolver.Requests.Count, "끝난 시전은 더 치지 않는다");
        }

        [Test]
        public void Tick_FineCoarseAndSkippedTicks_ProduceTheSameHitsAndEndPosition()
        {
            Rig fine = new Rig(Spec(), 2, ACTIVE_TWO);
            Rig coarse = new Rig(Spec(), 2, ACTIVE_TWO);
            Rig skip = new Rig(Spec(), 2, ACTIVE_TWO);
            foreach (Rig rig in new[] { fine, coarse, skip })
            {
                rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 9f));
                rig.AddPlayer(PLAYER_B, new Vector3(0f, 0f, 27f));
                rig.Start();
            }

            fine.TickRange(0.0, 8.0, 0.01);
            coarse.TickRange(0.0, 8.0, 0.9);
            skip.Fsm.Tick(DASH0_AT + 0.5);
            skip.Fsm.Tick(DASH1_AT + 0.2);
            skip.Fsm.Tick(100.0);

            foreach (int id in new[] { PLAYER_A, PLAYER_B })
            {
                Assert.AreEqual(1, fine.Count(id), "fine " + id);
                Assert.AreEqual(1, coarse.Count(id), "coarse " + id);
                Assert.AreEqual(1, skip.Count(id), "skip " + id);
            }

            Assert.AreEqual(fine.Self.Position.z, coarse.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(fine.Self.Position.z, skip.Self.Position.z, POSITION_TOLERANCE);
        }

        /****************************************
        *    Real Shared FSM Fixes Execute Time
        ****************************************/

        // 공용 FSM은 실행 구간을 정의의 ActiveSeconds로 고정한다. 세 번 달리려는 시전이 2.1초 정의에 올라가면 둘째 돌진까지만 달리고 셋째는 버려진다
        [Test]
        public void SharedFsm_ThreeDashPlanOnTwoDashDefinition_DropsThirdDashAndReportsTruncation()
        {
            Rig rig = new Rig(Spec(), 3, ACTIVE_TWO);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 45f));  // 셋째 돌진 경로(36~54m) 안
            rig.Start();

            rig.TickRange(0.0, 9.0, 0.01);

            Assert.AreEqual(2, rig.Listener.StartedCount);
            Assert.AreEqual(2, rig.Pattern.CompletedDashCount);
            Assert.AreEqual(3, rig.Listener.TruncatedPlanned);
            Assert.AreEqual(2, rig.Listener.TruncatedCompleted);
            Assert.AreEqual(0, rig.Count(PLAYER_A), "버려진 셋째 돌진은 맞히지 않는다");
            CollectionAssert.DoesNotContain(rig.Listener.Log, "start2");
            CollectionAssert.DoesNotContain(rig.Listener.Log, "reaim2", "시작할 수 없는 재조준은 보이지 않는다");
            Assert.IsTrue(rig.Pattern.IsEnded);
            Assert.AreEqual(36f, rig.Self.Position.z, POSITION_TOLERANCE);
        }

        // 한 틱에 여러 돌진이 끝나면 정지 시각은 큰 틱 시각이 아니라 각 돌진이 끝난 경계다
        [Test]
        public void HugeTick_StopTimes_AreEachDashEndBoundary()
        {
            Rig rig = new Rig(Spec(), 3, ACTIVE_TWO);
            rig.Start();

            rig.Fsm.Tick(100.0);

            Assert.AreEqual(2, rig.Listener.StopTimes.Count);
            for (int i = 0; i < rig.Listener.StopTimes.Count; ++i)
            {
                Assert.AreEqual(rig.Listener.StartTimes[i] + rig.Listener.StartSeconds[i], rig.Listener.StopTimes[i], 0.0001,
                    $"{i}번째 돌진");
            }
        }

        [Test]
        public void SharedFsm_ThreeDashPlanOnTwoDashDefinition_HugeTickAlsoDropsThirdDash()
        {
            Rig rig = new Rig(Spec(), 3, ACTIVE_TWO);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 45f));
            rig.Start();

            rig.Fsm.Tick(100.0);

            Assert.AreEqual(2, rig.Listener.StartedCount);
            Assert.AreEqual(0, rig.Count(PLAYER_A));
            Assert.AreEqual(2, rig.Listener.TruncatedCompleted);
            Assert.AreEqual(36f, rig.Self.Position.z, POSITION_TOLERANCE);
        }

        // 셋째 돌진이 실행 구간 안에서 시작했으나 끝나기 전에 구간이 끝나면 그 자리에서 멈추고 이후 구간에는 맞히지 않는다
        [Test]
        public void SharedFsm_ThirdDashStartedButBoundaryComesFirst_StopsMidDash()
        {
            const float ACTIVE_CUT = 2.9f;  // 셋째 돌진은 2.7초에 시작해 0.2초만 달린다
            Rig rig = new Rig(Spec(), 3, ACTIVE_CUT);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 39f));  // 셋째 돌진 시작(36m)에서 3m, 구간 안에 지난다
            rig.AddPlayer(PLAYER_B, new Vector3(0f, 0f, 44f));  // 8m, 구간이 끝난 뒤에 닿을 거리
            rig.Start();

            rig.TickRange(0.0, 9.0, 0.01);

            Assert.AreEqual(3, rig.Listener.StartedCount);
            Assert.AreEqual(1, rig.Count(PLAYER_A));
            Assert.AreEqual(0, rig.Count(PLAYER_B));
            Assert.AreEqual(2, rig.Listener.TruncatedCompleted);
            AssertInOrder(rig.Listener.Log, "start2", "stop2", "truncated");
            Assert.Less(rig.Self.Position.z, 42f);
            Assert.Greater(rig.Self.Position.z, 36f);
        }

        // 실행 시간이 계획보다 길면(개발 데이터) 두 번 달리고 남는 구간 동안 서 있는다
        [Test]
        public void SharedFsm_TwoDashPlanOnThreeDashDefinition_FinishesEarlyAndIdlesWithoutTruncation()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_THREE);
            rig.AddPlayer(PLAYER_B, new Vector3(0f, 0f, 27f));
            rig.Start();

            rig.TickRange(0.0, 9.0, 0.01);

            Assert.AreEqual(2, rig.Listener.StartedCount);
            Assert.AreEqual(1, rig.Count(PLAYER_B));
            Assert.AreEqual(-1, rig.Listener.TruncatedCompleted);
            Assert.AreEqual(36f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(EBossAttackState.Ready, rig.Fsm.State);
        }

        /****************************************
        *   Proposed FSM Reads The Execute Time
        ****************************************/

        [Test]
        public void ProposedFsm_ThreeDashes_ExecuteLastsWholePlanAndNothingIsDropped()
        {
            Rig rig = new Rig(Spec(), 3, ACTIVE_TWO);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 45f));
            ProposedExecuteFsm fsm = rig.StartProposed();

            TickProposed(fsm, 0.0, 9.0, 0.01);

            Assert.AreEqual(3, rig.Listener.StartedCount);
            Assert.AreEqual(3, rig.Pattern.CompletedDashCount);
            Assert.AreEqual(1, rig.Count(PLAYER_A));
            Assert.AreEqual(-1, rig.Listener.TruncatedCompleted);
            Assert.AreEqual(DASH0_AT + ACTIVE_THREE, fsm.RecoverBoundary, TIME_TOLERANCE, "회복은 3.45초 뒤에 시작한다");
            Assert.AreEqual(54f, rig.Self.Position.z, POSITION_TOLERANCE);
        }

        [Test]
        public void ProposedFsm_TwoDashes_ExecuteLastsOnlyTheTwoDashPlan()
        {
            Rig rig = new Rig(Spec(), 2, ACTIVE_THREE);
            ProposedExecuteFsm fsm = rig.StartProposed();

            TickProposed(fsm, 0.0, 9.0, 0.01);

            Assert.AreEqual(2, rig.Pattern.CompletedDashCount);
            Assert.AreEqual(DASH0_AT + ACTIVE_TWO, fsm.RecoverBoundary, TIME_TOLERANCE, "회복은 2.1초 뒤에 시작한다");
        }

        [Test]
        public void ProposedFsm_HugeTick_RunsTheWholeThreeDashPlan()
        {
            Rig rig = new Rig(Spec(), 3, ACTIVE_TWO);
            rig.AddPlayer(PLAYER_A, new Vector3(0f, 0f, 45f));
            ProposedExecuteFsm fsm = rig.StartProposed();

            fsm.Tick(100.0);

            Assert.AreEqual(3, rig.Pattern.CompletedDashCount);
            Assert.AreEqual(1, rig.Count(PLAYER_A));
            Assert.AreEqual(EBossAttackState.Ready, fsm.State);
        }

        [Test]
        public void GetExecuteSeconds_ReturnsThePlannedSecondsForTheChosenCount()
        {
            Rig two = new Rig(Spec(), 2, ACTIVE_THREE);
            Rig three = new Rig(Spec(), 3, ACTIVE_TWO);

            Assert.AreEqual(ACTIVE_TWO, two.Pattern.GetExecuteSeconds(ACTIVE_THREE), TOLERANCE);
            Assert.AreEqual(ACTIVE_THREE, three.Pattern.GetExecuteSeconds(ACTIVE_TWO), TOLERANCE);
        }
    }
}
