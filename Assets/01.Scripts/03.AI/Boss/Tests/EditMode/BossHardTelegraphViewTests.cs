// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Art;

namespace Shinshi.Tests
{
    // 월백 하드 2페이즈 P01~P06 표시 뷰를 서버 시각을 직접 주입해 검증한다. 서버 어셈블리를 참조하지 않으므로 서버 없이도 표현이 동작하는지 함께 보인다
    // 화면에 나온 모양과 색은 이 테스트가 확인하지 않고, 도형의 종류와 배치와 충전 비율과 풀 반환만 확인한다
    // EditMode라 Unity가 Awake를 부르지 않는다. 시작 처리는 ViewTestUtil로 직접 부르고, 프레임과 OnDisable이 필요한 시험은 PlayMode 시험이 맡는다
    public class BossHardTelegraphViewTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double START       = 100.0;
        private const float  TOLERANCE   = 0.02f;
        private const int    CAST        = 1;
        private const int    GRID_LINES  = 12;
        private const int    CIRCLE_COUNT = 13;
        private const int    BARRAGE_SHOTS = 8;
        private const float  ARENA_RADIUS = 20f;

        /****************************************
        *                Fields
        ****************************************/

        private GameObject        _root;
        private BossHardPhaseView _hard;
        private double            _clock;

        /****************************************
        *               Set Up
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("BossHardTelegraphViewTests");
            _hard = _root.AddComponent<BossHardPhaseView>();
            ViewTestUtil.Awake(_hard);
            ViewTestUtil.AwakeAll<BossHardTelegraphViewBase>(_root);
            _clock = START;
            _hard.SetServerClock(() => _clock);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hard != null)
            {
                _hard.ShapePool.Dispose();
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
            RefreshAll();
        }

        private void RefreshAll()
        {
            _hard.StarStage.Refresh();
            _hard.GridBeam.Refresh();
            _hard.ChainFan.Refresh();
            _hard.TrackingArea.Refresh();
            _hard.Barrage.Refresh();
            _hard.CircleBurst.Refresh();
        }

        private static BossStarShape Star()
        {
            return new BossStarShape(Vector3.zero, ARENA_RADIUS, Vector3.forward, 14f, 5, 2, 1.5f, 0f);
        }

        private bool BeginStar(int castId = CAST, int struckCount = 0)
        {
            BossStarShape shape = Star();
            return _hard.StarStage.Begin(castId, 10, in shape, 30f, 3, START, 2f, 0.8f, struckCount);
        }

        private bool BeginGrid(int castId = CAST, int telegraphed = 0, int struck = 0)
        {
            return _hard.GridBeam.Begin(castId, Vector3.zero, ARENA_RADIUS, GRID_LINES, 3f, 3f, START, 1f, 0.3f, 1f, telegraphed, struck);
        }

        private bool BeginFan(int castId = CAST, int blasted = 0, bool ended = false)
        {
            return _hard.ChainFan.Begin(castId, Vector3.zero, Vector3.forward, 16f, 7, 45f, START, 1f, 2.5f, 0.5f, 0.8f, blasted, ended);
        }

        private bool BeginBarrage(int castId = CAST, int fired = 0)
        {
            return _hard.Barrage.Begin(castId, Vector3.zero, START, BARRAGE_SHOTS, 1f, 0.2f, 1.5f, fired);
        }

        private bool BeginBurst(int castId = CAST, bool isStruck = false)
        {
            List<Vector3> centers = new List<Vector3>();
            List<float>   radii   = new List<float>();
            for (int i = 0; i < CIRCLE_COUNT; ++i)
            {
                centers.Add(new Vector3(i, 0f, -i));
                radii.Add((i == 0) ? 4f : 3f);
            }

            return _hard.CircleBurst.Begin(castId, centers, radii, START, 2f, isStruck);
        }

        /****************************************
        *        P01 단계형 별 빔
        ****************************************/

        [Test]
        public void StarStage_Begin_ShowsFirstStageCharging()
        {
            Assert.That(BeginStar(), Is.True);
            RefreshAll();

            Assert.That(_hard.StarStage.TryGetState(CAST, out int struck, out float charge, out float rotation, out int charging, out int flash), Is.True);
            Assert.That(struck, Is.Zero);
            Assert.That(rotation, Is.Zero);
            Assert.That(charging, Is.EqualTo(5));
            Assert.That(flash, Is.Zero);
            Assert.That(charge, Is.EqualTo(0f).Within(TOLERANCE));
            Assert.That(_hard.StarStage.GetChargingBeam(CAST, 0).Kind, Is.EqualTo(EBossGroundShapeKind.Rect));

            Advance(1.0);
            _hard.StarStage.TryGetState(CAST, out _, out charge, out _, out _, out _);
            Assert.That(charge, Is.EqualTo(0.5f).Within(TOLERANCE));
        }

