// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Art;
using Shinshi.Data;

namespace Shinshi.Tests
{
    // 무철 전용 표시 뷰를 서버 시각을 직접 주입해 검증한다. 서버 어셈블리를 참조하지 않고 클라이언트 안전한 값만 넣는다
    // 예고 표현 풀(프리팹)은 없어도 예고 표현 진입점이 시전 상태를 들고 있으므로, 어떤 예고가 어떤 형태와 방향과 시각으로 시작됐는지 그 상태로 확인한다
    // 화면에 나온 모양과 색은 이 테스트가 확인하지 않는다
    public class BossMucheolViewTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double START     = 100.0;
        private const float  TOLERANCE = 0.02f;
        private const int    BOSS      = 1;
        private const int    CAST      = 5;

        /****************************************
        *                Fields
        ****************************************/

        private GameObject            _root;
        private BossTelegraphPresenter _presenter;
        private BossMucheolPhaseView  _phase;
        private double                _clock;

        /****************************************
        *               Set Up
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _clock = START;
            _root = new GameObject("BossMucheolViewTests");
            _presenter = _root.AddComponent<BossTelegraphPresenter>();
            _presenter.SetServerClock(() => _clock);
            _phase = _root.AddComponent<BossMucheolPhaseView>();
            ViewTestUtil.Awake(_phase);
            ViewTestUtil.AwakeAll<BossPresenterViewBase>(_root);
            ViewTestUtil.AwakeAll<BossHardTelegraphViewBase>(_root);
            _phase.SetServerClock(() => _clock);
        }

        [TearDown]
        public void TearDown()
        {
            if (_phase != null)
            {
                _phase.ShapePool.Dispose();
            }

            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        /****************************************
        *               Helpers
        ****************************************/

        private void Advance(double seconds)
        {
            _clock += seconds;
            _phase.Battlefield.Refresh();
            _phase.ChargeImpact.Refresh();
        }

        private BossTelegraphCast CastOf(int key)
        {
            Assert.That(_presenter.TryGetVisuals(key, out BossTelegraphCast cast, out _, out _), Is.True, "no telegraph for key " + key);
            return cast;
        }

        private bool HasCast(int key)
        {
            return _presenter.TryGetVisuals(key, out _, out _, out _);
        }

        private static float[] F(params float[] values)
        {
            return values;
        }

        private bool BeginCombo(int castId = CAST, int struck = 0)
        {
            return _phase.Combo.Begin(BOSS, castId, new Vector3(1f, 0f, 2f), Vector3.forward, START, 0.7f,
                F(5f, 6f), F(120f, 70f), F(0f, 0.4f), struck);
        }

        private bool BeginSequence(int castId = CAST, int struck = 0)
        {
            EBossTelegraphShape[] shapes = { EBossTelegraphShape.Fan, EBossTelegraphShape.Line, EBossTelegraphShape.Circle };
            return _phase.Sequence.Begin(BOSS, castId, BossPatternCatalogSO.MUCHEOL_P2_P01_ID, Vector3.zero, Vector3.forward, START, 0.8f,
                shapes, F(5f, 10f, 4f), F(120f, 0f, 0f), F(0f, 2f, 0f), F(0f, 30f, 0f), F(0f, 0.3f, 0.6f), struck);
        }

        private bool BeginDash(int castId = CAST, int dashCount = 2)
        {
            return _phase.MultiDash.Begin(BOSS, castId, BossPatternCatalogSO.MUCHEOL_P2_P03_ID, Vector3.zero, Vector3.forward, 8f, 2.5f,
                START, 0.9f, dashCount);
        }

        /****************************************
        *        P1 P05 콤보
        ****************************************/

        [Test]
        public void Combo_Begin_StartsOneFanPerHitWithOwnTimings()
        {
            Assert.That(BeginCombo(), Is.True);

            Assert.That(_presenter.ActiveCount, Is.EqualTo(2));
            Assert.That(_phase.Combo.TryGetKey(CAST, 0, out int first), Is.True);
            Assert.That(_phase.Combo.TryGetKey(CAST, 1, out int second), Is.True);
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(first, Is.LessThan(0));
            BossTelegraphCast a = CastOf(first);
            BossTelegraphCast b = CastOf(second);
            Assert.That(a.Shape, Is.EqualTo(EBossTelegraphShape.Fan));
            Assert.That(a.PatternId, Is.EqualTo(BossPatternCatalogSO.MUCHEOL_P05_ID));
            Assert.That(a.GroupId, Is.EqualTo(CAST));
            Assert.That(a.Origin, Is.EqualTo(new Vector3(1f, 0f, 2f)));
            Assert.That(a.ArcAngle, Is.EqualTo(120f));
            Assert.That(a.Start.TelegraphSeconds, Is.EqualTo(0.7f).Within(TOLERANCE));
            Assert.That(b.ArcAngle, Is.EqualTo(70f));
            Assert.That(b.Start.TelegraphSeconds, Is.EqualTo(1.1f).Within(TOLERANCE));
        }

