// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 부채꼴 지면 폭발 연출 한 개를 재생한다, 피해 판정과 네트워크는 다루지 않는다 </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SectorVFXController : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    MIN_SAMPLE_COUNT      = 8;
        private const int    MAX_SAMPLE_COUNT      = 512;
        private const int    DEFAULT_SAMPLE_COUNT  = 160;
        private const float  DEFAULT_GROUND_OFFSET = 0.035f;
        private const float  DEFAULT_ENERGY_HEIGHT = 0.8f;
        private const float  ANGLE_SEQUENCE_STEP   = 0.61803398875f;
        private const float  SEED_STEP             = 0.754877666f;
        private const float  ENERGY_OUTWARD_SPEED  = 0.35f;
        private const float  DEBRIS_OUTWARD_SPEED  = 0.5f;
        private const float  DEBRIS_RISE_SPEED     = 0.6f;
        private const float  ENERGY_SIZE_MIN       = 0.16f;
        private const float  ENERGY_SIZE_MAX       = 0.36f;
        private const float  DEBRIS_SIZE_MIN       = 0.08f;
        private const float  DEBRIS_SIZE_MAX       = 0.18f;
        private const string MESH_NAME             = "SectorGroundVFX Runtime Mesh";

        [SerializeField] private SectorShape _shape = SectorShape.Default;
        [SerializeField] private SectorBehaviour _behaviour = SectorBehaviour.Default;
        [SerializeField] private ParticleSystem _energy;
        [SerializeField] private ParticleSystem _debris;
        [SerializeField, Range(MIN_SAMPLE_COUNT, MAX_SAMPLE_COUNT)] private int _sampleCount = DEFAULT_SAMPLE_COUNT;
        [SerializeField, Min(0f)] private float _groundOffset = DEFAULT_GROUND_OFFSET;
        [SerializeField, Min(0f)] private float _energyHeight = DEFAULT_ENERGY_HEIGHT;
        [SerializeField, ColorUsage(true, true)] private Color _energyColor = new Color(3f, 0.55f, 0.08f, 1f);
        [SerializeField] private Color _debrisColor = new Color(0.3f, 0.23f, 0.18f, 0.45f);

        private static readonly int _clockId  = Shader.PropertyToID("_Clock");
        private static readonly int _timingId = Shader.PropertyToID("_Timing");
        private static readonly int _sweepId  = Shader.PropertyToID("_Sweep");
        private static readonly int _shapeId  = Shader.PropertyToID("_Shape");

        private Mesh _mesh;
        private MeshFilter _filter;
        private MeshRenderer _surface;
        private MaterialPropertyBlock _properties;
        private SectorShape _builtShape;
        private SectorShape _activeShape;
        private SectorBehaviour _activeBehaviour;
        private float _elapsed;
        private float _finishTime;
        private bool _initialized;
        private bool _playing;
        private bool[] _emitted;
        private Vector3[] _positions;
        private float[] _starts;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsPlaying => _playing;

        // 지면 소멸과 입자 꼬리가 모두 끝난 뒤에 불린다. 풀은 여기서 반환한다
        public event Action<SectorVFXController> Completed;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            Initialize();
        }

        private void OnDisable()
        {
            ResetForPool();
        }

        private void Update()
        {
            if (!_playing)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            UpdateVisual();

            if (_elapsed < _finishTime)
            {
                return;
            }

            ResetForPool();
            Completed?.Invoke(this);
        }

        private void OnValidate()
        {
            // 메시 재생성은 Play에서만 한다, 에디터 리페인트마다 하지 않는다
            _shape.Clamp();
            _behaviour.Clamp();
        }

        private void OnDestroy()
        {
            if (_mesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_mesh);
            }
            else
            {
                DestroyImmediate(_mesh);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 각도와 반경을 바꿔 재생한다, duration은 충전 시간이지 전체 길이가 아니다 </summary>
        public void Play(float angle, float radius, float duration)
        {
            SectorShape requested = _shape;
            requested.Angle = angle;
            requested.Radius = radius;

            SectorBehaviour timing = _behaviour;
            timing.TelegraphDuration = duration;

            Play(requested, timing);
        }

        [ContextMenu("Play Preview")]
        public void Play()
        {
            Play(_shape, _behaviour);
        }

        public void Play(SectorShape requestedShape, SectorBehaviour requestedBehaviour)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[SectorVFX] 플레이 모드에서만 재생할 수 있다", this);
                return;
            }

            Initialize();
            ResetForPool();

            requestedShape.Clamp();
            requestedBehaviour.Clamp();
            _activeShape = requestedShape;
            _activeBehaviour = requestedBehaviour;

            if ((_mesh.vertexCount == 0) || (!_builtShape.IsSameMesh(_activeShape)))
            {
                SectorMeshGenerator.Rebuild(_mesh, _activeShape);
                _builtShape = _activeShape;
            }

            PrepareSamples();

            float travel = (_activeBehaviour.Mode == ESectorExplosionMode.Simultaneous)
                ? 0f : _activeBehaviour.SweepDuration;
            float stagger = (_activeBehaviour.SegmentCount - 1) * _activeBehaviour.SegmentDelay;
            float visualEnd = _activeBehaviour.TelegraphDuration + travel + stagger
                + _activeBehaviour.ExplosionDuration + _activeBehaviour.DissolveDuration;
            float tails = Mathf.Max(MaxLifetime(_energy), MaxLifetime(_debris));
            _finishTime = Mathf.Max(visualEnd, _activeBehaviour.TelegraphDuration + travel + stagger + tails);

            _elapsed = 0f;
            _playing = true;
            enabled = true;
            _surface.enabled = true;

            _surface.GetPropertyBlock(_properties);
            _properties.SetVector(_timingId, new Vector4(_activeBehaviour.TelegraphDuration,
                _activeBehaviour.ExplosionDuration, _activeBehaviour.DissolveDuration, _activeBehaviour.SweepDuration));
            _properties.SetVector(_sweepId, new Vector4((int)_activeBehaviour.Mode, (int)_activeBehaviour.Direction,
                _activeBehaviour.SegmentCount, _activeBehaviour.SegmentDelay));
            _properties.SetVector(_shapeId, new Vector4(_activeShape.InnerRadius, _activeShape.Radius,
                _activeShape.Angle * Mathf.Deg2Rad, _groundOffset));
            UpdateVisual();
        }

        /// <summary> 즉시 화면과 입자를 지운다 </summary>
        public void Stop()
        {
            ResetForPool();
        }

        public void ResetForPool()
        {
            _playing = false;
            _elapsed = 0f;

            if (_surface != null)
            {
                _surface.enabled = false;
            }

            if (_energy != null)
            {
                _energy.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (_debris != null)
            {
                _debris.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            enabled = false;
        }

        /// <summary> 지점의 폭발 시작 시각, 지면 셰이더와 같은 식이다 </summary>
        public static float StartAt(float angularUV, float radialUV, SectorBehaviour timing)
        {
            float u = (timing.Direction == ESectorSweepDirection.RightToLeft) ? (1f - angularUV) : angularUV;
            int segment = Mathf.Min(timing.SegmentCount - 1, Mathf.FloorToInt(Mathf.Clamp01(u) * timing.SegmentCount));

            float travel = 0f;
            if (timing.Mode == ESectorExplosionMode.RadialSweep)
            {
                travel = radialUV;
            }
            else if (timing.Mode == ESectorExplosionMode.AngularSweep)
            {
                travel = u;
            }

            return timing.TelegraphDuration + (travel * timing.SweepDuration) + (segment * timing.SegmentDelay);
        }

        private void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _filter = GetComponent<MeshFilter>();
            _surface = GetComponent<MeshRenderer>();
            _properties = new MaterialPropertyBlock();
            _mesh = new Mesh { name = MESH_NAME };
            _mesh.MarkDynamic();
            _filter.sharedMesh = _mesh;
            _surface.enabled = false;
            _initialized = true;
            enabled = false;
        }

        private static float MaxLifetime(ParticleSystem system)
        {
            if (system == null)
            {
                return 0f;
            }

            ParticleSystem.MinMaxCurve life = system.main.startLifetime;
            return Mathf.Max(life.constantMax, life.constantMin, life.curveMultiplier, life.constant);
        }

        private void PrepareSamples()
        {
            int count = Mathf.Clamp(_sampleCount, MIN_SAMPLE_COUNT, MAX_SAMPLE_COUNT);
            if ((_positions == null) || (_positions.Length != count))
            {
                _positions = new Vector3[count];
                _starts = new float[count];
                _emitted = new bool[count];
            }

            Array.Clear(_emitted, 0, _emitted.Length);

            float innerSquared = _activeShape.InnerRadius * _activeShape.InnerRadius;
            float outerSquared = _activeShape.Radius * _activeShape.Radius;
            float span = _activeShape.Radius - _activeShape.InnerRadius;

            for (int i = 0; i < count; ++i)
            {
                // 반경은 면적이 같도록, 각도는 저불일치 수열로 고르게 뿌린다
                float u = Mathf.Repeat((i + 0.5f) * ANGLE_SEQUENCE_STEP, 1f);
                float r = Mathf.Sqrt(Mathf.Lerp(innerSquared, outerSquared, (i + 0.5f) / count));
                float v = (r - _activeShape.InnerRadius) / span;
                float a = (u - 0.5f) * _activeShape.Angle * Mathf.Deg2Rad;

                _positions[i] = new Vector3(Mathf.Sin(a) * r, _groundOffset, Mathf.Cos(a) * r);
                _starts[i] = StartAt(u, v, _activeBehaviour);
            }
        }

        private void UpdateVisual()
        {
            _properties.SetFloat(_clockId, _elapsed);
            _surface.SetPropertyBlock(_properties);

            for (int i = 0; i < _starts.Length; ++i)
            {
                if ((_emitted[i]) || (_elapsed < _starts[i]))
                {
                    continue;
                }

                _emitted[i] = true;

                // 프레임이 크게 튀어 수명을 통째로 건너뛴 표본은 뒤늦게 터뜨리지 않는다
                if ((_elapsed - _starts[i]) > _activeBehaviour.ExplosionDuration)
                {
                    continue;
                }

                Emit(_energy, _positions[i], i, false);
                Emit(_debris, _positions[i], i, true);
            }
        }

        private void Emit(ParticleSystem system, Vector3 local, int index, bool debris)
        {
            if (system == null)
            {
                return;
            }

            float seed = Mathf.Repeat(index * SEED_STEP, 1f);
            Vector3 outward = new Vector3(local.x, 0f, local.z).normalized;
            float outwardSpeed = debris ? DEBRIS_OUTWARD_SPEED : ENERGY_OUTWARD_SPEED;
            float riseSpeed = debris ? DEBRIS_RISE_SPEED : _energyHeight;
            Vector3 velocity = (outward * outwardSpeed) + (Vector3.up * riseSpeed);

            // 수명은 지정하지 않는다, 각 ParticleSystem의 startLifetime을 따른다
            ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
            {
                position = transform.TransformPoint(local),
                velocity = transform.TransformDirection(velocity),
                startSize = debris
                    ? Mathf.Lerp(DEBRIS_SIZE_MIN, DEBRIS_SIZE_MAX, seed)
                    : Mathf.Lerp(ENERGY_SIZE_MIN, ENERGY_SIZE_MAX, seed),
                startColor = debris ? _debrisColor : _energyColor
            };
            system.Emit(emitParams, 1);
        }
    }
}
