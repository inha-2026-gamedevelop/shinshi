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
