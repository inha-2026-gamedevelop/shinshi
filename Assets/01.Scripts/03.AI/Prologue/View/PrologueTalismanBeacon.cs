// Unity
using UnityEngine;
using UnityEngine.Rendering;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    /// <summary> 프롤로그 숲의 부적 조사 위치를 개발 씬과 같은 노란 빛 입자와 점광으로 표시한다 </summary>
    public sealed class PrologueTalismanBeacon : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MARKERS_ROOT    = "PrologueMarkers";
        private const string TALISMAN_MARKER = "Talisman";
        private const string ROOT_NAME       = "PrologueTalismanBeacon";
        private const string GLOW_SHADER     = "Universal Render Pipeline/Particles/Unlit";
        private const string FALLBACK_SHADER = "Sprites/Default";

        private const float BEACON_LIFT          = 0.2f;
        private const float BEACON_LIFETIME      = 3f;
        private const float BEACON_RISE_SPEED    = 0.8f;
        private const float BEACON_SIZE_MIN      = 0.3f;
        private const float BEACON_SIZE_MAX      = 0.9f;
        private const int   BEACON_MAX_PARTICLES = 80;
        private const float BEACON_RATE          = 25f;
        private const float BEACON_CONE_ANGLE    = 4f;
        private const float BEACON_RADIUS        = 1.25f;
        private const float BEACON_FADE_IN       = 0.3f;
        private const float LIGHT_HEIGHT         = 1.5f;
        private const float LIGHT_RANGE          = 8f;
        private const float LIGHT_INTENSITY      = 4f;
        private const int   TEXTURE_SIZE         = 32;
        private const float FALLOFF              = 2f;
        private const float HALF                 = 0.5f;
        private const float UPWARD_ROTATION      = -90f;
        private const float ADDITIVE_BLEND_MODE  = 2f;

        private static readonly Color G_BeaconColor = new Color(1f, 0.85f, 0.2f, 1f);

        private Material  _material;
        private Texture2D _texture;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 씬의 부적 위치를 한 번 읽고 표현만 만든다. 마커가 없거나 서버 빌드면 만들지 않는다 </summary>
        public static PrologueTalismanBeacon Create(Transform parent)
        {
            if (IsServerBuild)
            {
                return null;
            }

            GameObject markers = GameObject.Find(MARKERS_ROOT);
            Transform talisman = null;
            if (markers != null)
            {
                foreach (Transform marker in markers.GetComponentsInChildren<Transform>(true))
                {
                    if (marker.name == TALISMAN_MARKER)
                    {
                        talisman = marker;
                        break;
                    }
                }
            }
            if (talisman == null)
            {
                Debug.LogWarning("[Prologue] 부적 위치 마커가 없어 조사 위치의 빛을 표시하지 않는다");
                return null;
            }

            var root = new GameObject(ROOT_NAME);
            root.transform.SetParent(parent, false);
            root.transform.position = talisman.position;
            var beacon = root.AddComponent<PrologueTalismanBeacon>();
            beacon.Build();
            return beacon;
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
            }
            if (_texture != null)
            {
                Destroy(_texture);
            }
        }

        private void Build()
        {
            var glowObject = new GameObject("Glow");
            glowObject.transform.SetParent(transform, false);
            glowObject.transform.localPosition = Vector3.up * BEACON_LIFT;
            glowObject.transform.localRotation = Quaternion.Euler(UPWARD_ROTATION, 0f, 0f);

            var glow = glowObject.AddComponent<ParticleSystem>();
            var main = glow.main;
            main.loop          = true;
            main.startLifetime = BEACON_LIFETIME;
            main.startSpeed    = BEACON_RISE_SPEED;
            main.startSize     = new ParticleSystem.MinMaxCurve(BEACON_SIZE_MIN, BEACON_SIZE_MAX);
            main.startColor    = G_BeaconColor;
            main.maxParticles  = BEACON_MAX_PARTICLES;

            var emission = glow.emission;
            emission.rateOverTime = BEACON_RATE;
            var shape = glow.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = BEACON_CONE_ANGLE;
            shape.radius = BEACON_RADIUS;

            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, BEACON_FADE_IN), new GradientAlphaKey(0f, 1f) });
            var colorOverLifetime = glow.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = fade;
            glowObject.GetComponent<ParticleSystemRenderer>().sharedMaterial = CreateGlowMaterial();

            var lightObject = new GameObject("GlowLight");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = Vector3.up * LIGHT_HEIGHT;
            var glowLight = lightObject.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = G_BeaconColor;
            glowLight.range = LIGHT_RANGE;
            glowLight.intensity = LIGHT_INTENSITY;
        }

        // Dev 프롤로그의 빛과 같은 부드러운 원형 입자를 사용한다. 복사본의 재질과 텍스처는 이 표현의 수명에 묶는다.
        private Material CreateGlowMaterial()
        {
            Shader shader = Shader.Find(GLOW_SHADER);
            _material = new Material((shader != null) ? shader : Shader.Find(FALLBACK_SHADER));
            _texture = new Texture2D(TEXTURE_SIZE, TEXTURE_SIZE, TextureFormat.RGBA32, false);
            float half = (TEXTURE_SIZE - 1) * HALF;
            for (int y = 0; y < TEXTURE_SIZE; ++y)
            {
                for (int x = 0; x < TEXTURE_SIZE; ++x)
                {
                    float distance = Mathf.Sqrt(((x - half) * (x - half)) + ((y - half) * (y - half))) / half;
                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - distance), FALLOFF);
                    _texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            _texture.Apply();
            _texture.wrapMode = TextureWrapMode.Clamp;
            _material.mainTexture = _texture;
            _material.SetTexture("_BaseMap", _texture);
            _material.SetFloat("_Surface", 1f);
            _material.SetFloat("_Blend", ADDITIVE_BLEND_MODE);
            _material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            _material.SetFloat("_DstBlend", (float)BlendMode.One);
            _material.SetFloat("_ZWrite", 0f);
            _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _material.SetOverrideTag("RenderType", "Transparent");
            _material.renderQueue = (int)RenderQueue.Transparent;
            return _material;
        }
    }
}
