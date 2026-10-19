// System
using System.Reflection;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// 서드파티
using NUnit.Framework;
using TMPro;

// 프로젝트 내부
using Shinshi.Network;
using Shinshi.Systems;
using Shinshi.UI;

namespace Shinshi.Tests.EditMode
{
    /// <summary> HudChatView 큰 창: 누를 수 있는 진입, 마스크 스크롤, 원위치 복원, 마을 아닌 곳의 파티 초대 막기 </summary>
    public sealed class HudChatViewExpandTests
    {
        private const string KOREAN_FONT_PATH = "Assets/01.Scripts/06.UI/Hud/Fonts/HudQuestKorean-Regular.asset";
        private const string EXPANDED_ROOT    = "ChatExpandedRoot";

        private GameObject      _canvas;
        private RectTransform   _group;
        private RectTransform   _compactScroll;
        private HudChatView     _view;
        private TMP_Text        _messages;
        private TMP_InputField  _input;
        private Button[]        _tabs;

        [SetUp]
        public void SetUp()
        {
            ChatLog.Clear();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KOREAN_FONT_PATH);
            Assert.IsNotNull(font, KOREAN_FONT_PATH);

            _canvas = new GameObject("ChatTestCanvas", typeof(Canvas));
            _group  = NewRect("ChatGroup", _canvas.transform, new Vector2(300f, 200f));

            var viewRect = NewRect("Chat", _group, new Vector2(420f, 260f));
            _view = viewRect.gameObject.AddComponent<HudChatView>();

            _compactScroll = NewRect("CompactScroll", viewRect, new Vector2(400f, 180f));
            _compactScroll.gameObject.AddComponent<RectMask2D>();
            var messageRect = NewRect("Messages", _compactScroll, new Vector2(400f, 180f));
            _messages = messageRect.gameObject.AddComponent<TextMeshProUGUI>();
            _messages.font = font;

            var inputRect = NewRect("Input", viewRect, new Vector2(300f, 36f));
            inputRect.gameObject.AddComponent<Image>();
            _input = inputRect.gameObject.AddComponent<TMP_InputField>();

            _tabs = new Button[5];
            for (int i = 0; i < _tabs.Length; ++i)
            {
                var tabRect = NewRect("Tab" + i, viewRect, new Vector2(60f, 24f));
                var image   = tabRect.gameObject.AddComponent<Image>();
                _tabs[i] = tabRect.gameObject.AddComponent<Button>();
                _tabs[i].targetGraphic = image;
            }

            SetField("_messages", _messages);
            SetField("_input", _input);
            SetField("_tabs", _tabs);
            Invoke("Awake");
        }

        [TearDown]
        public void TearDown()
        {
            ChatLog.Clear();
            Object.DestroyImmediate(_canvas);
        }

        [Test]
        public void CompactChat_HasAClickableExpandButton_ThatOpensAndCloseButtonCollapses()
        {
            Transform expand = _view.transform.Find(HudChatView.EXPAND_BUTTON_NAME);
            Assert.IsNotNull(expand, "작은 채팅에 확대 단추가 있어야 한다");

            expand.GetComponent<Button>().onClick.Invoke();
            Assert.IsTrue(_view.IsExpanded);

            Transform close = _view.transform.Find(EXPANDED_ROOT + "/CloseButton");
            Assert.IsNotNull(close);
            close.GetComponent<Button>().onClick.Invoke();

            Assert.IsFalse(_view.IsExpanded);
        }

