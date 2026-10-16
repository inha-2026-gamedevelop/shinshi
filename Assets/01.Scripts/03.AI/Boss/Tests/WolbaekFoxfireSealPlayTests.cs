// System
using System.Collections;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Art;

namespace Shinshi.Tests
{
    // BossState와 Mirror 중계가 생기기 전 정식 프리팹에 서버 사건 DTO를 직접 주입한다.
    public class WolbaekFoxfireSealPlayTests
    {
        private const string PREFAB_PATH = "Assets/02.Prefab/Boss/PF_WolbaekBoss.prefab";
        private GameObject _boss;
        private WolbaekFoxfireSealView _view;
        private BossNetworkViewBinder _binder;
        private double _clock;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
#else
            GameObject prefab = null;
#endif
            Assert.IsNotNull(prefab);
            _boss = Object.Instantiate(prefab);
            _view = _boss.GetComponent<WolbaekFoxfireSealView>();
            Assert.IsNotNull(_view);
            _binder = _boss.AddComponent<BossNetworkViewBinder>();
            typeof(BossNetworkViewBinder).GetField("_foxfireSealView", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_binder, _view);
            _clock = 100d;
            _view.SetServerClock(() => _clock);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_view != null) _view.Clear();
            if (_boss != null) Object.Destroy(_boss);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PrepareTrackDetachLockResolveAndCounter_UseAuthoritativeEvents()
        {
            WolbaekSealStart start = Start(51);
            WolbaekSealTurn turn = Turn();
            var first = new GameObject("seal-member-first");
            var second = new GameObject("seal-member-second");
            first.transform.position = new Vector3(2f, 0f, 3f);
            second.transform.position = new Vector3(-4f, 0f, 5f);
            _view.SetMemberAnchor(11, first.transform);
            _view.SetMemberAnchor(12, second.transform);

            Assert.IsTrue(_view.StartSeal(in start, _clock));
            _view.TurnStarted(51, in turn, _clock);
            yield return null;
            Assert.IsTrue(IsPlaying("defense_inplace"));
            Assert.AreEqual(1, CountClones("SB_Shield_Blue"));
            Assert.AreEqual(2, _view.VisibleCoreCount);
            Assert.AreEqual(2, _view.VisibleDropCount);
            foreach (WolbaekSealCorePlayer core in Object.FindObjectsByType<WolbaekSealCorePlayer>())
            {
                Vector3 expected = turn.Cores[core.CurrentSnapshot.CoreIndex].Center;
                Assert.That(Vector3.Distance(expected, core.CurrentSnapshot.WorldCenter), Is.LessThan(0.01f));
            }

            _clock = 103d;
            _view.TrackingStarted(51, 0, 103d, _clock);
            yield return null;
            AssertDropTarget(0, first.transform.position);
            first.transform.position = new Vector3(6f, 0f, 7f);
            yield return null;
            AssertDropTarget(0, first.transform.position);

            Vector3 detached = new Vector3(8f, 0f, -2f);
            _clock = 104d;
            _view.FoxfireDetached(51, 0, 0, 104d, detached, _clock);
            yield return null;
            first.transform.position = Vector3.one * 20f;
            yield return null;
            AssertDropTarget(0, detached);

            var locked = new[] { new Vector3(10f, 0f, 1f), new Vector3(-9f, 0f, 2f) };
            _clock = 107d;
            _view.FoxfiresLocked(51, 0, 107d, locked, _clock);
            yield return null;
            AssertDropTarget(0, locked[0]);
            AssertDropTarget(1, locked[1]);

            _clock = 109d;
            _view.TurnResolved(51, 0, 109d, 1, _clock);
            yield return null;
            foreach (WolbaekSealCorePlayer core in Object.FindObjectsByType<WolbaekSealCorePlayer>())
                Assert.AreNotEqual(WolbaekSealCorePlayer.EPhase.Destroyed, core.CurrentSnapshot.Phase);

            _clock = 110d;
            _view.Succeeded(51, 110d, 116d, _clock);
            yield return null;
            Assert.IsTrue(_view.IsCounter);
            Assert.AreEqual(0, CountClones("SB_Shield_Blue"));
            Assert.AreEqual(0, _view.VisibleCoreCount);
            Assert.AreEqual(0, _view.VisibleDropCount);
            Assert.IsTrue(IsPlaying("down_groggy02_loop_inplace"));
            _clock = 116d;
            yield return null;
            Assert.IsTrue(IsPlaying("down_groggy02_loop_inplace"), "클라이언트 시각만으로 반격을 끝내면 안 된다");
            _binder.OnSealCounterEnded(51, 116d, false);
            yield return null;
            Assert.IsFalse(_view.IsCounter);
            Assert.IsTrue(IsPlaying("Idle"));
            Object.Destroy(first);
            Object.Destroy(second);
        }

