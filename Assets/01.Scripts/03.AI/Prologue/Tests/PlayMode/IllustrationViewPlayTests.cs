// System
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class IllustrationViewPlayTests
    {
        private IllustrationView _view;
        private int              _finishedCount;

        private static readonly IllustrationPage[] PAGES =
        {
            new IllustrationPage(null, "첫 번째 장의 글입니다."),
            new IllustrationPage(null, "두 번째 장의 글입니다.")
        };

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _finishedCount = 0;
            _view = IllustrationView.Create();
            _view.Finished += () => ++_finishedCount;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_view != null)
            {
                Object.Destroy(_view.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Click_WhileTyping_DoesNotMoveToNextPage()
        {
            _view.Show(PAGES, false, true);
            yield return null;

            _view.HandleClick();

            Assert.AreEqual(0, _finishedCount);
            Assert.IsTrue(_view.IsShowing);
        }

        [UnityTest]
        public IEnumerator Click_ThroughAllPages_FinishesOnce()
        {
            _view.Show(PAGES, false, true);
            yield return null;

            // 장마다 글 완성 클릭 하나, 다음 클릭 하나
            _view.HandleClick();
            _view.HandleClick();
            _view.HandleClick();
            Assert.AreEqual(0, _finishedCount);

            _view.HandleClick();
            _view.HandleClick();

            Assert.AreEqual(1, _finishedCount);
            Assert.IsFalse(_view.IsShowing);
        }

        [UnityTest]
        public IEnumerator Skip_Midway_FinishesAtOnce()
        {
            _view.Show(PAGES, false, true);
            yield return null;

            _view.HandleSkip();
            _view.HandleSkip();

            Assert.AreEqual(1, _finishedCount);
        }

        [UnityTest]
        public IEnumerator Show_StartRevealedSinglePage_ClosesWithOneClick()
        {
            _view.Show(new[] { new IllustrationPage(null, "신시의 부적과 닮은 문양이다.") }, true, false);
            yield return null;

            _view.HandleClick();

            Assert.AreEqual(1, _finishedCount);
        }

        [UnityTest]
        public IEnumerator Click_AfterFinished_DoesNotFinishAgain()
        {
            _view.Show(new[] { new IllustrationPage(null, "한 장") }, true, false);
            yield return null;

            _view.HandleClick();
            _view.HandleClick();

            Assert.AreEqual(1, _finishedCount);
        }
    }
}
