// System
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.UI;

// Third Party
using NUnit.Framework;
using TMPro;

// Project
using Shinshi.Data;
using Shinshi.Contracts;
using Shinshi.Network;
using Shinshi.UI;
using Shinshi.Combat;
using Shinshi.Common;
using UnityEngine.SceneManagement;

namespace Shinshi.Tests
{
    public sealed class HudExperienceTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private CharacterLevelConfigSO _config;
        private CharacterLevelSettings _settings;
        private GameObject _root;
        private HudPlayerStatusView _status;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<CharacterLevelConfigSO>();
            _settings = _config.ToSettings();
            _root = new GameObject("HudExperienceTest", typeof(RectTransform));
            _status = _root.AddComponent<HudPlayerStatusView>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_config);
        }

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(0, 1, 0, 300)]
        [TestCase(2850, 5, 0, 1400)]
        [TestCase(2855, 5, 5, 1400)]
        [TestCase(4250, 6, 0, 1675)]
        public void Read_LevelBoundaries_ReturnsStageProgress(long total, int level, long current, long required)
        {
            Assert.IsTrue(HudExperienceRules.TryRead(_settings, total, out HudExperienceProgress result));
            Assert.AreEqual(level, result.Level);
            Assert.AreEqual(current, result.Current);
            Assert.AreEqual(required, result.Required);
        }

        [Test]
        public void Read_MaximumAndInvalid_NoNextLevelOrAssumedLevel()
        {
            Assert.IsTrue(HudExperienceRules.TryRead(_settings, long.MaxValue, out HudExperienceProgress result));
            Assert.AreEqual(30, result.Level);
            Assert.IsTrue(result.IsMaximum);
            Assert.AreEqual(1, result.Ratio);
            Assert.IsFalse(HudExperienceRules.TryRead(_settings, -1, out _));
            Assert.IsFalse(HudExperienceRules.TryRead(null, 0, out _));
        }

        [Test]
        public void Show_InitialDuplicateAndLevelRise_ReusesChildrenAndNotifiesOnlyRise()
        {
            _status.SetExperience(_settings, 2850);
            HudExperienceView view = _root.GetComponentInChildren<HudExperienceView>();
            Assert.IsFalse(view.NoticeVisible);
            Assert.AreEqual("EXP 0 / 1,400", view.DisplayText);
            int count = _root.GetComponentsInChildren<RectTransform>(true).Length;
            _status.SetExperience(_settings, 2850);
            Assert.AreEqual(count, _root.GetComponentsInChildren<RectTransform>(true).Length);
            _status.SetExperience(_settings, 4250);
            Assert.IsTrue(view.NoticeVisible);
            Assert.IsNull(typeof(HudExperienceView).GetField("_noticeTween", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(view), "EditMode preview must not allocate a running tween");
            _status.SetExperience(_settings, 120350);
            Assert.AreEqual("EXP MAX", view.DisplayText);
            foreach (Graphic graphic in _root.GetComponentsInChildren<Graphic>(true))
            {
                Assert.IsFalse(graphic.raycastTarget);
            }
        }

        [Test]
        public void SetExperience_InitiallyInactive_DoesNotCreateChildrenOrSeedNotification()
        {
            _root.SetActive(false);
            _status.SetExperience(_settings, 2850);
            _status.SetExperience(_settings, 4250);
            Assert.IsNull(_root.GetComponentInChildren<HudExperienceView>(true));
            _root.SetActive(true);
            _status.SetExperience(_settings, 5925);
            HudExperienceView view = _root.GetComponentInChildren<HudExperienceView>();
            Assert.IsNotNull(view);
            Assert.IsFalse(view.NoticeVisible);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ResetAndDisable_NewInitialValue_DoesNotNotify(bool disableExperienceChild)
        {
            _status.SetExperience(_settings, 2850);
            _status.SetExperience(_settings, 4250);
            HudExperienceView view = _root.GetComponentInChildren<HudExperienceView>();
            _status.ResetExperience();
            _status.SetExperience(_settings, 5925);
            Assert.IsFalse(view.NoticeVisible);
            // 일반 MonoBehaviour의 EditMode 시험에서는 SetActive가 런타임 메시지를 보장하지 않는다.
            // 부모 HUD와 XP 자식의 실제 비활성화 처리기를 각각 호출해 초기화 계약을 검사한다.
            object target = disableExperienceChild ? (object)view : _status;
            target.GetType().GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(target, null);
            _status.SetExperience(_settings, 120350);
            Assert.IsFalse(view.NoticeVisible);
        }

        [Test]
        public void CharacterSlot_LevelAndEmpty_UpdatesEveryLabel()
        {
            var labelObject = new GameObject("Level", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(_root.transform);
            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            var slot = new CharacterSelectSlot();
            typeof(CharacterSelectSlot).GetField("_levelTexts", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(slot, new[] { label });
            slot.Show("test", "warrior", 5);
            Assert.AreEqual("Lv.5", label.text);
            slot.Show(null, null);
            Assert.IsEmpty(label.text);
        }

        [TestCase("OnSceneLoaded")]
        [TestCase("OnDisable")]
        [TestCase("OnSnapshot")]
        public void Binder_LifecycleBoundary_ClearsPreviousNotice(string boundary)
        {
            var player = new GameObject("HudBinderTest", typeof(Mirror.NetworkIdentity));
            try
            {
                LocalPlayerHudBinder binder = player.AddComponent<LocalPlayerHudBinder>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(LocalPlayerHudBinder).GetField("_statusView", flags).SetValue(binder, _status);
                typeof(LocalPlayerHudBinder).GetField("_isBound", flags).SetValue(binder, true);
                _status.SetExperience(_settings, 2850);
                _status.SetExperience(_settings, 4250);
                HudExperienceView view = _root.GetComponentInChildren<HudExperienceView>();
                Assert.IsTrue(view.NoticeVisible);
                object[] arguments = boundary == "OnSceneLoaded"
                    ? new object[] { default(Scene), LoadSceneMode.Single }
                    : boundary == "OnSnapshot"
                        ? new object[] { new LocalPlayerSnapshot(7, EPlayerClass.Warrior, EParticipantLifeState.Alive, 0) }
                        : null;
                typeof(LocalPlayerHudBinder).GetMethod(boundary, flags).Invoke(binder, arguments);
                Assert.IsFalse(view.NoticeVisible);
                _status.SetExperience(_settings, 120350);
                Assert.IsFalse(view.NoticeVisible);
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void Binder_NoSceneHud_OwnsOneFallbackAndReleasesOnUnbind()
        {
            var player = new GameObject("FallbackBinderTest", typeof(Mirror.NetworkIdentity));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                Assert.IsNotNull(Resources.Load<GameObject>("PF_PlayerStatusHud"));
                LocalPlayerHudBinder binder = player.AddComponent<LocalPlayerHudBinder>();
                MethodInfo resolve = typeof(LocalPlayerHudBinder).GetMethod("ResolveStatusHud", flags);
                FieldInfo owned = typeof(LocalPlayerHudBinder).GetField("_ownedStatusHud", flags);
                resolve.Invoke(binder, new object[] { new HudPlayerStatusView[0] });
                var first = (GameObject)owned.GetValue(binder);
                Assert.IsNotNull(first);
                Assert.IsNull(first.transform.parent);
                resolve.Invoke(binder, new object[] { first.GetComponentsInChildren<HudPlayerStatusView>(true) });
                Assert.AreSame(first, owned.GetValue(binder));
                typeof(LocalPlayerHudBinder).GetMethod("Unbind", flags).Invoke(binder, null);
                Assert.IsTrue(first == null);
                Assert.IsNull(owned.GetValue(binder));
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void Binder_SceneHudAppears_ReleasesFallbackAndUsesSceneView()
        {
            var player = new GameObject("FallbackReplacementTest", typeof(Mirror.NetworkIdentity));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                LocalPlayerHudBinder binder = player.AddComponent<LocalPlayerHudBinder>();
                MethodInfo resolve = typeof(LocalPlayerHudBinder).GetMethod("ResolveStatusHud", flags);
                FieldInfo owned = typeof(LocalPlayerHudBinder).GetField("_ownedStatusHud", flags);
                resolve.Invoke(binder, new object[] { new HudPlayerStatusView[0] });
                var fallback = (GameObject)owned.GetValue(binder);
                Assert.IsNotNull(fallback);
                resolve.Invoke(binder, new object[] { new[] { fallback.GetComponentInChildren<HudPlayerStatusView>(), _status } });
                Assert.IsTrue(fallback == null);
                Assert.IsNull(owned.GetValue(binder));
                Assert.AreSame(_status, typeof(LocalPlayerHudBinder).GetField("_statusView", flags).GetValue(binder));
                typeof(LocalPlayerHudBinder).GetMethod("Unbind", flags).Invoke(binder, null);
                Assert.IsTrue(_status != null);
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        [TestCase(LoadSceneMode.Single)]
        [TestCase(LoadSceneMode.Additive)]
        public void Binder_SceneLoad_ReplacesOnlyForSingleMode(LoadSceneMode mode)
        {
            var player = new GameObject("SceneFallbackTest", typeof(Mirror.NetworkIdentity));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            LocalPlayerHudBinder binder = player.AddComponent<LocalPlayerHudBinder>();
            try
            {
                _status.enabled = false;
                typeof(LocalPlayerHudBinder).GetMethod("ResolveStatusHud", flags)
                    .Invoke(binder, new object[] { new HudPlayerStatusView[0] });
                FieldInfo owned = typeof(LocalPlayerHudBinder).GetField("_ownedStatusHud", flags);
                var before = (GameObject)owned.GetValue(binder);
                Assert.IsNotNull(before);
                typeof(LocalPlayerHudBinder).GetMethod("OnSceneLoaded", flags)
                    .Invoke(binder, new object[] { default(Scene), mode });
                var after = (GameObject)owned.GetValue(binder);
                Assert.IsNotNull(after);
                if (mode == LoadSceneMode.Single)
                {
                    Assert.IsTrue(before == null);
                    Assert.AreNotSame(before, after);
                }
                else
                {
                    Assert.AreSame(before, after);
                }
            }
            finally
            {
                typeof(LocalPlayerHudBinder).GetMethod("Unbind", flags).Invoke(binder, null);
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void Binder_NewOwnerBeforeOldUnbind_KeepsIndependentFallback()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var oldPlayer = new GameObject("OldBinder", typeof(Mirror.NetworkIdentity));
            var newPlayer = new GameObject("NewBinder", typeof(Mirror.NetworkIdentity));
            LocalPlayerHudBinder oldBinder = oldPlayer.AddComponent<LocalPlayerHudBinder>();
            LocalPlayerHudBinder newBinder = newPlayer.AddComponent<LocalPlayerHudBinder>();
            MethodInfo resolve = typeof(LocalPlayerHudBinder).GetMethod("ResolveStatusHud", flags);
            MethodInfo unbind = typeof(LocalPlayerHudBinder).GetMethod("Unbind", flags);
            FieldInfo owned = typeof(LocalPlayerHudBinder).GetField("_ownedStatusHud", flags);
            try
            {
                resolve.Invoke(oldBinder, new object[] { new HudPlayerStatusView[0] });
                var oldHud = (GameObject)owned.GetValue(oldBinder);
                HudPlayerStatusView oldView = oldHud.GetComponentInChildren<HudPlayerStatusView>();
                Assert.IsTrue(oldView.IsRuntimeOwned);
                resolve.Invoke(newBinder, new object[] { new[] { oldView } });
                var newHud = (GameObject)owned.GetValue(newBinder);
                Assert.IsNotNull(newHud);
                Assert.AreNotSame(oldHud, newHud);
                unbind.Invoke(oldBinder, null);
                Assert.IsTrue(oldHud == null);
                Assert.IsTrue(newHud != null);
                Assert.AreSame(newHud, owned.GetValue(newBinder));
            }
            finally
            {
                unbind.Invoke(oldBinder, null);
                unbind.Invoke(newBinder, null);
                Object.DestroyImmediate(oldPlayer);
                Object.DestroyImmediate(newPlayer);
            }
        }
    }
}
