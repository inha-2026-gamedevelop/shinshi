// Unity
using UnityEngine;
using UnityEngine.Rendering;

namespace Shinshi.Art
{
    /// <summary> 짧은 백열 섬광과 식으며 떨어지는 잔불을 분리해 재생하는 불똥 풀, 이 클라이언트 화면에만 보이는 표현이다 </summary>
    public sealed class HitSparkEmitter : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SHADER_NAME       = "Shinshi/VFX/ReleaseParticle";
        private const string TEXTURE_PROPERTY  = "_BaseMap";
        private const string TINT_PROPERTY     = "_Tint";
        private const int    POOL_SIZE         = 12;
        private const int    MAX_PARTICLES     = 64;
        private const int    MASK_SIZE         = 64;
        private const float  MIN_DIRECTION_SQR = 0.0001f;
        private const float  CORE_FALLOFF      = 100f;
        private const float  GLOW_FALLOFF      = 15f;
        private const float  GLOW_ALPHA        = 0.22f;
        private const float  MASK_EDGE_BLUE    = 0.1f;
        private const float  EDGE_START        = 0.35f;
        private const float  HALF              = 0.5f;
        private const float  SHAPE_RADIUS      = 0.06f;
        private const float  STREAK_GRAVITY    = 0.5f;
        private const float  EMBER_STRETCH     = 0.008f;
        private const float  STREAK_LENGTH     = 1.6f;
        private const float  BOUNCE            = 0.25f;
        private const float  LIFETIME_LOSS     = 0.3f;
        private const float  COLLISION_RADIUS  = 0.1f;
        private const float  STREAK_HOT_KEY    = 0.55f; // 수명 비율, 흰 선이 미색을 유지하는 구간 끝
        private const float  STREAK_WARM_KEY   = 0.85f;
        private const float  EMBER_HOT_KEY     = 0.2f;  // 잔불은 빨리 주황으로 식는다
        private const float  EMBER_WARM_KEY    = 0.55f;
        private const float  OPAQUE_UNTIL      = 0.35f; // 40m 시점 가독성을 위해 알파를 온전히 유지하는 구간
        private const float  FADE_ALPHA        = 0.75f;
        private const float  FADE_ALPHA_KEY    = 0.65f;

        [Header("백열 섬광 (초, m/s, m)")]
        [SerializeField] private Vector2 _streakLifetime = new Vector2(0.14f, 0.3f);
        [SerializeField] private Vector2 _streakSpeed    = new Vector2(7f, 18f);
        [SerializeField] private Vector2 _streakWidth    = new Vector2(0.2f, 0.34f);
        [SerializeField, Range(0f, 1f)] private float _streakRatio = 0.65f;
        [SerializeField, Min(0f)] private float _streakExposure = 6f;
        [SerializeField, Min(0f)] private float _streakStretch  = 0.05f;
        [SerializeField, Range(0.5f, 3f)] private float _density = 2f;

        [Header("잔불 (초, m/s, m)")]
        [SerializeField] private Vector2 _emberLifetime = new Vector2(0.28f, 0.7f);
        [SerializeField] private Vector2 _emberSpeed    = new Vector2(2.5f, 7.5f);
        [SerializeField] private Vector2 _emberWidth    = new Vector2(0.16f, 0.3f);
        [SerializeField, Min(0f)] private float _emberExposure = 4f;
        [SerializeField, Min(0f)] private float _emberGravity  = 1.6f;

        [Header("퍼짐과 감속")]
        [SerializeField, Range(0f, 90f)] private float _spreadAngle = 68f;
        [SerializeField, Min(0f)] private float _upBias = 0.65f;
        [SerializeField, Min(0f)] private float _drag   = 1.4f;
        [SerializeField, Min(1f)] private float _criticalWidthScale = 1.15f;

        [Header("접점 섬광 (초, m)")]
        [SerializeField, Min(0.01f)] private float _flashSeconds = 0.08f;
        [SerializeField, Min(0f)] private float _flashSize = 1.1f;

        [Header("재질 (비우면 셰이더 이름으로 찾아 한 번 생성)")]
        [SerializeField] private Material _sparkMaterial;

        private static readonly Color s_hotColor  = new Color(1f, 0.94f, 0.8f, 1f);
        private static readonly Color s_warmColor = new Color(1f, 0.48f, 0.12f, 1f);
        private static readonly Color s_coolColor = new Color(0.7f, 0.08f, 0.01f, 1f);

        private readonly ParticleSystem[] _streakPool = new ParticleSystem[POOL_SIZE];
        private readonly ParticleSystem[] _emberPool  = new ParticleSystem[POOL_SIZE];
        private readonly ParticleSystem[] _flashPool  = new ParticleSystem[POOL_SIZE];