        [Test]
        public void Combo_HitsStrikeOnlyTheConfirmedHitAndFinishTheCast()
        {
            BeginCombo();
            _phase.Combo.TryGetKey(CAST, 0, out int first);
            _phase.Combo.TryGetKey(CAST, 1, out int second);

            Assert.That(_phase.Combo.Hit(CAST, 0, START + 0.7), Is.True);
            Assert.That(CastOf(first).HasExecuted, Is.True);
            Assert.That(CastOf(second).HasExecuted, Is.False);
            Assert.That(_phase.Combo.Hit(CAST, 0, START + 0.7), Is.False);
            Assert.That(_phase.Combo.Hit(CAST, 9, START + 0.7), Is.False);
            Assert.That(_phase.Combo.CastCount, Is.EqualTo(1));

            Assert.That(_phase.Combo.Hit(CAST, 1, START + 1.1), Is.True);
            Assert.That(_phase.Combo.CastCount, Is.Zero);
            Assert.That(_phase.Combo.TrackedCastCount, Is.Zero);
        }

        [Test]
        public void Combo_AimAndLock_ReachEveryHit()
        {
            BeginCombo();
            _phase.Combo.TryGetKey(CAST, 0, out int first);
            _phase.Combo.TryGetKey(CAST, 1, out int second);
            Vector3 aimed = new Vector3(1f, 0f, 1f).normalized;

            Assert.That(_phase.Combo.AimChanged(CAST, aimed, START + 0.2), Is.True);
            Assert.That(Vector3.Distance(CastOf(first).Direction, aimed), Is.LessThan(TOLERANCE));
            Assert.That(Vector3.Distance(CastOf(second).Direction, aimed), Is.LessThan(TOLERANCE));

            Assert.That(_phase.Combo.DirectionLocked(CAST, Vector3.right), Is.True);
            Assert.That(CastOf(first).IsDirectionLocked, Is.True);
            Assert.That(CastOf(second).Direction, Is.EqualTo(Vector3.right));
        }

        [Test]
        public void Combo_Cancel_RemovesUnstruckHitsAndBlocksLateStart()
        {
            BeginCombo();
            _phase.Combo.TryGetKey(CAST, 0, out int first);
            _phase.Combo.Hit(CAST, 0, START + 0.7);

            Assert.That(_phase.Combo.Cancel(CAST), Is.True);

            Assert.That(HasCast(first), Is.True);
            Assert.That(_presenter.ActiveCount, Is.EqualTo(1));
            Assert.That(_phase.Combo.CastCount, Is.Zero);
            Assert.That(BeginCombo(), Is.False);
        }

        [Test]
        public void Combo_CancelBeforeStart_BlocksTheLateStart()
        {
            Assert.That(_phase.Combo.Cancel(CAST), Is.False);

            Assert.That(BeginCombo(), Is.False);
            Assert.That(_presenter.ActiveCount, Is.Zero);
            Assert.That(_phase.Combo.CastCount, Is.Zero);
        }

        [Test]
        public void Combo_LateJoin_RestoresStruckHitAndKeepsTheRestTelegraphing()
        {
            _clock = START + 0.9;

            Assert.That(BeginCombo(CAST, 1), Is.True);

            _phase.Combo.TryGetKey(CAST, 0, out int first);
            _phase.Combo.TryGetKey(CAST, 1, out int second);
            Assert.That(CastOf(first).HasExecuted, Is.True);
            Assert.That(CastOf(second).HasExecuted, Is.False);
            Assert.That(CastOf(second).GetTelegraphProgress(_clock), Is.EqualTo(0.9f / 1.1f).Within(TOLERANCE));
        }

        [Test]
        public void Combo_InvalidInput_IsRejected()
        {
            Assert.That(_phase.Combo.Begin(BOSS, CAST, Vector3.zero, Vector3.forward, START, 0.7f, F(5f, 6f), F(120f), F(0f, 0.4f)), Is.False);
            Assert.That(_phase.Combo.Begin(BOSS, CAST, Vector3.zero, Vector3.forward, START, 0.7f, null, null, null), Is.False);
            Assert.That(_presenter.ActiveCount, Is.Zero);
        }

