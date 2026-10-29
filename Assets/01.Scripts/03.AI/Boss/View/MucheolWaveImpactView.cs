// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 무철 원형 충격파 VFX. 땅에서 바깥으로 퍼지는 불꽃 고리와 전기 고리, 번지는 연기 고리, 튀는 바위를 반경에 맞춰 재생한다
    // 기준 반경 8m에서 만든 프리팹을 반경 비율로 키운다. 약한 충격파는 바위와 파편을 뺀다
    /// <summary> 무철 원형 충격파 착탄 VFX 재생기 </summary>
    [DisallowMultipleComponent]
    public sealed class MucheolWaveImpactView : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float REFERENCE_RADIUS   = 8f;
        private const float MIN_RADIUS         = 0.5f;
        private const float MIN_ARC            = 10f;
        private const float FULL_CIRCLE        = 360f;
        private const float FULL_ARC           = 359f;    // deg, 이 이상이면 사방으로 퍼지는 원형으로 본다
        private const float ARC_FORWARD_OFFSET = 90f;     // 원판은 +X에서 시작하므로 호의 가운데를 +Z로 돌리는 각도
        private const float RING_INNER_MIN     = 0.5f;
        private const float RING_INNER_MAX     = 0.98f;
        private const float RING_INNER_DEFAULT = 0.8f;    // 고리 안쪽 반지름 비율이 지정되지 않았을 때
        private const float RING_INNER_FIRST   = 0.62f;   // 바깥 불꽃 고리의 안쪽 반지름 비율
        private const float RING_INNER_SECOND  = 0.88f;   // 안쪽 얇은 밝은 고리의 안쪽 반지름 비율
        private const float GROW_SECONDS       = 0.42f;   // 고리가 시작 크기에서 끝 반경까지 퍼지는 시간
        private const float FADE_SECONDS       = 0.55f;   // 끝 반경에 닿은 뒤 사라지는 시간
        private const float END_MARGIN         = 2.4f;    // 고리가 사라진 뒤 입자가 끝나기를 기다리는 시간
        private const float START_SCALE        = 0.1f;    // 고리가 시작할 때 크기 비율
        private const float SECOND_DELAY       = 0.04f;   // 안쪽 얇은 고리가 늦게 따라오는 시간
        private const float MIN_VISIBLE_FADE   = 0.001f;
        private const int   RING_SEGMENTS      = 96;
        private const int   MIN_RING_SEGMENTS  = 8;
        private const float RING_NOISE_SCALE   = 0.45f;
        private const float RING_NOISE_SEED    = 3.7f;
        private const float RING_LIGHT_BASE    = 0.65f;   // 고리 칸마다 밝기가 이 값에서 조금씩 달라진다
        private const float RING_LIGHT_RANGE   = 0.35f;
        private const float FLASH_PEAK         = 70f;     // 섬광 라이트 최대 세기
        private const float FLASH_RANGE        = 14f;     // m, 기준 반경일 때
        private const float LIGHT_SECONDS      = 0.8f;
        private const float LIGHT_DECAY        = 8f;
        private const float LIGHT_ATTACK       = 0.02f;

        private static readonly int FADE_ID = Shader.PropertyToID("_Fade");

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField, Min(MIN_RADIUS)] private float _radius = 8f;   // m, 충격파 반경
        [SerializeField] private ParticleSystem[] _systems;       // 재생과 정지를 받는 전체
        [SerializeField] private ParticleSystem[] _strongOnly;    // 강한 충격파에서만 켜는 것(바위, 파편)
        [SerializeField] private MeshRenderer[]   _rings;         // 바깥 불꽃 고리, 안쪽 얇은 밝은 고리 순서
        [SerializeField, Range(RING_INNER_MIN, RING_INNER_MAX)] private float[] _ringInner = { RING_INNER_FIRST, RING_INNER_SECOND }; // 고리 안쪽 반지름 비율
        [SerializeField] private Light _flashLight;

        private Mesh[]                _meshes;
        private MaterialPropertyBlock _block;
        private bool                  _isStrong = true;
        private float                 _arc      = FULL_CIRCLE;   // 도, 정면(+Z)을 가운데로 한 부채꼴 각도
        private float                 _builtArc = -1f;
        private float                 _playTime = -1f;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 재생 중인가, 풀에서 놀고 있는 것을 고를 때 쓴다 </summary>
        public bool IsPlaying => _playTime >= 0f;

        /****************************************
        *               Unity Events
        ****************************************/

        private void Awake()
        {
            EnsureRings();
            HideRings();
        }

        private void Update()
        {
            if (_playTime < 0f)
            {
                return;
            }

            float t = Time.time - _playTime;
            Evaluate(t);
            if (t > (GROW_SECONDS + FADE_SECONDS + END_MARGIN))
            {
                _playTime = -1f;
                HideRings();
            }
        }

        private void OnDisable()
        {
            _playTime = -1f;
            HideRings();
        }

        private void OnDestroy()
        {
            if (_meshes == null)
            {
                return;
            }

            for (int i = 0; i < _meshes.Length; ++i)
            {
                if (_meshes[i] != null)
                {
                    Destroy(_meshes[i]);
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 반경과 세기와 퍼지는 각도를 정한다. 약한 충격파는 바위와 파편을 뺀다. 각도가 360이면 둘레 전체다 </summary>
        public void Setup(float radius, bool isStrong, float arc = FULL_CIRCLE)
        {
            _radius   = Mathf.Max(MIN_RADIUS, radius);
            _isStrong = isStrong;
            _arc      = Mathf.Clamp(arc, MIN_ARC, FULL_CIRCLE);
            float scale = _radius / REFERENCE_RADIUS;
            transform.localScale = new Vector3(scale, scale, scale);
            if (_flashLight != null)
            {
                _flashLight.range = FLASH_RANGE * scale;
            }
        }

        public void Play()
        {
            ApplyArc();
            EnsureRings();
            for (int i = 0; i < _systems.Length; ++i)
            {
                _systems[i].Clear(false);
            }

            for (int i = 0; i < _systems.Length; ++i)
            {
                if ((!_isStrong) && (System.Array.IndexOf(_strongOnly, _systems[i]) >= 0))
                {
                    continue;
                }

                _systems[i].Play(false);
            }

            _playTime = Time.time;
            Evaluate(0f);
        }

        public void Stop()
        {
            for (int i = 0; i < _systems.Length; ++i)
            {
                _systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            _playTime = -1f;
            HideRings();
        }

        // 퍼지는 각도에 맞춰 입자 방출 호를 정면 중심으로 돌린다. 360이면 사방이다
        private void ApplyArc()
        {
            for (int i = 0; (_systems != null) && (i < _systems.Length); ++i)
            {
                ParticleSystem.ShapeModule shape = _systems[i].shape;
                if (!shape.enabled)
                {
                    continue;
                }

                shape.arc      = _arc;
                shape.rotation = new Vector3(0f, 0f, ARC_FORWARD_OFFSET - (_arc * 0.5f));
            }
        }

        // 고리는 처음에 빠르게 퍼지고 끝에서 느려진다. 끝 반경에 닿으면 서서히 사라진다
        private void Evaluate(float t)
        {
            for (int i = 0; (_rings != null) && (i < _rings.Length); ++i)
            {
                if (_rings[i] == null)
                {
                    continue;
                }

                float local = t - (SECOND_DELAY * i);
                float grow  = Mathf.Clamp01(local / GROW_SECONDS);
                grow = 1f - ((1f - grow) * (1f - grow) * (1f - grow));
                float scale = Mathf.Lerp(START_SCALE, 1f, grow) * REFERENCE_RADIUS;
                float fade  = (local < 0f) ? 0f : ((local < GROW_SECONDS) ? 1f : (1f - Mathf.Clamp01((local - GROW_SECONDS) / FADE_SECONDS)));
                _rings[i].transform.localScale = new Vector3(scale, 1f, scale);
                SetRing(_rings[i], fade * fade);
            }

            if (_flashLight != null)
            {
                _flashLight.intensity = (t < LIGHT_SECONDS) ? (FLASH_PEAK * Mathf.Exp(-t * LIGHT_DECAY) * Mathf.Clamp01(t / LIGHT_ATTACK)) : 0f;
            }
        }

        private void SetRing(MeshRenderer ring, float fade)
        {
            ring.enabled = fade > MIN_VISIBLE_FADE;
            if (!ring.enabled)
            {
                return;
            }

            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }

            ring.GetPropertyBlock(_block);
            _block.SetFloat(FADE_ID, fade);
            ring.SetPropertyBlock(_block);
        }

        private void HideRings()
        {
            for (int i = 0; (_rings != null) && (i < _rings.Length); ++i)
            {
                if (_rings[i] != null)
                {
                    _rings[i].enabled = false;
                }
            }

            if (_flashLight != null)
            {
                _flashLight.intensity = 0f;
            }
        }

        // 고리 메시는 안쪽 반지름 비율만 다른 평평한 띠다. UV.x가 안쪽 0에서 바깥쪽 1이다
        private void EnsureRings()
        {
            if (_rings == null)
            {
                return;
            }

            if ((_meshes != null) && Mathf.Approximately(_builtArc, _arc))
            {
                return;
            }

            if (_meshes != null)
            {
                for (int i = 0; i < _meshes.Length; ++i)
                {
                    if (_meshes[i] != null)
                    {
                        Destroy(_meshes[i]);
                    }
                }
            }

            _builtArc = _arc;
            _meshes = new Mesh[_rings.Length];
            for (int i = 0; i < _rings.Length; ++i)
            {
                if (_rings[i] == null)
                {
                    continue;
                }

                float inner = ((_ringInner != null) && (i < _ringInner.Length)) ? _ringInner[i] : RING_INNER_DEFAULT;
                _meshes[i] = BuildRingMesh(inner, _arc);
                MeshFilter filter = _rings[i].GetComponent<MeshFilter>();
                if (filter != null)
                {
                    filter.sharedMesh = _meshes[i];
                }
            }
        }

        private static Mesh BuildRingMesh(float inner, float arc)
        {
            int segments = Mathf.Max(MIN_RING_SEGMENTS, Mathf.CeilToInt(RING_SEGMENTS * (arc / FULL_CIRCLE)));
            Vector3[] vertices = new Vector3[(segments + 1) * 2];
            Vector2[] uvs      = new Vector2[vertices.Length];
            Color[]   colors   = new Color[vertices.Length];
            int[]     tris     = new int[segments * 6];
            bool isFull = arc >= FULL_ARC;
            for (int i = 0; i <= segments; ++i)
            {
                // 정면(+Z)을 가운데로 양쪽으로 펼친다. 360이면 한 바퀴를 돌아 처음과 끝이 이어진다
                float a = ((-arc * 0.5f) + (arc * (isFull ? (i % segments) : i) / segments)) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                // 한 칸마다 밝기가 조금씩 달라 번쩍이는 불규칙한 가장자리가 된다
                float g = RING_LIGHT_BASE + (RING_LIGHT_RANGE * Mathf.PerlinNoise(i * RING_NOISE_SCALE, RING_NOISE_SEED));
                vertices[i * 2]       = dir * inner;
                vertices[(i * 2) + 1] = dir;
                uvs[i * 2]            = new Vector2(0f, 0f);
                uvs[(i * 2) + 1]      = new Vector2(1f, 0f);
                colors[i * 2]         = new Color(g, g, g, 1f);
                colors[(i * 2) + 1]   = new Color(g, g, g, 1f);
            }

            for (int i = 0; i < segments; ++i)
            {
                int v = i * 2;
                int t = i * 6;
                tris[t]     = v;     tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v + 1; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
            }

            Mesh mesh = new Mesh { name = "MucheolWaveRing" };
            mesh.vertices  = vertices;
            mesh.uv        = uvs;
            mesh.colors    = colors;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
