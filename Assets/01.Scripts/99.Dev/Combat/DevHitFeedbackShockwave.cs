// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 강공격이 바닥을 내려칠 때 퍼지는 충격 링과 먼지를 보여 주는 시험용 연출 </summary>
    public sealed class DevHitFeedbackShockwave : MonoBehaviour
    {
        // 링 하나와 먼지 파티클 하나를 코드로 만들어 다시 쓴다. 에셋 없이 Sprites/Default 셰이더만 쓰고,
        // 먼지가 네모로 보이지 않게 둥근 그라데이션 텍스처를 코드로 한 장 만든다

        /****************************************
        *                Fields
        ****************************************/

        private const string SHADER_NAME = "Sprites/Default";

        private const int   RING_SEGMENTS     = 48;
        private const float RING_INNER_RATIO  = 0.8f;  // 바깥 반지름 대비 안쪽 반지름
        private const float RING_SECONDS      = 0.45f;
        private const float RING_START_RADIUS = 0.4f;  // m
        private const float RING_HEIGHT       = 0.06f; // m, 바닥에 파묻히지 않게 띄운다

        private const int   SOFT_TEXTURE_SIZE = 64;

        private const int   DUST_MAX_PARTICLES = 96;
        private const int   DUST_COUNT         = 32;
        private const float DUST_LIFETIME_MIN  = 0.5f;
        private const float DUST_LIFETIME_MAX  = 0.9f;
        private const float DUST_SPEED_MIN     = 1.5f; // m/s
        private const float DUST_SPEED_MAX     = 3.5f; // m/s
        private const float DUST_SIZE_MIN      = 0.5f; // m
        private const float DUST_SIZE_MAX      = 1.1f; // m
        private const float DUST_RISE          = -0.12f; // 중력 배율, 음수라 천천히 떠오른다
        private const float DUST_EDGE_RATIO    = 0.7f;  // 충격 반지름 대비 먼지가 나오는 원 반지름
        private const float DUST_GROW          = 2f;    // 수명이 다할 때의 크기 배율
        private const float TWO                = 2f;

        private static readonly Color s_ringColor = new Color(1f, 0.7f, 0.35f, 0.9f);
        private static readonly Color s_dustColor = new Color(0.6f, 0.52f, 0.42f, 0.6f);

        private Material       _ringMaterial = null;
        private Material       _dustMaterial = null;
        private Texture2D      _softTexture  = null;
        private Transform      _ring         = null;
        private ParticleSystem _dust         = null;
        private float          _startTime    = -1f;
        private float          _radius       = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            // 시험용 씬이라 머티리얼 에셋을 따로 두지 않고 Awake에서 한 번만 이름으로 찾는다
            Shader shader = Shader.Find(SHADER_NAME);
            if (shader == null)
            {
                Debug.LogError($"[DevShockwave] 셰이더를 찾지 못했다: {SHADER_NAME}", this);
                enabled = false;
                return;
            }

            _ringMaterial = new Material(shader);
            _softTexture  = CreateSoftTexture();
            _dustMaterial = new Material(shader) { mainTexture = _softTexture };

            _ring = CreateRing();
            _dust = CreateDust();
        }

        private void OnDestroy()
        {
            if (_ringMaterial != null)
            {
                Destroy(_ringMaterial);
            }

            if (_dustMaterial != null)
            {
                Destroy(_dustMaterial);
            }

            if (_softTexture != null)
            {
                Destroy(_softTexture);
            }
        }

        private void Update()
        {
            if (_startTime < 0f)
            {
                return;
            }

            float t = (Time.time - _startTime) / RING_SECONDS;
            if (t >= 1f)
            {
                _ring.gameObject.SetActive(false);
                _startTime = -1f;
                return;
            }

            // 처음에 확 퍼지고 끝으로 갈수록 느려지며 옅어진다
            float eased  = 1f - ((1f - t) * (1f - t));
            float radius = Mathf.Lerp(RING_START_RADIUS, _radius, eased);
            _ring.localScale = new Vector3(radius, 1f, radius);

            Color color = s_ringColor;
            color.a *= 1f - t;
            _ringMaterial.color = color;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> center 바닥에서 radius까지 퍼지는 충격 링과 먼지를 터뜨린다 </summary>
        public void Play(Vector3 center, float radius)
        {
            if (!enabled)
            {
                return;
            }

            _radius    = radius;
            _startTime = Time.time;

            _ring.position = center + (Vector3.up * RING_HEIGHT);
            _ring.localScale = new Vector3(RING_START_RADIUS, 1f, RING_START_RADIUS);
            _ring.gameObject.SetActive(true);

            _dust.transform.position = center;
            var shape = _dust.shape;
            shape.radius = radius * DUST_EDGE_RATIO;
            _dust.Play();
            _dust.Emit(DUST_COUNT);
        }

        // 바깥 반지름 1인 납작한 고리. 크기는 스케일로 조절한다
        private Transform CreateRing()
        {
            var go = new GameObject("DevShockwaveRing");
            go.transform.SetParent(transform, false);

            int vertexCount = (RING_SEGMENTS + 1) * 2;
            var vertices    = new Vector3[vertexCount];
            var triangles   = new int[RING_SEGMENTS * 6];

            for (int i = 0; i <= RING_SEGMENTS; ++i)
            {
                float   rad = ((float)i / RING_SEGMENTS) * Mathf.PI * TWO;
                Vector3 dir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));

                vertices[i * 2]       = dir * RING_INNER_RATIO;
                vertices[(i * 2) + 1] = dir;
            }

            for (int i = 0; i < RING_SEGMENTS; ++i)
            {
                int v = i * 2;
                int t = i * 6;
                triangles[t]     = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2;
                triangles[t + 4] = v + 1;
                triangles[t + 5] = v + 3;
            }

            var mesh = new Mesh { name = "DevShockwaveRing", vertices = vertices, triangles = triangles };
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = _ringMaterial;

            go.SetActive(false);
            return go.transform;
        }

        private ParticleSystem CreateDust()
        {
            var go = new GameObject("DevShockwaveDust");
            go.transform.SetParent(transform, false);

            // 원 모양 발생기는 로컬 XY 평면에 놓이므로 눕혀서 바닥과 맞춘다
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop            = false;
            main.playOnAwake     = false;
            main.maxParticles    = DUST_MAX_PARTICLES;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = DUST_RISE;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(DUST_LIFETIME_MIN, DUST_LIFETIME_MAX);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(DUST_SPEED_MIN, DUST_SPEED_MAX);
            main.startSize       = new ParticleSystem.MinMaxCurve(DUST_SIZE_MIN, DUST_SIZE_MAX);
            main.startColor      = s_dustColor;

            var emission = system.emission;
            emission.enabled = false;

            var shape = system.shape;
            shape.enabled         = true;
            shape.shapeType       = ParticleSystemShapeType.Circle;
            shape.radiusThickness = 0f;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size    = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f / DUST_GROW, 1f, 1f));

            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });

            var colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color   = fade;

            var particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = _dustMaterial;
            particleRenderer.renderMode     = ParticleSystemRenderMode.Billboard;

            return system;
        }

        // 가운데가 진하고 가장자리로 갈수록 투명해지는 둥근 점
        private static Texture2D CreateSoftTexture()
        {
            var texture = new Texture2D(SOFT_TEXTURE_SIZE, SOFT_TEXTURE_SIZE, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp
            };

            float center = (SOFT_TEXTURE_SIZE - 1) * 0.5f;
            for (int y = 0; y < SOFT_TEXTURE_SIZE; ++y)
            {
                for (int x = 0; x < SOFT_TEXTURE_SIZE; ++x)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / center;
                    float alpha    = Mathf.Clamp01(1f - distance);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * alpha));
                }
            }

            texture.Apply();
            return texture;
        }
    }
}