        [Test]
        public void Combo_DuplicateStart_IsIgnored()
        {
            BeginCombo();

            Assert.That(BeginCombo(), Is.False);
            Assert.That(_presenter.ActiveCount, Is.EqualTo(2));
        }

        /****************************************
        *        P2 순차 타격
        ****************************************/

        [Test]
        public void Sequence_Begin_UsesPerStepShapeAndAimOffset()
        {
            Assert.That(BeginSequence(), Is.True);

            Assert.That(_presenter.ActiveCount, Is.EqualTo(3));
            _phase.Sequence.TryGetKey(CAST, 0, out int fanKey);
            _phase.Sequence.TryGetKey(CAST, 1, out int lineKey);
            _phase.Sequence.TryGetKey(CAST, 2, out int circleKey);
            BossTelegraphCast fan = CastOf(fanKey);
            BossTelegraphCast line = CastOf(lineKey);
            BossTelegraphCast circle = CastOf(circleKey);
            Assert.That(fan.Shape, Is.EqualTo(EBossTelegraphShape.Fan));
            Assert.That(line.Shape, Is.EqualTo(EBossTelegraphShape.Line));
            Assert.That(circle.Shape, Is.EqualTo(EBossTelegraphShape.Circle));
            Vector3 offset = Quaternion.AngleAxis(30f, Vector3.up) * Vector3.forward;
            Assert.That(Vector3.Distance(line.Direction, offset), Is.LessThan(TOLERANCE));
            Assert.That(line.Width, Is.EqualTo(2f));
            Assert.That(line.Start.Length, Is.EqualTo(10f));
            Assert.That(fan.GroupId, Is.EqualTo(CAST));
            Assert.That(line.Start.TelegraphSeconds, Is.EqualTo(1.1f).Within(TOLERANCE));
            Assert.That(circle.Start.TelegraphSeconds, Is.EqualTo(1.4f).Within(TOLERANCE));
            Assert.That(circle.Start.Length, Is.EqualTo(4f));
        }

        [Test]
        public void Sequence_AimAndLock_RotateEachStepByItsOffsetAndSkipCircle()
        {
            BeginSequence();
            _phase.Sequence.TryGetKey(CAST, 1, out int lineKey);
            _phase.Sequence.TryGetKey(CAST, 2, out int circleKey);
            Vector3 circleBefore = CastOf(circleKey).Direction;

            Assert.That(_phase.Sequence.AimChanged(CAST, Vector3.right, START + 0.2), Is.True);
            Vector3 expected = Quaternion.AngleAxis(30f, Vector3.up) * Vector3.right;
            Assert.That(Vector3.Distance(CastOf(lineKey).Direction, expected), Is.LessThan(TOLERANCE));
            Assert.That(CastOf(circleKey).Direction, Is.EqualTo(circleBefore));

            Assert.That(_phase.Sequence.DirectionLocked(CAST, Vector3.back), Is.True);
            Assert.That(CastOf(lineKey).IsDirectionLocked, Is.True);
        }

        [Test]
        public void Sequence_StepHit_StrikesOnlyThatStep()
        {
            BeginSequence();
            _phase.Sequence.TryGetKey(CAST, 0, out int fanKey);
            _phase.Sequence.TryGetKey(CAST, 1, out int lineKey);

            Assert.That(_phase.Sequence.StepHit(CAST, 1, START + 1.1), Is.True);

            Assert.That(CastOf(lineKey).HasExecuted, Is.True);
            Assert.That(CastOf(fanKey).HasExecuted, Is.False);
            Assert.That(_phase.Sequence.StepHit(CAST, 1, START + 1.1), Is.False);
        }

        [Test]
        public void Sequence_CancelAndLateJoin_FollowTheServerState()
        {
            BeginSequence();
            _phase.Sequence.Cancel(CAST);
            Assert.That(_presenter.ActiveCount, Is.Zero);
            Assert.That(BeginSequence(), Is.False);

            _clock = START + 1.2;
            Assert.That(BeginSequence(8, 2), Is.True);
            _phase.Sequence.TryGetKey(8, 0, out int fan);
            _phase.Sequence.TryGetKey(8, 1, out int line);
            _phase.Sequence.TryGetKey(8, 2, out int circle);
            Assert.That(CastOf(fan).HasExecuted, Is.True);
            Assert.That(CastOf(line).HasExecuted, Is.True);
            Assert.That(CastOf(circle).HasExecuted, Is.False);
        }