        [Test]
        public void Collapse_RestoresViewMessagesAndTabsToTheirOriginalParentsAndRects()
        {
            var viewRect        = (RectTransform)_view.transform;
            var messageRect     = (RectTransform)_messages.transform;
            Vector2 viewMin     = viewRect.anchorMin;
            Vector2 viewSize    = viewRect.sizeDelta;
            Vector2 messageSize = messageRect.sizeDelta;
            Vector2 messagePos  = messageRect.anchoredPosition;
            Vector2 messageMin  = messageRect.anchorMin;
            Vector2 messageMax  = messageRect.anchorMax;
            int tabSibling      = _tabs[3].transform.GetSiblingIndex();

            _view.SetExpanded(true);
            Assert.AreEqual(_canvas.transform, viewRect.parent, "큰 창은 캔버스 바로 아래에서 화면 가운데에 뜬다");

            _view.SetExpanded(false);

            Assert.AreEqual(_group, viewRect.parent);
            Assert.AreEqual(viewMin, viewRect.anchorMin);
            Assert.AreEqual(viewSize, viewRect.sizeDelta);
            Assert.AreEqual(_compactScroll, messageRect.parent, "메시지는 원래 마스크 부모로 돌아온다");
            Assert.AreEqual(messageSize, messageRect.sizeDelta);
            Assert.AreEqual(messagePos, messageRect.anchoredPosition);
            Assert.AreEqual(messageMin, messageRect.anchorMin);
            Assert.AreEqual(messageMax, messageRect.anchorMax);
            Assert.AreEqual(viewRect, _tabs[3].transform.parent);
            Assert.AreEqual(tabSibling, _tabs[3].transform.GetSiblingIndex());
        }

        [Test]
        public void Expanded_MessagesLiveInsideMaskedScrollViewport_AndOverflowKeepsLatestLineReachable()
        {
            for (int i = 0; i < 60; ++i)
            {
                ChatLog.AddMessage(EChatChannel.All, "달빛", "줄 " + i);
            }

            _view.SetExpanded(true);

            Transform viewport = _view.transform.Find(EXPANDED_ROOT + "/" + HudChatView.VIEWPORT_NAME);
            Assert.IsNotNull(viewport);
            Assert.IsNotNull(viewport.GetComponent<RectMask2D>(), "넘치는 줄을 자르는 마스크가 있어야 한다");
            Assert.IsTrue(_messages.transform.IsChildOf(viewport), "메시지는 스크롤 뷰포트 안에 있어야 한다");

            var scroll = viewport.GetComponent<ScrollRect>();
            Assert.IsTrue(scroll.content.rect.height > ((RectTransform)viewport).rect.height, "60줄은 뷰포트보다 길어야 한다");
            Assert.AreEqual(0f, scroll.verticalNormalizedPosition, 0.01f, "열면 가장 최근 줄에 있다");

            ChatLog.AddMessage(EChatChannel.All, "달빛", "마지막 줄");
            _view.SelectTab(EChatTab.All);
            Assert.AreEqual(0f, scroll.verticalNormalizedPosition, 0.01f, "바닥에 있으면 새 줄을 따라간다");
        }

        [Test]
        public void Expanded_WhenReaderScrolledUp_NewLineDoesNotYankPosition_AndHistoryIsKept()
        {
            for (int i = 0; i < 60; ++i)
            {
                ChatLog.AddMessage(EChatChannel.All, "달빛", "줄 " + i);
            }

            _view.SetExpanded(true);
            var scroll = _view.transform.Find(EXPANDED_ROOT + "/" + HudChatView.VIEWPORT_NAME).GetComponent<ScrollRect>();
            scroll.verticalNormalizedPosition = 1f;
            scroll.onValueChanged.Invoke(new Vector2(0f, 1f));

            ChatLog.AddMessage(EChatChannel.All, "달빛", "새 줄");
            _view.SelectTab(EChatTab.All);
            Assert.Greater(scroll.verticalNormalizedPosition, 0.5f, "위로 올려 읽는 중이면 위치를 지킨다");

            _view.ScrollToLatest();
            Assert.AreEqual(0f, scroll.verticalNormalizedPosition, 0.01f);

            _view.SetExpanded(false);
            Assert.IsTrue(_messages.text.Contains("줄 0"), "접어도 오래된 줄이 남는다");
            Assert.IsTrue(_messages.text.Contains("새 줄"), "접어도 최신 줄이 남는다");
        }

        [Test]
        public void Expanded_Viewport_IsRaycastableAndReceivesScrollEvents()
        {
            for (int i = 0; i < 60; ++i)
            {
                ChatLog.AddMessage(EChatChannel.All, "달빛", "줄 " + i);
            }

            _view.SetExpanded(true);
            Transform viewport = _view.transform.Find(EXPANDED_ROOT + "/" + HudChatView.VIEWPORT_NAME);
            var hit = viewport.GetComponent<Image>();
            Assert.IsNotNull(hit, "뷰포트에 레이캐스트 받는 그래픽이 있어야 한다");
            Assert.IsTrue(hit.raycastTarget);
            Assert.AreEqual(0f, hit.color.a, "보이지 않는 투명 그래픽이어야 한다");

            var canvas = _view.GetComponentInParent<Canvas>();
            Assert.IsTrue(GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas).Contains(hit), "GraphicRaycaster가 순회하는 목록에 들어 있어야 한다");