        [Test]
        public void StarStage_Struck_FlashesThenAdvancesToNextStageRotation()
        {
            BeginStar();
            Advance(2.0);

            Assert.That(_hard.StarStage.Struck(CAST, 0, 0f, START + 2.0), Is.True);
            _hard.StarStage.TryGetState(CAST, out int struck, out _, out _, out int charging, out int flash);
            Assert.That(struck, Is.EqualTo(1));
            Assert.That(charging, Is.Zero);
            Assert.That(flash, Is.EqualTo(5));

            Advance(0.1);
            _hard.StarStage.TryGetState(CAST, out _, out float charge, out float rotation, out charging, out flash);
            Assert.That(rotation, Is.EqualTo(30f));
            Assert.That(charging, Is.EqualTo(5));
            Assert.That(flash, Is.EqualTo(5));
            Assert.That(charge, Is.EqualTo(0.125f).Within(TOLERANCE));

            Advance(0.5);
            _hard.StarStage.TryGetState(CAST, out _, out _, out _, out _, out flash);
            Assert.That(flash, Is.Zero);
        }

        [Test]
        public void StarStage_Struck_DuplicateOrStaleStageIsIgnored()
        {
            BeginStar();
            Advance(2.0);
            Assert.That(_hard.StarStage.Struck(CAST, 0, 0f, START + 2.0), Is.True);

            Assert.That(_hard.StarStage.Struck(CAST, 0, 0f, START + 2.0), Is.False);
            Assert.That(_hard.StarStage.Struck(CAST, 7, 0f, START + 2.0), Is.False);
            Assert.That(_hard.StarStage.Struck(99, 0, 0f, START + 2.0), Is.False);
        }

        [Test]
        public void StarStage_AllStagesStruck_RemovesAndReturnsShapes()
        {
            BeginStar();
            Advance(2.0);
            _hard.StarStage.Struck(CAST, 0, 0f, START + 2.0);
            Advance(0.8);
            _hard.StarStage.Struck(CAST, 1, 30f, START + 2.8);
            Advance(0.8);
            _hard.StarStage.Struck(CAST, 2, 60f, START + 3.6);
            Assert.That(_hard.StarStage.Count, Is.EqualTo(1));

            Advance(0.5);

            Assert.That(_hard.StarStage.Count, Is.Zero);
            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
            Assert.That(BeginStar(), Is.False);
        }

        [Test]
        public void StarStage_UnconfirmedStrike_ExpiresAfterGrace()
        {
            BeginStar();
            Advance(2.4);
            Assert.That(_hard.StarStage.Count, Is.EqualTo(1));

            Advance(0.2);

            Assert.That(_hard.StarStage.Count, Is.Zero);
            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
        }

        [Test]
        public void StarStage_Cancel_ClearsAndBlocksLateStart()
        {
            BeginStar();
            RefreshAll();

            Assert.That(_hard.StarStage.Cancel(CAST), Is.True);
            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
            Assert.That(BeginStar(), Is.False);
            Assert.That(_hard.StarStage.Cancel(99), Is.False);
            Assert.That(BeginStar(99), Is.False);
        }

        [Test]
        public void StarStage_LateJoin_ResumesAtServerStage()
        {
            _clock = START + 3.0;

            Assert.That(BeginStar(CAST, 2), Is.True);
            RefreshAll();

            _hard.StarStage.TryGetState(CAST, out int struck, out float charge, out float rotation, out int charging, out int flash);
            Assert.That(struck, Is.EqualTo(2));
            Assert.That(rotation, Is.EqualTo(60f));
            Assert.That(charge, Is.EqualTo(0.25f).Within(TOLERANCE));
            Assert.That(charging, Is.EqualTo(5));
            Assert.That(flash, Is.Zero);
        }

        [Test]
        public void StarStage_LateJoin_AfterAllStages_IsRejected()
        {
            _clock = START + 5.0;

            Assert.That(BeginStar(), Is.False);
            Assert.That(BeginStar(CAST, 3), Is.False);
        }

        /****************************************
        *        P02 격자 순차 빔
        ****************************************/

        [Test]
        public void GridBeam_TelegraphThenStrike_ChargesFlashesAndTurnsSafe()
        {
            BeginGrid();
            Assert.That(_hard.GridBeam.Telegraph(CAST, 100, true, 0, START, START + 1.0), Is.True);
            RefreshAll();

            Assert.That(_hard.GridBeam.TryGetLine(CAST, true, 0, out bool telegraphed, out bool struck, out BossGroundShapeVFX shape), Is.True);
            Assert.That(telegraphed, Is.True);
            Assert.That(struck, Is.False);
            Assert.That(shape.Kind, Is.EqualTo(EBossGroundShapeKind.ArenaBand));
            Assert.That(shape.Direction, Is.EqualTo(Vector3.left));

            Advance(0.5);
            Assert.That(shape.Charge, Is.EqualTo(0.5f).Within(TOLERANCE));

            Advance(0.5);
            Assert.That(_hard.GridBeam.Strike(CAST, 100, true, 0, START + 1.0), Is.True);
            RefreshAll();
            _hard.GridBeam.TryGetLine(CAST, true, 0, out telegraphed, out struck, out shape);
            Assert.That(struck, Is.True);
            Assert.That(shape.Charge, Is.EqualTo(1f));

            Advance(0.5);
            Assert.That(shape.Charge, Is.Zero);
            Assert.That(shape.FillColor.a, Is.Zero);
            Assert.That(shape.BaseColor.a, Is.GreaterThan(0f));
        }