        [Test]
        public void Sequence_UnsupportedShapeOrMismatchedLists_IsRejected()
        {
            EBossTelegraphShape[] dash = { EBossTelegraphShape.Dash };

            Assert.That(_phase.Sequence.Begin(BOSS, CAST, "x", Vector3.zero, Vector3.forward, START, 0.8f, dash, F(5f), F(0f), F(0f), F(0f), F(0f)), Is.False);
            Assert.That(_phase.Sequence.Begin(BOSS, CAST, "x", Vector3.zero, Vector3.forward, START, 0.8f,
                new[] { EBossTelegraphShape.Fan }, F(5f, 6f), F(120f), F(0f), F(0f), F(0f)), Is.False);
            Assert.That(_presenter.ActiveCount, Is.Zero);
        }

        /****************************************
        *        P2 연속 돌진
        ****************************************/

        [Test]
        public void MultiDash_Begin_StartsFirstDashTelegraph()
        {
            Assert.That(BeginDash(), Is.True);

            Assert.That(_presenter.ActiveCount, Is.EqualTo(1));
            _phase.MultiDash.Keys.TryGet(CAST, 0, out int key);
            BossTelegraphCast cast = CastOf(key);
            Assert.That(cast.Shape, Is.EqualTo(EBossTelegraphShape.Dash));
            Assert.That(cast.GroupId, Is.EqualTo(CAST));
            Assert.That(cast.Start.Length, Is.EqualTo(8f));
            Assert.That(cast.Width, Is.EqualTo(2.5f));
            Assert.That(_phase.MultiDash.TryGetCast(CAST, out int count, out bool truncated, out _), Is.True);
            Assert.That(count, Is.EqualTo(2));
            Assert.That(truncated, Is.False);
        }

        [Test]
        public void MultiDash_FullRun_ShowsEachDashSeparatelyAndFinishes()
        {
            BeginDash();
            _phase.MultiDash.Keys.TryGet(CAST, 0, out int first);

            Assert.That(_phase.MultiDash.AimChanged(CAST, 0, Vector3.right, 7f, START + 0.3), Is.True);
            Assert.That(CastOf(first).Direction, Is.EqualTo(Vector3.right));
            Assert.That(_phase.MultiDash.DirectionLocked(CAST, 0, Vector3.right, 7f), Is.True);
            Assert.That(_phase.MultiDash.DashStarted(CAST, 0, 7f, 0.45f, 15f, START + 0.9), Is.True);
            Assert.That(CastOf(first).HasExecuted, Is.True);
            Assert.That(CastOf(first).DashDistance, Is.EqualTo(7f));
            Assert.That(_phase.MultiDash.DashStopped(CAST, 0, new Vector3(7f, 0f, 0f), false), Is.True);
            Assert.That(CastOf(first).IsDashStopped, Is.True);

            _clock = START + 1.4;
            Assert.That(_phase.MultiDash.ReaimStarted(CAST, 1, new Vector3(7f, 0f, 0f), Vector3.left, 9f, 2.5f, START + 1.4, 0.5f), Is.True);
            _phase.MultiDash.Keys.TryGet(CAST, 1, out int second);
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(CastOf(second).HasExecuted, Is.False);
            Assert.That(CastOf(second).Direction, Is.EqualTo(Vector3.left));
            Assert.That(_presenter.ActiveCount, Is.EqualTo(2));

            Assert.That(_phase.MultiDash.DashStarted(CAST, 1, 9f, 0.6f, 15f, START + 1.9), Is.True);
            Assert.That(_phase.MultiDash.DashStopped(CAST, 1, new Vector3(-2f, 0f, 0f), true), Is.True);
            Assert.That(CastOf(second).DashStoppedByWall, Is.True);
            Assert.That(_phase.MultiDash.CastCount, Is.Zero);
        }

        [Test]
        public void MultiDash_Truncated_RecordsWithoutChangingTheDisplay()
        {
            BeginDash(CAST, 3);

            Assert.That(_phase.MultiDash.Truncated(CAST, 3, 2), Is.True);

            Assert.That(_phase.MultiDash.TryGetCast(CAST, out _, out bool truncated, out int completed), Is.True);
            Assert.That(truncated, Is.True);
            Assert.That(completed, Is.EqualTo(2));
            Assert.That(_presenter.ActiveCount, Is.EqualTo(1));
        }

