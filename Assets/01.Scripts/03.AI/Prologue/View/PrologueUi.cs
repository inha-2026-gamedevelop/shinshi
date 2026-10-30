// Unity
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 서드파티
using TMPro;

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
    }
}
