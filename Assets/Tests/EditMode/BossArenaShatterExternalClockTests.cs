// System
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Art;

namespace Shinshi.Tests
{
    // 서버 시계 기준 맵 붕괴 평가: 낙하 표는 swap 1초, 조각 하나가 1초 뒤부터 2초 동안 10m 떨어진다(끝 3초)
    public class BossArenaShatterExternalClockTests
    {
        private const BindingFlags INSTANCE_ANY = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string TIMELINE_JSON =
            "{\"swap_time\":1.0,\"crack_glow_start\":0.2,\"crack_glow_peak\":0.5,\"crack_glow_end\":1.0,\"objects\":[" +
            "{\"object_name\":\"Piece\",\"start_delay\":0.0,\"duration\":2.0,\"fall_distance\":10.0,\"total_spin_angle\":0.0," +
            "\"initial_position\":[0,0,0],\"rotation_center\":[0,0,0],\"outward_drift_vector\":[0,0,0],\"spin_axis\":[0,1,0],\"kept\":false}]}";

        private GameObject           _root;
        private GameObject           _intact;
        private GameObject           _fractured;
        private Transform            _piece;
        private BossArenaShatterView _arena;
        private BossNetworkViewBinder _binder;

        [SetUp]
        public void SetUp()
        {
            _root       = new GameObject("ArenaShatterTests");
            _intact     = new GameObject("Intact");
            _fractured  = new GameObject("Fractured");
            _piece      = new GameObject("Piece").transform;
            _intact.transform.SetParent(_root.transform, false);
            _fractured.transform.SetParent(_root.transform, false);
            _piece.SetParent(_fractured.transform, false);

            _arena = _root.AddComponent<BossArenaShatterView>();
            SetField(_arena, "_timelineJson", new TextAsset(TIMELINE_JSON));
            SetField(_arena, "_intactRoot", _intact);
            SetField(_arena, "_fracturedRoot", _fractured.transform);
            typeof(BossArenaShatterView).GetMethod("OnViewAwake", INSTANCE_ANY).Invoke(_arena, null);
            typeof(BossArenaShatterView).GetMethod("OnEnable", INSTANCE_ANY).Invoke(_arena, null);

            _binder = _root.AddComponent<BossNetworkViewBinder>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_binder != null)
            {
                _binder.Unbind();
            }

            if (_arena != null)
            {
                typeof(BossArenaShatterView).GetMethod("OnDisable", INSTANCE_ANY).Invoke(_arena, null);
            }

            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void EvaluateAt_LongStall_JumpsToAbsoluteElapsed()
        {
            Assert.That(_arena.EvaluateAt(0.1f), Is.True);

            // 1.9초 멈춘 프레임: DOTween deltaTime 상한이 아니라 넘긴 경과 시간 그대로 평가해야 한다
            Assert.That(_arena.EvaluateAt(2.0f), Is.True);

            Assert.That(_arena.EvaluatedTime, Is.EqualTo(2.0f).Within(1e-4f));
            Assert.That(_piece.position.y, Is.EqualTo(-2.5f).Within(1e-3f));
            Assert.That(_arena.IsPlaying, Is.False);
        }

        [Test]
        public void EvaluateAt_FirstCallLate_StartsAtThatPoint()
        {
            Assert.That(_arena.EvaluateAt(2.0f), Is.True);

            Assert.That(_fractured.activeSelf, Is.True);
            Assert.That(_intact.activeSelf, Is.False);
            Assert.That(_piece.position.y, Is.EqualTo(-2.5f).Within(1e-3f));
        }

        [Test]
        public void EvaluateAt_BeyondEnd_FinishesOnceAndResetRestarts()
        {
            Assert.That(_arena.EvaluateAt(100f), Is.False);
            Assert.That(_fractured.activeSelf, Is.False);
            Assert.That(_arena.EvaluateAt(0.5f), Is.False);

            _arena.ResetToIntact();

            Assert.That(_arena.EvaluateAt(0.5f), Is.True);
            Assert.That(_intact.activeSelf, Is.True);
        }

