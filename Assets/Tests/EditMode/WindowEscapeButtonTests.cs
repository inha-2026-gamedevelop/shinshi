// System
using System.Reflection;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Systems;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class WindowEscapeButtonTests
    {
        // 편집 모드에서는 OnEnable, OnDisable이 저절로 불리지 않아 반사로 부른다

        /****************************************
        *                Fields
        ****************************************/

        private GameObject         _host   = null;
        private HotkeyRouter       _router = null;
        private Button             _button = null;
        private WindowEscapeButton _escape = null;
        private int                _clicks = 0;

        /****************************************
        *                Methods
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _host   = new GameObject("WindowEscapeButtonTests");
            _router = _host.AddComponent<HotkeyRouter>();

            var window = new GameObject("Window");
            window.transform.SetParent(_host.transform, false);
            _button = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            _button.transform.SetParent(window.transform, false);
            _button.onClick.AddListener(() => ++_clicks);

            _escape = window.AddComponent<WindowEscapeButton>();
            var data = new SerializedObject(_escape);
            data.FindProperty("_router").objectReferenceValue      = _router;
            data.FindProperty("_closeButton").objectReferenceValue = _button;
            data.ApplyModifiedPropertiesWithoutUndo();
            _clicks = 0;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
        }

        [Test]
        public void Escape_WhileEnabled_ClicksCloseButton()
        {
            Call("OnEnable");

            Assert.That(_router.Trigger(EHotkeyAction.CloseWindow), Is.True);
            Assert.That(_clicks, Is.EqualTo(1));
        }

        [Test]
        public void Escape_ButtonNotInteractable_ConsumesWithoutClicking()
        {
            Call("OnEnable");
            _button.interactable = false;

            Assert.That(_router.Trigger(EHotkeyAction.CloseWindow), Is.True, "뒤의 창으로 넘기지 않는다");
            Assert.That(_clicks, Is.Zero);
        }

        [Test]
        public void Escape_AfterDisable_IsNotHandled()
        {
            Call("OnEnable");
            Call("OnDisable");

            Assert.That(_router.Trigger(EHotkeyAction.CloseWindow), Is.False);
            Assert.That(_clicks, Is.Zero);
        }

        private void Call(string method)
        {
            typeof(WindowEscapeButton).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_escape, null);
        }
    }
}
