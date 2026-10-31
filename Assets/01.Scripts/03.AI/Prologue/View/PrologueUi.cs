// Unity
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.UI;

namespace Shinshi.AI
{
    /// <summary> 프롤로그 화면들이 같이 쓰는 코드 생성 도우미, 프리팹 없이 캔버스와 이미지와 글자를 만든다 </summary>
    public static class PrologueUi
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float REFERENCE_WIDTH    = 1920f;
        private const float REFERENCE_HEIGHT   = 1080f;
        private const float WIDTH_HEIGHT_MATCH = 0.5f;

        // 기존 창(ModernDarkKit)과 같은 치수다
        private const float CORNER_SIZE        = 24f;
        private const float CORNER_INSET       = 4f;
        private const float TITLE_DIAMOND_GAP  = 28f;
        private const float TITLE_LINE_DROP    = 32f;
        private const float TITLE_LINE_INSET   = 24f;
        private const float TITLE_LINE_HEIGHT  = 1f;
        private const float TITLE_LINE_ALPHA   = 0.5f;

        private static readonly Color TITLE_LINE_COLOR = new Color(UiModernDark.GoldDim.r, UiModernDark.GoldDim.g, UiModernDark.GoldDim.b, TITLE_LINE_ALPHA);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 전체 화면 오버레이 캔버스를 붙인다, blocksInput이 참이면 그래픽 레이캐스터를 붙여 UI 클릭을 받는다 </summary>
        public static void AddCanvas(GameObject root, int sortingOrder, bool blocksInput)
        {
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            if (blocksInput)
            {
                root.AddComponent<GraphicRaycaster>();
            }

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
            scaler.matchWidthOrHeight  = WIDTH_HEIGHT_MATCH;
        }

        /// <summary> 입력을 받으려면 씬에 EventSystem이 있어야 한다. 없으면 parent와 함께 사라지는 것을 만든다 </summary>
        public static void EnsureEventSystem(Transform parent)
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystem.transform.SetParent(parent, false);
        }

        public static RectTransform CreateImage(string objectName, Transform parent, Color color, bool blocksInput)
        {
            var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer));
            imageObject.transform.SetParent(parent, false);

            var image = imageObject.AddComponent<Image>();
            image.color         = color;
            image.raycastTarget = blocksInput;
            return image.rectTransform;
        }

        public static TextMeshProUGUI CreateText(string objectName, Transform parent, float fontSize, Color color,
            TextAlignmentOptions alignment)
        {
            var textObject = new GameObject(objectName, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);

            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.fontSize      = fontSize;
            text.color         = color;
            text.alignment     = alignment;
            text.raycastTarget = false;
            return text;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary> 창 바탕을 기존 성장·장비 창과 같은 어두운 스타일로 바꾼다, 바탕 스프라이트와 네 모서리 장식이다 </summary>
        public static void StylePanel(RectTransform panel)
        {
            var image = panel.GetComponent<Image>();
            ModernDarkKit.SetSliced(image, UiModernDark.SPRITE_PANEL, Color.white);

            AddCorner(panel, "CornerTL", new Vector2(0f, 1f), new Vector2(1f, 1f));
            AddCorner(panel, "CornerTR", new Vector2(1f, 1f), new Vector2(-1f, 1f));
            AddCorner(panel, "CornerBL", new Vector2(0f, 0f), new Vector2(1f, -1f));
            AddCorner(panel, "CornerBR", new Vector2(1f, 0f), new Vector2(-1f, -1f));
        }

        /// <summary> 기존 창의 단추 스타일로 단추를 만든다. primary는 청록 강조 단추다 </summary>
        public static TextMeshProUGUI CreateButton(string objectName, Transform parent, float fontSize, bool primary,
            UnityEngine.Events.UnityAction onClick, out RectTransform rect)
        {
            rect = CreateImage(objectName, parent, Color.white, true);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(onClick);

            TextMeshProUGUI label = CreateText("Label", rect, fontSize, UiModernDark.TextMain, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);

            ModernDarkKit.StyleButton(button, primary);
            return label;
        }

        /// <summary> 제목 양옆의 마름모와 아래 구분선을 놓는다, 글자 너비가 정해진 뒤에 부른다 </summary>
        public static void DecorateTitle(RectTransform panel, TextMeshProUGUI title, float titleCenterY)
        {
            float half = (title.preferredWidth * 0.5f) + TITLE_DIAMOND_GAP;
            AddDiamond(panel, "TitleDiamondL", -half, titleCenterY);
            AddDiamond(panel, "TitleDiamondR", half, titleCenterY);

            RectTransform line = CreateImage("TitleLine", panel, TITLE_LINE_COLOR, false);
            line.anchorMin        = new Vector2(0f, 1f);
            line.anchorMax        = new Vector2(1f, 1f);
            line.pivot            = new Vector2(0.5f, 1f);
            line.anchoredPosition = new Vector2(0f, titleCenterY - TITLE_LINE_DROP);
            line.sizeDelta        = new Vector2(-(TITLE_LINE_INSET * 2f), TITLE_LINE_HEIGHT);
        }

        private static void AddCorner(RectTransform panel, string objectName, Vector2 anchor, Vector2 scale)
        {
            RectTransform rect = CreateImage(objectName, panel, Color.white, false);
            var image = rect.GetComponent<Image>();
            image.sprite = UiModernDarkSprites.Get(UiModernDark.SPRITE_CORNER);
            image.type   = Image.Type.Simple;

            float center = CORNER_INSET + (CORNER_SIZE * 0.5f);
            rect.anchorMin        = anchor;
            rect.anchorMax        = anchor;
            rect.pivot            = new Vector2(0.5f, 0.5f);
            rect.sizeDelta        = new Vector2(CORNER_SIZE, CORNER_SIZE);
            rect.anchoredPosition = new Vector2((anchor.x > 0.5f) ? -center : center, (anchor.y > 0.5f) ? -center : center);
            rect.localScale       = new Vector3(scale.x, scale.y, 1f);
        }

        // x는 패널 가로 가운데에서의 거리, y는 위쪽 가장자리에서 내려온 거리다
        private static void AddDiamond(RectTransform panel, string objectName, float x, float centerY)
        {
            RectTransform rect = CreateImage(objectName, panel, Color.white, false);
            var image = rect.GetComponent<Image>();
            image.sprite = UiModernDarkSprites.Get(UiModernDark.SPRITE_DIAMOND);
            image.type   = Image.Type.Simple;
            image.SetNativeSize();

            rect.anchorMin        = new Vector2(0.5f, 1f);
            rect.anchorMax        = new Vector2(0.5f, 1f);
            rect.pivot            = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, centerY);
        }
    }
}
