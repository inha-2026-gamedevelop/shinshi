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
    public class BossNetworkViewBinderTests
    {
        private GameObject _root;
        private BossState _state;
        private BossTelegraphPresenter _presenter;
        private BossNetworkViewBinder _binder;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("BossNetworkViewBinderTests");
            _root.AddComponent<NetworkIdentity>();
            _state = _root.AddComponent<BossState>();
            _presenter = _root.AddComponent<BossTelegraphPresenter>();
            _binder = _root.AddComponent<BossNetworkViewBinder>();
            typeof(BossNetworkViewBinder).GetField("_presenter", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_binder, _presenter);
        }

        [TearDown]
        public void TearDown()
        {
            if (_binder != null)
            {
                _binder.Unbind();
                InvokeDestroy();
            }
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void OnViewAwake_ChildEffectsRoot_DetachesWithoutChangingWorldPose()
        {
            var effects = new GameObject("Effects");
            effects.transform.SetParent(_root.transform);
            effects.transform.position = new Vector3(3f, 2f, 5f);
            SetEffectsRoot(effects.transform);

            InvokeViewAwake();
            _root.transform.position = Vector3.one * 10f;

            Assert.That(effects.transform.parent, Is.Null);
            Assert.That(effects.transform.position, Is.EqualTo(new Vector3(3f, 2f, 5f)));
            // EditMode AddComponent는 실제 Awake를 실행하지 않아 OnDestroy도 자동 보장되지 않는다.
            InvokeDestroy();
            Object.DestroyImmediate(_root);
            Assert.That(effects == null, Is.True);
        }

        [Test]
        public void OnDestroy_ExternalEffectsRoot_DoesNotDestroySharedObject()
        {
            var effects = new GameObject("ExternalEffects");
            try
            {
                SetEffectsRoot(effects.transform);
                InvokeViewAwake();

                InvokeDestroy();
                Object.DestroyImmediate(_root);

                Assert.That(effects != null, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(effects);
            }
        }

        [Test]
        public void OnDisable_DetachedEffectsRoot_HidesUntilReenabled()
        {
            var effects = new GameObject("Effects");
            effects.transform.SetParent(_root.transform);
            SetEffectsRoot(effects.transform);
            InvokeViewAwake();

            typeof(BossNetworkViewBinder).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_binder, null);
            Assert.That(effects.activeSelf, Is.False);
            typeof(BossNetworkViewBinder).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_binder, null);
            Assert.That(effects.activeSelf, Is.True);
        }

        private void SetEffectsRoot(Transform effects)
        {
            typeof(BossNetworkViewBinder).GetField("_effectsRoot", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_binder, effects);
        }

        private void InvokeViewAwake()
        {
            typeof(BossNetworkViewBinder).GetMethod("OnViewAwake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_binder, null);
        }

        private void InvokeDestroy()
        {
            typeof(BossNetworkViewBinder).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_binder, null);
        }

        [Test]
        public void Bind_LateClient_RestoresCurrentAttackAndKeepsOriginalServerTime()
        {
            BossTelegraphStart start = MakeStart(1);
            _state.ClientLedger.OnTelegraphStarted(in start);
            _state.OnStartClient();

            _binder.Bind(_state);

            Assert.That(_presenter.ActiveCount, Is.EqualTo(1));
            Assert.That(_presenter.TryGetSnapshot(1, out BossTelegraphSnapshot snapshot), Is.True);
            Assert.That(snapshot.Start.StartServerTime, Is.EqualTo(start.StartServerTime));
            _binder.Bind(_state);
            Assert.That(_presenter.ActiveCount, Is.EqualTo(1));
        }

        [Test]
        public void Unbind_ActiveAttack_ClearsAndRejectsFurtherCallbacks()
        {
            _binder.Bind(_state);
            BossTelegraphStart start = MakeStart(1);
            _binder.OnTelegraphStarted(in start);
            Assert.That(_presenter.ActiveCount, Is.EqualTo(1));

            _binder.Unbind();
            _binder.OnTelegraphStarted(in start);

            Assert.That(_presenter.ActiveCount, Is.Zero);
        }

        [Test]
        public void Bind_AfterUnbind_ReplaysLedgerAgain()
        {
            BossTelegraphStart start = MakeStart(1);
            _state.ClientLedger.OnTelegraphStarted(in start);
            _state.OnStartClient();
            _binder.Bind(_state);
            _binder.Unbind();

            _binder.Bind(_state);

            Assert.That(_presenter.ActiveCount, Is.EqualTo(1));
        }

        [Test]
        public void Bind_DifferentBoss_ClearsPreviousAttackBeforeReplay()
        {
            _binder.Bind(_state);
            BossTelegraphStart first = MakeStart(1);
            _binder.OnTelegraphStarted(in first);
            var otherRoot = new GameObject("OtherBoss");
            try
            {
                otherRoot.AddComponent<NetworkIdentity>();
                BossState otherState = otherRoot.AddComponent<BossState>();
                BossTelegraphStart next = MakeStart(2);
                otherState.ClientLedger.OnTelegraphStarted(in next);
                otherState.OnStartClient();

                _binder.Bind(otherState);

                Assert.That(_presenter.TryGetSnapshot(1, out _), Is.False);
                Assert.That(_presenter.TryGetSnapshot(2, out _), Is.True);
                Assert.That(_presenter.ActiveCount, Is.EqualTo(1));
            }
            finally
            {
                _binder.Unbind();
                Object.DestroyImmediate(otherRoot);
            }
        }

        [Test]
        public void Update_StoppedClient_ClearsVisuals()
        {
            _binder.Bind(_state);
            BossTelegraphStart start = MakeStart(1);
            _binder.OnTelegraphStarted(in start);
            _state.OnStopClient();

            typeof(BossNetworkViewBinder).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_binder, null);

            Assert.That(_presenter.ActiveCount, Is.Zero);
        }

        [Test]
        public void OnTelegraphStarted_SleepingPresenter_WakesForNetworkEvent()
        {
            _binder.Bind(_state);
            _presenter.enabled = false;
            BossTelegraphStart start = MakeStart(1);

            _binder.OnTelegraphStarted(in start);

            Assert.That(_presenter.enabled, Is.True);
            Assert.That(_presenter.ActiveCount, Is.EqualTo(1));
        }

        [Test]
        public void ResetNetworkSession_EndedProjectile_AllowsNewObservationButClearAllDoesNot()
        {
            _binder.Bind(_state);
            var start = new BossProjectileStart(1, 4, Vector3.zero, Vector3.forward, 1f, 1f, 100f, NetworkTime.time);
            Assert.That(_presenter.LaunchProjectile(in start), Is.True);
            _presenter.ClearAll();
            Assert.That(_presenter.LaunchProjectile(in start), Is.False);

            _presenter.ResetNetworkSession();

            Assert.That(_presenter.LaunchProjectile(in start), Is.True);
            Assert.That(_presenter.ProjectileCount, Is.EqualTo(1));
        }

        [Test]
        public void Bind_AfterCancelledGroup_AllowsAuthoritativeReplayOnNewObservation()
        {
            _binder.Bind(_state);
            BossTelegraphStart start = BossTelegraphStart.Circle(1, 2, "wolbaek_p07", Vector3.zero, 3f,
                NetworkTime.time, 10f, 9);
            _binder.OnSweepCancelled(9);
            _binder.OnSweepZoneScheduled(in start);
            Assert.That(_presenter.ActiveCount, Is.Zero);
            _binder.Unbind();
            _state.ClientLedger.OnTelegraphStarted(in start);
            _state.OnStartClient();

            _binder.Bind(_state);

            Assert.That(_presenter.ActiveCount, Is.EqualTo(1));
        }

        [Test]
        public void OnSweepCancelled_ActiveGroup_DoesNotRestoreLateZone()
        {
            _binder.Bind(_state);
            BossTelegraphStart start = BossTelegraphStart.Circle(1, 2, "wolbaek_p07", Vector3.zero, 3f,
                NetworkTime.time, 10f, 9);
            _binder.OnSweepZoneScheduled(in start);

            _binder.OnSweepCancelled(9);
            _binder.OnSweepZoneScheduled(in start);

            Assert.That(_presenter.ActiveCount, Is.Zero);
        }

        private static BossTelegraphStart MakeStart(int castId)
        {
            return BossTelegraphStart.Circle(1, castId, "wolbaek_p03", Vector3.zero, 3f, NetworkTime.time, 10f);
        }
    }
}
