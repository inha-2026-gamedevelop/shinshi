// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Systems;

namespace Shinshi.Tests
{
    public sealed class HotkeyTests
    {
        /****************************************
        *                Fields
        ****************************************/

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
