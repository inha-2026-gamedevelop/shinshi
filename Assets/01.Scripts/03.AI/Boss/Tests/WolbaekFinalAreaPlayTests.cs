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
    public class WolbaekFinalAreaPlayTests
    {
        private const string PREFAB_PATH = "Assets/02.Prefab/Boss/PF_WolbaekBoss.prefab";

        private GameObject _boss;
        private WolbaekFinalAreaView _view;
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
            _view = _boss.GetComponent<WolbaekFinalAreaView>();
            Assert.IsNotNull(_view);
            _binder = _boss.AddComponent<BossNetworkViewBinder>();
            typeof(BossNetworkViewBinder).GetField("_finalAreaView", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_binder, _view);
            _clock = 100d;
            _view.SetServerClock(() => _clock);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_view != null)
            {
                _view.Clear();
            }
            if (_boss != null)
            {
                Object.Destroy(_boss);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Start_ShowsGroggyShieldAndAuthoritativeSafeBoundary()
        {
            WolbaekFinalStart start = Start(71);
            _binder.OnFinalStarted(in start);
            yield return null;

            Assert.IsTrue(_view.IsActive);
            Assert.IsTrue(IsPlaying("down_groggy02_loop_inplace"));
            Assert.AreEqual(1, CountClones("SB_Shield_Blue"));
            GameObject boundary = GameObject.Find("WolbaekFinalSafeBoundary");
            Assert.IsNotNull(boundary);
            float maxRadius = 0f;
            foreach (Vector3 vertex in boundary.GetComponent<MeshFilter>().sharedMesh.vertices)
            {
                maxRadius = Mathf.Max(maxRadius, vertex.magnitude);
            }
            Assert.That(maxRadius, Is.EqualTo(start.CentralRadius + 0.18f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator FloorYellowsAndReds_UseServerPositionsAndConfirmedEvents()
        {
            WolbaekFinalStart start = Start(72);
            Assert.IsTrue(_view.StartFinal(in start, _clock));
            _clock = 104d;
            _view.OuterFloorRemoved(72, 104d, _clock);
            WolbaekFinalYellow[] yellows = Yellows();
            _view.YellowsSpawned(72, yellows, _clock);
            yield return null;

            Assert.IsTrue(_view.IsFloorRemoved);
            Assert.AreEqual(2, _view.VisibleYellowCount);
            Assert.IsNull(GameObject.Find("WolbaekFinalSafeBoundary"));
            Assert.That(FindRunningAt(yellows[0].Position), Is.Not.Null);

            var first = new GameObject("final-member-first");
            var second = new GameObject("final-member-second");
            first.transform.position = new Vector3(3f, 0f, 2f);
            second.transform.position = new Vector3(-4f, 0f, 5f);
            _view.SetParticipantAnchor(11, first.transform);
            _view.SetParticipantAnchor(12, second.transform);
            WolbaekFinalBundle bundle = Bundle();
            _view.BundleStarted(72, in bundle, _clock);
            yield return null;
            Assert.AreEqual(2, _view.VisibleRedCount);

            Vector3 detached = new Vector3(6f, 0f, -3f);
            _clock = 106d;
            _view.RedDetached(72, 0, 0, 106d, detached, _clock);
            first.transform.position = Vector3.one * 30f;
            yield return null;

            var centers = new[] { detached, new Vector3(-7f, 0f, 4f) };
            _clock = 108d;
            _view.LockReds(72, 0, 108d, centers, _clock);
            yield return null;
            Assert.IsTrue(_view.RedsLocked);
            Assert.That(FindRunningAt(centers[0]), Is.Not.Null);
            Assert.That(FindRunningAt(centers[1]), Is.Not.Null);

            _clock = 110d;
            _view.BundleResolved(72, 0, 110d, _clock);
            _view.YellowDestroyed(72, 0, 110d, _clock);
            yield return null;
            Assert.AreEqual(2, _view.VisibleRedCount);
            Assert.AreEqual(2, _view.VisibleYellowCount);
            Object.Destroy(first);
            Object.Destroy(second);
        }

        [UnityTest]
        public IEnumerator Restore_DoesNotReplayPastFloorLockExplosionOrYellowBreakEffects()
        {
            WolbaekFinalStart start = Start(73);
            WolbaekFinalYellow[] yellows = Yellows();
            WolbaekFinalBundle bundle = Bundle();
            var health = new[] { 0f, 10f };
            var positions = new[] { new Vector3(5f, 0f, 1f), new Vector3(-5f, 0f, 2f) };
            var detached = new[] { true, true };
            var snapshot = new WolbaekFinalSnapshot(EWolbaekFinalStep.Hunt, in start, true,
                yellows, health, true, in bundle, positions, detached, true);
            _clock = 109d;

            Assert.IsTrue(_view.Restore(in snapshot, _clock));
            yield return null;

            Assert.IsTrue(_view.IsActive);
            Assert.IsTrue(_view.IsFloorRemoved);
            Assert.AreEqual(1, _view.VisibleYellowCount);
            Assert.AreEqual(2, _view.VisibleRedCount);
            Assert.AreEqual(0, CountClones("SB_Shockwave_Flash_Blue"));
            Assert.AreEqual(0, CountClones("SB_Shockwave_Flash_Red"));
        }

        [UnityTest]
        public IEnumerator FinalSuccess_ClearsGimmickAndUsesExistingDeathDissolvePath()
        {
            WolbaekFinalStart start = Start(74);
            Assert.IsTrue(_view.StartFinal(in start, _clock));
            _clock = 104d;
            _view.OuterFloorRemoved(74, 104d, _clock);
            _view.YellowsSpawned(74, Yellows(), _clock);
            yield return null;

            _clock = 112d;
            _view.Succeeded(74, 112d, _clock);
            yield return null;

            Assert.IsFalse(_view.IsActive);
            Assert.AreEqual(0, _view.VisibleYellowCount);
            Assert.AreEqual(0, CountClones("SB_Shield_Blue"));
            Assert.IsTrue(IsPlaying("dead01_inplace") || IsPlaying("dead01_hold"));
        }

        [UnityTest]
        public IEnumerator FailureHaltClearUnbindDeathAndReset_RemoveEveryFinalVisual()
        {
            WolbaekFinalStart failed = Start(75);
            _view.StartFinal(in failed, _clock);
            _binder.OnFinalFailed(75);
            yield return null;
            AssertCleared();
            Assert.IsFalse(_view.StartFinal(in failed, _clock));

            WolbaekFinalStart halted = Start(76);
            _view.StartFinal(in halted, _clock);
            _binder.OnFinalHalted(76);
            yield return null;
            AssertCleared();

            WolbaekFinalStart unbound = Start(77);
            _view.StartFinal(in unbound, _clock);
            _binder.Unbind();
            yield return null;
            AssertCleared();

            WolbaekFinalStart dead = Start(78);
            _view.StartFinal(in dead, _clock);
            BossView bossView = _boss.GetComponent<BossView>();
            bossView.SetDefeated(true);
            yield return null;
            Assert.IsFalse(_view.IsActive);
            Assert.IsTrue(IsPlaying("dead01_inplace") || IsPlaying("dead01_hold"));
            bossView.ResetView();
            yield return null;
            Assert.IsTrue(IsPlaying("Idle"));
            WolbaekFinalStart reused = Start(1);
            Assert.IsTrue(_view.StartFinal(in reused, _clock));
        }

        private static WolbaekFinalStart Start(int castId)
        {
            return new WolbaekFinalStart(1, castId, 100d, 104d, 8f, 2, 10f,
                4f, 2f, 2f, new Vector3(10f, 0f, -6f));
        }

        private static WolbaekFinalYellow[] Yellows()
        {
            return new[]
            {
                new WolbaekFinalYellow(0, 201, new Vector3(12f, 0f, -3f), 10f),
                new WolbaekFinalYellow(1, 202, new Vector3(7f, 0f, -8f), 10f)
            };
        }

        private static WolbaekFinalBundle Bundle()
        {
            var reds = new[]
            {
                new WolbaekFinalRed(0, 11),
                new WolbaekFinalRed(1, 12)
            };
            return new WolbaekFinalBundle(0, 104d, 108d, 110d, reds);
        }

        private static WolbaekFoxfirePlayer FindRunningAt(Vector3 position)
        {
            foreach (WolbaekFoxfirePlayer player in Object.FindObjectsByType<WolbaekFoxfirePlayer>())
            {
                if (player.IsRunning && (Vector3.Distance(player.transform.position, position) < 0.05f))
                {
                    return player;
                }
            }
            return null;
        }

        private void AssertCleared()
        {
            Assert.IsFalse(_view.IsActive);
            Assert.AreEqual(0, _view.VisibleYellowCount);
            Assert.AreEqual(0, _view.VisibleRedCount);
            Assert.AreEqual(0, CountClones("SB_Shield_Blue"));
            Assert.IsNull(GameObject.Find("WolbaekFinalSafeBoundary"));
            Assert.IsTrue(IsPlaying("Idle"));
        }

        private static int CountClones(string name)
        {
            int count = 0;
            foreach (Transform item in Object.FindObjectsByType<Transform>())
            {
                if (item.name == name + "(Clone)")
                {
                    ++count;
                }
            }
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
