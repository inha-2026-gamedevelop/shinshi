// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.AI
{
    /// <summary> 성장 메뉴 위에 뜨는 안내 화면, 강조할 대상만 밝게 두고 나머지를 어둡게 덮고 설명과 다음, 건너뛰기를 보인다 </summary>
    public sealed class MenuGuideView : ClientView
    {
        // 메뉴 창의 오브젝트를 직접 고치지 않는다. 대상의 화면 위치만 읽어 그 둘레를 어둡게 덮는다.
        // 대상 이름을 못 찾거나 비어 있으면 강조 없이 가운데에 설명만 보인다. 성장 실행은 막지도 시키지도 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const int    SORTING_ORDER   = 31200;
        private const float  HALF            = 0.5f;
        private const float  FRAME_THICKNESS = 4f;
        private const float  FRAME_PADDING   = 8f;
        private const float  PANEL_WIDTH     = 620f;
        private const float  PANEL_HEIGHT    = 260f;
        private const float  PANEL_GAP       = 24f;
        private const float  TEXT_MARGIN     = 36f;
        private const float  TEXT_SIZE       = 30f;
        private const float  COUNTER_SIZE    = 22f;
        private const float  BUTTON_SIZE     = 26f;
        private const float  BUTTON_WIDTH    = 200f;
        private const float  BUTTON_HEIGHT   = 64f;
        private const float  BUTTON_MARGIN   = 28f;
        private const float  COUNTER_HEIGHT  = 34f;
        private const float  COUNTER_TOP     = 12f;
        private const string ROOT_NAME       = "MenuGuide";
        private const string NEXT_LABEL      = "다음";
        private const string DONE_LABEL      = "완료";
        private const string SKIP_LABEL      = "건너뛰기";
        private const string COUNTER_FORMAT  = "{0} / {1}";

        // 기존 창과 같은 색을 쓴다. 강조 테두리는 기존 창의 선택 강조색(청록)이다
        private static readonly Color DIM_COLOR   = new Color(0f, 0f, 0f, 0.7f);
        private static readonly Color FRAME_COLOR = UiModernDark.Cyan;

        // 위, 아래, 왼쪽, 오른쪽 어둠막과 강조 테두리 네 변
        private readonly RectTransform[] _dims   = new RectTransform[4];
        private readonly RectTransform[] _frames = new RectTransform[4];

        private RectTransform   _panel       = null;
        private TextMeshProUGUI _bodyText    = null;
        private TextMeshProUGUI _counterText = null;
        private TextMeshProUGUI _nextText    = null;
        private TextMeshProUGUI _skipText    = null;

        private IReadOnlyList<MenuGuideStepData> _steps    = null;
        private Func<string, RectTransform>      _resolver = null;
        private MenuGuideSession                 _session  = null;
        private bool                             _reported = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsShowing => (_session != null) && (!_session.IsFinished);
        public int  StepIndex => (_session != null) ? _session.Index : 0;

        // 안내가 끝났을 때 한 번 불린다. 인자는 건너뛰었는지다. 도중에 Hide하면 불리지 않는다
        public event Action<bool> Finished;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 코드로 계층을 만든 화면을 돌려준다, 서버 빌드에서는 객체만 만들어지고 아무것도 그리지 않는다 </summary>
        public static MenuGuideView Create()
        {
            var root = new GameObject(ROOT_NAME);
            return root.AddComponent<MenuGuideView>();
        }

        public void SetFont(TMP_FontAsset font)
        {
            if ((font == null) || (_bodyText == null))
            {
                return;
            }

            _bodyText.font    = font;
            _counterText.font = font;
            _nextText.font    = font;
            _skipText.font    = font;
        }

        /// <summary> 안내를 처음 단계부터 보인다, resolver는 대상 이름으로 화면 위 오브젝트를 돌려주고 못 찾으면 null이다 </summary>
        public void Show(IReadOnlyList<MenuGuideStepData> steps, Func<string, RectTransform> resolver)
        {
            if ((steps == null) || (steps.Count == 0))
            {
                throw new ArgumentException("steps is empty", nameof(steps));
            }

            if (_bodyText == null)
            {
                return;
            }

            _steps    = steps;
            _resolver = resolver;
            _session  = new MenuGuideSession(steps.Count);
            _reported = false;

            gameObject.SetActive(true);
            ShowStep();
        }

        /// <summary> 다음 버튼, 키보드나 패드 입력도 이 메서드를 부르면 같은 흐름이다 </summary>
        public void HandleNext()
        {
            if ((_session == null) || (_session.IsFinished))
            {
                return;
            }

            _session.Next();
            if (_session.IsFinished)
            {
                Report();
                return;
            }

            ShowStep();
        }

        public void HandleSkip()
        {
            if ((_session == null) || (_session.IsFinished))
            {
                return;
            }

            _session.Skip();
            Report();
        }

        /// <summary> 안내를 끝내지 않고 내린다, 완료로 치지 않아 다음 진입 때 다시 안내한다 </summary>
        public void Abort()
        {
            _session = null;
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

            // 어둠막 네 장이 뒤 클릭을 막고, 강조 영역만 비워 두어 대상은 그대로 눌린다
            for (int i = 0; i < _dims.Length; ++i)
            {
                _dims[i] = PrologueUi.CreateImage("Dim" + i, transform, DIM_COLOR, true);
            }

            for (int i = 0; i < _frames.Length; ++i)
            {
                _frames[i] = PrologueUi.CreateImage("Frame" + i, transform, FRAME_COLOR, false);
            }

            _panel = PrologueUi.CreateImage("Panel", transform, Color.white, true);
            _panel.anchorMin = new Vector2(0f, 0f);
            _panel.anchorMax = new Vector2(0f, 0f);
            _panel.pivot     = new Vector2(HALF, HALF);
            _panel.sizeDelta = new Vector2(PANEL_WIDTH, PANEL_HEIGHT);
            PrologueUi.StylePanel(_panel);

            _bodyText = PrologueUi.CreateText("Body", _panel, TEXT_SIZE, UiModernDark.TextMain, TextAlignmentOptions.TopLeft);
            _bodyText.textWrappingMode = TextWrappingModes.Normal;
            _bodyText.rectTransform.anchorMin = Vector2.zero;
            _bodyText.rectTransform.anchorMax = Vector2.one;
            _bodyText.rectTransform.offsetMin = new Vector2(TEXT_MARGIN, BUTTON_HEIGHT + (BUTTON_MARGIN * 2f));
            _bodyText.rectTransform.offsetMax = new Vector2(-TEXT_MARGIN, -(COUNTER_HEIGHT + COUNTER_TOP));

            _counterText = PrologueUi.CreateText("Counter", _panel, COUNTER_SIZE, UiModernDark.TextMuted, TextAlignmentOptions.TopRight);
            _counterText.rectTransform.anchorMin = new Vector2(0f, 1f);
            _counterText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _counterText.rectTransform.pivot     = new Vector2(HALF, 1f);
            _counterText.rectTransform.offsetMin = new Vector2(TEXT_MARGIN, -(COUNTER_HEIGHT + COUNTER_TOP));
            _counterText.rectTransform.offsetMax = new Vector2(-TEXT_MARGIN, -COUNTER_TOP);

            _nextText = MakeButton("NextButton", 1f, -BUTTON_MARGIN, true, HandleNext);
            _skipText = MakeButton("SkipButton", 0f, BUTTON_MARGIN, false, HandleSkip);
            _skipText.SetText(SKIP_LABEL);

            gameObject.SetActive(false);
        }

        private void Report()
        {
            if (_reported)
            {
                return;
            }

            _reported = true;
            gameObject.SetActive(false);
            Finished?.Invoke(_session.WasSkipped);
        }

        private void ShowStep()
        {
            MenuGuideStepData step = _steps[_session.Index];

            _bodyText.SetText(step.Text);
            _counterText.SetText(string.Format(COUNTER_FORMAT, _session.Index + 1, _session.StepCount));
            _nextText.SetText(_session.IsLastStep ? DONE_LABEL : NEXT_LABEL);

            RectTransform target = ((_resolver != null) && (!string.IsNullOrEmpty(step.TargetKey))) ? _resolver(step.TargetKey) : null;
            if ((target != null) && (TryGetScreenRect(target, out Rect screen)))
            {
                ApplyHighlight(screen);
                PlacePanelNear(screen);
                return;
            }

            ApplyFullDim();
            PlacePanelCenter();
        }

        // 대상의 화면 좌표 사각형, 캔버스 종류와 상관없이 픽셀 값을 얻는다
        private static bool TryGetScreenRect(RectTransform target, out Rect screen)
        {
            screen = default;
            if ((target == null) || (!target.gameObject.activeInHierarchy))
            {
                return false;
            }

            Canvas canvas = target.GetComponentInParent<Canvas>();
            Camera camera = ((canvas != null) && (canvas.renderMode != RenderMode.ScreenSpaceOverlay)) ? canvas.worldCamera : null;

            var corners = new Vector3[4];
            target.GetWorldCorners(corners);

            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            screen = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
            return (screen.width > 0f) && (screen.height > 0f);
        }

        // 화면 전체를 어둡게 덮는다, 강조 대상이 없을 때다
        private void ApplyFullDim()
        {
            SetNormalized(_dims[0], 0f, 0f, 1f, 1f);
            for (int i = 1; i < _dims.Length; ++i)
            {
                SetNormalized(_dims[i], 0f, 0f, 0f, 0f);
            }

            for (int i = 0; i < _frames.Length; ++i)
            {
                SetNormalized(_frames[i], 0f, 0f, 0f, 0f);
            }
        }

        private void ApplyHighlight(Rect screen)
        {
            float w = Screen.width;
            float h = Screen.height;

            float x0 = Mathf.Clamp01((screen.xMin - FRAME_PADDING) / w);
            float x1 = Mathf.Clamp01((screen.xMax + FRAME_PADDING) / w);
            float y0 = Mathf.Clamp01((screen.yMin - FRAME_PADDING) / h);
            float y1 = Mathf.Clamp01((screen.yMax + FRAME_PADDING) / h);

            SetNormalized(_dims[0], 0f, y1, 1f, 1f);   // 위
            SetNormalized(_dims[1], 0f, 0f, 1f, y0);   // 아래
            SetNormalized(_dims[2], 0f, y0, x0, y1);   // 왼쪽
            SetNormalized(_dims[3], x1, y0, 1f, y1);   // 오른쪽

            float tx = FRAME_THICKNESS / w;
            float ty = FRAME_THICKNESS / h;
            SetNormalized(_frames[0], x0, y1 - ty, x1, y1);
            SetNormalized(_frames[1], x0, y0, x1, y0 + ty);
            SetNormalized(_frames[2], x0, y0, x0 + tx, y1);
            SetNormalized(_frames[3], x1 - tx, y0, x1, y1);
        }

        // 대상 아래에 두고, 아래 공간이 모자라면 위에 둔다. 화면 밖으로 나가지 않게 가둔다
        private void PlacePanelNear(Rect screen)
        {
            float scale  = ScreenToCanvasScale();
            float width  = PANEL_WIDTH  * scale;
            float height = PANEL_HEIGHT * scale;
            float gap    = PANEL_GAP    * scale;

            float centerX = Mathf.Clamp(screen.center.x, width * HALF, Screen.width - (width * HALF));
            float below   = screen.yMin - gap - (height * HALF);
            float above   = screen.yMax + gap + (height * HALF);
            float centerY = (below - (height * HALF) >= 0f) ? below : above;
            centerY       = Mathf.Clamp(centerY, height * HALF, Screen.height - (height * HALF));

            SetPanelCenter(centerX, centerY);
        }

        private void PlacePanelCenter()
        {
            SetPanelCenter(Screen.width * HALF, Screen.height * HALF);
        }

        private void SetPanelCenter(float x, float y)
        {
            // 패널의 앵커는 화면 왼쪽 아래 한 점이라 위치는 캔버스 단위로 환산해서 넣는다
            float inverse = 1f / ScreenToCanvasScale();
            _panel.anchoredPosition = new Vector2(x * inverse, y * inverse);
        }

        // 캔버스 단위 하나가 화면에서 몇 픽셀인지, 기준 해상도와 실제 해상도의 비율이다
        private float ScreenToCanvasScale()
        {
            var canvas = GetComponent<Canvas>();
            return ((canvas != null) && (canvas.scaleFactor > 0f)) ? canvas.scaleFactor : 1f;
        }

        private static void SetNormalized(RectTransform rect, float x0, float y0, float x1, float y1)
        {
            rect.anchorMin = new Vector2(x0, y0);
            rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private TextMeshProUGUI MakeButton(string objectName, float anchorX, float offsetX, bool primary,
            UnityEngine.Events.UnityAction onClick)
        {
            TextMeshProUGUI text = PrologueUi.CreateButton(objectName, _panel, BUTTON_SIZE, primary, onClick, out RectTransform rect);
            rect.anchorMin        = new Vector2(anchorX, 0f);
            rect.anchorMax        = new Vector2(anchorX, 0f);
            rect.pivot            = new Vector2(anchorX, 0f);
            rect.anchoredPosition = new Vector2(offsetX, BUTTON_MARGIN);
            rect.sizeDelta        = new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT);
            return text;
        }
    }
}
