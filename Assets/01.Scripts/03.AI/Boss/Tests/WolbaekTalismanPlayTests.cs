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
    // 정식 프리팹에 서버 DTO를 직접 공급한다. BossState 복제와 다인 접속은 이 시험의 범위 밖이다.
    public class WolbaekTalismanPlayTests
    {
        private const string PREFAB_PATH = "Assets/02.Prefab/Boss/PF_WolbaekBoss.prefab";
        private GameObject _root;
        private WolbaekTalismanView _view;
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
            _root = Object.Instantiate(prefab);
            _view = _root.GetComponent<WolbaekTalismanView>();
            Assert.IsNotNull(_view);
            _clock = 100d;
            _view.SetServerClock(() => _clock);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            yield return null;
        }

        private static WolbaekTalismanStart Start(int castId)
        {
            var placements = new WolbaekTalismanStructure[]
            {
                new WolbaekTalismanStructure(0, EWolbaekTalismanColor.Red, 0, new Vector3(11f, 0f, 1f), 2.2f, 21),
                new WolbaekTalismanStructure(1, EWolbaekTalismanColor.Blue, 1, new Vector3(1f, 0f, 13f), 2.5f, 22),
                new WolbaekTalismanStructure(2, EWolbaekTalismanColor.Yellow, 2, new Vector3(-12f, 0f, 1f), 2.8f, 23),
                new WolbaekTalismanStructure(3, EWolbaekTalismanColor.Green, 3, new Vector3(1f, 0f, -14f), 3.1f, 24)
            };
            return new WolbaekTalismanStart(1, castId, 100d, 103d, 5f, Vector3.zero, placements);
        }

        private IEnumerator At(double time)
        {
            _clock = time;
            _view.Tick(time);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StartHoldResetSuccessClear_UsesServerPlacementsAndProgress()
        {
            WolbaekTalismanStart start = Start(7);
            Assert.IsTrue(_view.StartFormation(in start, _clock));
            yield return null;
            Assert.AreEqual(4, _view.VisibleStructureCount);
            Assert.IsTrue(IsPlaying("defense_inplace"));
            WolbaekTalismanPlayer[] structures = Object.FindObjectsByType<WolbaekTalismanPlayer>(FindObjectsSortMode.None);
            Assert.AreEqual(4, structures.Length);
            foreach (WolbaekTalismanPlayer structure in structures)
            {
                int sign = structure.CurrentSnapshot.Sign == EWolbaekTalismanSign.Flower ? 0
                    : structure.CurrentSnapshot.Sign == EWolbaekTalismanSign.Moon ? 1
                    : (int)structure.CurrentSnapshot.Sign;
                Assert.That(Vector3.Distance(start.Structures[sign].Center, structure.CurrentSnapshot.WorldCenter), Is.LessThan(0.01f));
                Assert.AreEqual(start.Structures[sign].Radius, structure.CurrentSnapshot.Radius);
            }
            _view.HoldStarted(7, 103d, 103d);
            yield return At(105.5d);
            foreach (WolbaekTalismanPlayer structure in structures) Assert.That(structure.DisplayedProgress, Is.InRange(0.45f, 0.55f));
            _view.HoldReset(7, 105.5d, 105.5d);
            yield return At(106d);
            foreach (WolbaekTalismanPlayer structure in structures) Assert.AreEqual(WolbaekTalismanPlayer.EPhase.Reset, structure.CurrentSnapshot.Phase);
            _view.HoldStarted(7, 106d, 106d);
            _view.Succeeded(7, 111d, 117d, 111d);
            yield return At(116.5d);
            Assert.IsTrue(_view.IsActive);
            _view.CounterEnded(7);
            yield return null;
            Assert.IsFalse(_view.IsActive);
            Assert.AreEqual(0, _view.VisibleStructureCount);
        }

        [UnityTest]
        public IEnumerator HardAttack_StrikesOnceAndCancelRemovesTelegraph()
        {
            WolbaekTalismanStart start = Start(8);
            Assert.IsTrue(_view.StartFormation(in start, _clock));
            WolbaekTalismanAttack ring = WolbaekTalismanAttack.Ring(1, 101d, 102.2d, new Vector3(4f, 0f, -3f), 2f, 7f);
            _view.AttackTelegraphed(8, in ring, 101d);
            yield return At(101.2d);
            Assert.IsTrue(IsPlaying("attack02_inplace"));
            yield return At(102.2d);
            _view.AttackStruck(8, 1, 102.2d, 102.2d);
            _view.AttackStruck(8, 1, 102.2d, 102.2d);
            yield return null;
            Assert.AreEqual(1, CountClones("SB_Shockwave_Pulses_Blue"));
            WolbaekTalismanAttack line = WolbaekTalismanAttack.Line(2, 103d, 104.2d,
                new Vector3(2f, 0f, 3f), Vector3.forward, 15f, 2f, 22);
            _view.AttackTelegraphed(8, in line, 103d);
            yield return At(103.7d);
            Assert.IsTrue(IsPlaying("attack09_inplace"));
            _view.AttackCancelled(8, 2);
            yield return null;
            Assert.IsNull(GameObject.Find("WolbaekTalismanAttackRange"));
            _view.Clear();
        }

        [UnityTest]
        public IEnumerator RestoreCounter_DoesNotReplayPastStrikeAndResetAcceptsNewCast()
        {
            WolbaekTalismanStart start = Start(9);
            WolbaekTalismanAttack oldAttack = WolbaekTalismanAttack.Ring(1, 102d, 103.2d,
                Vector3.zero, 2f, 7f);
            var snapshot = new WolbaekTalismanSnapshot(EWolbaekTalismanState.Counter, in start,
                false, 0d, false, in oldAttack, 117d);
            Assert.IsTrue(_view.Restore(in snapshot, 114d));
            yield return null;
            Assert.AreEqual(0, CountClones("SB_Shockwave_Pulses_Blue"));
            _view.Clear();
            WolbaekTalismanStart next = Start(10);
            Assert.IsTrue(_view.StartFormation(in next, 100d));
            yield return null;
            Assert.AreEqual(10, _view.CastId);
            Assert.AreEqual(4, _view.VisibleStructureCount);
        }

        [UnityTest]
        public IEnumerator HardLine_UsesServerShapeAndResetClearsOldCast()
        {
            WolbaekTalismanStart start = Start(11);
            Assert.IsTrue(_view.StartFormation(in start, 100d));
            WolbaekTalismanAttack line = WolbaekTalismanAttack.Line(1, 101d, 102.2d,
                new Vector3(6f, 0f, 9f), Vector3.left, 17f, 3f, 23);
            _view.AttackTelegraphed(11, in line, 101d);
            GameObject range = GameObject.Find("WolbaekTalismanAttackRange");
            Assert.IsNotNull(range);
            Assert.That(Vector3.Distance(line.Origin + Vector3.up * 0.05f, range.transform.position), Is.LessThan(0.01f));
            yield return At(102.2d);
            _view.AttackStruck(11, 1, 102.2d, 102.2d);
            yield return null;
            int beams = 0;
            foreach (WolbaekBeamPlayer player in Object.FindObjectsByType<WolbaekBeamPlayer>(FindObjectsSortMode.None))
            {
                if (player.CastId == 11) ++beams;
            }
            Assert.AreEqual(1, beams);
            _view.ClearCast(11);
            yield return null;
            Assert.IsFalse(_view.StartFormation(in start, 102.2d), "정리된 이전 시전이 다시 시작되면 안 된다");
            _view.ResetNetworkSession();
            Assert.IsTrue(_view.StartFormation(in start, 102.2d), "새 연결의 첫 시전 번호는 다시 쓸 수 있다");
        }

        [UnityTest]
        public IEnumerator RestoreActive_ResumesHoldAndPendingWarningWithoutOldImpact()
        {
            WolbaekTalismanStart start = Start(12);
            WolbaekTalismanAttack pending = WolbaekTalismanAttack.Ring(3, 104d, 105.2d,
                new Vector3(-4f, 0f, 6f), 7f, 10f);
            var snapshot = new WolbaekTalismanSnapshot(EWolbaekTalismanState.Active, in start,
                true, 103d, true, in pending, 111d);
            _clock = 104.5d;
            Assert.IsTrue(_view.Restore(in snapshot, _clock));
            yield return null;
            Assert.AreEqual(4, _view.VisibleStructureCount);
            Assert.IsNotNull(GameObject.Find("WolbaekTalismanAttackRange"));
            Assert.AreEqual(0, CountClones("SB_Shockwave_Pulses_Blue"));
            _view.ClearCast(12);
            yield return null;
            Assert.IsFalse(_view.IsActive);
            Assert.IsNull(GameObject.Find("WolbaekTalismanAttackRange"));
        }

        [UnityTest]
        public IEnumerator DeathThenReset_ClearsAndAcceptsNewBattleCast()
        {
            WolbaekTalismanStart start = Start(13);
            Assert.IsTrue(_view.StartFormation(in start, 100d));
            yield return null;
            BossView boss = _root.GetComponent<BossView>();
            boss.SetDefeated(true);
            yield return null;
            Assert.IsFalse(_view.IsActive);
            Assert.AreEqual(0, _view.VisibleStructureCount);
            boss.ResetView();
            WolbaekTalismanStart next = Start(1);
            Assert.IsTrue(_view.StartFormation(in next, 100d));
            yield return null;
            Assert.AreEqual(4, _view.VisibleStructureCount);
            Assert.IsTrue(IsPlaying("defense_inplace"));
        }

        [UnityTest]
        public IEnumerator FailureAndHaltReceivers_ClearRangesAndRejectOldCast()
        {
            BossNetworkViewBinder binder = _root.AddComponent<BossNetworkViewBinder>();
            typeof(BossNetworkViewBinder).GetField("_talismanView", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(binder, _view);
            WolbaekTalismanStart first = Start(14);
            Assert.IsTrue(_view.StartFormation(in first, 100d));
            WolbaekTalismanAttack attack = WolbaekTalismanAttack.Ring(1, 100.1d, 101.3d, Vector3.zero, 4f, 8f);
            _view.AttackTelegraphed(14, in attack, 100.1d);
            Assert.IsNotNull(GameObject.Find("WolbaekTalismanAttackRange"));
            binder.OnTalismanFailed(14);
            yield return null;
            Assert.IsFalse(_view.IsActive);
            Assert.IsNull(GameObject.Find("WolbaekTalismanAttackRange"));
            Assert.IsFalse(_view.StartFormation(in first, 100d));

            WolbaekTalismanStart second = Start(15);
            Assert.IsTrue(_view.StartFormation(in second, 100d));
            binder.OnTalismanHalted(15);
            yield return null;
            Assert.IsFalse(_view.IsActive);
            Assert.AreEqual(0, _view.VisibleStructureCount);
        }

        private static int CountClones(string name)
        {
            int count = 0;
            foreach (Transform item in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (item.name.StartsWith(name) && item.name.EndsWith("(Clone)")) ++count;
            }
            return count;
        }

        private bool IsPlaying(string state)
        {
            Animator animator = _root.GetComponentInChildren<Animator>();
            return animator.GetCurrentAnimatorStateInfo(0).IsName(state)
                || animator.GetNextAnimatorStateInfo(0).IsName(state);
        }
    }
}
