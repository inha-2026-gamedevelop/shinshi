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

        [UnityTest]
        public IEnumerator ShowNotice_ShowsTextThenHidesItAfterTheGivenSeconds()
        {
            _view.ShowNotice("체력이 회복되었습니다.", 0.2f);
            yield return null;

            TextMeshProUGUI notice = FindText("Notice");
            Assert.IsTrue(notice.gameObject.activeSelf);
            Assert.AreEqual("체력이 회복되었습니다.", notice.text);

            float waited = 0f;
            while ((notice.gameObject.activeSelf) && (waited < 3f))
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsFalse(notice.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator ShowNotice_NewNoticeReplacesTheOldOne()
        {
            _view.ShowNotice("첫 알림", 5f);
            yield return null;
            _view.ShowNotice("둘째 알림", 5f);
            yield return null;

            Assert.AreEqual("둘째 알림", FindText("Notice").text);
        }

        [UnityTest]
        public IEnumerator HideNotice_HidesImmediately()
        {
            _view.ShowNotice("알림", 5f);
            yield return null;

            _view.HideNotice();

            Assert.IsFalse(FindText("Notice").gameObject.activeSelf);
            Assert.IsFalse(FindText("Notice").raycastTarget);
        }

        private TextMeshProUGUI FindText(string objectName)
        {
            Transform child = _view.transform.Find(objectName);
            Assert.IsNotNull(child, objectName);
            return child.GetComponent<TextMeshProUGUI>();
        }
    }
}
