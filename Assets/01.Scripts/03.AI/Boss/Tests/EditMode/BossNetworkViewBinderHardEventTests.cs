// System
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    // 바인더가 월백 하드 2페이즈 서버 사건(IBossHardPhaseStateListener)을 하드 표시 뷰로 전달하는지 확인한다
    // 이 사건이 바인더에 구현되지 않았을 때는 BossState가 사건을 버려 하드 표시가 그려지지 않았다
    public class BossNetworkViewBinderHardEventTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double START   = 100.0;
        private const int    BOSS_ID = 1;
        private const int    CAST_ID = 7;

        /****************************************
        *                Fields
        ****************************************/

        private GameObject             _root;
        private BossNetworkViewBinder  _binder;
        private BossHardPhaseView      _hard;

        /****************************************
        *               Set Up
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("BossNetworkViewBinderHardEventTests");
            _root.AddComponent<NetworkIdentity>();
            _root.AddComponent<BossState>();
            _root.AddComponent<BossTelegraphPresenter>();
            _hard = _root.AddComponent<BossHardPhaseView>();
            _binder = _root.AddComponent<BossNetworkViewBinder>();

            ViewTestUtil.Awake(_hard);
            ViewTestUtil.AwakeAll<BossHardTelegraphViewBase>(_root);
            typeof(BossNetworkViewBinder).GetField("_hardPhaseView", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_binder, _hard);
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
        *               Tests
        ****************************************/

        [Test]
        public void Binder_ImplementsTheHardPhaseListenerContract()
        {
            Assert.That(_binder is IBossHardPhaseStateListener, Is.True);
        }

        [Test]
        public void StarStage_StartAndCancel_ReachesTheStarStageView()
        {
            BossStarShape shape = BossStarGeometry.Place(Vector3.zero, 18f, Vector3.forward, 14f, 5, 2, 1.5f, 0f, true);
            var start = new BossStarStageStart(BOSS_ID, CAST_ID, 3, shape, 30f, 3, START, 2f, 0.8f);

            _binder.OnStarStageStarted(in start);
            Assert.That(_hard.StarStage.Count, Is.EqualTo(1));

            _binder.OnStarStageCancelled(CAST_ID);
            Assert.That(_hard.StarStage.Count, Is.EqualTo(0));
        }

        [Test]
        public void GridBeam_StartTelegraphStrike_ReachesTheGridView()
        {
            var start = new BossGridBeamStart(BOSS_ID, CAST_ID, Vector3.zero, 18f, 12, 3f, 3f, START, 1f, 0.5f, 1.5f);

            _binder.OnGridBeamStarted(in start);
            Assert.That(_hard.GridBeam.Count, Is.EqualTo(1));

            _binder.OnGridBeamLineTelegraphed(CAST_ID, 0, true, 0, START, START + 1.0);
            Assert.That(_hard.GridBeam.TryGetLine(CAST_ID, true, 0, out bool isTelegraphed, out bool isStruck, out _), Is.True);
            Assert.That(isTelegraphed, Is.True);
            Assert.That(isStruck, Is.False);

            _binder.OnGridBeamLineStruck(CAST_ID, 0, true, 0, START + 1.0);
            _hard.GridBeam.TryGetLine(CAST_ID, true, 0, out _, out isStruck, out _);
            Assert.That(isStruck, Is.True);

            _binder.OnGridBeamCancelled(CAST_ID);
            Assert.That(_hard.GridBeam.Count, Is.EqualTo(0));
        }

        [Test]
        public void ChainFan_StartAndBlast_ReachesTheChainFanView()
        {
            var start = new BossChainFanStart(BOSS_ID, CAST_ID, Vector3.zero, Vector3.back, 12f, 7, 45f, START, 0.5f, 2.5f, 0.5f, 3f);

            _binder.OnChainFanStarted(in start);
            Assert.That(_hard.ChainFan.Count, Is.EqualTo(1));

            _binder.OnChainFanBlasted(CAST_ID, 0, START + 2.5);
            _hard.ChainFan.TryGetState(CAST_ID, out int blastedCount, out _, out _);
            Assert.That(blastedCount, Is.EqualTo(1));

            _binder.OnChainFanCancelled(CAST_ID);
            Assert.That(_hard.ChainFan.Count, Is.EqualTo(0));
        }

        [Test]
        public void TrackingArea_StartFixCancel_ReachesTheTrackingAreaView()
        {
            var start = new BossTrackingAreaStart(BOSS_ID, CAST_ID, 9, 3, Vector3.zero, 6f, START, 4f, 2.5f);

            _binder.OnTrackingAreaStarted(in start);
            Assert.That(_hard.TrackingArea.Count, Is.EqualTo(1));

            _binder.OnTrackingAreaFixed(9, new Vector3(2f, 0f, 2f), START + 4.0);
            _hard.TrackingArea.TryGetState(9, out Vector3 center, out bool isFixed, out _, out _);
            Assert.That(isFixed, Is.True);
            Assert.That(center, Is.EqualTo(new Vector3(2f, 0f, 2f)));

            _binder.OnTrackingAreaCancelled(9);
            Assert.That(_hard.TrackingArea.Count, Is.EqualTo(0));
        }

        [Test]
        public void Barrage_StartShotFireCancel_ReachesTheBarrageView()
        {
            var start = new BossBarrageStart(BOSS_ID, CAST_ID, Vector3.zero, START, 8, 1f, 0.2f, 1f);

            _binder.OnBarrageStarted(in start);
            Assert.That(_hard.Barrage.Count, Is.EqualTo(1));

            var shot = new BossBarrageShot(CAST_ID, 0, 2, Vector3.right, 10f, START + 1.0);
            _binder.OnBarrageShotTelegraphed(in shot);
            Assert.That(_hard.Barrage.TryGetShot(CAST_ID, 0, out bool isTelegraphed, out bool isFired, out _), Is.True);
            Assert.That(isTelegraphed, Is.True);
            Assert.That(isFired, Is.False);

            _binder.OnBarrageShotFired(CAST_ID, 0, 55, true, START + 1.0);
            _hard.Barrage.TryGetShot(CAST_ID, 0, out _, out isFired, out _);
            Assert.That(isFired, Is.True);

            _binder.OnBarrageCancelled(CAST_ID);
            Assert.That(_hard.Barrage.Count, Is.EqualTo(0));
        }

        [Test]
        public void CircleBurst_StartAndStrike_ReachesTheCircleBurstView()
        {
            var start = new BossCircleBurstStart(BOSS_ID, CAST_ID, new[] { Vector3.zero, new Vector3(6f, 0f, 6f) }, new[] { 5.5f, 3f }, START, 2f);

            _binder.OnCircleBurstStarted(in start);
            Assert.That(_hard.CircleBurst.Count, Is.EqualTo(1));

            _binder.OnCircleBurstStruck(CAST_ID, START + 2.0);
            _hard.CircleBurst.TryGetState(CAST_ID, out bool isStruck, out _);
            Assert.That(isStruck, Is.True);
        }

        [Test]
        public void Restored_Snapshots_ContinueTheDisplays()
        {
            var circle = new BossCircleBurstStart(BOSS_ID, CAST_ID, new[] { Vector3.zero }, new[] { 5f }, START, 2f);
            _binder.OnCircleBurstRestored(new BossCircleBurstSnapshot { Start = circle, IsStruck = false });
            Assert.That(_hard.CircleBurst.Count, Is.EqualTo(1));

            var barrage = new BossBarrageStart(BOSS_ID, 8, Vector3.zero, START, 8, 1f, 0.2f, 1f);
            _binder.OnBarrageRestored(new BossBarrageSnapshot { Start = barrage, FiredCount = 2, Shots = new BossBarrageShot[0] });
            Assert.That(_hard.Barrage.Count, Is.EqualTo(1));
        }
    }
}