        [Test]
        public void MultiDash_Cancel_RemovesTelegraphsAndBlocksLateStart()
        {
            BeginDash();
            _phase.MultiDash.Keys.TryGet(CAST, 0, out int first);
            _phase.MultiDash.DashStarted(CAST, 0, 7f, 0.45f, 15f, START + 0.9);
            _clock = START + 1.4;
            _phase.MultiDash.ReaimStarted(CAST, 1, Vector3.zero, Vector3.left, 9f, 2.5f, START + 1.4, 0.5f);

            Assert.That(_phase.MultiDash.Cancel(CAST), Is.True);

            Assert.That(HasCast(first), Is.True);
            Assert.That(_presenter.ActiveCount, Is.EqualTo(1));
            Assert.That(_phase.MultiDash.CastCount, Is.Zero);
            Assert.That(BeginDash(), Is.False);
        }

        [Test]
        public void MultiDash_UnknownOrStaleCalls_AreIgnored()
        {
            Assert.That(_phase.MultiDash.AimChanged(99, 0, Vector3.right, 5f, START), Is.False);
            Assert.That(_phase.MultiDash.DashStarted(99, 0, 5f, 0.4f, 12f, START), Is.False);
            Assert.That(_phase.MultiDash.ReaimStarted(99, 1, Vector3.zero, Vector3.right, 5f, 2f, START, 0.5f), Is.False);
            Assert.That(_phase.MultiDash.Truncated(99, 2, 1), Is.False);
            Assert.That(BeginDash(CAST, 0), Is.False);
        }

        [Test]
        public void MultiDash_LateJoin_RestoresTheCurrentDashFromServerTime()
        {
            _clock = START + 2.0;

            bool restored = _phase.MultiDash.RestoreDash(BOSS, CAST, BossPatternCatalogSO.MUCHEOL_P2_P03_ID, 1, 2, new Vector3(7f, 0f, 0f),
                Vector3.left, 9f, 2.5f, true, START + 1.4, 0.5f, true, START + 1.9, 9f, 0.6f, 15f, -1f, false);

            Assert.That(restored, Is.True);
            _phase.MultiDash.Keys.TryGet(CAST, 1, out int key);
            BossTelegraphCast cast = CastOf(key);
            Assert.That(cast.HasExecuted, Is.True);
            Assert.That(cast.Direction, Is.EqualTo(Vector3.left));
            Assert.That(cast.GetDashTraveled(_clock), Is.EqualTo(1.5f).Within(TOLERANCE));
            Assert.That(_phase.MultiDash.RestoreDash(BOSS, CAST, "x", 0, 2, Vector3.zero, Vector3.forward, 5f, 2f, false, START, 0.5f,
                false, 0.0, 0f, 0f, 0f, -1f, false), Is.False);
        }

        [Test]
        public void MultiDash_LateJoin_AfterTelegraphGrace_DrawsNothing()
        {
            _clock = START + 5.0;

            Assert.That(_phase.MultiDash.RestoreDash(BOSS, CAST, "x", 0, 2, Vector3.zero, Vector3.forward, 5f, 2f, false, START, 0.5f,
                false, 0.0, 0f, 0f, 0f, -1f, false), Is.False);
            Assert.That(_presenter.ActiveCount, Is.Zero);
            Assert.That(_phase.MultiDash.CastCount, Is.Zero);
            Assert.That(_phase.MultiDash.TrackedCastCount, Is.Zero);
            Assert.That(_phase.MultiDash.Keys.TryGet(CAST, 0, out _), Is.False);
            Assert.That(_phase.MultiDash.Keys.Count, Is.Zero);
        }

        [Test]
        public void MultiDash_FailedRestore_DoesNotBlockALaterValidStart()
        {
            _clock = START + 5.0;
            _phase.MultiDash.RestoreDash(BOSS, CAST, "x", 0, 2, Vector3.zero, Vector3.forward, 5f, 2f, false, START, 0.5f,
                false, 0.0, 0f, 0f, 0f, -1f, false);

            _clock = START + 6.0;
            bool restored = _phase.MultiDash.RestoreDash(BOSS, CAST, "x", 0, 2, Vector3.zero, Vector3.forward, 5f, 2f, false, START + 6.0, 0.5f,
                false, 0.0, 0f, 0f, 0f, -1f, false);

            Assert.That(restored, Is.True);
            Assert.That(_phase.MultiDash.CastCount, Is.EqualTo(1));
        }

