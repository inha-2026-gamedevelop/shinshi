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
    // BossState/Mirror 복제 전의 정식 프리팹 주입 시험이다.
    public class WolbaekColorWardPlayTests
    {
        private const string PREFAB_PATH = "Assets/02.Prefab/Boss/PF_WolbaekBoss.prefab";
        private GameObject _boss;
        private WolbaekColorWardView _view;
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
            _view = _boss.GetComponent<WolbaekColorWardView>();
            Assert.IsNotNull(_view);
            _binder = _boss.AddComponent<BossNetworkViewBinder>();
            typeof(BossNetworkViewBinder).GetField("_colorWardView", BindingFlags.NonPublic | BindingFlags.Instance)
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

        private static WolbaekWardStart Start(int castId)
        {
            var members = new[]
            {
                new WolbaekWardMember(11, EWolbaekWardColor.Red, 0),
                new WolbaekWardMember(12, EWolbaekWardColor.Red, 0),
                new WolbaekWardMember(13, EWolbaekWardColor.Blue, 1),
                new WolbaekWardMember(14, EWolbaekWardColor.Blue, 1)
            };
            return new WolbaekWardStart(1, castId, 100d, new Vector3(8f, 0f, -4f),
                5f, 8f, 3f, 3f, 4f, members);
        }

        private static WolbaekWardStage Stage()
        {
            return new WolbaekWardStage(2, 110d, 117d, 1.5f, 3,
                new Vector3(-11f, 0f, 5f), new Vector3(13f, 0f, 8f), 3.7f);
        }

        [UnityTest]
        public IEnumerator StartSuccessAndPhaseTransition_ControlGroggyFromAuthoritativeEvents()
        {
            WolbaekWardStart start = Start(30);
            _binder.OnWardStarted(in start);
            yield return null;
            Assert.IsTrue(IsPlaying("down_groggy02_loop_inplace"));

            _binder.OnWardSucceeded(30);
            yield return null;
            Assert.IsFalse(_view.IsActive);
            Assert.IsTrue(_view.IsAwaitingPhaseTransition);
            Assert.IsTrue(IsPlaying("down_groggy02_loop_inplace"), "성공 사건만으로 Idle에 복귀하면 안 된다");

            _view.PhaseTransitionStarted();
            yield return null;
            Assert.IsFalse(_view.IsAwaitingPhaseTransition);
            Assert.IsTrue(IsPlaying("Idle"));
        }

        [UnityTest]
        public IEnumerator GatherHoldAndStage_UseServerCenterAndRadius()
        {
            WolbaekWardStart start = Start(31);
            Assert.IsTrue(_view.StartWard(in start, _clock));
            Assert.IsTrue(_view.IsGathering);
            _view.HoldStarted(31, 101d);
            _clock = 105d;
            yield return null;
            Assert.That(_view.HoldProgress, Is.InRange(0.49f, 0.51f));
            _view.HoldReset(31);
            Assert.AreEqual(0f, _view.HoldProgress);
            _view.GatherCompleted(31);
            WolbaekWardStage stage = Stage();
            _view.StageStarted(31, in stage, 110d);
            yield return null;
            Assert.IsFalse(_view.IsGathering);
            Assert.AreEqual(stage.Index, _view.CurrentStage.Index);
            WolbaekWardPlayer[] wards = Object.FindObjectsByType<WolbaekWardPlayer>();
            Assert.AreEqual(2, wards.Length);
            foreach (WolbaekWardPlayer ward in wards)
            {
                Vector3 wanted = ward.CurrentSnapshot.Sign == EWolbaekWardSign.Diamond
                    ? stage.RedCenter : stage.BlueCenter;
                Assert.That(Vector3.Distance(wanted, ward.CurrentSnapshot.WorldCenter), Is.LessThan(0.01f));
                Assert.AreEqual(stage.Radius, ward.CurrentSnapshot.Radius);
            }
        }

        [UnityTest]
        public IEnumerator StrikeAndRestore_DoNotReplayPastFlash()
        {
            WolbaekWardStart start = Start(32);
            WolbaekWardStage stage = Stage();
            _view.StartWard(in start, 100d);
            _view.StageStarted(32, in stage, 110d);
            var strike = new WolbaekWardStrike(2, 0, 115.5d, 117d, new Vector3(2f, 0f, 3f), 0f);
            _view.StrikeTelegraphed(32, in strike, 116d);
            Assert.IsTrue(_view.HasTelegraph);
            _view.StrikeResolved(32, 2, 0, 117d, 117d);
            _view.StrikeResolved(32, 2, 0, 117d, 117d);
            yield return null;
            Assert.IsFalse(_view.HasTelegraph);
            Assert.AreEqual(1, _view.NextStrikeIndex);
            Assert.AreEqual(1, CountFlash());
            var grace = new double[] { 0d, 120d, 0d, 0d };
            var snapshot = new WolbaekWardSnapshot(EWolbaekWardState.Strike, in start,
                false, 0d, in stage, 1, grace);
            _clock = 118d;
            _view.Restore(in snapshot, _clock);
            _view.StrikeResolved(32, 2, 0, 117d, _clock);
            yield return null;
            Assert.AreEqual(1, _view.NextStrikeIndex);
            Assert.AreEqual(0, CountFlash());
        }

        [UnityTest]
        public IEnumerator LinksNeedAnchorsAndClearRemovesAll()
        {
            WolbaekWardStart start = Start(33);
            WolbaekWardStage stage = Stage();
            _view.StartWard(in start, 100d);
            _view.StageStarted(33, in stage, 110d);
            var a = new GameObject("ward-member-a");
            var b = new GameObject("ward-member-b");
            a.transform.position = Vector3.zero;
            b.transform.position = Vector3.right * 3.5f;
            _view.SetMemberAnchor(11, a.transform);
            _view.SetMemberAnchor(12, b.transform);
            _clock = 111d;
            yield return null;
            WolbaekLinkPlayer[] links = Object.FindObjectsByType<WolbaekLinkPlayer>();
            WolbaekLinkPlayer red = null;
            foreach (WolbaekLinkPlayer link in links)
                if (link.IsRunning) red = link;
            Assert.IsNotNull(red);
            Assert.AreEqual(WolbaekLinkPlayer.ESeverity.Warning, red.CurrentSnapshot.Severity);
            b.transform.position = Vector3.right * 4.5f;
            yield return null;
            Assert.AreEqual(WolbaekLinkPlayer.ESeverity.Danger, red.CurrentSnapshot.Severity);
            _view.SetMemberAnchor(12, null);
            yield return null;
            Assert.IsFalse(red.IsRunning);
            _view.ClearCast(33);
            yield return null;
            Assert.IsFalse(_view.IsActive);
            Assert.AreEqual(0, Object.FindObjectsByType<WolbaekWardPlayer>().Length);
            Object.Destroy(a);
            Object.Destroy(b);
        }

        [UnityTest]
        public IEnumerator GatherRestoreAndTerminalStates_DoNotReviveOldCast()
        {
            WolbaekWardStart start = Start(34);
            WolbaekWardStage stage = Stage();
            var grace = new double[] { 0d, 0d, 0d, 0d };
            var gather = new WolbaekWardSnapshot(EWolbaekWardState.Gather, in start,
                true, 102d, in stage, 0, grace);
            _clock = 106d;
            _view.Restore(in gather, _clock);
            yield return null;
            Assert.IsTrue(_view.IsGathering);
            Assert.IsTrue(_view.IsHolding);
            Assert.IsTrue(IsPlaying("down_groggy02_loop_inplace"));
            Assert.That(_view.HoldProgress, Is.InRange(0.49f, 0.51f));
            Assert.AreEqual(0, Object.FindObjectsByType<WolbaekWardPlayer>().Length);

            var ended = new WolbaekWardSnapshot(EWolbaekWardState.Completed, in start,
                false, 0d, in stage, 3, grace);
            _view.Restore(in ended, 140d);
            yield return null;
            Assert.IsFalse(_view.IsActive);
            Assert.IsFalse(_view.IsAwaitingPhaseTransition);
            Assert.IsTrue(IsPlaying("Idle"));
            Assert.IsFalse(_view.StartWard(in start, 140d));
            WolbaekWardStart next = Start(35);
            Assert.IsTrue(_view.StartWard(in next, 141d));
            _view.ClearCast(35);
            yield return null;
            Assert.IsFalse(_view.IsActive);
            _view.ResetNetworkSession();
            Assert.IsTrue(_view.StartWard(in start, 142d));
        }

        [UnityTest]
        public IEnumerator LinkTargetReplacement_UsesCurrentInjectedAnchor()
        {
            WolbaekWardStart start = Start(36);
            WolbaekWardStage stage = Stage();
            _view.StartWard(in start, 100d);
            _view.StageStarted(36, in stage, 110d);
            var first = new GameObject("ward-first");
            var oldSecond = new GameObject("ward-old-second");
            var newSecond = new GameObject("ward-new-second");
            first.transform.position = Vector3.zero;
            oldSecond.transform.position = Vector3.right * 3.5f;
            newSecond.transform.position = Vector3.right * 4.5f;
            _view.SetMemberAnchor(11, first.transform);
            _view.SetMemberAnchor(12, oldSecond.transform);
            _clock = 111d;
            yield return null;
            WolbaekLinkPlayer red = null;
            foreach (WolbaekLinkPlayer link in Object.FindObjectsByType<WolbaekLinkPlayer>())
                if (link.IsRunning) red = link;
            Assert.IsNotNull(red);
            Assert.AreEqual(WolbaekLinkPlayer.ESeverity.Warning, red.CurrentSnapshot.Severity);
            _view.SetMemberAnchor(12, newSecond.transform);
            yield return null;
            Assert.AreEqual(WolbaekLinkPlayer.ESeverity.Danger, red.CurrentSnapshot.Severity);
            _view.ClearCast(36);
            Object.Destroy(first);
            Object.Destroy(oldSecond);
            Object.Destroy(newSecond);
        }

        [UnityTest]
        public IEnumerator BossDeathAndReset_ClearWardGeneration()
        {
            WolbaekWardStart start = Start(37);
            Assert.IsTrue(_view.StartWard(in start, 100d));
            yield return null;
            Assert.IsTrue(IsPlaying("down_groggy02_loop_inplace"));
            BossView bossView = _boss.GetComponent<BossView>();
            Assert.IsNotNull(bossView);
            bossView.SetDefeated(true);
            yield return null;
            Assert.IsFalse(_view.IsActive);
            Assert.IsFalse(IsPlaying("down_groggy02_loop_inplace"));
            Assert.IsTrue(IsPlaying("dead01_inplace") || IsPlaying("dead01_hold"));
            Assert.IsFalse(_view.StartWard(in start, 101d));
            bossView.ResetView();
            yield return null;
            Assert.IsTrue(IsPlaying("Idle"));
            Assert.IsTrue(_view.StartWard(in start, 102d));
        }

        [UnityTest]
        public IEnumerator FailureHaltAndClear_StopGroggyAndRejectEndedCasts()
        {
            WolbaekWardStart failed = Start(38);
            Assert.IsTrue(_view.StartWard(in failed, 100d));
            _binder.OnWardFailed(38);
            yield return null;
            Assert.IsFalse(_view.IsActive);
            Assert.IsTrue(IsPlaying("Idle"));
            Assert.IsFalse(_view.StartWard(in failed, 101d));

            WolbaekWardStart halted = Start(39);
            Assert.IsTrue(_view.StartWard(in halted, 101d));
            _binder.OnWardHalted(39);
            yield return null;
            Assert.IsFalse(_view.IsActive);
            Assert.IsTrue(IsPlaying("Idle"));

            WolbaekWardStart cleared = Start(40);
            Assert.IsTrue(_view.StartWard(in cleared, 102d));
            _binder.OnWardCleared(40);
            yield return null;
            Assert.IsFalse(_view.IsActive);
            Assert.IsTrue(IsPlaying("Idle"));

            WolbaekWardStart unbound = Start(41);
            Assert.IsTrue(_view.StartWard(in unbound, 103d));
            _binder.Unbind();
            yield return null;
            Assert.IsFalse(_view.IsActive);
            Assert.IsTrue(IsPlaying("Idle"));
        }

        private static int CountFlash()
        {
            int count = 0;
            foreach (ParticleSystem particle in Object.FindObjectsByType<ParticleSystem>())
                if (particle.name == "SB_Shockwave_Flash_Blue(Clone)") ++count;
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
