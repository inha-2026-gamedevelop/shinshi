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
    // 바인더가 월백 하드 표시와 무철 표시의 수명(연결 시작, 해제, 장판 활성 전달)을 올바로 부르는지 검증한다
    // 서버 사건을 이 뷰들에 전달하는 호출은 아직 계약이 없어 여기에 없다
    public class BossNetworkViewBinderPhaseViewTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double START = 100.0;

        /****************************************
        *                Fields
        ****************************************/

        private GameObject             _root;
        private BossState              _state;
        private BossTelegraphPresenter _presenter;
        private BossNetworkViewBinder  _binder;
        private BossHardPhaseView      _hard;
        private BossMucheolPhaseView   _mucheol;

        /****************************************
        *               Set Up
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("BossNetworkViewBinderPhaseViewTests");
            _root.AddComponent<NetworkIdentity>();
            _state = _root.AddComponent<BossState>();
            _presenter = _root.AddComponent<BossTelegraphPresenter>();
            _hard = _root.AddComponent<BossHardPhaseView>();
            _mucheol = _root.AddComponent<BossMucheolPhaseView>();
            _binder = _root.AddComponent<BossNetworkViewBinder>();

            ViewTestUtil.Awake(_hard);
            ViewTestUtil.AwakeAll<BossHardTelegraphViewBase>(_root);
            ViewTestUtil.Awake(_mucheol);
            ViewTestUtil.AwakeAll<BossPresenterViewBase>(_root);

            SetField("_presenter", _presenter);
            SetField("_hardPhaseView", _hard);
            SetField("_mucheolPhaseView", _mucheol);
        }

        [TearDown]
        public void TearDown()
        {
            if (_binder != null)
            {
                _binder.Unbind();
            }

            if (_hard != null)
            {
                _hard.ShapePool.Dispose();
            }

            if (_mucheol != null)
            {
                _mucheol.ShapePool.Dispose();
            }

            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        /****************************************
        *               Helpers
        ****************************************/

        private void SetField(string name, object value)
        {
            typeof(BossNetworkViewBinder).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_binder, value);
        }

        private void BeginAnyDisplays()
        {
            _hard.TrackingArea.BeginMoving(1, 7, 3, Vector3.zero, 6f, START, 4f, 2.5f);
            _hard.Barrage.Begin(2, Vector3.zero, START, 8, 1f, 0.2f, 1.5f);
            _mucheol.Battlefield.Begin(1, 3, Vector3.zero, Vector3.forward, 44f, 315f, START, 1.8f);
            _mucheol.Combo.Begin(1, 4, Vector3.zero, Vector3.forward, START, 0.7f, new[] { 5f }, new[] { 120f }, new[] { 0f });
        }

        /****************************************
        *               Tests
        ****************************************/

        [Test]
        public void Bind_GivesTheMucheolViewsThePresenter()
        {
            _binder.Bind(_state);

            Assert.That(_mucheol.Combo.Presenter, Is.SameAs(_presenter));
            Assert.That(_mucheol.Sequence.Presenter, Is.SameAs(_presenter));
            Assert.That(_mucheol.MultiDash.Presenter, Is.SameAs(_presenter));
        }

        [Test]
        public void Unbind_ClearsBothPhaseViews()
        {
            _binder.Bind(_state);
            BeginAnyDisplays();
            Assert.That(_hard.TrackingArea.Count + _hard.Barrage.Count + _mucheol.Battlefield.Count + _mucheol.Combo.CastCount, Is.EqualTo(4));

            _binder.Unbind();

            Assert.That(_hard.TrackingArea.Count + _hard.Barrage.Count + _mucheol.Battlefield.Count + _mucheol.Combo.CastCount, Is.Zero);
            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
            Assert.That(_mucheol.ShapePool.LeasedCount, Is.Zero);
        }

        [Test]
        public void Bind_AgainResetsEndedMemoryOfBothPhaseViews()
        {
            _binder.Bind(_state);
            _hard.TrackingArea.BeginMoving(1, 7, 3, Vector3.zero, 6f, START, 4f, 2.5f);
            _hard.TrackingArea.Cancel(7);
            Assert.That(_hard.TrackingArea.BeginMoving(1, 7, 3, Vector3.zero, 6f, START, 4f, 2.5f), Is.False);

            _binder.Unbind();
            _binder.Bind(_state);

            Assert.That(_hard.TrackingArea.BeginMoving(1, 7, 3, Vector3.zero, 6f, START, 4f, 2.5f), Is.True);
        }

        [Test]
        public void OnAreaActivated_HandsTheTrackingTelegraphOverToTheAreaDisplay()
        {
            _binder.Bind(_state);
            _hard.TrackingArea.BeginMoving(1, 7, 3, Vector3.zero, 6f, START, 4f, 2.5f);
            BossAreaStart area = new BossAreaStart(1, 7, Vector3.zero, 6f, 60f, 1f, START + 6.5);

            _binder.OnAreaActivated(in area);

            Assert.That(_hard.TrackingArea.Count, Is.Zero);
        }

        [Test]
        public void OnAreaActivated_BeforeBind_DoesNotTouchThePhaseViews()
        {
            _hard.TrackingArea.BeginMoving(1, 7, 3, Vector3.zero, 6f, START, 4f, 2.5f);
            BossAreaStart area = new BossAreaStart(1, 7, Vector3.zero, 6f, 60f, 1f, START + 6.5);

            _binder.OnAreaActivated(in area);

            Assert.That(_hard.TrackingArea.Count, Is.EqualTo(1));
        }
    }
}