        /****************************************
        *        하드 P07 전장 공격
        ****************************************/

        [Test]
        public void Battlefield_Begin_DrawsFanAndOppositeSafeSector()
        {
            Assert.That(_phase.Battlefield.Begin(BOSS, CAST, new Vector3(1f, 0f, 2f), Vector3.forward, 44f, 315f, START, 1.8f), Is.True);
            Advance(0.0);

            Assert.That(_presenter.ActiveCount, Is.EqualTo(1));
            BossTelegraphCast cast = CastOf(CAST);
            Assert.That(cast.Shape, Is.EqualTo(EBossTelegraphShape.Fan));
            Assert.That(cast.PatternId, Is.EqualTo(BossPatternCatalogSO.MUCHEOL_HARD_P2_P07_ID));
            Assert.That(cast.ArcAngle, Is.EqualTo(315f));
            Assert.That(cast.Start.Length, Is.EqualTo(44f));
            Assert.That(_phase.Battlefield.TryGetState(CAST, out bool struck, out BossGroundShapeVFX safe), Is.True);
            Assert.That(struck, Is.False);
            Assert.That(safe.Kind, Is.EqualTo(EBossGroundShapeKind.Sector));
            Assert.That(safe.ArcDegrees, Is.EqualTo(45f).Within(TOLERANCE));
            Assert.That(safe.Direction, Is.EqualTo(Vector3.back));
            Assert.That(safe.Origin, Is.EqualTo(new Vector3(1f, 0f, 2f)));
            Assert.That(safe.Radius, Is.EqualTo(44f));
        }

        [Test]
        public void Battlefield_Hit_StrikesThenFadesTheSafeSector()
        {
            _phase.Battlefield.Begin(BOSS, CAST, Vector3.zero, Vector3.forward, 44f, 315f, START, 1.8f);
            Advance(1.8);

            Assert.That(_phase.Battlefield.Hit(CAST, START + 1.8), Is.True);
            Assert.That(CastOf(CAST).HasExecuted, Is.True);
            Assert.That(_phase.Battlefield.Hit(CAST, START + 1.8), Is.False);
            Advance(0.1);
            Assert.That(_phase.Battlefield.Count, Is.EqualTo(1));

            Advance(0.6);

            Assert.That(_phase.Battlefield.Count, Is.Zero);
            Assert.That(_phase.ShapePool.LeasedCount, Is.Zero);
        }

        [Test]
        public void Battlefield_UnconfirmedHit_ExpiresAfterGrace()
        {
            _phase.Battlefield.Begin(BOSS, CAST, Vector3.zero, Vector3.forward, 44f, 315f, START, 1.8f);
            Advance(2.2);
            Assert.That(_phase.Battlefield.Count, Is.EqualTo(1));

            Advance(0.2);

            Assert.That(_phase.Battlefield.Count, Is.Zero);
            Assert.That(_phase.ShapePool.LeasedCount, Is.Zero);
        }

        [Test]
        public void Battlefield_Cancel_ClearsFanAndSafeSectorAndBlocksLateStart()
        {
            _phase.Battlefield.Begin(BOSS, CAST, Vector3.zero, Vector3.forward, 44f, 315f, START, 1.8f);
            Advance(0.5);

            Assert.That(_phase.Battlefield.Cancel(CAST), Is.True);

            Assert.That(_presenter.ActiveCount, Is.Zero);
            Assert.That(_phase.ShapePool.LeasedCount, Is.Zero);
            Assert.That(_phase.Battlefield.Begin(BOSS, CAST, Vector3.zero, Vector3.forward, 44f, 315f, START, 1.8f), Is.False);
        }

        [Test]
        public void Battlefield_LateJoin_ResumesFromOriginalStartTime()
        {
            _clock = START + 1.0;

            Assert.That(_phase.Battlefield.Begin(BOSS, CAST, Vector3.zero, Vector3.forward, 44f, 315f, START, 1.8f), Is.True);

            Assert.That(CastOf(CAST).GetTelegraphProgress(_clock), Is.EqualTo(1f / 1.8f).Within(TOLERANCE));
            _clock = START + 5.0;
            Assert.That(_phase.Battlefield.Begin(BOSS, 9, Vector3.zero, Vector3.forward, 44f, 315f, START, 1.8f), Is.False);
            Assert.That(_phase.Battlefield.Begin(BOSS, 10, Vector3.zero, Vector3.forward, 44f, 315f, START, 1.8f, true), Is.False);
        }

