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
        private const int BLOOD_SLOTS = 4;
        private const float BLOOD_SPLASH_SECONDS = 0.16f;
        private const string LENS_BLOOD_TEXTURE = "DevHitBlood/T_LensBloodDroplets";

        private RawImage  _flash           = null;
        private float     _flashAlpha      = 0f;
        private float     _flashSeconds    = 1f;
        private float     _flashLeft       = 0f;
        private readonly UnityEngine.UI.RawImage[] _blood = new UnityEngine.UI.RawImage[BLOOD_SLOTS];
        private readonly float[] _bloodLeft = new float[BLOOD_SLOTS];
        private readonly Vector2[] _bloodStart = new Vector2[BLOOD_SLOTS];
        private readonly Vector2[] _bloodSize = new Vector2[BLOOD_SLOTS];
        private float _bloodDuration = 4f;
        private int _bloodSlot = 0;

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
            Texture lensBlood = Resources.Load<Texture>(LENS_BLOOD_TEXTURE);
            if (lensBlood != null)
            {
                for (int i = 0; i < BLOOD_SLOTS; ++i)
                {
                    _blood[i] = CreateLayer(canvasObject.transform, "LensBlood" + i, lensBlood);
                    RectTransform rect = _blood[i].rectTransform;
                    rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 1f);
                    rect.sizeDelta = new Vector2(300f, 300f);
                }
            }
        }

        private void Update()
        {
            Fade(_flash, Color.white, _flashAlpha, _flashSeconds, ref _flashLeft);
            for (int i = 0; i < BLOOD_SLOTS; ++i)
            {
                if ((_blood[i] == null) || (_bloodLeft[i] <= 0f))
                {
                    continue;
                }
                _bloodLeft[i] = Mathf.Max(0f, _bloodLeft[i] - Time.unscaledDeltaTime);
                float progress = 1f - (_bloodLeft[i] / _bloodDuration);
                float splash = Mathf.Clamp01((_bloodDuration - _bloodLeft[i]) / BLOOD_SPLASH_SECONDS);
                float spread = 1f - ((1f - splash) * (1f - splash));
                _blood[i].rectTransform.anchoredPosition = _bloodStart[i];
                _blood[i].rectTransform.sizeDelta = new Vector2(_bloodSize[i].x * Mathf.Lerp(0.3f, 1f, spread),
                    _bloodSize[i].y * Mathf.Lerp(0.25f, 0.7f, spread) * Mathf.Lerp(1f, 1.08f, progress));
                Color tint = new Color(1f, 1f, 1f, 0.8f * Mathf.Clamp01(_bloodLeft[i]));
                _blood[i].color = tint;
            }
        }

        private void OnDisable()
        {
            _flashLeft = 0f;
            if (_flash != null)
            {
                _flash.color = Color.clear;
            }
            for (int i = 0; i < BLOOD_SLOTS; ++i)
            {
                _bloodLeft[i] = 0f;
                if (_blood[i] != null)
                {
                    _blood[i].color = Color.clear;
                }
            }
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

        public void SplashBlood()
        {
            UnityEngine.UI.RawImage image = _blood[_bloodSlot];
            if (image == null)
            {
                return;
            }
            _bloodStart[_bloodSlot] = new Vector2(Random.Range(-Screen.width * 0.28f, Screen.width * 0.28f),
                Random.Range(Screen.height * 0.1f, Screen.height * 0.35f));
            image.rectTransform.anchoredPosition = _bloodStart[_bloodSlot];
            float size = Mathf.Min(Screen.width, Screen.height) * Random.Range(0.28f, 0.36f);
            _bloodSize[_bloodSlot] = new Vector2(size * 1.5f, size);
            image.rectTransform.sizeDelta = new Vector2(size * 1.5f * 0.3f, size * 0.25f);
            image.color = new Color(1f, 1f, 1f, 0.8f);
            _bloodLeft[_bloodSlot] = _bloodDuration;
            _bloodSlot = (_bloodSlot + 1) % BLOOD_SLOTS;
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
