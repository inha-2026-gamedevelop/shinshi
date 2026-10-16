// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;
using Shinshi.Systems;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class HotkeyTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string CONFIG_PATH = "Assets/09.Data/UI/HotkeyConfig.asset";

        private HotkeyConfigSO _config = null;
        private GameObject     _host   = null;

        /****************************************
        *                Methods
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<HotkeyConfigSO>();
            _host   = new GameObject("HotkeyTests");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_host);
        }

        [Test]
        public void Validate_DefaultBindings_HasNoErrors()
        {
            Assert.That(CollectErrors(), Is.Empty);
            Assert.That(_config.TryGetKey(EHotkeyAction.Inventory, out KeyCode key), Is.True);
            Assert.That(key, Is.EqualTo(KeyCode.I));
        }

        [Test]
        public void Validate_DuplicateKey_ReportsError()
        {
            SetBinding(1, EHotkeyAction.AuctionHouse, KeyCode.I);

            Assert.That(CollectErrors(), Has.Some.Contains("다른 동작이 이미 쓴다"));
        }

        [Test]
        public void Validate_CombatKey_ReportsError()
        {
            SetBinding(0, EHotkeyAction.Inventory, Constants.Player.KEY_SKILL_Q);

            Assert.That(CollectErrors(), Has.Some.Contains("전투 입력이 쓴다"));
        }

        [Test]
        public void Validate_CancelKeyOnOtherAction_ReportsError()
        {
            SetBinding(2, EHotkeyAction.CloseWindow, KeyCode.F10);
            SetBinding(0, EHotkeyAction.Inventory, Constants.Player.KEY_CANCEL_TARGETING);

            Assert.That(CollectErrors(), Has.Some.Contains("전투 입력이 쓴다"));
        }

        [Test]
        public void Validate_DuplicateAction_ReportsError()
        {
            SetBinding(1, EHotkeyAction.Inventory, KeyCode.Y);

            Assert.That(CollectErrors(), Has.Some.Contains("두 번 있다"));
        }

        [Test]
        public void Trigger_LaterHandlerConsumes_EarlierHandlerNotCalled()
        {
            HotkeyRouter router = _host.AddComponent<HotkeyRouter>();
            int first  = 0;
            int second = 0;
            router.Register(EHotkeyAction.CloseWindow, () => { ++first; return true; });
            router.Register(EHotkeyAction.CloseWindow, () => { ++second; return true; });

            Assert.That(router.Trigger(EHotkeyAction.CloseWindow), Is.True);
            Assert.That(second, Is.EqualTo(1));
            Assert.That(first, Is.EqualTo(0));
        }

        [Test]
        public void Trigger_LaterHandlerPasses_EarlierHandlerCalled()
        {
            HotkeyRouter router = _host.AddComponent<HotkeyRouter>();
            int first = 0;
            router.Register(EHotkeyAction.CloseWindow, () => { ++first; return true; });
            router.Register(EHotkeyAction.CloseWindow, () => false);

            Assert.That(router.Trigger(EHotkeyAction.CloseWindow), Is.True);
            Assert.That(first, Is.EqualTo(1));
        }

        [Test]
        public void Register_SameHandlerAgain_MovesToFront()
        {
            HotkeyRouter router = _host.AddComponent<HotkeyRouter>();
            var calls = new List<string>();
            System.Func<bool> a = () => { calls.Add("a"); return true; };
            System.Func<bool> b = () => { calls.Add("b"); return true; };
            router.Register(EHotkeyAction.CloseWindow, a);
            router.Register(EHotkeyAction.CloseWindow, b);
            router.Register(EHotkeyAction.CloseWindow, a);

            router.Trigger(EHotkeyAction.CloseWindow);

            Assert.That(calls, Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void Trigger_NoHandler_ReturnsFalse()
        {
            HotkeyRouter router = _host.AddComponent<HotkeyRouter>();

            Assert.That(router.Trigger(EHotkeyAction.Inventory), Is.False);
        }

        [Test]
        public void Validate_DefaultConfig_SkillWindowIsKAndCombatMeterIsL()
        {
            Assert.That(_config.TryGetKey(EHotkeyAction.SkillWindow, out KeyCode skill), Is.True);
            Assert.That(skill, Is.EqualTo(KeyCode.K));
            Assert.That(_config.TryGetKey(EHotkeyAction.CombatMeter, out KeyCode meter), Is.True);
            Assert.That(meter, Is.EqualTo(KeyCode.L));
        }

        [Test]
        public void Validate_ProjectAsset_HasNoErrorsAndSkillWindowIsK()
        {
            var asset  = AssetDatabase.LoadAssetAtPath<HotkeyConfigSO>(CONFIG_PATH);
            var errors = new List<string>();
            asset.Validate(errors);

            Assert.That(errors, Is.Empty, string.Join("\n", errors));
            Assert.That(asset.TryGetKey(EHotkeyAction.SkillWindow, out KeyCode skill), Is.True);
            Assert.That(skill, Is.EqualTo(KeyCode.K));
            Assert.That(asset.TryGetKey(EHotkeyAction.CombatMeter, out KeyCode meter), Is.True);
            Assert.That(meter, Is.EqualTo(KeyCode.L));
        }

        [Test]
        public void CloseGuard_UnsavedChanges_EscapeToggleAndMenuKeepWindowOpen()
        {
            HotkeyWindowToggle toggle = CreateGuardedToggle(out GameObject window, out SkillWindowBinder binder,
                out HotkeyRouter router);
            MakeUnsaved(binder);
            toggle.SetOpen(true);

            Assert.That(router.Trigger(EHotkeyAction.CloseWindow), Is.True);
            Assert.That(window.activeSelf, Is.True);
            Assert.That(PendingConfirm(binder), Is.Not.Null);

            // 확인이 떠 있을 때 다시 닫기를 누르면 확인만 접는다
            toggle.Toggle();
            Assert.That(PendingConfirm(binder), Is.Null);
            toggle.SetOpen(false);
            Assert.That(PendingConfirm(binder), Is.Not.Null);
            Assert.That(window.activeSelf, Is.True);
        }

        [Test]
        public void CloseGuard_ConfirmAccepted_ClosesWindowAndDropsDraft()
        {
            HotkeyWindowToggle toggle = CreateGuardedToggle(out GameObject window, out SkillWindowBinder binder,
                out HotkeyRouter router);
            MakeUnsaved(binder);
            toggle.SetOpen(true);
            router.Trigger(EHotkeyAction.CloseWindow);

            typeof(SkillWindowBinder).GetMethod("OnCloseConfirmed", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(binder, null);

            Assert.That(window.activeSelf, Is.False);
            Assert.That(binder.HasUnsavedChanges, Is.False);
            Assert.That(router.Trigger(EHotkeyAction.CloseWindow), Is.False);
        }

        [Test]
        public void CloseGuard_NoUnsavedChanges_ClosesImmediately()
        {
            HotkeyWindowToggle toggle = CreateGuardedToggle(out GameObject window, out SkillWindowBinder binder, out _);
            toggle.SetOpen(true);

            toggle.Toggle();

            Assert.That(window.activeSelf, Is.False);
            Assert.That(PendingConfirm(binder), Is.Null);
        }

        // 에디트 모드에서는 Awake가 돌지 않으므로 토글이 처음 쓰일 때 가드를 읽는다
        private HotkeyWindowToggle CreateGuardedToggle(out GameObject window, out SkillWindowBinder binder,
            out HotkeyRouter router)
        {
            router = _host.AddComponent<HotkeyRouter>();
            window = new GameObject("GuardedWindow");
            window.transform.SetParent(_host.transform);
            window.SetActive(false);
            SkillWindowView view = window.AddComponent<SkillWindowView>();
            binder = _host.AddComponent<SkillWindowBinder>();
            var binderData = new SerializedObject(binder);
            binderData.FindProperty("_view").objectReferenceValue = view;
            binderData.ApplyModifiedPropertiesWithoutUndo();

            HotkeyWindowToggle toggle = _host.AddComponent<HotkeyWindowToggle>();
            var serialized = new SerializedObject(toggle);
            serialized.FindProperty("_router").objectReferenceValue     = router;
            serialized.FindProperty("_window").objectReferenceValue     = window;
            serialized.FindProperty("_closeGuard").objectReferenceValue = binder;
            serialized.FindProperty("_action").intValue                 = (int)EHotkeyAction.SkillWindow;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return toggle;
        }

        // 서버 조합이 비어 있는 상태에서 임시 조합에 한 칸을 넣어 저장하지 않은 변경을 만든다
        private static void MakeUnsaved(SkillWindowBinder binder)
        {
            const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.NonPublic;
            var draft = (List<TownLoadoutEntry>)typeof(SkillWindowBinder).GetField("_draft", FLAGS).GetValue(binder);
            draft.Add(new TownLoadoutEntry("skill_swordsman_slash", 0, "T13-1A"));
            typeof(SkillWindowBinder).GetField("_draftChanged", FLAGS).SetValue(binder, true);
            Assert.That(binder.HasUnsavedChanges, Is.True);
        }

        private static object PendingConfirm(SkillWindowBinder binder)
        {
            return typeof(SkillWindowBinder).GetField("_confirmClose", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(binder);
        }

        private List<string> CollectErrors()
        {
            var errors = new List<string>();
            _config.Validate(errors);
            return errors;
        }

        private void SetBinding(int index, EHotkeyAction action, KeyCode key)
        {
            // 인스펙터 즉시 검사(OnValidate)가 틀린 값을 오류 로그로 남긴다. 검사 결과는 Validate로 따로 확인한다
            LogAssert.ignoreFailingMessages = true;

            var serialized = new SerializedObject(_config);
            SerializedProperty binding = serialized.FindProperty("_bindings").GetArrayElementAtIndex(index);
            binding.FindPropertyRelative("_action").intValue = (int)action;
            binding.FindPropertyRelative("_key").intValue    = (int)key;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