        [Test]
        public void Battlefield_PresenterRefusesTheCast_LeavesNoSafeSectorOrEntry()
        {
            // 예고 표현이 이미 끝난 번호로 기억하고 있으면 시작을 거절한다
            _presenter.Cancel(CAST);

            Assert.That(_phase.Battlefield.Begin(BOSS, CAST, Vector3.zero, Vector3.forward, 44f, 315f, START, 1.8f), Is.False);
            Advance(0.1);

            Assert.That(_phase.Battlefield.Count, Is.Zero);
            Assert.That(_phase.ShapePool.LeasedCount, Is.Zero);
            Assert.That(_presenter.ActiveCount, Is.Zero);
        }

        [Test]
        public void Battlefield_InvalidShape_IsRejected()
        {
            Assert.That(_phase.Battlefield.Begin(BOSS, CAST, Vector3.zero, Vector3.forward, 44f, 360f, START, 1.8f), Is.False);
            Assert.That(_phase.Battlefield.Begin(BOSS, CAST, Vector3.zero, Vector3.forward, 0f, 315f, START, 1.8f), Is.False);
            Assert.That(_presenter.ActiveCount, Is.Zero);
        }

        /****************************************
        *        P1 P06 충돌 결과
        ****************************************/

        [Test]
        public void ChargeImpact_Collision_FlashesThenShowsRecoveryUntilItEnds()
        {
            Assert.That(_phase.ChargeImpact.ChargeEnded(CAST, true, new Vector3(4f, 0f, 9f), 12.5f, 77, 1.5f, START), Is.True);
            Advance(0.1);

            Assert.That(_phase.ChargeImpact.TryGetState(CAST, out Vector3 position, out BossGroundShapeVFX impact, out BossGroundShapeVFX recovery), Is.True);
            Assert.That(position, Is.EqualTo(new Vector3(4f, 0f, 9f)));
            Assert.That(impact, Is.Not.Null);
            Assert.That(impact.Kind, Is.EqualTo(EBossGroundShapeKind.Disc));
            Assert.That(recovery, Is.Not.Null);
            Assert.That(recovery.Charge, Is.EqualTo(0.1f / 1.5f).Within(TOLERANCE));

            Advance(0.5);
            _phase.ChargeImpact.TryGetState(CAST, out _, out impact, out recovery);
            Assert.That(impact, Is.Null);
            Assert.That(recovery, Is.Not.Null);

            Advance(1.5);
            Assert.That(_phase.ChargeImpact.Count, Is.Zero);
            Assert.That(_phase.ShapePool.LeasedCount, Is.Zero);
        }

        [Test]
        public void ChargeImpact_NormalEnd_DrawsNothingAndBlocksDuplicates()
        {
            Assert.That(_phase.ChargeImpact.ChargeEnded(CAST, false, Vector3.zero, 18f, 0, 0.8f, START), Is.False);
            Advance(0.1);

            Assert.That(_phase.ChargeImpact.Count, Is.Zero);
            Assert.That(_phase.ShapePool.LeasedCount, Is.Zero);
            Assert.That(_phase.ChargeImpact.ChargeEnded(CAST, true, Vector3.zero, 10f, 1, 0.8f, START), Is.False);
        }

        [Test]
        public void ChargeImpact_LateJoin_SkipsFinishedAndKeepsRemainingRecovery()
        {
            _clock = START + 0.8;
            Assert.That(_phase.ChargeImpact.ChargeEnded(CAST, true, Vector3.zero, 10f, 1, 1.5f, START), Is.True);
            Advance(0.0);
            _phase.ChargeImpact.TryGetState(CAST, out _, out BossGroundShapeVFX impact, out BossGroundShapeVFX recovery);
            Assert.That(impact, Is.Null);
            Assert.That(recovery, Is.Not.Null);

            _clock = START + 9.0;
            Assert.That(_phase.ChargeImpact.ChargeEnded(8, true, Vector3.zero, 10f, 1, 1.5f, START), Is.False);
        }

        [Test]
        public void ChargeImpact_Cancel_ClearsShapes()
        {
            _phase.ChargeImpact.ChargeEnded(CAST, true, Vector3.zero, 10f, 1, 1.5f, START);
            Advance(0.1);

            Assert.That(_phase.ChargeImpact.Cancel(CAST), Is.True);

            Assert.That(_phase.ShapePool.LeasedCount, Is.Zero);
        }

        /****************************************
        *        공통 정리
        ****************************************/

