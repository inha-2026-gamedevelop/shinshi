// System
using System;
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    /// <summary> 프롤로그 일러스트와 자막 화면, 프리팹 없이 코드로 만든다. 부적 조사 자막은 한 장짜리 전체 표시로 같이 쓴다 </summary>
    public sealed class IllustrationView : ClientView
    {
        // 클릭 흐름은 IllustrationSequence가 정하고 이 뷰는 그 결과를 화면에 옮기기만 한다.
        // 전체 화면 클릭 영역은 스킵 버튼보다 아래에 두어 스킵 클릭이 다음 장 이동으로 새지 않는다.
        // 글꼴은 프로젝트 한글 TMP 글꼴을 SetFont로 받는다. 안 받으면 TMP 기본 글꼴이라 한글이 깨질 수 있다

        /****************************************
        *                Fields
        ****************************************/

        private const int    SORTING_ORDER      = 31000;
        private const float  PANEL_HEIGHT       = 260f;
        private const float  TEXT_MARGIN        = 120f;
        private const float  TEXT_FONT_SIZE     = 34f;
        private const float  HINT_FONT_SIZE     = 22f;
        private const float  HINT_HEIGHT        = 36f;
        private const float  HINT_MARGIN        = 24f;
        private const float  SKIP_WIDTH         = 160f;
        private const float  SKIP_HEIGHT        = 56f;
        private const float  SKIP_MARGIN        = 32f;
        private const float  SKIP_FONT_SIZE     = 24f;
        private const float  CHARS_PER_SECOND   = 30f;
        private const string ROOT_OBJECT_NAME   = "PrologueIllustration";
        private const string HINT_REVEAL        = "클릭하여 전체 표시";
        private const string HINT_NEXT          = "클릭하여 다음으로";
        private const string SKIP_LABEL         = "건너뛰기";

        // 일러스트는 아래쪽이 종이색으로 번지게 만들어져 있다. 자막 패널은 그 종이색에 맞춘 연한 색 위에 짙은 먹색 글자를 쓴다
        private static readonly Color BACK_COLOR  = new Color(0.04f, 0.05f, 0.07f, 1f);
        private static readonly Color PANEL_COLOR = new Color(0.94f, 0.89f, 0.78f, 0.88f);
        private static readonly Color TEXT_COLOR  = new Color(0.17f, 0.12f, 0.08f, 1f);
        private static readonly Color SKIP_COLOR  = new Color(0.94f, 0.89f, 0.78f, 0.85f);
        private static readonly Color HINT_COLOR  = new Color(0.17f, 0.12f, 0.08f, 0.6f);
        private static readonly Color CLEAR_COLOR = new Color(1f, 1f, 1f, 0f);

        private Image           _illustration;
        private TextMeshProUGUI _bodyText;
        private TextMeshProUGUI _hintText;
        private TextMeshProUGUI _skipText;
        private GameObject      _skipButton;

        private IReadOnlyList<IllustrationPage> _pages     = null;
        private IllustrationSequence            _sequence  = null;
        private Coroutine                       _typing    = null;
        private bool                            _completed = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsShowing => (_sequence != null) && (!_completed);

        // 구간이 끝났을 때 한 번 불린다, 마지막 장을 닫았거나 건너뛰었을 때다. 화면은 호출자가 내린다
        public event Action Finished;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 코드로 계층을 만든 화면을 돌려준다, 서버 빌드에서는 객체만 만들어지고 아무것도 그리지 않는다 </summary>
        public static IllustrationView Create()
        {
            var root = new GameObject(ROOT_OBJECT_NAME);
            return root.AddComponent<IllustrationView>();
        }

        /// <summary> 한글이 들어 있는 TMP 글꼴로 바꾼다 </summary>
        public void SetFont(TMP_FontAsset font)
        {
            if ((font == null) || (_bodyText == null))
            {
                return;
            }

            _bodyText.font = font;
            _hintText.font = font;
            _skipText.font = font;
        }

        /// <summary> 장들을 처음부터 보인다, startRevealed는 글을 처음부터 전부 보이고 allowSkip이 거짓이면 건너뛰기 버튼을 숨긴다 </summary>
        public void Show(IReadOnlyList<IllustrationPage> pages, bool startRevealed, bool allowSkip)
        {
            if ((pages == null) || (pages.Count == 0))
            {
                throw new ArgumentException("pages is empty", nameof(pages));
            }

            if (_bodyText == null)
            {
                return;
            }

            _pages     = pages;
            _sequence  = new IllustrationSequence(pages.Count, startRevealed);
            _completed = false;

            _skipButton.SetActive(allowSkip);
            gameObject.SetActive(true);
            ShowPage();
        }

        /// <summary> 화면 클릭 한 번, 키보드나 패드 입력도 이 메서드를 부르면 같은 흐름이다 </summary>
        public void HandleClick()
        {
            if ((_sequence == null) || (_completed))
            {
                return;
            }

            switch (_sequence.Click())
            {
                case EIllustrationClick.RevealedText:
                    StopTyping();
                    _bodyText.maxVisibleCharacters = int.MaxValue;
                    RefreshHint();
                    break;
                case EIllustrationClick.NextPage:
                    ShowPage();
                    break;
                case EIllustrationClick.Finished:
                    Complete();
                    break;
                default:
                    break;
            }
        }

        /// <summary> 이 일러스트 구간 전체를 건너뛴다 </summary>
        public void HandleSkip()
        {
            if ((_sequence == null) || (_completed))
            {
                return;
            }

            _sequence.Skip();
            Complete();
        }

        /// <summary> 화면을 내리되 객체는 남긴다, 같은 뷰로 다시 Show할 수 있다 </summary>
        public void Close()
        {
            StopTyping();
            gameObject.SetActive(false);
        }

        public void Hide()
        {
            Destroy(gameObject);
        }

        protected override void OnViewAwake()
        {
            BuildCanvas();
            gameObject.SetActive(false);
        }

        private void Complete()
        {
            StopTyping();
            _completed = true;
            Finished?.Invoke();
        }

        private void ShowPage()
        {
            IllustrationPage page = _pages[_sequence.PageIndex];

            _illustration.sprite  = page.Image;
            _illustration.enabled = page.Image != null;

            StopTyping();
            _bodyText.text = page.Text;

            if (_sequence.IsTyping)
            {
                _bodyText.maxVisibleCharacters = 0;
                _bodyText.ForceMeshUpdate();
                _typing = StartCoroutine(CoType(_bodyText.textInfo.characterCount));
            }
            else
            {
                _bodyText.maxVisibleCharacters = int.MaxValue;
            }

            RefreshHint();
        }

        private void StopTyping()
        {
            if (_typing == null)
            {
                return;
            }

            StopCoroutine(_typing);
            _typing = null;
        }

        private void RefreshHint()
        {
            _hintText.SetText(_sequence.IsTyping ? HINT_REVEAL : HINT_NEXT);
        }

        // 일시정지 중에도 흘러야 하므로 실제 시간으로 센다
        private IEnumerator CoType(int total)
        {
            float shown = 0f;
            while (shown < total)
            {
                shown += CHARS_PER_SECOND * Time.unscaledDeltaTime;
                _bodyText.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(shown));
                yield return null;
            }

            _typing = null;
            _sequence.OnTextCompleted();
            RefreshHint();
        }

        private void BuildCanvas()
        {
            PrologueUi.AddCanvas(gameObject, SORTING_ORDER, true);
            PrologueUi.EnsureEventSystem(transform);

            RectTransform back = PrologueUi.CreateImage("Back", transform, BACK_COLOR, false);
            PrologueUi.Stretch(back);

            _illustration = PrologueUi.CreateImage("Illustration", transform, Color.white, false).GetComponent<Image>();
            _illustration.preserveAspect = true;
            PrologueUi.Stretch(_illustration.rectTransform);

            // 전체 화면 클릭 영역, 투명하지만 입력은 받는다
            RectTransform clickArea = PrologueUi.CreateImage("ClickArea", transform, CLEAR_COLOR, true);
            PrologueUi.Stretch(clickArea);
            clickArea.gameObject.AddComponent<Button>().onClick.AddListener(HandleClick);

            RectTransform panel = PrologueUi.CreateImage("Panel", transform, PANEL_COLOR, false);
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(1f, 0f);
            panel.pivot     = new Vector2(0.5f, 0f);
            panel.offsetMin = new Vector2(0f, 0f);
            panel.offsetMax = new Vector2(0f, PANEL_HEIGHT);

            _bodyText = PrologueUi.CreateText("Body", panel, TEXT_FONT_SIZE, TEXT_COLOR, TextAlignmentOptions.TopLeft);
            _bodyText.textWrappingMode = TextWrappingModes.Normal;
            _bodyText.rectTransform.anchorMin = Vector2.zero;
            _bodyText.rectTransform.anchorMax = Vector2.one;
            _bodyText.rectTransform.offsetMin = new Vector2(TEXT_MARGIN, HINT_HEIGHT + HINT_MARGIN);
            _bodyText.rectTransform.offsetMax = new Vector2(-TEXT_MARGIN, -HINT_MARGIN);

            _hintText = PrologueUi.CreateText("Hint", panel, HINT_FONT_SIZE, HINT_COLOR, TextAlignmentOptions.BottomRight);
            _hintText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _hintText.rectTransform.anchorMax = new Vector2(1f, 0f);
            _hintText.rectTransform.pivot     = new Vector2(0.5f, 0f);
            _hintText.rectTransform.offsetMin = new Vector2(TEXT_MARGIN, HINT_MARGIN);
            _hintText.rectTransform.offsetMax = new Vector2(-TEXT_MARGIN, HINT_MARGIN + HINT_HEIGHT);

            RectTransform skip = PrologueUi.CreateImage("Skip", transform, SKIP_COLOR, true);
            skip.anchorMin        = new Vector2(1f, 1f);
            skip.anchorMax        = new Vector2(1f, 1f);
            skip.pivot            = new Vector2(1f, 1f);
            skip.anchoredPosition = new Vector2(-SKIP_MARGIN, -SKIP_MARGIN);
            skip.sizeDelta        = new Vector2(SKIP_WIDTH, SKIP_HEIGHT);
            skip.gameObject.AddComponent<Button>().onClick.AddListener(HandleSkip);
            _skipButton = skip.gameObject;

            _skipText = PrologueUi.CreateText("Label", skip, SKIP_FONT_SIZE, TEXT_COLOR, TextAlignmentOptions.Center);
            _skipText.SetText(SKIP_LABEL);
            PrologueUi.Stretch(_skipText.rectTransform);
        }

    }
}
