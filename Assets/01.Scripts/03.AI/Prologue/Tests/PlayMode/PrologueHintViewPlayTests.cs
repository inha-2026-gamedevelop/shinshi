// System
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;
using TMPro;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class PrologueHintViewPlayTests
    {
        private PrologueHintView _view;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _view = PrologueHintView.Create();
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
        public IEnumerator SetHint_NonEmpty_ShowsTextAndEmptyHidesIt()
        {
            _view.SetHint("마우스 왼쪽 버튼으로 공격");
            yield return null;

            TextMeshProUGUI hint = FindText("Hint");
            Assert.IsTrue(hint.gameObject.activeSelf);
            Assert.AreEqual("마우스 왼쪽 버튼으로 공격", hint.text);

            _view.SetHint(string.Empty);
            yield return null;

            Assert.IsFalse(hint.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator SetObjective_NonEmpty_ShowsObjective()
        {
            _view.SetObjective(PrologueHintTexts.WOLBAEK_OBJECTIVE);
            yield return null;

            TextMeshProUGUI objective = FindText("Objective");
            Assert.IsTrue(objective.gameObject.activeSelf);
            Assert.AreEqual(PrologueHintTexts.WOLBAEK_OBJECTIVE, objective.text);
        }

        [UnityTest]
        public IEnumerator Texts_Always_DoNotBlockRaycasts()
        {
            _view.SetHint("가나다");
            _view.SetObjective("라마바");
            yield return null;

            Assert.IsFalse(FindText("Hint").raycastTarget);
            Assert.IsFalse(FindText("Objective").raycastTarget);
            Assert.IsNull(_view.GetComponent<GraphicRaycaster>());
        }

        private TextMeshProUGUI FindText(string objectName)
        {
            Transform child = _view.transform.Find(objectName);
            Assert.IsNotNull(child, objectName);
            return child.GetComponent<TextMeshProUGUI>();
        }
    }
}