        [Test]
        public void Scheduled_BeforeServerStart_WaitsThenFollowsServerTime()
        {
            var cue = new WolbaekPhase2TransitionCue(1, 1, 10d, 14d);

            Present(EWolbaekPhase2TransitionDecision.Scheduled, cue, 9.7d);
            Tick(9.9d);

            Assert.That(_arena.EvaluatedTime, Is.EqualTo(0f));
            Assert.That(_intact.activeSelf, Is.False);

            Tick(10d);
            Assert.That(_intact.activeSelf, Is.True);

            // 한 프레임이 2초 멈춰도 다음 평가는 서버 절대 경과 시간이다
            Tick(12d);
            Assert.That(_arena.EvaluatedTime, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(_piece.position.y, Is.EqualTo(-2.5f).Within(1e-3f));
        }

        [Test]
        public void Play_LateJoin_EvaluatesServerElapsedImmediately()
        {
            var cue = new WolbaekPhase2TransitionCue(1, 1, 10d, 14d);

            Present(EWolbaekPhase2TransitionDecision.Play, cue, 12d);

            Assert.That(_arena.EvaluatedTime, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(_piece.position.y, Is.EqualTo(-2.5f).Within(1e-3f));
        }

        [Test]
        public void Ended_ShowsFinishedAndStopsTracking()
        {
            var cue = new WolbaekPhase2TransitionCue(1, 1, 10d, 14d);

            Present(EWolbaekPhase2TransitionDecision.Ended, cue, 20d);

            Assert.That(_fractured.activeSelf, Is.False);
            Assert.That(_intact.activeSelf, Is.False);
            Assert.That(GetField<bool>(_binder, "_isArenaTracking"), Is.False);
        }

        [TestCase(EWolbaekPhase2TransitionDecision.Duplicate)]
        [TestCase(EWolbaekPhase2TransitionDecision.TooEarly)]
        [TestCase(EWolbaekPhase2TransitionDecision.OtherSession)]
        [TestCase(EWolbaekPhase2TransitionDecision.Invalid)]
        public void RejectedDecision_DoesNotRestartOrRetarget(EWolbaekPhase2TransitionDecision rejected)
        {
            Present(EWolbaekPhase2TransitionDecision.Play, new WolbaekPhase2TransitionCue(1, 1, 10d, 14d), 11d);

            Present(rejected, new WolbaekPhase2TransitionCue(1, 1, 100d, 104d), 11.5d);
            Tick(12d);

            Assert.That(_arena.EvaluatedTime, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void Unbind_StopsTrackingAndRestoresIntact()
        {
            Present(EWolbaekPhase2TransitionDecision.Play, new WolbaekPhase2TransitionCue(1, 1, 10d, 14d), 12d);

            _binder.Unbind();
            Tick(13d);

            Assert.That(GetField<bool>(_binder, "_isArenaTracking"), Is.False);
            Assert.That(_arena.EvaluatedTime, Is.EqualTo(0f));
            Assert.That(_intact.activeSelf, Is.False);
            Assert.That(_fractured.activeSelf, Is.False);
        }

        private void Present(EWolbaekPhase2TransitionDecision decision, WolbaekPhase2TransitionCue cue, double serverNow)
        {
            typeof(BossNetworkViewBinder).GetMethod("PresentArenaShatter", INSTANCE_ANY)
                .Invoke(_binder, new object[] { decision, cue, serverNow });
        }

        private void Tick(double serverNow)
        {
            typeof(BossNetworkViewBinder).GetMethod("TickArenaShatter", INSTANCE_ANY)
                .Invoke(_binder, new object[] { serverNow });
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, INSTANCE_ANY).SetValue(target, value);
        }

        private static T GetField<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, INSTANCE_ANY).GetValue(target);
        }
    }
}
