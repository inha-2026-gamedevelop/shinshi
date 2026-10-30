// System
using System;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    /// <summary> 계정에서 프롤로그를 한 번 마친 뒤 새 캐릭터를 만들었을 때 띄우는 선택창, 프리팹 없이 코드로 만든다 </summary>
    public sealed class PrologueChoiceView : ClientView
    {
        // 어떤 로비 화면에서 언제 띄울지는 이 클래스가 정하지 않는다. 호출하는 쪽이 Show를 부르고 Chosen을 받는다.
        // 선택은 한 번만 전달하고, 뒤따르는 클릭은 무시한다

        /****************************************
        *                Fields
        ****************************************/

        private const int    SORTING_ORDER   = 31500;
        private const float  PANEL_WIDTH     = 760f;
        private const float  PANEL_HEIGHT    = 380f;
        private const float  TITLE_SIZE      = 40f;
        private const float  BODY_SIZE       = 28f;
        private const float  BUTTON_SIZE     = 30f;
        private const float  BUTTON_WIDTH    = 320f;
        private const float  BUTTON_HEIGHT   = 80f;
        private const float  BUTTON_GAP      = 40f;
        private const float  BUTTON_BOTTOM   = 50f;
        private const float  TITLE_TOP       = 50f;
        private const float  BODY_TOP        = 130f;
        private const float  TEXT_HEIGHT     = 70f;
        private const float  HALF            = 0.5f;
        private const string ROOT_NAME       = "PrologueChoice";
        private const string TITLE_TEXT      = "프롤로그를 진행할까요?";
        private const string BODY_TEXT       = "이미 프롤로그를 마친 계정입니다.";
        private const string PLAY_LABEL      = "프롤로그 진행";
        private const string SKIP_LABEL      = "건너뛰고 신시로 이동";

        private static readonly Color DIM_COLOR    = new Color(0f, 0f, 0f, 0.65f);
        private static readonly Color PANEL_COLOR  = new Color(0.94f, 0.89f, 0.78f, 0.98f);
        private static readonly Color BUTTON_COLOR = new Color(0.83f, 0.74f, 0.58f, 1f);
        private static readonly Color TEXT_COLOR   = new Color(0.17f, 0.12f, 0.08f, 1f);

        private TextMeshProUGUI[] _texts  = null;
        private bool              _chosen = false;

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
            _chosen = false;
            gameObject.SetActive(true);
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
            gameObject.SetActive(false);
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

            RectTransform panel = PrologueUi.CreateImage("Panel", transform, PANEL_COLOR, true);
            Center(panel, new Vector2(PANEL_WIDTH, PANEL_HEIGHT), 0f);

            TextMeshProUGUI title = MakeLabel("Title", panel, TITLE_TEXT, TITLE_SIZE, TITLE_TOP);
            TextMeshProUGUI body  = MakeLabel("Body", panel, BODY_TEXT, BODY_SIZE, BODY_TOP);

            TextMeshProUGUI play = MakeButton("PlayButton", panel, PLAY_LABEL, -((BUTTON_WIDTH + BUTTON_GAP) * HALF), HandlePlay);
            TextMeshProUGUI skip = MakeButton("SkipButton", panel, SKIP_LABEL, (BUTTON_WIDTH + BUTTON_GAP) * HALF, HandleSkip);

            _texts = new[] { title, body, play, skip };
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

        private static void Center(RectTransform rect, Vector2 size, float offsetY)
        {
            rect.anchorMin        = new Vector2(HALF, HALF);
            rect.anchorMax        = new Vector2(HALF, HALF);
            rect.pivot            = new Vector2(HALF, HALF);
            rect.anchoredPosition = new Vector2(0f, offsetY);
            rect.sizeDelta        = size;
        }

        private static TextMeshProUGUI MakeLabel(string objectName, Transform parent, string text, float size, float top)
        {
            TextMeshProUGUI label = PrologueUi.CreateText(objectName, parent, size, TEXT_COLOR, TextAlignmentOptions.Center);
            label.SetText(text);

            RectTransform rect = label.rectTransform;
            rect.anchorMin        = new Vector2(0f, 1f);
            rect.anchorMax        = new Vector2(1f, 1f);
            rect.pivot            = new Vector2(HALF, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta        = new Vector2(0f, TEXT_HEIGHT);
            return label;
        }

        private static TextMeshProUGUI MakeButton(string objectName, Transform parent, string label, float offsetX,
            UnityEngine.Events.UnityAction onClick)
        {
            RectTransform rect = PrologueUi.CreateImage(objectName, parent, BUTTON_COLOR, true);
            rect.anchorMin        = new Vector2(HALF, 0f);
            rect.anchorMax        = new Vector2(HALF, 0f);
            rect.pivot            = new Vector2(HALF, 0f);
            rect.anchoredPosition = new Vector2(offsetX, BUTTON_BOTTOM);
            rect.sizeDelta        = new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT);
            rect.gameObject.AddComponent<Button>().onClick.AddListener(onClick);

            TextMeshProUGUI text = PrologueUi.CreateText("Label", rect, BUTTON_SIZE, TEXT_COLOR, TextAlignmentOptions.Center);
            text.SetText(label);
            PrologueUi.Stretch(text.rectTransform);
            return text;
        }
    }
}
