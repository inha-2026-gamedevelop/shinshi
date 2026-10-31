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
        private const float  PANEL_HEIGHT       = 170f;
        private const float  TEXT_MARGIN        = 120f;
        private const float  TEXT_FONT_SIZE     = 34f;
        private const float  HINT_FONT_SIZE     = 22f;
        private const float  HINT_HEIGHT        = 36f;
        private const float  HINT_MARGIN        = 24f;
        private const float  SKIP_WIDTH         = 300f;
        private const float  SKIP_HEIGHT        = 56f;
        private const float  SKIP_MARGIN        = 32f;
        private const float  SKIP_FONT_SIZE     = 24f;
        private const float  CHARS_PER_SECOND   = 30f;
        private const float  INK_SECONDS        = 1.2f;
        private const float  INK_SEED_RANGE     = 100f;
        private const float  INK_CENTER_MIN     = 0.3f;
        private const float  INK_CENTER_MAX     = 0.7f;
        private const float  SHADE_EXTRA        = 90f;
        private const int    SHADE_STEPS        = 32;
        private const float  HALF               = 0.5f;
        private const float  OUTLINE_WIDTH      = 0.25f;
        private const string ROOT_OBJECT_NAME   = "PrologueIllustration";
        private const string HINT_REVEAL        = "클릭하여 전체 표시";
        private const string HINT_NEXT          = "클릭하여 다음으로";
        private const string HINT_CLOSE         = "클릭하여 닫기";
        private const string SKIP_LABEL         = "건너뛰기";

        // 일러스트는 아래쪽이 종이색으로 번지게 만들어져 있어서 자막은 바탕 없이 그 종이색 위에 먹색 글자로 바로 얹는다.
        // 그림이 아직 없는 임시 상태는 바탕이 어두우므로 그때만 밝은 글자를 쓴다
        private static readonly Color BACK_COLOR  = new Color(0.04f, 0.05f, 0.07f, 1f);

        // 첫 장을 가리는 막 색, 화면 바탕색과 같아서 막이 깔리는 순간에 깜빡이지 않는다
        private static readonly Color INK_COVER_COLOR = BACK_COLOR;
        private static readonly Color TEXT_COLOR       = new Color(0.07f, 0.05f, 0.04f, 1f);
        private static readonly Color HINT_COLOR       = new Color(0.07f, 0.05f, 0.04f, 0.55f);
        private static readonly Color TEXT_ON_DARK     = new Color(0.9f, 0.92f, 0.94f, 1f);
        private static readonly Color HINT_ON_DARK     = new Color(0.9f, 0.92f, 0.94f, 0.6f);
        private static readonly Color CLEAR_COLOR      = new Color(1f, 1f, 1f, 0f);
        private static readonly Color SHADE_COLOR      = new Color(0.94f, 0.89f, 0.78f, 0.9f);
        private static readonly Color32 OUTLINE_COLOR  = new Color32(0, 0, 0, 255);

        private static readonly int PROGRESS_ID = Shader.PropertyToID("_Progress");
        private static readonly int SEED_ID     = Shader.PropertyToID("_Seed");
        private static readonly int CENTER_ID   = Shader.PropertyToID("_Center");

        // 장이 바뀔 때 이전 그림을 위에 한 장 더 올려 먹이 번지며 지워지게 한다. 아래의 새 그림이 그 자리에서 드러난다
        private Image           _illustration;
        private Image           _previous;
        private Material        _inkMaterial  = null;
        private Sprite          _coverSprite  = null;
        private Sprite          _shadeSprite  = null;
        private Texture2D       _shadeTexture = null;
        private Coroutine       _inkRoutine   = null;
        private Coroutine       _closeRoutine = null;
        private bool            _closesWithTap = false;
        private bool            _outlined      = false;
        private GameObject      _shadeObject  = null;
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

        /// <summary> 자막을 흰 글자에 검은 테두리로 바꾼다, 시험용이라 켜는 쪽이 따로 부른다. 이미 보이는 장에도 바로 적용한다 </summary>
        public void UseOutlinedSubtitle()
        {
            _outlined = true;

            if (_bodyText == null)
            {
                return;
            }

            _bodyText.outlineWidth = OUTLINE_WIDTH;
            _bodyText.outlineColor = OUTLINE_COLOR;
            _bodyText.color        = Color.white;
        }

        /// <summary> 장이 바뀔 때 쓸 먹 번짐 지우기 재질, 없으면 전환 없이 바로 바뀐다. 에셋을 고치지 않게 복사본을 만들어 쓴다 </summary>
        public void SetTransitionMaterial(Material source)
        {
            if (_inkMaterial != null)
            {
                Destroy(_inkMaterial);
                _inkMaterial = null;
            }

            if (source != null)
            {
                _inkMaterial = new Material(source);
            }
        }

        /// <summary> 장들을 처음부터 보인다. startRevealed는 글을 처음부터 전부 보이고, allowSkip이 거짓이면 건너뛰기 버튼을 숨기며, skipLabel은 구간에 맞는 버튼 글자다 </summary>
        public void Show(IReadOnlyList<IllustrationPage> pages, bool startRevealed, bool allowSkip, string skipLabel = null)
        {
            if ((pages == null) || (pages.Count == 0))
            {
                throw new ArgumentException("pages is empty", nameof(pages));
            }

            if (_bodyText == null)
            {
                return;
            }

            // 방금 Close로 내려가는 중이었다면 취소한다. 안 그러면 새로 띄운 화면이 끝에 가서 꺼진다
            CancelPendingClose();

            _pages          = pages;
            _sequence       = new IllustrationSequence(pages.Count, startRevealed);
            _completed      = false;
            _closesWithTap  = (startRevealed) && (pages.Count == 1);

            _skipText.SetText(string.IsNullOrEmpty(skipLabel) ? SKIP_LABEL : skipLabel);
            _skipButton.SetActive(allowSkip);
            gameObject.SetActive(true);
            CompleteInk();

            // 이전 구간의 마지막 그림이 남아 있으면 첫 장이 그 그림에서 이어져 보이므로 비워 둔다. 첫 장은 먹색 막이 걷히며 나타난다
            _illustration.sprite = null;
            ShowPage();
        }

        /// <summary> 화면 클릭 한 번, 키보드나 패드 입력도 이 메서드를 부르면 같은 흐름이다 </summary>
        public void HandleClick()
        {
            if ((_sequence == null) || (_completed))
            {
                return;
            }

            // 지워지는 중에 누르면 전환을 바로 끝낸다. 클릭을 삼키지는 않고 그대로 다음 동작으로 이어 간다
            CompleteInk();

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
            CompleteInk();

            if (!gameObject.activeInHierarchy)
            {
                return;
            }

            // 화면을 닫는 클릭이 같은 프레임에 이동이나 공격 입력으로 읽히지 않게, 한 프레임 동안은 입력을 막은 채 둔다.
            // 입력 쪽이 "포인터가 UI 위"를 보고 전투 입력을 거르기 때문이다. 이 완화가 실제로 누수를 막는지는 확인하지 못했다
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
            BuildCanvas();
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_inkMaterial != null)
            {
                Destroy(_inkMaterial);
                _inkMaterial = null;
            }

            if (_coverSprite != null)
            {
                Destroy(_coverSprite);
                _coverSprite = null;
            }

            if (_shadeSprite != null)
            {
                Destroy(_shadeSprite);
                _shadeSprite = null;
            }

            if (_shadeTexture != null)
            {
                Destroy(_shadeTexture);
                _shadeTexture = null;
            }
        }

        private void Complete()
        {
            StopTyping();
            CompleteInk();
            _completed = true;
            Finished?.Invoke();
        }

        // 이전 그림이 먹처럼 지워지며 새 그림이 드러난다. 이전 그림이 없는 첫 장은 먹색 막이 걷히며 나타난다.
        // 그림이 없거나 재질이 없으면 바로 바뀐다
        private void ShowPage()
        {
            IllustrationPage page = _pages[_sequence.PageIndex];

            Sprite old = _illustration.sprite;

            _illustration.sprite  = page.Image;
            _illustration.enabled = page.Image != null;

            bool onArt = page.Image != null;
            _bodyText.color = _outlined ? Color.white : (onArt ? TEXT_COLOR : TEXT_ON_DARK);
            _hintText.color = onArt ? HINT_COLOR : HINT_ON_DARK;

            if ((_inkMaterial != null) && (page.Image != null))
            {
                if (old == null)
                {
                    BeginInk(GetCoverSprite(), INK_COVER_COLOR, false);
                }
                else if (old != page.Image)
                {
                    BeginInk(old, Color.white, true);
                }
            }

            StopTyping();
            _bodyText.text = page.Text;

            // 자막과 대사가 없는 장은 종이색 그라데이션도 걷고, 출력할 글이 없으니 글 완성 단계 없이 첫 클릭으로 다음으로 넘어간다
            bool hasText = !string.IsNullOrEmpty(page.Text);
            _shadeObject.SetActive(hasText);
            if (!hasText)
            {
                _sequence.OnTextCompleted();
            }

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

        // 아래가 불투명하고 위로 갈수록 투명해지는 세로 그라데이션, 색은 Image 색이 입힌다
        private Sprite GetShadeSprite()
        {
            if (_shadeSprite == null)
            {
                _shadeTexture = new Texture2D(1, SHADE_STEPS, TextureFormat.RGBA32, false)
                {
                    wrapMode   = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags  = HideFlags.HideAndDontSave
                };

                // 텍스처의 0번 줄이 스프라이트의 맨 아래다. 아래가 불투명하고 위로 갈수록 투명해야 한다
                for (int y = 0; y < SHADE_STEPS; ++y)
                {
                    float t     = y / (float)(SHADE_STEPS - 1);
                    float alpha = Mathf.SmoothStep(1f, 0f, t);
                    _shadeTexture.SetPixel(0, y, new Color(1f, 1f, 1f, alpha));
                }

                _shadeTexture.Apply();
                _shadeSprite = Sprite.Create(_shadeTexture, new Rect(0f, 0f, 1f, SHADE_STEPS), new Vector2(HALF, HALF));
            }

            return _shadeSprite;
        }

        // 흰 한 점짜리 스프라이트를 한 번만 만든다, 첫 장의 먹색 막은 이것에 색을 입혀 쓴다
        private Sprite GetCoverSprite()
        {
            if (_coverSprite == null)
            {
                Texture2D white = Texture2D.whiteTexture;
                _coverSprite = Sprite.Create(white, new Rect(0f, 0f, white.width, white.height), new Vector2(HALF, HALF));
            }

            return _coverSprite;
        }

        // keepAspect는 이전 그림이 새 그림과 같은 비율로 놓여야 할 때 참이다. 먹색 막은 화면 전체를 덮어야 하므로 거짓이다
        private void BeginInk(Sprite old, Color tint, bool keepAspect)
        {
            _previous.sprite         = old;
            _previous.preserveAspect = keepAspect;
            _previous.color          = tint;
            _previous.material = _inkMaterial;
            _previous.enabled  = true;

            // 매번 같은 모양이 되지 않게 무늬와 시작 지점을 바꾼다
            _inkMaterial.SetFloat(SEED_ID, UnityEngine.Random.Range(0f, INK_SEED_RANGE));
            _inkMaterial.SetVector(CENTER_ID, new Vector4(
                UnityEngine.Random.Range(INK_CENTER_MIN, INK_CENTER_MAX), UnityEngine.Random.Range(INK_CENTER_MIN, INK_CENTER_MAX), 0f, 0f));
            ApplyInkProgress(0f);

            _inkRoutine = StartCoroutine(CoInk());
        }

        // 일시정지 중에도 흘러야 하므로 실제 시간으로 센다
        private IEnumerator CoInk()
        {
            float elapsed = 0f;
            while (elapsed < INK_SECONDS)
            {
                elapsed += Time.unscaledDeltaTime;
                ApplyInkProgress(Mathf.Clamp01(elapsed / INK_SECONDS));
                yield return null;
            }

            _inkRoutine = null;
            _previous.enabled = false;
        }

        /// <summary> 진행 중인 전환을 바로 끝내고 이전 그림을 치운다 </summary>
        private void CompleteInk()
        {
            if (_inkRoutine != null)
            {
                StopCoroutine(_inkRoutine);
                _inkRoutine = null;
            }

            if (_previous != null)
            {
                _previous.enabled = false;
            }
        }

        private void ApplyInkProgress(float progress)
        {
            _inkMaterial.SetFloat(PROGRESS_ID, progress);

            // 이미지는 재질 값이 바뀐 것을 자동으로 다시 그리지 않을 수 있어 그래픽을 갱신 대상으로 표시한다
            _previous.SetMaterialDirty();
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
            // 부적 조사처럼 글이 처음부터 전부 보이는 한 장짜리는 다음 장이 없으니 닫는다고 안내한다
            string done = _closesWithTap ? HINT_CLOSE : HINT_NEXT;
            _hintText.SetText(_sequence.IsTyping ? HINT_REVEAL : done);
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

            // 이전 그림이 지워지는 층, 평소에는 꺼 둔다
            _previous = PrologueUi.CreateImage("InkPrevious", transform, Color.white, false).GetComponent<Image>();
            _previous.preserveAspect = true;
            _previous.enabled        = false;
            PrologueUi.Stretch(_previous.rectTransform);

            // 전체 화면 클릭 영역, 투명하지만 입력은 받는다
            RectTransform clickArea = PrologueUi.CreateImage("ClickArea", transform, CLEAR_COLOR, true);
            PrologueUi.Stretch(clickArea);
            clickArea.gameObject.AddComponent<Button>().onClick.AddListener(HandleClick);

            // 글 뒤에 종이색이 위로 갈수록 투명해지는 그라데이션을 깐다. 경계가 보이는 바탕이 아니라 그림의 종이색 번짐을 이어 주는 것이다
            RectTransform shade = PrologueUi.CreateImage("Shade", transform, SHADE_COLOR, false);
            _shadeObject = shade.gameObject;
            shade.GetComponent<Image>().sprite = GetShadeSprite();
            shade.anchorMin = new Vector2(0f, 0f);
            shade.anchorMax = new Vector2(1f, 0f);
            shade.pivot     = new Vector2(HALF, 0f);
            shade.offsetMin = Vector2.zero;
            shade.offsetMax = new Vector2(0f, PANEL_HEIGHT + SHADE_EXTRA);

            // 글을 놓는 자리일 뿐 바탕은 그리지 않는다
            RectTransform panel = PrologueUi.CreateImage("Panel", transform, CLEAR_COLOR, false);
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

            // 건너뛰기 단추는 다른 창과 같은 단추 스타일이다
            _skipText = PrologueUi.CreateButton("Skip", transform, SKIP_FONT_SIZE, false, HandleSkip, out RectTransform skip);
            skip.anchorMin        = new Vector2(1f, 1f);
            skip.anchorMax        = new Vector2(1f, 1f);
            skip.pivot            = new Vector2(1f, 1f);
            skip.anchoredPosition = new Vector2(-SKIP_MARGIN, -SKIP_MARGIN);
            skip.sizeDelta        = new Vector2(SKIP_WIDTH, SKIP_HEIGHT);
            _skipButton           = skip.gameObject;
            _skipText.SetText(SKIP_LABEL);
        }

    }
}