        [Test]
        public void GridBeam_VerticalLines_UseForwardAxisAndSeparateSlots()
        {
            BeginGrid();
            _hard.GridBeam.Telegraph(CAST, 200, false, 0, START + 3.0, START + 4.0);
            Advance(3.5);

            Assert.That(_hard.GridBeam.TryGetLine(CAST, false, 0, out bool telegraphed, out _, out BossGroundShapeVFX shape), Is.True);
            Assert.That(telegraphed, Is.True);
            Assert.That(shape.Direction, Is.EqualTo(Vector3.forward));
            _hard.GridBeam.TryGetLine(CAST, true, 0, out telegraphed, out _, out BossGroundShapeVFX horizontal);
            Assert.That(telegraphed, Is.False);
            Assert.That(horizontal, Is.Null);
            Assert.That(_hard.GridBeam.Telegraph(CAST, 1, true, GRID_LINES, START, START + 1.0), Is.False);
        }

        [Test]
        public void GridBeam_AllTwentyFourLines_ShowTogether()
        {
            BeginGrid();
            for (int i = 0; i < GRID_LINES; ++i)
            {
                _hard.GridBeam.Telegraph(CAST, i, true, i, START, START + 2.0);
                _hard.GridBeam.Telegraph(CAST, 100 + i, false, i, START, START + 2.0);
            }

            RefreshAll();

            Assert.That(_hard.ShapePool.LeasedCount, Is.EqualTo(GRID_LINES * 2));
        }

        [Test]
        public void GridBeam_DuplicateTelegraphAndStrike_AreIgnored()
        {
            BeginGrid();

            Assert.That(_hard.GridBeam.Telegraph(CAST, 1, true, 0, START, START + 1.0), Is.True);
            Assert.That(_hard.GridBeam.Telegraph(CAST, 1, true, 0, START, START + 1.0), Is.False);
            Assert.That(_hard.GridBeam.Strike(CAST, 1, true, 0, START + 1.0), Is.True);
            Assert.That(_hard.GridBeam.Strike(CAST, 1, true, 0, START + 1.0), Is.False);
            Assert.That(_hard.GridBeam.Telegraph(CAST, 1, true, 0, START, START + 1.0), Is.False);
        }

        [Test]
        public void GridBeam_UnconfirmedLine_ExpiresWithoutAffectingOthers()
        {
            BeginGrid();
            _hard.GridBeam.Telegraph(CAST, 1, true, 0, START, START + 1.0);
            _hard.GridBeam.Telegraph(CAST, 2, true, 1, START + 0.3, START + 1.3);
            Advance(1.6);

            _hard.GridBeam.TryGetLine(CAST, true, 0, out bool first, out _, out BossGroundShapeVFX firstShape);
            _hard.GridBeam.TryGetLine(CAST, true, 1, out bool second, out _, out BossGroundShapeVFX secondShape);
            Assert.That(first, Is.False);
            Assert.That(firstShape, Is.Null);
            Assert.That(second, Is.True);
            Assert.That(secondShape, Is.Not.Null);
        }

        [Test]
        public void GridBeam_Cancel_ClearsEveryLineAndBlocksLateStart()
        {
            BeginGrid();
            _hard.GridBeam.Telegraph(CAST, 1, true, 0, START, START + 1.0);
            _hard.GridBeam.Strike(CAST, 2, true, 1, START);
            RefreshAll();

            Assert.That(_hard.GridBeam.Cancel(CAST), Is.True);

            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
            Assert.That(BeginGrid(), Is.False);
        }

        [Test]
        public void GridBeam_LateJoin_RestoresStruckAndTelegraphedLines()
        {
            _clock = START + 2.0;

            Assert.That(BeginGrid(CAST, 5, 3), Is.True);
            RefreshAll();

            for (int i = 0; i < 3; ++i)
            {
                _hard.GridBeam.TryGetLine(CAST, true, i, out _, out bool struck, out BossGroundShapeVFX shape);
                Assert.That(struck, Is.True);
                Assert.That(shape.FillColor.a, Is.Zero);
            }

            for (int i = 3; i < 5; ++i)
            {
                _hard.GridBeam.TryGetLine(CAST, true, i, out bool telegraphed, out _, out BossGroundShapeVFX shape);
                Assert.That(telegraphed, Is.True);
                Assert.That(shape, Is.Not.Null);
            }

            _hard.GridBeam.TryGetLine(CAST, true, 5, out bool idle, out _, out BossGroundShapeVFX none);
            Assert.That(idle, Is.False);
            Assert.That(none, Is.Null);
        }

