// System
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 테스트
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class NetworkOverlayViewTests
    {
        // 실제 NetworkOverlayView가 만든 이름표 캔버스와 HUD 순서의 창 캔버스를 한 장면에 두고,
        // 캔버스 정렬 규칙(독립 정렬이면 자기 값, 아니면 부모 값 상속)으로 누가 위에 그려지는지 비교한다

        private const int HUD_ORDER = 10;

        private GameObject  _windowCanvas;
        private GameObject  _overlay;
        private Canvas      _windowCanvasComponent;
        private Canvas      _statsCanvas;
        private Canvas      _nameplateCanvas;
        private TMP_Text    _nameplateText;

        [SetUp]
        public void SetUp()
        {
            _windowCanvas = new GameObject("FixtureWindowCanvas", typeof(RectTransform), typeof(Canvas));
            _windowCanvasComponent = _windowCanvas.GetComponent<Canvas>();
            _windowCanvasComponent.sortingOrder = HUD_ORDER;

            _overlay = new GameObject(nameof(NetworkOverlayView));
            var view = _overlay.AddComponent<NetworkOverlayView>();
            typeof(NetworkOverlayView).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);

            _statsCanvas     = _overlay.GetComponent<Canvas>();
            _nameplateCanvas = _overlay.transform.Find("Nameplates").GetComponent<Canvas>();

            MethodInfo createText = typeof(NetworkOverlayView).GetMethod("CreateText", BindingFlags.Instance | BindingFlags.NonPublic);
            _nameplateText = (TMP_Text)createText.Invoke(view, new object[] { "Nameplate", _nameplateCanvas.transform, 22f, TextAlignmentOptions.Center });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_windowCanvas);
            Object.DestroyImmediate(_overlay);
        }

        [Test]
        public void Nameplate_IsDrawnBelowTopWindowWhileStatsStayOnTop()
        {
            Assert.IsTrue(_nameplateCanvas.overrideSorting, "이름표 캔버스는 부모 정렬을 상속하면 안 된다");
            Assert.Less(EffectiveOrder(_nameplateCanvas), EffectiveOrder(_windowCanvasComponent), "겹치는 상단 창이 이름표를 가려야 한다");
            Assert.Greater(EffectiveOrder(_statsCanvas), EffectiveOrder(_windowCanvasComponent), "FPS와 핑 통계는 창 위에 남아야 한다");
        }

        [Test]
        public void Nameplate_TextBelongsToIndependentCanvasAndParentStaysOverlay()
        {
            Assert.AreSame(_nameplateCanvas, _nameplateText.GetComponentInParent<Canvas>(), "이름표 글자는 독립 이름표 캔버스 안에 있어야 한다");
            Assert.AreSame(_statsCanvas, _nameplateCanvas.transform.parent.GetComponent<Canvas>());
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, _statsCanvas.renderMode);
        }

        [Test]
        public void Nameplate_WithoutIndependentSort_WouldCoverTheWindow()
        {
            // 독립 정렬을 끈 옛 상태를 되살리면 이 시험대가 겹침 회귀를 잡아내는지 확인한다
            _nameplateCanvas.overrideSorting = false;

            Assert.Greater(EffectiveOrder(_nameplateCanvas), EffectiveOrder(_windowCanvasComponent));
        }

        private static int EffectiveOrder(Canvas canvas)
        {
            for (Canvas walk = canvas; walk != null; walk = (walk.transform.parent != null) ? walk.transform.parent.GetComponentInParent<Canvas>() : null)
            {
                if (walk.isRootCanvas || walk.overrideSorting)
                {
                    return walk.sortingOrder;
                }
            }

            return 0;
        }
    }
}