            var rect = (RectTransform)viewport;
            Vector2 center = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            Assert.IsTrue(((ICanvasRaycastFilter)hit).IsRaycastLocationValid(center, null), "뷰포트 가운데는 적중해야 한다");

            var scroll = viewport.GetComponent<ScrollRect>();
            Assert.AreEqual(0f, scroll.verticalNormalizedPosition, 0.01f);
            var data = new UnityEngine.EventSystems.PointerEventData(null) { scrollDelta = new Vector2(0f, 1f) };
            UnityEngine.EventSystems.ExecuteEvents.Execute(viewport.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.scrollHandler);
            Assert.Greater(scroll.verticalNormalizedPosition, 0.005f, "휠을 올리면 이전 줄 쪽으로 움직여야 한다");
        }

        [Test]
        public void CompactExpandButton_SitsAboveChatTopEdge_WithReadableLabel()
        {
            var button = _view.transform.Find(HudChatView.EXPAND_BUTTON_NAME);
            Assert.IsNotNull(button);

            var rect = (RectTransform)button;
            Assert.AreEqual(1f, rect.anchorMax.y, "채팅 윗변 기준");
            Assert.AreEqual(0f, rect.pivot.y, "버튼 아랫변이 채팅 윗변에 붙는다");
            Assert.GreaterOrEqual(rect.anchoredPosition.y, 0f, "탭 위가 아니라 채팅 바깥 위쪽에 놓는다");

            var label = button.GetComponentInChildren<TMP_Text>(true);
            Assert.AreEqual(16f, label.fontSize);
            var labelRect = (RectTransform)label.transform;
            Assert.AreEqual(2f, labelRect.offsetMin.x);
            Assert.AreEqual(2f, labelRect.offsetMin.y);
            Assert.GreaterOrEqual(rect.sizeDelta.y + labelRect.offsetMin.y + labelRect.offsetMax.y, 16f, "글자 높이가 잘리지 않아야 한다");
        }

        [Test]
        public void CanInvite_RequiresTownAndPartner()
        {
            Assert.IsTrue(HudChatView.CanInvite(true, "달빛"));
            Assert.IsFalse(HudChatView.CanInvite(false, "달빛"), "던전에서는 마을 메시지를 보내면 접속이 끊긴다");
            Assert.IsFalse(HudChatView.CanInvite(true, string.Empty));
            Assert.IsFalse(HudChatView.CanInvite(true, null));
        }

        [Test]
        public void InviteButtons_WhenNotConnectedToTown_DoNotSendAndLeaveASystemNotice()
        {
            ChatLog.AddMessage(EChatChannel.Whisper, "달빛", "안녕하세요");
            _view.SetExpanded(true);
            _view.SelectPartnerName("달빛");

            Assert.IsFalse(_view.TryInvitePartner());
            AssertLastLineIsInviteNotice();

            ChatLog.Clear();
            _view.transform.Find(EXPANDED_ROOT + "/InviteButton").GetComponent<Button>().onClick.Invoke();
            AssertLastLineIsInviteNotice();

            ChatLog.Clear();
            _view.transform.Find(EXPANDED_ROOT + "/HeaderInvite").GetComponent<Button>().onClick.Invoke();
            AssertLastLineIsInviteNotice();
        }

        private static void AssertLastLineIsInviteNotice()
        {
            Assert.Greater(ChatLog.Entries.Count, 0);
            Assert.IsTrue(ChatLog.Entries[ChatLog.Entries.Count - 1].Text.Contains(HudChatView.INVITE_TOWN_ONLY_TEXT));
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            return rect;
        }

        private void SetField(string name, object value)
        {
            FieldInfo field = typeof(HudChatView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(_view, value);
        }

        private void Invoke(string method)
        {
            MethodInfo info = typeof(HudChatView).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, method);
            info.Invoke(_view, null);
        }
    }
}
