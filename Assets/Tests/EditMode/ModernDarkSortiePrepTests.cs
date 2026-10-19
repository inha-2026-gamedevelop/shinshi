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
    public class ModernDarkSortiePrepTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void Layout_NoSerializedNotice_CreatesReadableLabel_AndSetNoticeShowsText()
        {
            SortiePrepView view = NewView(false, out _, out _, out _, out _);

            view.EnsureModernLayout();
            view.SetNotice("파티 방을 만드는 중이다.");

            Transform created = view.transform.Find("MD_Notice");
            Assert.IsNotNull(created, "안내 글자가 없으면 배치가 만들어야 한다");
            var label = created.GetComponent<TMP_Text>();
            Assert.AreEqual("파티 방을 만드는 중이다.", label.text);
            Assert.IsFalse(label.raycastTarget);
            Assert.GreaterOrEqual((label.color.r + label.color.g + label.color.b) / 3f, 0.6f, "어두운 바탕에서 읽혀야 한다");
            Assert.IsTrue(created.gameObject.activeSelf, "HideLegacy가 꺼서는 안 된다");

            int children = view.transform.childCount;
            view.EnsureModernLayout();
            Assert.AreEqual(children, view.transform.childCount, "여러 번 불러도 더 만들지 않는다");
        }

        [Test]
        public void Layout_ExistingNotice_IsKept_AndNoSecondLabelIsCreated()
        {
            SortiePrepView view = NewView(true, out _, out _, out _, out _);
            var existing = (TMP_Text)typeof(SortiePrepView).GetField("_noticeLabel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);

            view.EnsureModernLayout();
            view.SetNotice("기존");

            Assert.IsNull(view.transform.Find("MD_Notice"));
            Assert.AreEqual("기존", existing.text);
        }

        [Test]
        public void SetRoomControls_NoRoom_DisablesReadyAndLeave_AndPendingAlsoDifficulty()
        {
            SortiePrepView view = NewView(true, out Button ready, out Button leave, out Button prev, out Button next);

            view.SetRoomControls(false, false);
            Assert.IsFalse(ready.interactable);
            Assert.IsFalse(leave.interactable);
            Assert.IsTrue(prev.interactable, "방이 없고 대기 중이 아니면 난이도로 다시 만들 수 있다");
            Assert.IsTrue(next.interactable);

            view.SetRoomControls(false, true);
            Assert.IsFalse(prev.interactable);
            Assert.IsFalse(next.interactable);

            view.SetRoomControls(true, false);
            Assert.IsTrue(ready.interactable);
            Assert.IsTrue(leave.interactable);
            Assert.IsTrue(prev.interactable);
            Assert.IsTrue(next.interactable);
        }

        private SortiePrepView NewView(bool withNotice, out Button ready, out Button leave, out Button prev, out Button next)
        {
            _root = new GameObject("SortiePrepTest", typeof(RectTransform));
            _root.SetActive(false);
            var view = _root.AddComponent<SortiePrepView>();

            ready = NewButton("Ready");
            leave = NewButton("Leave");
            prev  = NewButton("Prev");
            next  = NewButton("Next");
            SetField(view, "_readyButton", ready);
            SetField(view, "_leaveButton", leave);
            SetField(view, "_prevDifficultyButton", prev);
            SetField(view, "_nextDifficultyButton", next);

            if (withNotice)
            {
                var go = new GameObject("Notice", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                go.transform.SetParent(_root.transform, false);
                SetField(view, "_noticeLabel", go.GetComponent<TextMeshProUGUI>());
            }

            return view;
        }

        private Button NewButton(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_root.transform, false);
            return go.GetComponent<Button>();
        }

        private static void SetField(object owner, string name, object value)
        {
            owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        }
    }
}
