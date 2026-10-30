// System
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class MenuGuidePlayTests
    {
        private const string MENU_ID   = "menu_test";
        private const string OTHER_ID  = "menu_other";
        private const string TARGET    = "target_button";

        private MenuGuideView       _view;
        private MenuGuideCatalogSO  _catalog;
        private MemoryMenuGuideRecord _record;
        private MenuGuideController _controller;
        private GameObject          _canvasObject;
        private RectTransform       _targetRect;
        private int                 _finishedCount;
        private bool                _lastSkipped;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _finishedCount = 0;
            _view = MenuGuideView.Create();
            _view.Finished += skipped =>
            {
                ++_finishedCount;
                _lastSkipped = skipped;
            };

            _catalog = ScriptableObject.CreateInstance<MenuGuideCatalogSO>();
            _catalog.Fill(new[]
            {
                new MenuGuideCatalogSO.MenuEntry(MENU_ID, new[]
                {
                    new MenuGuideCatalogSO.StepEntry("첫 단계", TARGET),
                    new MenuGuideCatalogSO.StepEntry("둘째 단계", string.Empty),
                    new MenuGuideCatalogSO.StepEntry("셋째 단계", TARGET)
                }),
                new MenuGuideCatalogSO.MenuEntry(OTHER_ID, new[] { new MenuGuideCatalogSO.StepEntry("한 단계", string.Empty) })
            });

            _record     = new MemoryMenuGuideRecord();
            _controller = new MenuGuideController(_catalog, _record, _view);

            // 강조 대상이 될 가짜 메뉴 버튼
            _canvasObject = new GameObject("TestMenuCanvas", typeof(Canvas), typeof(CanvasScaler));
            _canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var target = new GameObject("Target", typeof(RectTransform));
            target.transform.SetParent(_canvasObject.transform, false);
            _targetRect = target.GetComponent<RectTransform>();
            _targetRect.anchorMin        = new Vector2(0.5f, 0.5f);
            _targetRect.anchorMax        = new Vector2(0.5f, 0.5f);
            _targetRect.sizeDelta        = new Vector2(200f, 80f);
            _targetRect.anchoredPosition = Vector2.zero;

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _controller.Dispose();
            Object.Destroy(_view.gameObject);
            Object.Destroy(_canvasObject);
            Object.DestroyImmediate(_catalog);
            yield return null;
        }

        private RectTransform Resolve(string key)
        {
            return (key == TARGET) ? _targetRect : null;
        }

        [UnityTest]
        public IEnumerator Controller_FirstTime_ShowsAndRecordsAfterLastStep()
        {
            Assert.IsTrue(_controller.TryShowFirstTime(MENU_ID, Resolve));
            yield return null;

            Assert.IsTrue(_view.IsShowing);
            Assert.IsFalse(_record.IsCompleted(MENU_ID));

            _view.HandleNext();
            _view.HandleNext();
            Assert.IsFalse(_record.IsCompleted(MENU_ID));

            _view.HandleNext();

            Assert.IsTrue(_record.IsCompleted(MENU_ID));
            Assert.AreEqual(1, _finishedCount);
            Assert.IsFalse(_lastSkipped);
            Assert.IsFalse(_view.IsShowing);
        }

        private string TextAt(string path)
        {
            Transform target = _view.transform.Find(path);
            Assert.IsNotNull(target, path);
            return target.GetComponent<TMPro.TMP_Text>().text;
        }

        [UnityTest]
        public IEnumerator Buttons_FollowTheDraftWording()
        {
            _controller.TryShowFirstTime(MENU_ID, Resolve);
            yield return null;

            Assert.AreEqual("안내 건너뛰기", TextAt("Panel/SkipButton/Label"));
            Assert.AreEqual("다음", TextAt("Panel/NextButton/Label"));

            _view.HandleNext();
            _view.HandleNext();

            // 마지막 단계의 단추는 "다음"이 아니라 "닫기"다
            Assert.AreEqual("닫기", TextAt("Panel/NextButton/Label"));
        }

        [UnityTest]
        public IEnumerator Finish_HidesAfterOneFrameAndShowAgainRightAwayStaysVisible()
        {
            _controller.TryShowFirstTime(OTHER_ID, Resolve);
            yield return null;

            _view.HandleNext();
            Assert.IsTrue(_view.gameObject.activeSelf);

            _controller.Replay(OTHER_ID, Resolve);
            yield return null;
            yield return null;

            Assert.IsTrue(_view.gameObject.activeSelf);
            Assert.IsTrue(_view.IsShowing);
        }

        [UnityTest]
        public IEnumerator Controller_Skip_AlsoRecordsCompletion()
        {
            _controller.TryShowFirstTime(MENU_ID, Resolve);
            yield return null;
            _view.HandleNext();

            _view.HandleSkip();

            Assert.IsTrue(_record.IsCompleted(MENU_ID));
            Assert.IsTrue(_lastSkipped);
        }

        [UnityTest]
        public IEnumerator Controller_AbortMidway_DoesNotRecordSoItShowsAgain()
        {
            _controller.TryShowFirstTime(MENU_ID, Resolve);
            yield return null;
            _view.HandleNext();

            _controller.Abort();

            Assert.IsFalse(_record.IsCompleted(MENU_ID));
            Assert.AreEqual(0, _finishedCount);
            Assert.IsFalse(_view.IsShowing);
            Assert.IsTrue(_controller.TryShowFirstTime(MENU_ID, Resolve));
        }

        [UnityTest]
        public IEnumerator Controller_AlreadyCompleted_DoesNotShowOnEntryButReplayDoes()
        {
            _record.MarkCompleted(MENU_ID);

            Assert.IsFalse(_controller.TryShowFirstTime(MENU_ID, Resolve));
            Assert.IsFalse(_view.IsShowing);

            Assert.IsTrue(_controller.Replay(MENU_ID, Resolve));
            yield return null;

            Assert.IsTrue(_view.IsShowing);
        }

        [UnityTest]
        public IEnumerator Controller_UnknownMenu_DoesNothing()
        {
            Assert.IsFalse(_controller.TryShowFirstTime("menu_unknown", Resolve));
            yield return null;

            Assert.IsFalse(_view.IsShowing);
        }

        [UnityTest]
        public IEnumerator Controller_OtherMenuWhileShowing_AbortsFirstWithoutRecording()
        {
            _controller.TryShowFirstTime(MENU_ID, Resolve);
            yield return null;

            _controller.TryShowFirstTime(OTHER_ID, Resolve);

            Assert.IsFalse(_record.IsCompleted(MENU_ID));
            Assert.IsTrue(_view.IsShowing);

            _view.HandleNext();

            Assert.IsTrue(_record.IsCompleted(OTHER_ID));
        }

        [UnityTest]
        public IEnumerator View_StepWithTarget_LeavesAHoleAroundIt()
        {
            _controller.TryShowFirstTime(MENU_ID, Resolve);
            yield return null;

            // 강조 단계에서는 위쪽 어둠막이 화면 맨 위까지 가지 않고 대상 위에서 끝난다
            RectTransform topDim = _view.transform.Find("Dim0") as RectTransform;
            Assert.IsNotNull(topDim);
            Assert.Less(topDim.anchorMin.y, 1f);
            Assert.Greater(topDim.anchorMin.y, 0f);

            // 대상이 없는 둘째 단계는 화면 전체를 덮는다
            _view.HandleNext();
            Assert.AreEqual(0f, topDim.anchorMin.y, 0.0001f);
            Assert.AreEqual(1f, topDim.anchorMax.y, 0.0001f);
        }

        [UnityTest]
        public IEnumerator View_Handle_AfterFinished_DoesNotReportTwice()
        {
            _controller.TryShowFirstTime(OTHER_ID, Resolve);
            yield return null;

            _view.HandleNext();
            _view.HandleNext();
            _view.HandleSkip();

            Assert.AreEqual(1, _finishedCount);
        }
    }
}