        [Test]
        public void ClearAll_Death_RemovesEveryMucheolDisplay()
        {
            BeginCombo(1);
            BeginSequence(2);
            BeginDash(3);
            _phase.Battlefield.Begin(BOSS, 4, Vector3.zero, Vector3.forward, 44f, 315f, START, 1.8f);
            _phase.ChargeImpact.ChargeEnded(6, true, Vector3.zero, 10f, 1, 1.5f, START);
            Advance(0.1);
            Assert.That(_presenter.ActiveCount, Is.GreaterThan(0));
            Assert.That(_phase.ShapePool.LeasedCount, Is.GreaterThan(0));

            _phase.ClearAll();

            Assert.That(_presenter.ActiveCount, Is.Zero);
            Assert.That(_phase.ShapePool.LeasedCount, Is.Zero);
            Assert.That(_phase.Combo.CastCount + _phase.Sequence.CastCount + _phase.MultiDash.CastCount
                + _phase.Battlefield.Count + _phase.ChargeImpact.Count, Is.Zero);
            Assert.That(_phase.Combo.Keys.Count + _phase.Sequence.Keys.Count + _phase.MultiDash.Keys.Count, Is.Zero);
        }

        [Test]
        public void SeveralCasts_UseDistinctKeysAndDoNotCancelEachOther()
        {
            // 같은 보스의 새 시전은 예고 표현이 이전 시전을 끝내므로, 번호 충돌만 보려고 보스를 달리한다
            EBossTelegraphShape[] shapes = { EBossTelegraphShape.Fan, EBossTelegraphShape.Line, EBossTelegraphShape.Circle };
            _phase.Combo.Begin(BOSS, 1, Vector3.zero, Vector3.forward, START, 0.7f, F(5f, 6f), F(120f, 70f), F(0f, 0.4f));
            _phase.Sequence.Begin(BOSS + 1, 2, BossPatternCatalogSO.MUCHEOL_P2_P01_ID, Vector3.zero, Vector3.forward, START, 0.8f,
                shapes, F(5f, 10f, 4f), F(120f, 0f, 0f), F(0f, 2f, 0f), F(0f, 30f, 0f), F(0f, 0.3f, 0.6f));

            Assert.That(_presenter.ActiveCount, Is.EqualTo(5));
            _phase.Combo.TryGetKey(1, 0, out int comboKey);
            _phase.Sequence.TryGetKey(2, 0, out int sequenceKey);
            Assert.That(comboKey, Is.Not.EqualTo(sequenceKey));

            _phase.Combo.Cancel(1);

            Assert.That(_presenter.ActiveCount, Is.EqualTo(3));
            Assert.That(_phase.Sequence.CastCount, Is.EqualTo(1));
        }

        [Test]
        public void ResetNetworkSession_ForgetsEndedMucheolCasts()
        {
            _phase.Battlefield.Begin(BOSS, CAST, Vector3.zero, Vector3.forward, 44f, 315f, START, 1.8f);
            _phase.Battlefield.Cancel(CAST);
            Assert.That(_phase.Battlefield.Begin(BOSS, CAST, Vector3.zero, Vector3.forward, 44f, 315f, START, 1.8f), Is.False);

            _presenter.ResetNetworkSession();
            _phase.ResetNetworkSession();

            Assert.That(_phase.Battlefield.Begin(BOSS, CAST, Vector3.zero, Vector3.forward, 44f, 315f, START, 1.8f), Is.True);
        }

        [Test]
        public void SubCastKeys_DifferentViews_NeverShareAKey()
        {
            BossSubCastKeys first = new BossSubCastKeys();
            BossSubCastKeys second = new BossSubCastKeys();

            int a = first.Get(1, 0);
            int b = second.Get(1, 0);

            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void SubCastKeys_AreNegativeStableAndForgetPerCast()
        {
            BossSubCastKeys keys = new BossSubCastKeys();

            int a = keys.Get(3, 0);
            int b = keys.Get(3, 1);
            int c = keys.Get(4, 0);

            Assert.That(new HashSet<int> { a, b, c }.Count, Is.EqualTo(3));
            Assert.That(a, Is.LessThan(0));
            Assert.That(keys.Get(3, 0), Is.EqualTo(a));

            keys.Forget(3);

            Assert.That(keys.TryGet(3, 0, out _), Is.False);
            Assert.That(keys.TryGet(4, 0, out int stillThere), Is.True);
            Assert.That(stillThere, Is.EqualTo(c));
        }
    }
}