        [Test]
        public void GridBeam_AfterLastLine_FadesAndRemoves()
        {
            BeginGrid();
            for (int i = 0; i < GRID_LINES; ++i)
            {
                _hard.GridBeam.Strike(CAST, i, true, i, START);
                _hard.GridBeam.Strike(CAST, 100 + i, false, i, START);
            }

            Advance(0.1);
            Assert.That(_hard.GridBeam.Count, Is.EqualTo(1));

            Advance(2.0);

            Assert.That(_hard.GridBeam.Count, Is.Zero);
            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
        }

        /****************************************
        *        P03 잔류 부채꼴 연쇄
        ****************************************/

        [Test]
        public void ChainFan_SafeRevealThenZones_ShowsInOrder()
        {
            Assert.That(BeginFan(), Is.True);
            Advance(0.5);

            Assert.That(_hard.ChainFan.TryGetState(CAST, out int blasted, out bool ended, out BossGroundShapeVFX safe), Is.True);
            Assert.That(blasted, Is.Zero);
            Assert.That(ended, Is.False);
            Assert.That(safe.Kind, Is.EqualTo(EBossGroundShapeKind.Sector));
            Assert.That(safe.ArcDegrees, Is.EqualTo(45f).Within(TOLERANCE));
            Assert.That(safe.Direction, Is.EqualTo(Vector3.forward));
            _hard.ChainFan.TryGetZone(CAST, 0, out _, out BossGroundShapeVFX hidden);
            Assert.That(hidden, Is.Null);
            Assert.That(_hard.ShapePool.LeasedCount, Is.EqualTo(1));

            // 구역 예고는 폭발 간격마다 하나씩 나타난다, 구역 k는 1.0 + 0.5k초에 나타난다
            Advance(1.0);
            Assert.That(_hard.ShapePool.LeasedCount, Is.EqualTo(3));
            Advance(0.6);
            Assert.That(_hard.ShapePool.LeasedCount, Is.EqualTo(4));
            _hard.ChainFan.TryGetZone(CAST, 0, out _, out BossGroundShapeVFX first);
            Vector3 expected = new Vector3(Mathf.Sin(45f * Mathf.Deg2Rad), 0f, Mathf.Cos(45f * Mathf.Deg2Rad));
            Assert.That(Vector3.Distance(first.Direction, expected), Is.LessThan(TOLERANCE));
            Assert.That(first.ArcDegrees, Is.EqualTo(45f).Within(TOLERANCE));
            Assert.That(first.Radius, Is.EqualTo(16f));
        }

        [Test]
        public void ChainFan_Blast_FlashesThenStaysAsResidualUntilDangerEnds()
        {
            BeginFan();
            Advance(2.5);

            Assert.That(_hard.ChainFan.Blast(CAST, 0, START + 2.5), Is.True);
            RefreshAll();
            _hard.ChainFan.TryGetZone(CAST, 0, out bool blasted, out BossGroundShapeVFX zone);
            Assert.That(blasted, Is.True);
            Assert.That(zone.Charge, Is.EqualTo(1f));

            Advance(0.5);
            Assert.That(zone.FillColor.a, Is.GreaterThan(0.4f));
            Assert.That(_hard.ChainFan.Blast(CAST, 0, START + 2.5), Is.False);

            Advance(3.0);
            Assert.That(_hard.ChainFan.EndDanger(CAST, START + 6.0), Is.True);
            Advance(0.4);
            Assert.That(_hard.ChainFan.Count, Is.Zero);
            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
        }

        [Test]
        public void ChainFan_DangerEndNeverArrives_StillClearsAfterScheduledTime()
        {
            BeginFan();
            Advance(6.3);
            Assert.That(_hard.ChainFan.Count, Is.EqualTo(1));

            Advance(1.0);
            Assert.That(_hard.ChainFan.Count, Is.EqualTo(1));

            Advance(0.4);

            Assert.That(_hard.ChainFan.Count, Is.Zero);
            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
        }

        [Test]
        public void ChainFan_UnconfirmedZone_ExpiresAndRevivesWhenBlastArrivesLate()
        {
            BeginFan();
            Advance(3.1);

            _hard.ChainFan.TryGetZone(CAST, 0, out bool blasted, out BossGroundShapeVFX shape);
            Assert.That(blasted, Is.False);
            Assert.That(shape, Is.Null);

            Assert.That(_hard.ChainFan.Blast(CAST, 0, START + 2.5), Is.True);
            RefreshAll();
            _hard.ChainFan.TryGetZone(CAST, 0, out blasted, out shape);
            Assert.That(blasted, Is.True);
            Assert.That(shape, Is.Not.Null);
        }

        [Test]
        public void ChainFan_Cancel_ClearsResidualAndBlocksLateStart()
        {
            BeginFan();
            Advance(2.6);
            _hard.ChainFan.Blast(CAST, 0, START + 2.5);
            RefreshAll();

            Assert.That(_hard.ChainFan.Cancel(CAST), Is.True);

            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
            Assert.That(BeginFan(), Is.False);
            Assert.That(_hard.ChainFan.Blast(CAST, 1, START + 3.0), Is.False);
        }

