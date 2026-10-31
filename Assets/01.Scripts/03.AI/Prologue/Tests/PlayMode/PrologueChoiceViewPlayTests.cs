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
    public class PrologueChoiceViewPlayTests
    {
        private PrologueChoiceView _view;
        private int                _chosenCount;
        private EPrologueChoice    _lastChoice;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _chosenCount = 0;
            _view = PrologueChoiceView.Create();
            _view.Chosen += choice =>
            {
                ++_chosenCount;
                _lastChoice = choice;
            };
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
        public IEnumerator Create_BeforeShow_IsHidden()
        {
            yield return null;

            Assert.IsFalse(_view.gameObject.activeSelf);
            Assert.AreEqual(0, _chosenCount);
        }

        private string TextAt(string path)
        {
            Transform target = _view.transform.Find(path);
            Assert.IsNotNull(target, path);
            return target.GetComponent<TMPro.TMP_Text>().text;
        }

        [UnityTest]
        public IEnumerator Texts_FollowTheDraftWording()
        {
            yield return null;

            Assert.AreEqual("프롤로그 진행", TextAt("Panel/Title"));
            // 초안의 두 문장 사이에서 줄만 바꿨다
            Assert.AreEqual("프롤로그에서 이야기와 기본 조작을 다시 살펴볼 수 있습니다.\n건너뛰면 신시에서 바로 시작합니다.", TextAt("Panel/Body"));
            Assert.AreEqual("프롤로그 진행", TextAt("Panel/PlayButton/Label"));
            Assert.AreEqual("건너뛰고 신시로 이동", TextAt("Panel/SkipButton/Label"));
        }

        [UnityTest]
        public IEnumerator Close_KeepsWindowForOneFrameAndShowAfterCloseStaysVisible()
        {
            _view.Show();
            yield return null;
            _view.HandlePlay();

            _view.Close();
            Assert.IsTrue(_view.gameObject.activeSelf);

            yield return null;
            yield return null;
            Assert.IsFalse(_view.gameObject.activeSelf);

            _view.Show();
            _view.Close();
            _view.Show();
            yield return null;
            yield return null;

            Assert.IsTrue(_view.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator HandlePlay_AfterShow_ReportsPlayOnce()
        {
            _view.Show();
            yield return null;

            _view.HandlePlay();
            _view.HandlePlay();
            _view.HandleSkip();

            Assert.AreEqual(1, _chosenCount);
            Assert.AreEqual(EPrologueChoice.Play, _lastChoice);
        }

        [UnityTest]
        public IEnumerator HandleSkip_AfterShow_ReportsSkipToTown()
        {
            _view.Show();
            yield return null;

            _view.HandleSkip();

            Assert.AreEqual(1, _chosenCount);
            Assert.AreEqual(EPrologueChoice.SkipToTown, _lastChoice);
        }

        [UnityTest]
        public IEnumerator Handle_WhileHidden_IsIgnored()
        {
            yield return null;

            _view.HandlePlay();

            Assert.AreEqual(0, _chosenCount);
        }

        [UnityTest]
        public IEnumerator Show_AfterChosenAndClosed_CanChooseAgain()
        {
            _view.Show();
            yield return null;
            _view.HandlePlay();
            _view.Close();

            _view.Show();
            yield return null;
            _view.HandleSkip();

            Assert.AreEqual(2, _chosenCount);
            Assert.AreEqual(EPrologueChoice.SkipToTown, _lastChoice);
        }
    }
}
