// System
using System;
using System.Collections;

// Unity
using UnityEngine;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.AI
{
    /// <summary> 계정에서 프롤로그를 한 번 마친 뒤 새 캐릭터를 만들었을 때 띄우는 선택창, 기존 창과 같은 스타일이고 프리팹 없이 코드로 만든다 </summary>
    public sealed class PrologueChoiceView : ClientView
    {
        // 어떤 로비 화면에서 언제 띄울지는 이 클래스가 정하지 않는다. 호출하는 쪽이 Show를 부르고 Chosen을 받는다.
        // 선택은 한 번만 전달하고, 뒤따르는 클릭은 무시한다.
        // 제목 양옆 마름모는 글자 너비를 알아야 놓을 수 있어서 글꼴이 정해진 뒤 처음 Show할 때 한 번 놓는다

        /****************************************
        *                Fields
        ****************************************/

        private const int    SORTING_ORDER   = 31500;
        private const float  PANEL_WIDTH     = 920f;
        private const float  PANEL_HEIGHT    = 380f;
        private const float  TITLE_SIZE      = 36f;
        private const float  TITLE_HEIGHT    = 56f;
        private const float  BODY_SIZE       = 26f;
        private const float  BODY_TOP        = 104f;
        private const float  BODY_HEIGHT     = 110f;
        private const float  BODY_SIDE_MARGIN = 60f;
        private const float  BUTTON_SIZE     = 28f;
        private const float  BUTTON_WIDTH    = 330f;
        private const float  BUTTON_HEIGHT   = 76f;
        private const float  BUTTON_GAP      = 36f;
        private const float  BUTTON_BOTTOM   = 48f;
        private const float  HALF            = 0.5f;
        private const string ROOT_NAME       = "PrologueChoice";
        private const string TITLE_TEXT      = "프롤로그 진행";
        // 초안 문구의 두 문장 사이에서 줄을 바꾼다. TMP는 한글을 글자 단위로 끊어서 문장 중간에서 줄이 바뀌면 음절이 쪼개진다
        private const string BODY_TEXT       = "프롤로그에서 이야기와 기본 조작을 다시 살펴볼 수 있습니다.\n건너뛰면 신시에서 바로 시작합니다.";
        private const string PLAY_LABEL      = "프롤로그 진행";
        private const string SKIP_LABEL      = "건너뛰고 신시로 이동";

        private static readonly Color DIM_COLOR = new Color(0f, 0f, 0f, 0.65f);

        private RectTransform     _panel     = null;
        private TextMeshProUGUI   _title     = null;
        private TextMeshProUGUI[] _texts     = null;
        private bool              _decorated = false;
        private bool              _chosen    = false;
        private Coroutine         _closeRoutine = null;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsShowing => (gameObject.activeSelf) && (!_chosen);

        // 선택했을 때 한 번 불린다. 창은 호출하는 쪽이 내린다
        public event Action<EPrologueChoice> Chosen;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 코드로 계층을 만든 선택창을 돌려준다, 서버 빌드에서는 객체만 만들어지고 아무것도 그리지 않는다 </summary>
        public static PrologueChoiceView Create()
        {
            var root = new GameObject(ROOT_NAME);
            return root.AddComponent<PrologueChoiceView>();
        }

        public void SetFont(TMP_FontAsset font)
        {
            if ((font == null) || (_texts == null))
            {
                return;
            }

            for (int i = 0; i < _texts.Length; ++i)
            {
                _texts[i].font = font;
            }
        }

        public void Show()
        {
            // 방금 Close로 내려가는 중이었다면 취소한다. 안 그러면 새로 띄운 창이 곧 꺼진다
            CancelPendingClose();

            _chosen = false;
            gameObject.SetActive(true);

            if (!_decorated)
            {
                _title.ForceMeshUpdate();
                PrologueUi.DecorateTitle(_panel, _title, -(TITLE_HEIGHT * HALF));
                _decorated = true;
            }
        }

        /// <summary> 진행 버튼, 키보드나 패드 입력도 이 메서드를 부르면 같은 흐름이다 </summary>
        public void HandlePlay()
        {
            Choose(EPrologueChoice.Play);
        }

        public void HandleSkip()
        {
            Choose(EPrologueChoice.SkipToTown);
        }

        public void Close()
        {
            if (!gameObject.activeInHierarchy)
            {
                return;
            }

            // 선택을 누른 클릭이 같은 프레임에 이동이나 공격으로 읽히지 않게 한 프레임 뒤에 내린다.
            // 이 완화가 실제로 누수를 막는지는 확인하지 못했다
            CancelPendingClose();
            _closeRoutine = StartCoroutine(CoCloseNextFrame());
        }

        private IEnumerator CoCloseNextFrame()
        {
            yield return null;

            _closeRoutine = null;
            gameObject.SetActive(false);
        }

        private void CancelPendingClose()
        {
            if (_closeRoutine == null)
            {
                return;
            }

            StopCoroutine(_closeRoutine);
            _closeRoutine = null;
        }

        public void Hide()
        {
            Destroy(gameObject);
        }

        protected override void OnViewAwake()
        {
            PrologueUi.AddCanvas(gameObject, SORTING_ORDER, true);
            PrologueUi.EnsureEventSystem(transform);

            // 뒤쪽 클릭을 막는 어둠막
            RectTransform dim = PrologueUi.CreateImage("Dim", transform, DIM_COLOR, true);
            PrologueUi.Stretch(dim);

            _panel = PrologueUi.CreateImage("Panel", transform, Color.white, true);
            _panel.anchorMin        = new Vector2(HALF, HALF);
            _panel.anchorMax        = new Vector2(HALF, HALF);
            _panel.pivot            = new Vector2(HALF, HALF);
            _panel.anchoredPosition = Vector2.zero;
            _panel.sizeDelta        = new Vector2(PANEL_WIDTH, PANEL_HEIGHT);
            PrologueUi.StylePanel(_panel);

            _title = MakeLabel("Title", TITLE_TEXT, TITLE_SIZE, UiModernDark.TextGold, 0f, TITLE_HEIGHT, true);
            TextMeshProUGUI body = MakeLabel("Body", BODY_TEXT, BODY_SIZE, UiModernDark.TextMain, BODY_TOP, BODY_HEIGHT, false);

            TextMeshProUGUI play = MakeButton("PlayButton", PLAY_LABEL, true, -((BUTTON_WIDTH + BUTTON_GAP) * HALF), HandlePlay);
            TextMeshProUGUI skip = MakeButton("SkipButton", SKIP_LABEL, false, (BUTTON_WIDTH + BUTTON_GAP) * HALF, HandleSkip);

            _texts = new[] { _title, body, play, skip };
            gameObject.SetActive(false);
        }

        private void Choose(EPrologueChoice choice)
        {
            if ((_chosen) || (!gameObject.activeSelf))
            {
                return;
            }

            _chosen = true;
            Chosen?.Invoke(choice);
        }

        private TextMeshProUGUI MakeLabel(string objectName, string text, float size, Color color, float top, float height, bool bold)
        {
            TextMeshProUGUI label = PrologueUi.CreateText(objectName, _panel, size, color, TextAlignmentOptions.Center);
            label.SetText(text);
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;

            RectTransform rect = label.rectTransform;
            rect.anchorMin        = new Vector2(0f, 1f);
            rect.anchorMax        = new Vector2(1f, 1f);
            rect.pivot            = new Vector2(HALF, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta        = new Vector2(-(BODY_SIDE_MARGIN * 2f), height);
            return label;
        }

        private TextMeshProUGUI MakeButton(string objectName, string label, bool primary, float offsetX,
            UnityEngine.Events.UnityAction onClick)
        {
            TextMeshProUGUI text = PrologueUi.CreateButton(objectName, _panel, BUTTON_SIZE, primary, onClick, out RectTransform rect);
            rect.anchorMin        = new Vector2(HALF, 0f);
            rect.anchorMax        = new Vector2(HALF, 0f);
            rect.pivot            = new Vector2(HALF, 0f);
            rect.anchoredPosition = new Vector2(offsetX, BUTTON_BOTTOM);
            rect.sizeDelta        = new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT);
            text.SetText(label);
            return text;
        }
    }
}