        [Test]
        public void ChainFan_LateJoin_RestoresBlastedZonesAsResidual()
        {
            _clock = START + 3.5;

            Assert.That(BeginFan(CAST, 2), Is.True);
            RefreshAll();

            _hard.ChainFan.TryGetState(CAST, out int blasted, out _, out _);
            Assert.That(blasted, Is.EqualTo(2));
            _hard.ChainFan.TryGetZone(CAST, 1, out bool zoneBlasted, out BossGroundShapeVFX shape);
            Assert.That(zoneBlasted, Is.True);
            Assert.That(shape.FillColor.a, Is.GreaterThan(0.4f));
            _hard.ChainFan.TryGetZone(CAST, 2, out zoneBlasted, out shape);
            Assert.That(zoneBlasted, Is.False);
            Assert.That(shape, Is.Not.Null);
        }

        [Test]
        public void ChainFan_LateJoin_AfterDangerEnded_IsRejected()
        {
            Assert.That(BeginFan(CAST, 7, true), Is.False);
            _clock = START + 20.0;
            Assert.That(BeginFan(2), Is.False);
        }

        /****************************************
        *        P04 대상 추적 장판
        ****************************************/

        [Test]
        public void TrackingArea_Fix_SnapsToServerCenter()
        {
            Assert.That(_hard.TrackingArea.BeginMoving(CAST, 7, 3, new Vector3(5f, 0f, 5f), 6f, START, 4f, 2.5f), Is.True);
            RefreshAll();
            _hard.TrackingArea.TryGetState(7, out Vector3 center, out bool isFixed, out int target, out BossGroundShapeVFX shape);
            Assert.That(center, Is.EqualTo(new Vector3(5f, 0f, 5f)));
            Assert.That(isFixed, Is.False);
            Assert.That(target, Is.EqualTo(3));
            Assert.That(shape.Kind, Is.EqualTo(EBossGroundShapeKind.Disc));
            Assert.That(shape.Radius, Is.EqualTo(6f));
            Color followColor = shape.FillColor;

            Advance(4.0);
            Assert.That(_hard.TrackingArea.UpdateMoving(7, new Vector3(8f, 0f, 5f), START + 3.9), Is.True);
            Assert.That(_hard.TrackingArea.FixCenter(7, new Vector3(9f, 0f, 5f), START + 4.0), Is.True);
            RefreshAll();

            _hard.TrackingArea.TryGetState(7, out center, out isFixed, out _, out shape);
            Assert.That(isFixed, Is.True);
            Assert.That(center, Is.EqualTo(new Vector3(9f, 0f, 5f)));
            Assert.That(shape.Origin, Is.EqualTo(new Vector3(9f, 0f, 5f)));
            Assert.That(shape.FillColor.g, Is.Not.EqualTo(followColor.g));
            Assert.That(_hard.TrackingArea.UpdateMoving(7, new Vector3(1f, 0f, 1f), START + 4.1), Is.False);
            Assert.That(_hard.TrackingArea.FixCenter(7, new Vector3(1f, 0f, 1f), START + 4.1), Is.False);
        }

        [Test]
        public void TrackingArea_StaleUpdate_IsIgnored()
        {
            _hard.TrackingArea.BeginMoving(CAST, 7, 3, Vector3.zero, 6f, START, 4f, 2.5f);
            _hard.TrackingArea.UpdateMoving(7, Vector3.right, START + 1.0);

            Assert.That(_hard.TrackingArea.UpdateMoving(7, Vector3.left, START + 0.5), Is.False);
        }

        [Test]
        public void TrackingArea_Activated_HandsOffAndBlocksLateStart()
        {
            _hard.TrackingArea.BeginMoving(CAST, 7, 3, Vector3.zero, 6f, START, 4f, 2.5f);
            RefreshAll();

            _hard.NotifyAreaActivated(7);

            Assert.That(_hard.TrackingArea.Count, Is.Zero);
            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
            Assert.That(_hard.TrackingArea.BeginMoving(CAST, 7, 3, Vector3.zero, 6f, START, 4f, 2.5f), Is.False);
            _hard.NotifyAreaActivated(99);
        }

        [Test]
        public void TrackingArea_TwoAreasInOneCast_AreIndependent()
        {
            _hard.TrackingArea.BeginMoving(CAST, 7, 3, Vector3.zero, 6f, START, 4f, 2.5f);
            _hard.TrackingArea.BeginMoving(CAST, 8, 4, Vector3.right * 4f, 6f, START, 4f, 2.5f);
            RefreshAll();
            Assert.That(_hard.ShapePool.LeasedCount, Is.EqualTo(2));

            Assert.That(_hard.TrackingArea.Cancel(7), Is.True);

            Assert.That(_hard.TrackingArea.Count, Is.EqualTo(1));
            Assert.That(_hard.ShapePool.LeasedCount, Is.EqualTo(1));
            Assert.That(_hard.TrackingArea.BeginMoving(CAST, 7, 3, Vector3.zero, 6f, START, 4f, 2.5f), Is.False);
        }