        [UnityTest]
        public IEnumerator HardSweep_UsesServerShapeAndResolvesOnlyOnceWithoutBossAttackMotion()
        {
            WolbaekSealStart start = Start(52);
            WolbaekSealTurn turn = Turn();
            _view.StartSeal(in start, 100d);
            _view.TurnStarted(52, in turn, 100d);
            var sweep = new WolbaekSealSweep(0, 0, 103.7d, 104.5d,
                new Vector3(3f, 0f, -4f), Vector3.right, 16f, 60f, 1f);
            _clock = 104d;
            _view.SweepTelegraphed(52, in sweep, _clock);
            GameObject range = GameObject.Find("WolbaekSealSweepRange");
            Assert.IsNotNull(range);
            Assert.That(Vector3.Distance(sweep.Origin + (Vector3.up * 0.05f), range.transform.position), Is.LessThan(0.01f));
            float maxRadius = 0f;
            foreach (Vector3 vertex in range.GetComponent<MeshFilter>().sharedMesh.vertices)
                maxRadius = Mathf.Max(maxRadius, vertex.magnitude);
            Assert.That(maxRadius, Is.EqualTo(sweep.Range).Within(0.01f));
            Assert.IsFalse(IsPlaying("attack02_inplace") || IsPlaying("attack09_inplace"));

            _view.SweepResolved(52, 0, 0, 104.5d);
            _view.SweepResolved(52, 0, 0, 104.5d);
            yield return null;
            Assert.IsNull(GameObject.Find("WolbaekSealSweepRange"));
            Assert.AreEqual(2, CountClones("SB_Shockwave_Cone_Blue"));
            Assert.IsFalse(IsPlaying("attack02_inplace") || IsPlaying("attack09_inplace"));
        }

        [UnityTest]
        public IEnumerator RestoreTrackAndCounter_ResumeStateWithoutPastOneShotEffects()
        {
            WolbaekSealStart start = Start(53);
            WolbaekSealTurn turn = Turn();
            var pending = new WolbaekSealSweep(0, 1, 104d, 105d,
                new Vector3(-2f, 0f, 4f), Vector3.forward, 12f, 45f, 1f);
            var positions = new[] { new Vector3(4f, 0f, 1f), new Vector3(-5f, 0f, 2f) };
            var detached = new[] { true, false };
            var active = new WolbaekSealSnapshot(EWolbaekSealState.Active, EWolbaekSealStep.Track,
                in start, in turn, 0, positions, detached, false, true, in pending, 0d);
            _clock = 104.5d;
            Assert.IsTrue(_view.Restore(in active, _clock));
            yield return null;
            Assert.IsTrue(_view.IsActive);
            Assert.AreEqual(2, _view.VisibleCoreCount);
            Assert.AreEqual(2, _view.VisibleDropCount);
            Assert.IsTrue(_view.HasPendingSweep);
            Assert.AreEqual(0, CountClones("SB_Shockwave_Cone_Blue"));
            AssertDropTarget(0, positions[0]);

            var cleanup = new WolbaekSealSnapshot(EWolbaekSealState.Active, EWolbaekSealStep.Cleanup,
                in start, in turn, 1, positions, detached, true, false, in pending, 0d);
            _clock = 110d;
            Assert.IsTrue(_view.Restore(in cleanup, _clock));
            yield return null;
            Assert.AreEqual(0, _view.VisibleCoreCount);
            Assert.AreEqual(0, _view.VisibleDropCount);
            Assert.AreEqual(0, CountClones("SB_Shockwave_Cone_Blue"));

            WolbaekSealStart counterStart = Start(54);
            var counter = new WolbaekSealSnapshot(EWolbaekSealState.Counter, EWolbaekSealStep.Cleanup,
                in counterStart, in turn, 3, positions, detached, true, false, in pending, 116d);
            Assert.IsTrue(_view.Restore(in counter, 112d));
            yield return null;
            Assert.IsTrue(_view.IsCounter);
            Assert.IsTrue(IsPlaying("down_groggy02_loop_inplace"));
            Assert.AreEqual(0, CountClones("SB_Shockwave_Cone_Blue"));
        }

        [UnityTest]
        public IEnumerator MissingAnchorHidesTrackingUntilAuthoritativeAnchorOrDetachArrives()
        {
            WolbaekSealStart start = Start(55);
            WolbaekSealTurn turn = Turn();
            _view.StartSeal(in start, 100d);
            _view.TurnStarted(55, in turn, 100d);
            _clock = 103d;
            _view.TrackingStarted(55, 0, 103d, _clock);
            yield return null;
            Assert.AreEqual(0, _view.VisibleDropCount);
            var anchor = new GameObject("late-seal-member");
            anchor.transform.position = new Vector3(7f, 0f, 8f);
            _view.SetMemberAnchor(11, anchor.transform);
            yield return null;
            Assert.AreEqual(1, _view.VisibleDropCount);
            AssertDropTarget(0, anchor.transform.position);
            _view.SetMemberAnchor(11, null);
            yield return null;
            Assert.AreEqual(0, _view.VisibleDropCount);
            Vector3 lastServerPosition = new Vector3(9f, 0f, -3f);
            _view.FoxfireDetached(55, 0, 0, 104d, lastServerPosition, 104d);
            yield return null;
            Assert.AreEqual(1, _view.VisibleDropCount);
            AssertDropTarget(0, lastServerPosition);
            Object.Destroy(anchor);
        }

