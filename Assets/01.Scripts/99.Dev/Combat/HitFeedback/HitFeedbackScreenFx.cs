// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Shinshi.Dev
{
    /// <summary> 화면 전체를 덮는 번쩍임과 가장자리 비네트를 잠깐 보여 주는 연출 </summary>
    public sealed class HitFeedbackScreenFx : MonoBehaviour
    {
        // 이 클라이언트 화면에만 그리는 표현이다. 입력을 가로막지 않게 레이캐스트를 끄고, 다른 UI 위에 그리되 아주 짧게만 보인다.
        // 비네트는 가운데를 비워 두어 보스 예고 같은 바닥 표시를 가리지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const int   CANVAS_SORTING_ORDER = 5000;
        private const int   VIGNETTE_TEXTURE_SIZE = 128;
        private const float VIGNETTE_CLEAR_RADIUS = 0.55f; // 이 반지름 안쪽은 투명하다. 가운데 0, 모서리 1 기준
        private const float VIGNETTE_FULL_RADIUS  = 1.1f;  // 이 반지름에서 가장 진하다
        private const float CENTER_OFFSET         = 0.5f;
        private const float DOUBLE                = 2f;

        private Texture2D _vignetteTexture = null;
        private RawImage  _flash           = null;
        private RawImage  _vignette        = null;
        private Color     _vignetteColor   = Color.white;
        private float     _flashAlpha      = 0f;
        private float     _flashSeconds    = 1f;
        private float     _flashLeft       = 0f;
        private float     _vignetteAlpha   = 0f;
        private float     _vignetteSeconds = 1f;
        private float     _vignetteLeft    = 0f;

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

            _vignetteTexture = CreateVignetteTexture();
            _flash           = CreateLayer(canvasObject.transform, "Flash", Texture2D.whiteTexture);
            _vignette        = CreateLayer(canvasObject.transform, "Vignette", _vignetteTexture);
        }

        private void OnDestroy()
        {
            if (_vignetteTexture != null)
            {
                Destroy(_vignetteTexture);
            }
        }

        private void Update()
        {
            Fade(_flash, Color.white, _flashAlpha, _flashSeconds, ref _flashLeft);
            Fade(_vignette, _vignetteColor, _vignetteAlpha, _vignetteSeconds, ref _vignetteLeft);
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

        /// <summary> 화면 가장자리가 color 색으로 alpha 세기만큼 물들었다가 seconds 동안 사라진다 </summary>
        public void Vignette(Color color, float alpha, float seconds)
        {
            if (alpha <= 0f)
            {
                return;
            }

            _vignetteColor   = color;
            _vignetteAlpha   = Mathf.Max(alpha, _vignetteAlpha * Remain(_vignetteLeft, _vignetteSeconds));
            _vignetteSeconds = seconds;
            _vignetteLeft    = seconds;
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

        // 가운데는 투명하고 가장자리로 갈수록 진해지는 텍스처
        private static Texture2D CreateVignetteTexture()
        {
            var texture = new Texture2D(VIGNETTE_TEXTURE_SIZE, VIGNETTE_TEXTURE_SIZE, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp
            };

            for (int y = 0; y < VIGNETTE_TEXTURE_SIZE; ++y)
            {
                for (int x = 0; x < VIGNETTE_TEXTURE_SIZE; ++x)
                {
                    float nx = (((float)x / (VIGNETTE_TEXTURE_SIZE - 1)) - CENTER_OFFSET) * DOUBLE;
                    float ny = (((float)y / (VIGNETTE_TEXTURE_SIZE - 1)) - CENTER_OFFSET) * DOUBLE;
                    float radius = Mathf.Sqrt((nx * nx) + (ny * ny));
                    float alpha  = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(VIGNETTE_CLEAR_RADIUS, VIGNETTE_FULL_RADIUS, radius));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }
    }
}