        [Test]
        public void TrackingArea_ActivationNeverArrives_ClearsAfterWait()
        {
            _hard.TrackingArea.BeginMoving(CAST, 7, 3, Vector3.zero, 6f, START, 4f, 2.5f);
            Advance(7.4);
            Assert.That(_hard.TrackingArea.Count, Is.EqualTo(1));

            Advance(0.2);

            Assert.That(_hard.TrackingArea.Count, Is.Zero);
            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
        }

        [Test]
        public void TrackingArea_LateJoin_RestoresFixedCenter()
        {
            _clock = START + 5.0;

            Assert.That(_hard.TrackingArea.BeginMoving(CAST, 7, 3, new Vector3(2f, 0f, 3f), 6f, START, 4f, 2.5f), Is.True);
            Assert.That(_hard.TrackingArea.FixCenter(7, new Vector3(2f, 0f, 3f), START + 4.0), Is.True);
            RefreshAll();

            _hard.TrackingArea.TryGetState(7, out Vector3 center, out bool isFixed, out _, out BossGroundShapeVFX shape);
            Assert.That(isFixed, Is.True);
            Assert.That(center, Is.EqualTo(new Vector3(2f, 0f, 3f)));
            Assert.That(shape.Charge, Is.EqualTo(Mathf.Clamp01(1f / 6.5f * 5f)).Within(TOLERANCE));

            _clock = START + 20.0;
            Assert.That(_hard.TrackingArea.BeginMoving(CAST, 9, 3, Vector3.zero, 6f, START, 4f, 2.5f), Is.False);
        }

        /****************************************
        *        P05 전방위 난사
        ****************************************/

        [Test]
        public void Barrage_TelegraphThenFire_ChargesFlashesAndClears()
        {
            Assert.That(BeginBarrage(), Is.True);
            Assert.That(_hard.Barrage.TelegraphShot(CAST, 0, 3, Vector3.forward, 20f, START), Is.True);
            RefreshAll();

            _hard.Barrage.TryGetShot(CAST, 0, out bool telegraphed, out bool fired, out BossGroundShapeVFX shape);
            Assert.That(telegraphed, Is.True);
            Assert.That(fired, Is.False);
            Assert.That(shape.Kind, Is.EqualTo(EBossGroundShapeKind.Rect));
            Assert.That(shape.Length, Is.EqualTo(20f));
            Assert.That(shape.Width, Is.EqualTo(1.5f));
            Assert.That(shape.Direction, Is.EqualTo(Vector3.forward));

            Advance(0.5);
            Assert.That(shape.Charge, Is.EqualTo(0.5f).Within(TOLERANCE));

            Advance(0.5);
            Assert.That(_hard.Barrage.FireShot(CAST, 0, true, START + 1.0), Is.True);
            RefreshAll();
            Assert.That(shape.Charge, Is.EqualTo(1f));

            Advance(0.2);
            _hard.Barrage.TryGetShot(CAST, 0, out _, out fired, out BossGroundShapeVFX after);
            Assert.That(fired, Is.True);
            Assert.That(after, Is.Null);
            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
        }

        [Test]
        public void Barrage_RefusedShot_ClearsPathWithoutFlash()
        {
            BeginBarrage();
            _hard.Barrage.TelegraphShot(CAST, 0, 3, Vector3.forward, 20f, START);
            Advance(1.0);

            Assert.That(_hard.Barrage.FireShot(CAST, 0, false, START + 1.0), Is.True);

            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
            Assert.That(_hard.Barrage.FireShot(CAST, 0, true, START + 1.0), Is.False);
        }

        [Test]
        public void Barrage_AllEightPaths_ShowTogetherWithEachDirection()
        {
            BeginBarrage();
            for (int i = 0; i < BARRAGE_SHOTS; ++i)
            {
                Vector3 direction = Quaternion.AngleAxis(i * 45f + 10f, Vector3.up) * Vector3.forward;
                Assert.That(_hard.Barrage.TelegraphShot(CAST, i, i, direction, 10f + i, START), Is.True);
            }

            RefreshAll();

            Assert.That(_hard.ShapePool.LeasedCount, Is.EqualTo(BARRAGE_SHOTS));
            for (int i = 0; i < BARRAGE_SHOTS; ++i)
            {
                Vector3 expected = Quaternion.AngleAxis(i * 45f + 10f, Vector3.up) * Vector3.forward;
                _hard.Barrage.TryGetShot(CAST, i, out _, out _, out BossGroundShapeVFX shape);
                Assert.That(Vector3.Distance(shape.Direction, expected), Is.LessThan(TOLERANCE));
                Assert.That(shape.Length, Is.EqualTo(10f + i));
            }
        }