        [UnityTest]
        public IEnumerator FailureHaltClearUnbindDeathAndReset_RemoveAllPresentation()
        {
            WolbaekSealStart failed = Start(56);
            _view.StartSeal(in failed, 100d);
            _binder.OnSealFailed(56, 101d, EWolbaekSealFail.TimeExpired);
            yield return null;
            AssertCleared();
            Assert.IsFalse(_view.StartSeal(in failed, 101d));

            WolbaekSealStart halted = Start(57);
            _view.StartSeal(in halted, 101d);
            _binder.OnSealHalted(57, 102d, EWolbaekSealHaltReason.ArenaUnavailable);
            yield return null;
            AssertCleared();

            WolbaekSealStart cleared = Start(58);
            _view.StartSeal(in cleared, 102d);
            _binder.OnSealCleared(58);
            yield return null;
            AssertCleared();

            WolbaekSealStart unbound = Start(59);
            _view.StartSeal(in unbound, 103d);
            _binder.Unbind();
            yield return null;
            AssertCleared();

            WolbaekSealStart dead = Start(60);
            _view.StartSeal(in dead, 104d);
            BossView bossView = _boss.GetComponent<BossView>();
            bossView.SetDefeated(true);
            yield return null;
            Assert.IsFalse(_view.IsActive);
            Assert.IsTrue(IsPlaying("dead01_inplace") || IsPlaying("dead01_hold"));
            bossView.ResetView();
            yield return null;
            Assert.IsTrue(IsPlaying("Idle"));
            WolbaekSealStart reused = Start(1);
            Assert.IsTrue(_view.StartSeal(in reused, 105d));
        }

        [UnityTest]
        public IEnumerator TerminalAndNextBoundarySnapshots_DoNotRestoreOrStartZeroPercent()
        {
            WolbaekSealStart start = Start(61);
            WolbaekSealTurn turn = Turn();
            var positions = new[] { Vector3.zero, Vector3.one };
            var detached = new[] { false, false };
            WolbaekSealSweep sweep = default;
            var pending = new WolbaekSealSnapshot(EWolbaekSealState.NextBoundaryPending,
                EWolbaekSealStep.Cleanup, in start, in turn, 3, positions, detached, true, false, in sweep, 0d);
            Assert.IsFalse(_view.Restore(in pending, 120d));
            yield return null;
            AssertCleared();
            Assert.IsFalse(_view.StartSeal(in start, 121d));
        }

        private static WolbaekSealStart Start(int castId)
        {
            return new WolbaekSealStart(1, castId, 100d, 129d, 3,
                3f, 4f, 2f, 1f, 2.5f, 3f, Vector3.zero);
        }

        private static WolbaekSealTurn Turn()
        {
            var cores = new[]
            {
                new WolbaekSealCore(0, new Vector3(11f, 0f, 2f)),
                new WolbaekSealCore(1, new Vector3(-12f, 0f, 3f))
            };
            var foxfires = new[]
            {
                new WolbaekSealFoxfire(0, 11),
                new WolbaekSealFoxfire(1, 12)
            };
            return new WolbaekSealTurn(0, 100d, 103d, 107d, 109d, 110d, cores, foxfires);
        }

        private void AssertDropTarget(int index, Vector3 expected)
        {
            Assert.IsTrue(_view.TryGetDropTarget(index, out Vector3 actual));
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.01f));
        }

        private void AssertCleared()
        {
            Assert.IsFalse(_view.IsActive);
            Assert.IsFalse(_view.IsCounter);
            Assert.AreEqual(0, _view.VisibleCoreCount);
            Assert.AreEqual(0, _view.VisibleDropCount);
            Assert.IsFalse(_view.HasPendingSweep);
            Assert.IsTrue(IsPlaying("Idle"));
        }

        private static int CountClones(string name)
        {
            int count = 0;
            foreach (Transform item in Object.FindObjectsByType<Transform>())
                if (item.name == name + "(Clone)") ++count;
            return count;
        }

        private bool IsPlaying(string state)
        {
            Animator animator = _boss.GetComponentInChildren<Animator>();
            return animator.GetCurrentAnimatorStateInfo(0).IsName(state)
                || animator.GetNextAnimatorStateInfo(0).IsName(state);
        }
    }
}
