// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Shinshi.Art
{
    /// <summary> 화면 전체를 덮는 번쩍임을 잠깐 보여 주는 연출 </summary>
    public sealed class HitFeedbackScreenFx : MonoBehaviour
    {
        // 이 클라이언트 화면에만 그리는 표현이다. 입력을 가로막지 않게 레이캐스트를 끄고, 다른 UI 위에 그리되 아주 짧게만 보인다.

        /****************************************
        *                Fields
        ****************************************/

        private const int   CANVAS_SORTING_ORDER = 5000;

        private RawImage  _flash           = null;
        private float     _flashAlpha      = 0f;
        private float     _flashSeconds    = 1f;
        private float     _flashLeft       = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            var canvasObject = new GameObject("DevHitFeedbackScreenFx", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CANVAS_SORTING_ORDER;

            _flash           = CreateLayer(canvasObject.transform, "Flash", Texture2D.whiteTexture);
        }

        private void Update()
        {
            Fade(_flash, Color.white, _flashAlpha, _flashSeconds, ref _flashLeft);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 화면 전체가 alpha 세기로 번쩍였다가 seconds 동안 사라진다 </summary>
        public void Flash(float alpha, float seconds)
        {
            if (alpha <= 0f)
            {
                return;
            }

            _flashAlpha   = Mathf.Max(alpha, _flashAlpha * Remain(_flashLeft, _flashSeconds));
            _flashSeconds = seconds;
            _flashLeft    = seconds;
        }

        private static float Remain(float left, float seconds)
        {
            return (left > 0f) ? (left / seconds) : 0f;
        }

        // 처음에 가장 진하고 갈수록 빠르게 옅어진다
        private static void Fade(RawImage image, Color baseColor, float alpha, float seconds, ref float left)
        {
            if (left <= 0f)
            {
                return;
            }

            left = Mathf.Max(0f, left - Time.unscaledDeltaTime);

            float remain = left / seconds;
            Color color  = baseColor;
            color.a      = alpha * remain * remain;
            image.color  = color;
        }

        private static RawImage CreateLayer(Transform parent, string layerName, Texture texture)
        {
            var go = new GameObject(layerName, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.AddComponent<RawImage>();
            image.texture       = texture;
            image.raycastTarget = false;
            image.color         = Color.clear;
            return image;
        }
    }
}