        [Test]
        public void Barrage_DuplicateAndOutOfRangeShots_AreIgnored()
        {
            BeginBarrage();
            Assert.That(_hard.Barrage.TelegraphShot(CAST, 0, 3, Vector3.forward, 20f, START), Is.True);

            Assert.That(_hard.Barrage.TelegraphShot(CAST, 0, 3, Vector3.forward, 20f, START), Is.False);
            Assert.That(_hard.Barrage.TelegraphShot(CAST, BARRAGE_SHOTS, 3, Vector3.forward, 20f, START), Is.False);
            Assert.That(_hard.Barrage.TelegraphShot(CAST, 1, 3, Vector3.forward, 0f, START), Is.False);
            Assert.That(_hard.Barrage.TelegraphShot(99, 1, 3, Vector3.forward, 20f, START), Is.False);
        }

        [Test]
        public void Barrage_UnconfirmedShot_ExpiresAfterGrace()
        {
            BeginBarrage();
            _hard.Barrage.TelegraphShot(CAST, 0, 3, Vector3.forward, 20f, START);
            Advance(1.4);
            Assert.That(_hard.ShapePool.LeasedCount, Is.EqualTo(1));

            Advance(0.2);

            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
        }

        [Test]
        public void Barrage_Cancel_ClearsUnfiredPathsAndBlocksLateStart()
        {
            BeginBarrage();
            _hard.Barrage.TelegraphShot(CAST, 0, 3, Vector3.forward, 20f, START);
            _hard.Barrage.TelegraphShot(CAST, 1, 4, Vector3.right, 20f, START + 0.2);
            RefreshAll();

            Assert.That(_hard.Barrage.Cancel(CAST), Is.True);

            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
            Assert.That(BeginBarrage(), Is.False);
            Assert.That(_hard.Barrage.TelegraphShot(CAST, 2, 5, Vector3.back, 20f, START + 0.4), Is.False);
        }

        [Test]
        public void Barrage_LateJoin_RestoresOnlyUnfiredShots()
        {
            _clock = START + 1.6;

            Assert.That(BeginBarrage(CAST, 3), Is.True);
            Assert.That(_hard.Barrage.TelegraphShot(CAST, 1, 4, Vector3.right, 20f, START + 0.2), Is.False);
            Assert.That(_hard.Barrage.TelegraphShot(CAST, 3, 6, Vector3.back, 20f, START + 0.6), Is.True);
            Assert.That(_hard.Barrage.TelegraphShot(CAST, 4, 7, Vector3.left, 20f, START + 0.8), Is.True);
            RefreshAll();

            _hard.Barrage.TryGetShot(CAST, 0, out _, out bool fired, out BossGroundShapeVFX gone);
            Assert.That(fired, Is.True);
            Assert.That(gone, Is.Null);
            _hard.Barrage.TryGetShot(CAST, 3, out bool telegraphed, out _, out BossGroundShapeVFX shape);
            Assert.That(telegraphed, Is.True);
            Assert.That(shape.Charge, Is.EqualTo(1f).Within(TOLERANCE));
            Assert.That(_hard.ShapePool.LeasedCount, Is.EqualTo(2));
        }

        [Test]
        public void Barrage_AllShotsFired_RemovesCast()
        {
            BeginBarrage();
            for (int i = 0; i < BARRAGE_SHOTS; ++i)
            {
                _hard.Barrage.TelegraphShot(CAST, i, i, Vector3.forward, 10f, START + (i * 0.2));
            }

            _clock = START + 2.4;
            for (int i = 0; i < BARRAGE_SHOTS; ++i)
            {
                _hard.Barrage.FireShot(CAST, i, true, START + 1.0 + (i * 0.2));
            }

            Advance(0.3);

            Assert.That(_hard.Barrage.Count, Is.Zero);
            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
        }

        /****************************************
        *        P06 동시 원형 폭발
        ****************************************/

        [Test]
        public void CircleBurst_Begin_ShowsAllThirteenCirclesAtOnce()
        {
            Assert.That(BeginBurst(), Is.True);
            RefreshAll();

            Assert.That(_hard.CircleBurst.TryGetState(CAST, out bool struck, out int shown), Is.True);
            Assert.That(struck, Is.False);
            Assert.That(shown, Is.EqualTo(CIRCLE_COUNT));
            Assert.That(_hard.ShapePool.LeasedCount, Is.EqualTo(CIRCLE_COUNT));
            for (int i = 0; i < CIRCLE_COUNT; ++i)
            {
                BossGroundShapeVFX circle = _hard.CircleBurst.GetCircle(CAST, i);
                Assert.That(circle.Kind, Is.EqualTo(EBossGroundShapeKind.Disc));
                Assert.That(circle.Origin, Is.EqualTo(new Vector3(i, 0f, -i)));
                Assert.That(circle.Radius, Is.EqualTo((i == 0) ? 4f : 3f));
            }

            Advance(1.0);
            Assert.That(_hard.CircleBurst.GetCircle(CAST, 5).Charge, Is.EqualTo(0.5f).Within(TOLERANCE));
        }

