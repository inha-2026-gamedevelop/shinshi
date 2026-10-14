// System
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 부팅 프리워밍 동안 화면을 가리고 진행률과 단계 문구를 보인다, 프리팹 없이 코드로 만든다 </summary>
    public sealed class LoadingOverlayView : ClientView
    {
        // 오버레이는 DontDestroyOnLoad로 살려 둔다. 로비로 넘기는 LoadScene은 이 프레임이 끝난 뒤에 실행되므로
        // 콜백 안에서 곧바로 내리면 새 씬의 첫 화면 전에 가림막이 사라져 빈 화면이 번쩍인다.
        // 글꼴은 NetworkOverlayView와 같이 OS 한글 글꼴로 만든 이 표시 전용 TMP 글꼴을 쓴다

        /****************************************
        *                Fields
        ****************************************/

        private const int    SORTING_ORDER        = 32000;
        private const float  REFERENCE_WIDTH      = 1920f;
        private const float  REFERENCE_HEIGHT     = 1080f;
        private const float  WIDTH_HEIGHT_MATCH   = 0.5f;
        private const float  BAR_WIDTH            = 720f;
        private const float  BAR_HEIGHT           = 14f;
        private const float  BAR_BOTTOM_OFFSET    = 140f;
        private const float  TEXT_HEIGHT          = 48f;
        private const float  TEXT_GAP             = 12f;
        private const float  STAGE_FONT_SIZE      = 28f;
        private const int    RENDERED_FRAMES      = 2;
        private const float  HIDE_TIMEOUT_SECONDS = 10f;
        private const float  PERCENT_SCALE        = 100f;
        private const string OVERLAY_OBJECT_NAME  = "LoadingOverlay";

        private static readonly string[] KOREAN_FONT_FAMILIES = { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR" };

        private static readonly Color BACK_COLOR  = new Color(0.04f, 0.05f, 0.07f, 1f);
        private static readonly Color TRACK_COLOR = new Color(1f, 1f, 1f, 0.15f);
        private static readonly Color FILL_COLOR  = new Color(0.95f, 0.78f, 0.35f, 1f);

        private RectTransform   _fillRect;
        private TextMeshProUGUI _stageText;
        private TMP_FontAsset   _ownedFont;
        private string          _stage       = null;
        private int             _percent     = -1;
        private bool            _sceneLoaded = false;
        private bool            _isHiding    = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            DestroyOwnedFont();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 코드로 계층을 만든 오버레이를 돌려준다, 서버 빌드에서는 객체만 만들어지고 아무것도 그리지 않는다 </summary>
        public static LoadingOverlayView Create()
        {
            var root = new GameObject(OVERLAY_OBJECT_NAME);
            return root.AddComponent<LoadingOverlayView>();
        }

        /// <summary> 화면을 가리고 단계 문구를 보인다, 이때부터 씬 전환에도 살아남는다 </summary>
        public void Show(string stage)
        {
            if (_fillRect == null)
            {
                return;
            }

            DontDestroyOnLoad(gameObject);
            gameObject.SetActive(true);
            SetProgress(0f, stage);
        }

        /// <summary> 진행 막대와 문구를 갱신한다, 값이 바뀔 때만 다시 쓴다 </summary>
        public void SetProgress(float progress, string stage)
        {
            if (_fillRect == null)
            {
                return;
            }

            float clamped = Mathf.Clamp01(progress);
            int percent = Mathf.RoundToInt(clamped * PERCENT_SCALE);
            if (percent != _percent)
            {
                _percent = percent;
                _fillRect.anchorMax = new Vector2(clamped, 1f);
            }

            if (!string.Equals(stage, _stage))
            {
                _stage = stage;
                _stageText.SetText(stage);
            }
        }

        /// <summary> 다음 씬이 로드되고 렌더 프레임 두 개가 지나면 내린다, 씬 전환이 없어도 제한 시간 뒤에는 내린다 </summary>
        public void HideAfterNextSceneRendered()
        {
            if (_isHiding)
            {
                return;
            }

            _isHiding    = true;
            _sceneLoaded = false;
            SceneManager.sceneLoaded += OnSceneLoaded;
            StartCoroutine(CoHideAfterScene());
        }

        /// <summary> 바로 내린다 </summary>
        public void Hide()
        {
            Destroy(gameObject);
        }

        protected override void OnViewAwake()
        {
            BuildCanvas(CreateSystemKoreanFont(out _ownedFont));
        }

        // 직접 만든 글꼴만 파괴한다, TMP 기본 글꼴 폴백은 프로젝트 공용이라 건드리지 않는다
        private void DestroyOwnedFont()
        {
            if (_ownedFont == null)
            {
                return;
            }

            if (_ownedFont.atlasTextures != null)
            {
                for (int i = 0; i < _ownedFont.atlasTextures.Length; ++i)
                {
                    if (_ownedFont.atlasTextures[i] != null)
                    {
                        Destroy(_ownedFont.atlasTextures[i]);
                    }
                }
            }

            if (_ownedFont.material != null)
            {
                Destroy(_ownedFont.material);
            }

            Destroy(_ownedFont);
            _ownedFont = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _sceneLoaded = true;
        }

        private IEnumerator CoHideAfterScene()
        {
            float deadline = Time.realtimeSinceStartup + HIDE_TIMEOUT_SECONDS;
            while ((!_sceneLoaded) && (Time.realtimeSinceStartup < deadline))
            {
                yield return null;
            }

            for (int i = 0; i < RENDERED_FRAMES; ++i)
            {
                yield return null;
            }

            Hide();
        }

        // ownedFont는 이 오버레이가 만들어 파괴할 책임이 있는 글꼴이다, 기본 글꼴 폴백이면 null
        private static TMP_FontAsset CreateSystemKoreanFont(out TMP_FontAsset ownedFont)
        {
            ownedFont = null;
            for (int i = 0; i < KOREAN_FONT_FAMILIES.Length; ++i)
            {
                TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(KOREAN_FONT_FAMILIES[i], "Regular");
                if (font != null)
                {
                    ownedFont = font;
                    return font;
                }
            }

            return TMP_Settings.defaultFontAsset;
        }

        private void BuildCanvas(TMP_FontAsset font)
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SORTING_ORDER;

            // 입력 차단은 가림막 이미지의 raycastTarget과 이 레이캐스터가 함께 맡는다. EventSystem이 없는 씬에서는 차단할 UI 입력도 없다
            gameObject.AddComponent<GraphicRaycaster>();

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
            scaler.matchWidthOrHeight  = WIDTH_HEIGHT_MATCH;

            RectTransform back = CreateImage("Back", transform, BACK_COLOR, true);
            back.anchorMin = Vector2.zero;
            back.anchorMax = Vector2.one;
            back.offsetMin = Vector2.zero;
            back.offsetMax = Vector2.zero;

            RectTransform track = CreateImage("Track", transform, TRACK_COLOR, false);
            track.anchorMin        = new Vector2(0.5f, 0f);
            track.anchorMax        = new Vector2(0.5f, 0f);
            track.pivot            = new Vector2(0.5f, 0f);
            track.anchoredPosition = new Vector2(0f, BAR_BOTTOM_OFFSET);
            track.sizeDelta        = new Vector2(BAR_WIDTH, BAR_HEIGHT);

            _fillRect = CreateImage("Fill", track, FILL_COLOR, false);
            _fillRect.anchorMin = Vector2.zero;
            _fillRect.anchorMax = new Vector2(0f, 1f);
            _fillRect.offsetMin = Vector2.zero;
            _fillRect.offsetMax = Vector2.zero;

            var textObject = new GameObject("Stage", typeof(RectTransform));
            textObject.transform.SetParent(transform, false);
            _stageText = textObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                _stageText.font = font;
            }

            _stageText.fontSize         = STAGE_FONT_SIZE;
            _stageText.alignment        = TextAlignmentOptions.Center;
            _stageText.textWrappingMode = TextWrappingModes.NoWrap;
            _stageText.raycastTarget    = false;

            RectTransform textRect = _stageText.rectTransform;
            textRect.anchorMin        = new Vector2(0.5f, 0f);
            textRect.anchorMax        = new Vector2(0.5f, 0f);
            textRect.pivot            = new Vector2(0.5f, 0f);
            textRect.anchoredPosition = new Vector2(0f, BAR_BOTTOM_OFFSET + BAR_HEIGHT + TEXT_GAP);
            textRect.sizeDelta        = new Vector2(BAR_WIDTH, TEXT_HEIGHT);
        }

        private static RectTransform CreateImage(string objectName, Transform parent, Color color, bool blocksInput)
        {
            var imageObject = new GameObject(objectName, typeof(RectTransform));
            imageObject.transform.SetParent(parent, false);
            var image = imageObject.AddComponent<Image>();
            image.color         = color;
            image.raycastTarget = blocksInput;
            return (RectTransform)imageObject.transform;
        }
    }
}