        private Material  _streakMaterial = null;
        private Material  _emberMaterial  = null;
        private Texture2D _mask           = null;
        private bool      _isReady        = false;
        private int       _next           = 0;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (!CreateMaterials())
            {
                enabled = false;
                return;
            }

            for (int i = 0; i < POOL_SIZE; ++i)
            {
                _streakPool[i] = CreateSystem("HitSparks", i, _streakLifetime, _streakSpeed, _streakWidth, false);
                _emberPool[i]  = CreateSystem("HitEmbers", i, _emberLifetime, _emberSpeed, _emberWidth, true);
                _flashPool[i]  = CreateFlash(i);
            }

            _isReady = true;
        }

        private void OnDisable()
        {
            StopAll();
        }

        private void OnDestroy()
        {
            if (_streakMaterial != null)
            {
                Destroy(_streakMaterial);
            }

            if (_emberMaterial != null)
            {
                Destroy(_emberMaterial);
            }

            if (_mask != null)
            {
                Destroy(_mask);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 요청 개수에 밀도 배율을 적용해 백열 섬광과 잔불을 방출한다. 치명타도 선의 가는 형태를 유지한다 </summary>
        public void Burst(Vector3 point, Vector3 direction, int count, bool isCritical)
        {
            if ((!isActiveAndEnabled) || (!_isReady) || (count <= 0))
            {
                return;
            }

            // direction은 타격의 진행 방향이다. 접촉면 밖으로 튀어야 몸 안에서 대부분 가려지지 않는다
            Vector3 aim = -direction.normalized + (Vector3.up * _upBias);
            if (aim.sqrMagnitude <= MIN_DIRECTION_SQR)
            {
                aim = Vector3.up;
            }

            Vector3 up = Mathf.Abs(Vector3.Dot(aim.normalized, Vector3.up)) > (1f - MIN_DIRECTION_SQR)
                ? Vector3.forward : Vector3.up;
            Quaternion rotation = Quaternion.LookRotation(aim.normalized, up);
            int slot = _next;
            _next = (_next + 1) % POOL_SIZE;

            // 풀을 한 바퀴 돌면 이전 잔상을 지운다. 살아 있는 월드 입자의 방출 원점만 바꾸지 않는다
            Prepare(_streakPool[slot], point, rotation);
            Prepare(_emberPool[slot], point, rotation);
            Prepare(_flashPool[slot], point, rotation);

            int total = Mathf.Clamp(Mathf.RoundToInt(count * _density), 1, MAX_PARTICLES);
            int streakCount = Mathf.RoundToInt(total * _streakRatio);
            float widthScale = isCritical ? _criticalWidthScale : 1f;
            Emit(_streakPool[slot], streakCount, _streakWidth, widthScale);
            Emit(_emberPool[slot], total - streakCount, _emberWidth, widthScale);

            var flash = new ParticleSystem.EmitParams
            {
                startSize  = _flashSize * widthScale,
                startColor = s_hotColor
            };
            _flashPool[slot].Emit(flash, 1);
        }

        /// <summary> 비활성화와 미리보기 취소 때 잔상까지 즉시 지운다 </summary>
        public void StopAll()
        {
            if (!_isReady)
            {
                return;
            }

            for (int i = 0; i < POOL_SIZE; ++i)
            {
                _streakPool[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                _emberPool[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                _flashPool[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private bool CreateMaterials()
        {
            // 프리팹의 재질을 쓰고, 비어 있으면 셰이더 이름으로 찾는다. 이름 검색은 빌드에서 셰이더가 빠질 수 있어 프리팹 재질을 권장한다
            Shader shader = _sparkMaterial != null ? _sparkMaterial.shader : Shader.Find(SHADER_NAME);
            if (shader == null)
            {
                Debug.LogError($"[HitSparkEmitter] 셰이더를 찾지 못했다: {SHADER_NAME}", this);
                return false;
            }

            _mask = _sparkMaterial == null ? CreateMask() : null;
            _streakMaterial = _sparkMaterial != null ? new Material(_sparkMaterial) : new Material(shader);
            _emberMaterial  = _sparkMaterial != null ? new Material(_sparkMaterial) : new Material(shader);
            _streakMaterial.name = "HitSpark_WhiteHot";
            _emberMaterial.name  = "HitSpark_Ember";
            if (_mask != null)
            {
                _streakMaterial.SetTexture(TEXTURE_PROPERTY, _mask);
                _emberMaterial.SetTexture(TEXTURE_PROPERTY, _mask);
            }

            // 입자 startColor는 Color32라 HDR이 잘린다. 백열 밝기는 재질에 둔다
            _streakMaterial.SetColor(TINT_PROPERTY, new Color(_streakExposure, _streakExposure, _streakExposure, 1f));
            _emberMaterial.SetColor(TINT_PROPERTY, new Color(_emberExposure, _emberExposure, _emberExposure, 1f));
            return true;
        }

        private static Texture2D CreateMask()
        {
            var texture = new Texture2D(MASK_SIZE, MASK_SIZE, TextureFormat.RGBA32, false, true)
            {
                name       = "HitSpark_SoftCore",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[MASK_SIZE * MASK_SIZE];
            for (int y = 0; y < MASK_SIZE; ++y)
            {
                for (int x = 0; x < MASK_SIZE; ++x)
                {
                    float u = ((x + HALF) / MASK_SIZE) - HALF;
                    float v = ((y + HALF) / MASK_SIZE) - HALF;
                    float radiusSquared = (u * u) + (v * v);
                    float glow = Mathf.Exp(-radiusSquared * GLOW_FALLOFF);
                    float core = Mathf.Exp(-radiusSquared * CORE_FALLOFF);
                    float edge = 1f - Mathf.SmoothStep(EDGE_START, HALF, Mathf.Sqrt(radiusSquared));
                    float alpha = Mathf.Clamp01(core + (glow * GLOW_ALPHA)) * edge;
                    pixels[(y * MASK_SIZE) + x] = new Color(1f, Mathf.Lerp(HALF, 1f, core),
                        Mathf.Lerp(MASK_EDGE_BLUE, 1f, core), alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static void Prepare(ParticleSystem system, Vector3 point, Quaternion rotation)
        {
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.transform.SetPositionAndRotation(point, rotation);
            system.Play(false);
        }

        private static void Emit(ParticleSystem system, int count, Vector2 width, float widthScale)
        {
            for (int i = 0; i < count; ++i)
            {
                var emission = new ParticleSystem.EmitParams
                {
                    startSize            = Random.Range(width.x, width.y) * widthScale,
                    startColor           = Color.Lerp(s_hotColor, s_warmColor, Random.value * HALF),
                    applyShapeToPosition = true
                };
                system.Emit(emission, 1);
            }
        }

        private ParticleSystem CreateSystem(string prefix, int index, Vector2 lifetime, Vector2 speed,
            Vector2 width, bool isEmber)
        {
            var go = new GameObject($"{prefix}{index}");
            go.transform.SetParent(transform, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop            = false;
            main.playOnAwake     = false;
            main.maxParticles    = MAX_PARTICLES;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode     = ParticleSystemCullingMode.AlwaysSimulate;
            main.gravityModifier = isEmber ? _emberGravity : STREAK_GRAVITY;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize       = new ParticleSystem.MinMaxCurve(width.x, width.y);
            main.startColor      = Color.white;

            var emission = system.emission;
            emission.enabled = false;

            var shape = system.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle     = _spreadAngle;
            shape.radius    = SHAPE_RADIUS;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen  = 0f;
            limit.drag    = _drag;

            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(s_hotColor, isEmber ? EMBER_HOT_KEY : STREAK_HOT_KEY),
                    new GradientColorKey(s_warmColor, isEmber ? EMBER_WARM_KEY : STREAK_WARM_KEY),
                    new GradientColorKey(isEmber ? s_coolColor : s_warmColor, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, OPAQUE_UNTIL),
                    new GradientAlphaKey(FADE_ALPHA, FADE_ALPHA_KEY),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var collision = system.collision;
            collision.enabled                = true;
            collision.type                   = ParticleSystemCollisionType.World;
            collision.mode                   = ParticleSystemCollisionMode.Collision3D;
            collision.quality                = ParticleSystemCollisionQuality.Low;
            collision.enableDynamicColliders = false;
            collision.bounce                 = BOUNCE;
            collision.dampen                 = HALF;
            collision.lifetimeLoss           = LIFETIME_LOSS;
            collision.radiusScale            = COLLISION_RADIUS;

            var particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial      = isEmber ? _emberMaterial : _streakMaterial;
            particleRenderer.renderMode          = ParticleSystemRenderMode.Stretch;
            particleRenderer.velocityScale       = isEmber ? EMBER_STRETCH : _streakStretch;
            particleRenderer.lengthScale         = isEmber ? 1f : STREAK_LENGTH;
            particleRenderer.cameraVelocityScale = 0f;
            particleRenderer.shadowCastingMode   = ShadowCastingMode.Off;
            particleRenderer.receiveShadows      = false;
            return system;
        }

        private ParticleSystem CreateFlash(int index)
        {
            ParticleSystem system = CreateSystem("HitSparkFlash", index, new Vector2(_flashSeconds, _flashSeconds),
                Vector2.zero, new Vector2(_flashSize, _flashSize), false);
            var shape = system.shape;
            shape.enabled = false;
            var collision = system.collision;
            collision.enabled = false;
            var main = system.main;
            main.maxParticles    = 1;
            main.gravityModifier = 0f;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            return system;
        }
    }
}