        [Test]
        public void CircleBurst_Struck_FlashesTogetherThenLeavesNothing()
        {
            BeginBurst();
            Advance(2.0);

            Assert.That(_hard.CircleBurst.Struck(CAST, START + 2.0), Is.True);
            Assert.That(_hard.CircleBurst.Struck(CAST, START + 2.0), Is.False);
            RefreshAll();
            for (int i = 0; i < CIRCLE_COUNT; ++i)
            {
                Assert.That(_hard.CircleBurst.GetCircle(CAST, i).Charge, Is.EqualTo(1f));
            }

            Advance(0.4);

            Assert.That(_hard.CircleBurst.Count, Is.Zero);
            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
            Assert.That(BeginBurst(), Is.False);
        }

        [Test]
        public void CircleBurst_UnconfirmedStrike_ExpiresAfterGrace()
        {
            BeginBurst();
            Advance(2.4);
            Assert.That(_hard.CircleBurst.Count, Is.EqualTo(1));

            Advance(0.2);

            Assert.That(_hard.CircleBurst.Count, Is.Zero);
            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
        }

        [Test]
        public void CircleBurst_Cancel_ClearsAllAndBlocksLateStart()
        {
            BeginBurst();
            RefreshAll();

            Assert.That(_hard.CircleBurst.Cancel(CAST), Is.True);

            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
            Assert.That(BeginBurst(), Is.False);
            Assert.That(_hard.CircleBurst.Struck(CAST, START + 2.0), Is.False);
        }

        [Test]
        public void CircleBurst_LateJoin_SkipsFinishedBurstAndResumesCharging()
        {
            _clock = START + 1.5;
            Assert.That(BeginBurst(CAST), Is.True);
            RefreshAll();
            Assert.That(_hard.CircleBurst.GetCircle(CAST, 0).Charge, Is.EqualTo(0.75f).Within(TOLERANCE));

            _clock = START + 2.5;
            Assert.That(BeginBurst(2, true), Is.False);
        }

        [Test]
        public void CircleBurst_MismatchedLayout_IsRejected()
        {
            List<Vector3> centers = new List<Vector3> { Vector3.zero, Vector3.one };
            List<float>   radii   = new List<float> { 3f };

            Assert.That(_hard.CircleBurst.Begin(CAST, centers, radii, START, 2f), Is.False);
            Assert.That(_hard.CircleBurst.Begin(CAST, null, radii, START, 2f), Is.False);
        }

        /****************************************
        *        공통 정리
        ****************************************/

        [Test]
        public void ClearAll_Death_ReturnsEveryShapeFromAllSixViews()
        {
            BeginStar();
            BeginGrid();
            _hard.GridBeam.Telegraph(CAST, 1, true, 0, START, START + 1.0);
            BeginFan();
            _hard.TrackingArea.BeginMoving(CAST, 7, 3, Vector3.zero, 6f, START, 4f, 2.5f);
            BeginBarrage();
            _hard.Barrage.TelegraphShot(CAST, 0, 0, Vector3.forward, 10f, START);
            BeginBurst();
            Advance(1.5);
            Assert.That(_hard.ShapePool.LeasedCount, Is.GreaterThan(0));

            _hard.ClearAll();

            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
            Assert.That(_hard.StarStage.Count + _hard.GridBeam.Count + _hard.ChainFan.Count
                + _hard.TrackingArea.Count + _hard.Barrage.Count + _hard.CircleBurst.Count, Is.Zero);
        }

        [Test]
        public void ResetNetworkSession_ForgetsEndedCasts()
        {
            BeginBurst();
            _hard.CircleBurst.Cancel(CAST);
            Assert.That(BeginBurst(), Is.False);

            _hard.ResetNetworkSession();

            Assert.That(BeginBurst(), Is.True);
        }

        [Test]
        public void TrackingArea_ActivationArrivesBeforeStart_BlocksTheLateTelegraph()
        {
            _hard.NotifyAreaActivated(7);

            Assert.That(_hard.TrackingArea.BeginMoving(CAST, 7, 3, Vector3.zero, 6f, START, 4f, 2.5f), Is.False);
            Assert.That(_hard.TrackingArea.Count, Is.Zero);
        }

        [Test]
        public void ShapePool_Exhausted_CountsRejectedShapesInsteadOfHidingThem()
        {
            BossGroundShapePool pool = new BossGroundShapePool(2);
            try
            {
                Assert.That(pool.Acquire(), Is.Not.Null);
                Assert.That(pool.Acquire(), Is.Not.Null);
                Assert.That(pool.RejectedCount, Is.Zero);

                Assert.That(pool.Acquire(), Is.Null);

                Assert.That(pool.RejectedCount, Is.EqualTo(1));
                Assert.That(pool.LeasedCount, Is.EqualTo(2));
            }
            finally
            {
                pool.Dispose();
            }
        }
    }
}
