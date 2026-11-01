// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 무철 부채꼴 착탄 VFX. 각도와 반경만 받아 파티클 시스템의 부채꼴 호와 전체 크기를 맞추고,
    // 부채꼴 안을 다각형 판 조각(Voronoi)으로 쪼갠 크레이터와 틈의 용암 바닥을 만든다
    // 정면은 이 오브젝트의 +Z이고, 기준 반경 8m에서 만든 프리팹을 반경 비율로 키운다
    [DisallowMultipleComponent]
    public sealed class MucheolFanImpactView : MonoBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        private sealed class Plate
        {
            public Transform    Root;
            public MeshFilter   Filter;
            public MeshRenderer Renderer;
            public Mesh         Mesh;
            public float        Delay;   // s, 중심에서 멀수록 늦게 솟는다
            public float        Lift;    // m, 솟는 높이
            public float        Tilt;    // deg, 기울기
            public Vector3      Axis;    // 기울기 회전축
            public Vector3      Home;    // 판 중심의 바닥 위치
            public bool         Gone;    // 안쪽 판은 파편으로 날아가 사라져서 용암 구덩이를 드러낸다
            public int          Kind;    // PLATE_FIXED, PLATE_LAUNCHED, PLATE_CHIP
            public Vector3      Vel;     // m/s, 날아가는 판의 처음 속도
            public Vector3      SpinAxis;
            public float        SpinSpeed;
            public Vector3      Push;    // 솟으면서 바깥으로 밀려나는 거리. 부채꼴 밖으로 나가도 된다
            public float        Yaw;     // 솟으면서 제자리에서 도는 각도
            public Vector3      Scale = Vector3.one;   // 가장자리 바위는 두껍게 키운다
        }

        /****************************************
        *                Fields
        ****************************************/

        // 형태와 크기. 기준 반경 8m에서 만들고 반경 비율로 키운다
        private const float REFERENCE_RADIUS   = 8f;
        private const float MIN_ANGLE          = 10f;
        private const float MAX_ANGLE          = 360f;
        private const float MIN_RADIUS         = 0.5f;
        private const float FULL_ARC           = 359f;    // deg, 이 이상이면 사방으로 퍼지는 원형으로 본다
        private const float FULL_CIRCLE        = 360f;
        private const float CENTER_RATIO       = 0.5f;    // 부채꼴 중심이 시작점에서 반경의 이만큼 앞이다
        private const float MAX_HALF_ANGLE     = 180f;
        private const float FIT_HALF_ANGLE_MAX = 90f;
        private const float FIT_FULL_RATIO     = 0.4f;    // 원형일 때 중심 둘레 방출 반지름 비율
        private const float FIT_MARGIN         = 0.92f;
        private const float FIT_MIN_RADIUS     = 0.4f;
        private const float ARC_FORWARD_OFFSET = 90f;     // 원판은 +X에서 시작하므로 호의 가운데를 +Z로 돌리는 각도
        private const float MIN_DIRECTION_SQR  = 0.0001f;

        // 판 조각
        private const float PLATE_THICKNESS    = 0.26f;
        private const float PLATE_SPACING      = 0.9f;    // m, 판 조각 중심 사이의 평균 간격
        private const float PLATE_HIDDEN       = -0.21f;  // m, 처음 높이. 윗면이 바닥과 같은 높이라 틈만 보인다
        private const float PLATE_UV_SCALE     = 0.22f;
        private const float SHRINK_MIN         = 0.84f;   // 판마다 틈 두께가 다르다
        private const float SHRINK_MAX         = 0.95f;
        private const float SITE_JITTER        = 0.42f;
        private const float GHOST_MARGIN       = 3.4f;    // m, 부채꼴 밖에서 이웃 판의 모양만 잡아 주는 점을 두는 폭
        private const float CELL_BOX           = 3.2f;
        private const float NEIGHBOR_RANGE     = 1.6f;
        private const float KEEP_OUTER         = 0.42f;
        private const float KEEP_START         = 0.3f;
        private const float KEEP_SPAN          = 0.7f;
        private const float HOLE_START         = 2.5f;    // m, 이보다 먼 점은 일부 빼서 큰 판이 섞이게 한다
        private const float HOLE_KEEP_MIN      = 0.55f;
        private const float EDGE_OUTSIDE       = -1.5f;   // m, 경계 바깥으로 판이 나올 수 있는 한계
        private const float EDGE_INSIDE        = 0.8f;    // m, 이보다 안쪽은 경계와 상관없이 판이 된다
        private const float EDGE_RANDOM        = 0.8f;
        private const float GONE_DISTANCE      = 0.3f;    // 중심 가까이 이 비율 안의 판은 날아가 구덩이가 된다
        private const float GONE_CHANCE        = 0.75f;
        private const float RIM_START          = 0.28f;
        private const float RIM_SPAN           = 0.72f;
        private const float RIM_LIFT_MAX       = 1.3f;
        private const float RIM_LIFT_POWER     = 1.2f;
        private const float LIFT_RANDOM_MIN    = 0.35f;
        private const float LIFT_RANDOM_MAX    = 1.4f;
        private const float TILT_INNER         = 8f;
        private const float TILT_OUTER         = 55f;
        private const float TILT_RANDOM_MIN    = 0.35f;
        private const float TILT_RANDOM_MAX    = 1.25f;
        private const float EDGE_FROM          = 0.35f;
        private const float EDGE_SPAN          = 0.65f;
        private const float PUSH_MAX           = 1.3f;
        private const float YAW_MAX            = 45f;
        private const float CHUNK_EDGE         = 0.5f;
        private const float CHUNK_MIN          = 0.9f;
        private const float CHUNK_MAX          = 1.3f;
        private const float SCALE_Y_EXTRA      = 1.8f;
        private const float AXIS_JITTER_INNER  = 0.5f;
        private const float AXIS_JITTER_OUTER  = 1.1f;
        private const float PLATE_DELAY_JITTER = 0.04f;
        private const float LAUNCH_SPEED_MIN   = 8f;
        private const float LAUNCH_SPEED_MAX   = 20f;
        private const float LAUNCH_UP_MIN      = 8f;
        private const float LAUNCH_UP_MAX      = 14f;
        private const float LAUNCH_SPIN_MIN    = 150f;
        private const float LAUNCH_SPIN_MAX    = 500f;
        private const float LAUNCH_DELAY_MAX   = 0.06f;
        private const float TONE_MIN           = 0.65f;
        private const float TONE_MAX           = 1.45f;
        private const float HEAT_MIN           = 0.25f;
        private const float HEAT_RANDOM_MIN    = 0.4f;

        // 날아가는 작은 판 조각
        private const int   CHIP_COUNT         = 36;
        private const int   CHIP_SIDES_MIN     = 4;
        private const int   CHIP_SIDES_MAX     = 7;
        private const float CHIP_SIZE_MIN      = 0.22f;
        private const float CHIP_SIZE_MAX      = 0.6f;
        private const float CHIP_ANGLE_JITTER  = 0.3f;
        private const float CHIP_RADIUS_MIN    = 0.6f;
        private const float CHIP_RADIUS_MAX    = 1.1f;
        private const float CHIP_START_HEIGHT  = 0.1f;
        private const float CHIP_START_SPREAD  = 0.5f;
        private const float CHIP_SPEED_MIN     = 3f;
        private const float CHIP_SPEED_MAX     = 9.5f;
        private const float CHIP_UP_MIN        = 7f;
        private const float CHIP_UP_MAX        = 14f;
        private const float CHIP_DELAY_MAX     = 0.05f;
        private const float CHIP_SPIN_MIN      = 250f;
        private const float CHIP_SPIN_MAX      = 800f;

        // 가장자리에 솟는 두꺼운 바위
        private const int   RIM_BOULDER_COUNT  = 24;
        private const int   BOULDER_SIDES_MIN  = 5;
        private const int   BOULDER_SIDES_MAX  = 8;
        private const float BOULDER_SIZE_MIN   = 0.45f;
        private const float BOULDER_SIZE_MAX   = 1.05f;
        private const float BOULDER_ANGLE_JITTER = 0.25f;
        private const float BOULDER_RADIUS_MIN = 0.65f;
        private const float BOULDER_RADIUS_MAX = 1.1f;
        private const float BOULDER_DIST_MIN   = 0.78f;
        private const float BOULDER_DIST_MAX   = 1.12f;
        private const float BOULDER_DELAY_MAX  = 0.05f;
        private const float BOULDER_LIFT_MIN   = 0.5f;
        private const float BOULDER_LIFT_MAX   = 1.7f;
        private const float BOULDER_TILT_MIN   = 22f;
        private const float BOULDER_TILT_MAX   = 62f;
        private const float BOULDER_PUSH_MIN   = 0.2f;
        private const float BOULDER_PUSH_MAX   = 1.1f;
        private const float BOULDER_YAW_MAX    = 70f;
        private const float BOULDER_WIDTH_MIN  = 0.9f;
        private const float BOULDER_WIDTH_MAX  = 1.25f;
        private const float BOULDER_HEIGHT_MIN = 1.6f;
        private const float BOULDER_HEIGHT_MAX = 3.4f;

        // 시간
        private const float RISE_TIME          = 0.2f;    // s, 판 하나가 솟는 시간
        private const float SPREAD_TIME        = 0.26f;   // s, 중심에서 가장자리까지 판이 솟는 데 걸리는 시간
        private const float LAVA_START_DELAY   = 0.3f;    // s, 용암빛이 식기 시작하기 전 대기
        private const float LAVA_HOLD_TIME     = 0.8f;    // s, 갈라진 뒤 용암빛 유지
        private const float LAVA_COOL_TIME     = 1.2f;    // s, 이후 식어 가는 시간
        private const float SINK_TIME          = 0.5f;    // s, 마지막에 판이 가라앉는 시간
        private const float END_MARGIN         = 0.5f;    // s, 모두 끝난 뒤 정리까지 두는 여유
        private const float GRAVITY            = 22f;     // m/s^2, 날아가는 판에 쓰는 중력
        private const float LANDED_HEIGHT      = -0.2f;   // m, 날아가는 판이 이 아래로 내려가면 사라진다

        // 섬광 라이트
        private const float FLASH_PEAK         = 90f;     // 최대 세기
        private const float FLASH_HEIGHT       = 1.8f;    // m
        private const float FLASH_RANGE        = 15f;     // m, 기준 반경일 때
        private const float LIGHT_SECONDS      = 1.2f;
        private const float LIGHT_DECAY        = 7f;
        private const float LIGHT_ATTACK       = 0.02f;

        // 용암 바닥과 탄 돌바닥
        private const int   LAVA_RINGS         = 26;
        private const int   LAVA_MIN_SEGMENTS  = 10;
        private const float LAVA_SEGMENT_DEG   = 2.5f;
        private const float LAVA_HEIGHT        = 0.012f;
        private const float LAVA_UV_SCALE      = 0.18f;
        private const float LAVA_FALLOFF       = 0.8f;
        private const float LAVA_APEX_EDGE     = 0.18f;
        private const float LAVA_EDGE_LIGHT    = 0.55f;
        private const float LAVA_LIGHT_MIN     = 0.8f;
        private const float LAVA_LIGHT_MAX     = 1.2f;
        private const float EDGE_WOBBLE_RANGE  = 1.6f;
        private const float EDGE_WOBBLE_AMOUNT = 1.5f;
        private const float EDGE_NOISE_SCALE   = 0.8f;
        private const float EDGE_FEATHER       = 0.9f;
        private const float NOISE_SEED_RANGE   = 100f;

        // 목록 초기 크기
        private const int   PLATE_CAPACITY     = 128;
        private const int   VERTEX_CAPACITY    = 64;
        private const int   SITE_CAPACITY      = 384;
        private const int   CELL_CAPACITY      = 10;      // 네모에서 시작해 이웃 점으로 잘라 가며 늘어나는 꼭짓점 수

        // 판 종류
        private const int   PLATE_FIXED        = 0;   // 제자리에서 솟는 판
        private const int   PLATE_LAUNCHED     = 1;   // 처음엔 제자리였다가 날아가는 안쪽 판
        private const int   PLATE_CHIP         = 2;   // 날아갈 때만 보이는 작은 조각

        private const float TAU                = 6.2831855f;

        private static readonly int REVEAL_ID         = Shader.PropertyToID("_Reveal");
        private static readonly int FADE_ID           = Shader.PropertyToID("_Fade");
        private static readonly int BASE_COLOR_ID     = Shader.PropertyToID("_BaseColor");
        private static readonly int EMISSION_COLOR_ID = Shader.PropertyToID("_EmissionColor");

        private static readonly Color PLATE_BASE_COLOR     = new Color(0.15f, 0.125f, 0.175f, 1f);
        private static readonly Color PLATE_EMISSION_COLOR = new Color(3.2f, 0.3f, 0.05f, 1f);

        [SerializeField, Range(MIN_ANGLE, MAX_ANGLE)] private float _angle  = 90f;  // deg, 부채꼴 전체 각도
        [SerializeField, Min(MIN_RADIUS)]             private float _radius = 8f;   // m, 부채꼴 반경
        [SerializeField] private ParticleSystem[] _systems;         // 재생과 정지를 받는 전체
        [SerializeField] private ParticleSystem[] _radialSystems;   // 시작점에서 부채꼴 호를 따라 바깥으로 퍼지는 것
        [SerializeField] private ParticleSystem[] _centerSystems;   // 부채꼴 한가운데에서 터지는 것
        [SerializeField] private float[]          _centerRadius;    // 한가운데 방출 반지름 배율(맞춤 반지름 기준)
        [SerializeField] private float[]          _centerHeight;    // m, 방출 높이
        [SerializeField] private Material         _plateMaterial;
        [SerializeField] private Material         _sideMaterial;    // 판 두께 옆면, 열기로 주황빛
        [SerializeField] private MeshFilter       _charFilter;      // 불에 탄 돌 바닥
        [SerializeField] private MeshRenderer     _char;
        [SerializeField] private Light            _flashLight;
        [SerializeField] private MeshFilter       _lavaFilter;
        [SerializeField] private MeshRenderer     _lava;

        private readonly List<Plate>   _plates        = new List<Plate>(PLATE_CAPACITY);
        private readonly List<Vector3> _vertices      = new List<Vector3>(VERTEX_CAPACITY);
        private readonly List<int>     _triangles     = new List<int>(PLATE_CAPACITY);
        private readonly List<int>     _sideTriangles = new List<int>(PLATE_CAPACITY);
        private readonly List<Vector2> _uvs           = new List<Vector2>(VERTEX_CAPACITY);
        private Transform              _plateParent;
        private MaterialPropertyBlock  _block;
        private Mesh                   _lavaMesh;
        private float                  _playTime = -1f;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 재생 중인가, 풀에서 놀고 있는 것을 고를 때 쓴다 </summary>
        public bool IsPlaying => _playTime >= 0f;

        /****************************************
        *               Unity Events
        ****************************************/

        private void Update()
        {
            if (_playTime < 0f)
            {
                return;
            }

            float t = Time.time - _playTime;
            Evaluate(t);
            if (t > (LAVA_START_DELAY + LAVA_HOLD_TIME + LAVA_COOL_TIME + SINK_TIME + END_MARGIN))
            {
                Hide();
                _playTime = -1f;
            }
        }

        private void OnDisable()
        {
            _playTime = -1f;
            Hide();
        }

        private void OnValidate()
        {
            if ((_systems != null) && (_systems.Length > 0) && (_radialSystems != null) && (_centerSystems != null))
            {
                ApplyShape();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Setup(float angle, float radius)
        {
            _angle  = Mathf.Clamp(angle, MIN_ANGLE, MAX_ANGLE);
            _radius = Mathf.Max(MIN_RADIUS, radius);
            ApplyShape();
        }

        public void Play()
        {
            ApplyShape();
            Build(Random.Range(0, int.MaxValue));

            for (int i = 0; i < _systems.Length; ++i)
            {
                _systems[i].Clear(false);
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
            Hide();
        }

        // 부채꼴 경계까지의 부호 있는 거리(m). 안쪽이 양수이고, 호와 옆변 중 가까운 쪽 기준
        private float SectorDistance(Vector2 p, float half, bool full)
        {
            float radial = REFERENCE_RADIUS - p.magnitude;
            if (full)
            {
                return radial;
            }

            float a = Mathf.Atan2(p.x, p.y) * Mathf.Rad2Deg;
            float side = (half - Mathf.Abs(a)) * Mathf.Deg2Rad * Mathf.Max(p.magnitude, MIN_DIRECTION_SQR);
            return Mathf.Min(radial, side);
        }

        // 부채꼴의 한가운데(터지는 중심). 360도면 시작점이 곧 중심이다
        private Vector2 Center()
        {
            return (_angle >= FULL_ARC) ? Vector2.zero : new Vector2(0f, REFERENCE_RADIUS * CENTER_RATIO);
        }

        // 중심에서 부채꼴 안 가장 먼 곳까지의 거리. 판과 용암의 퍼짐 기준
        private float CenterSpan()
        {
            Vector2 c = Center();
            float half = Mathf.Min(_angle * 0.5f, MAX_HALF_ANGLE) * Mathf.Deg2Rad;
            float span = REFERENCE_RADIUS * CENTER_RATIO;
            span = Mathf.Max(span, c.magnitude);
            span = Mathf.Max(span, (new Vector2(Mathf.Sin(half), Mathf.Cos(half)) * REFERENCE_RADIUS - c).magnitude);
            span = Mathf.Max(span, (new Vector2(-Mathf.Sin(half), Mathf.Cos(half)) * REFERENCE_RADIUS - c).magnitude);
            span = Mathf.Max(span, (new Vector2(0f, REFERENCE_RADIUS) - c).magnitude);
            return span;
        }

        // 중심 둘레의 원판이 부채꼴 밖으로 나가지 않게 맞춘 반지름
        private float FitRadius()
        {
            if (_angle >= FULL_ARC)
            {
                return REFERENCE_RADIUS * FIT_FULL_RATIO;
            }

            float cz = REFERENCE_RADIUS * CENTER_RATIO;
            float sin = Mathf.Sin(Mathf.Min(_angle * 0.5f, FIT_HALF_ANGLE_MAX) * Mathf.Deg2Rad);
            return Mathf.Max(FIT_MIN_RADIUS, Mathf.Min(cz * sin, REFERENCE_RADIUS - cz) * FIT_MARGIN);
        }

        private void ApplyShape()
        {
            float scale = _radius / REFERENCE_RADIUS;
            transform.localScale = new Vector3(scale, scale, scale);

            for (int i = 0; i < _radialSystems.Length; ++i)
            {
                ParticleSystem.ShapeModule shape = _radialSystems[i].shape;

                // 원판은 +X에서 시작해 +Z 쪽으로 호가 늘어나므로, 호의 가운데가 정면(+Z)에 오게 돌린다
                shape.arc      = _angle;
                shape.rotation = new Vector3(0f, 0f, ARC_FORWARD_OFFSET - (_angle * 0.5f));
            }

            float fit = FitRadius();
            Vector2 c = Center();
            if (_flashLight != null)
            {
                _flashLight.transform.localPosition = new Vector3(0f, FLASH_HEIGHT, c.y);
                _flashLight.range = FLASH_RANGE * scale;
            }

            for (int i = 0; i < _centerSystems.Length; ++i)
            {
                ParticleSystem.ShapeModule shape = _centerSystems[i].shape;
                shape.arc      = FULL_CIRCLE;
                shape.rotation = Vector3.zero;
                shape.radius   = fit * _centerRadius[i];
                // 계통이 90도 눕혀져 있어 로컬 +Y가 월드 +Z, 로컬 -Z가 월드 위쪽이다
                shape.position = new Vector3(0f, c.y, -_centerHeight[i]);
            }
        }

        private void Hide()
        {
            for (int i = 0; i < _plates.Count; ++i)
            {
                _plates[i].Root.gameObject.SetActive(false);
            }

            SetLava(0f, 0f);
            if (_flashLight != null)
            {
                _flashLight.intensity = 0f;
            }
        }

        private void Evaluate(float t)
        {
            float spread = Mathf.Clamp01(t / SPREAD_TIME);
            spread = 1f - ((1f - spread) * (1f - spread));

            float cool = Mathf.Clamp01((t - LAVA_START_DELAY - LAVA_HOLD_TIME) / LAVA_COOL_TIME);
            SetLava(spread, 1f - (cool * cool));

            if (_flashLight != null)
            {
                _flashLight.intensity = (t < LIGHT_SECONDS) ? (FLASH_PEAK * Mathf.Exp(-t * LIGHT_DECAY) * Mathf.Clamp01(t / LIGHT_ATTACK)) : 0f;
            }

            float sink = Mathf.Clamp01((t - LAVA_START_DELAY - LAVA_HOLD_TIME - LAVA_COOL_TIME) / SINK_TIME);
            for (int i = 0; i < _plates.Count; ++i)
            {
                Plate plate = _plates[i];
                float local = t - plate.Delay;

                if (plate.Kind != PLATE_FIXED)
                {
                    // 날아가는 판: 포물선으로 튀었다가 바닥 높이 아래로 내려가면 사라진다
                    bool fly = local >= 0f;
                    float y = (plate.Home.y + (plate.Vel.y * local)) - (0.5f * GRAVITY * local * local);
                    bool airborne = (fly) && (y > LANDED_HEIGHT);
                    bool still = (plate.Kind == PLATE_LAUNCHED) && (!fly) && (sink < 1f);
                    plate.Root.gameObject.SetActive(airborne || still);
                    if (airborne)
                    {
                        plate.Root.localPosition = new Vector3(plate.Home.x + (plate.Vel.x * local), y, plate.Home.z + (plate.Vel.z * local));
                        plate.Root.localRotation = Quaternion.AngleAxis(plate.SpinSpeed * local, plate.SpinAxis);
                    }
                    else if (still)
                    {
                        plate.Root.localPosition = new Vector3(plate.Home.x, PLATE_HIDDEN, plate.Home.z);
                        plate.Root.localRotation = Quaternion.identity;
                    }

                    continue;
                }

                float p = Mathf.Clamp01(local / RISE_TIME);
                float rise = 1f - ((1f - p) * (1f - p) * (1f - p));
                bool visible = sink < 1f;
                plate.Root.gameObject.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                float h = Mathf.Lerp(PLATE_HIDDEN, plate.Lift, rise);
                h = Mathf.Lerp(h, PLATE_HIDDEN, sink * sink);
                plate.Root.localPosition = new Vector3(plate.Home.x + (plate.Push.x * rise), h, plate.Home.z + (plate.Push.z * rise));
                plate.Root.localRotation = Quaternion.AngleAxis(plate.Yaw * rise, Vector3.up) * Quaternion.AngleAxis(plate.Tilt * rise, plate.Axis);
                plate.Root.localScale    = Vector3.Lerp(Vector3.one, plate.Scale, rise);
            }
        }

        private void SetLava(float reveal, float fade)
        {
            SetChar(reveal, (fade > 0f) ? 1f : 0f);
            if (_lava == null)
            {
                return;
            }

            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }

            _lava.GetPropertyBlock(_block);
            _block.SetFloat(REVEAL_ID, reveal);
            _block.SetFloat(FADE_ID, fade);
            _lava.SetPropertyBlock(_block);
        }

        private void SetChar(float reveal, float fade)
        {
            if (_char == null)
            {
                return;
            }

            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }

            _char.GetPropertyBlock(_block);
            _block.SetFloat(REVEAL_ID, reveal);
            _block.SetFloat(FADE_ID, fade);
            _char.SetPropertyBlock(_block);
        }

        private void Build(int seed)
        {
            Random.State saved = Random.state;
            Random.InitState(seed);

            BuildLava();
            BuildPlates();

            Random.state = saved;
        }

        // 부채꼴 모양의 바닥 면. 중심이 밝고 가장자리로 갈수록 어두운 정점 색을 준다. UV.x는 중심에서의 거리 비율
        private void BuildLava()
        {
            if ((_lavaFilter == null) || (_plateMaterial == null))
            {
                return;
            }

            int segs   = Mathf.Max(LAVA_MIN_SEGMENTS, Mathf.CeilToInt(_angle / LAVA_SEGMENT_DEG));
            float half = _angle * 0.5f;

            List<Vector3> vertices = new List<Vector3>((LAVA_RINGS * (segs + 1)) + 1);
            List<Vector2> uvs      = new List<Vector2>(vertices.Capacity);
            List<Vector2> reveal   = new List<Vector2>(vertices.Capacity);
            List<Color>   colors   = new List<Color>(vertices.Capacity);
            List<int>     tris     = new List<int>(LAVA_RINGS * segs * 6);

            Vector2 center = Center();
            float span = CenterSpan();
            bool full = _angle >= FULL_ARC;
            float seedX = Random.value * NOISE_SEED_RANGE;
            float seedY = Random.value * NOISE_SEED_RANGE;

            vertices.Add(new Vector3(0f, LAVA_HEIGHT, 0f));
            float d0 = Mathf.Clamp01(center.magnitude / span);
            uvs.Add(Vector2.zero);
            reveal.Add(new Vector2(d0, 0f));
            float g0 = Mathf.Lerp(1f, LAVA_APEX_EDGE, Mathf.Pow(d0, LAVA_FALLOFF));
            colors.Add(new Color(g0, g0, g0, 1f));

            for (int r = 1; r <= LAVA_RINGS; ++r)
            {
                float d = (float)r / LAVA_RINGS;
                for (int s = 0; s <= segs; ++s)
                {
                    float a = (-half + (_angle * s / segs)) * Mathf.Deg2Rad;
                    Vector2 pos = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (REFERENCE_RADIUS * d);

                    // 경계 근처 정점을 안팎으로 흔들고 가장자리를 서서히 투명하게 해서 직선으로 잘려 보이지 않게 한다
                    float sd = SectorDistance(pos, half, full);
                    float edgeWeight = 1f - Mathf.Clamp01(sd / EDGE_WOBBLE_RANGE);
                    float wobble = (Mathf.PerlinNoise((pos.x * EDGE_NOISE_SCALE) + seedX, (pos.y * EDGE_NOISE_SCALE) + seedY) - 0.5f) * 2f;
                    Vector2 outDir = ((pos - center).sqrMagnitude > MIN_DIRECTION_SQR) ? (pos - center).normalized : Vector2.up;
                    pos += outDir * (wobble * EDGE_WOBBLE_AMOUNT * edgeWeight);
                    float feather = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sd / EDGE_FEATHER));
                    vertices.Add(new Vector3(pos.x, LAVA_HEIGHT, pos.y));
                    float cd = Mathf.Clamp01((pos - center).magnitude / span);
                    uvs.Add(pos * LAVA_UV_SCALE);
                    reveal.Add(new Vector2(cd, 0f));

                    // 중심이 조금 더 달아오르고 정점마다 밝기가 달라 얼룩덜룩하다
                    float g = Mathf.Lerp(1f, LAVA_EDGE_LIGHT, Mathf.Pow(cd, LAVA_FALLOFF)) * Random.Range(LAVA_LIGHT_MIN, LAVA_LIGHT_MAX);
                    colors.Add(new Color(g, g, g, feather));
                }
            }

            int stride = segs + 1;
            for (int s = 0; s < segs; ++s)
            {
                tris.Add(0); tris.Add(1 + s + 1); tris.Add(1 + s);
            }

            for (int r = 1; r < LAVA_RINGS; ++r)
            {
                int a0 = 1 + ((r - 1) * stride);
                int b0 = 1 + (r * stride);
                for (int s = 0; s < segs; ++s)
                {
                    tris.Add(a0 + s);     tris.Add(a0 + s + 1); tris.Add(b0 + s);
                    tris.Add(a0 + s + 1); tris.Add(b0 + s + 1); tris.Add(b0 + s);
                }
            }

            if (_lavaMesh == null)
            {
                _lavaMesh = new Mesh { name = "FanLava" };
            }

            _lavaMesh.Clear();
            _lavaMesh.SetVertices(vertices);
            _lavaMesh.SetUVs(0, uvs);
            _lavaMesh.SetUVs(1, reveal);
            _lavaMesh.SetColors(colors);
            _lavaMesh.SetTriangles(tris, 0);
            _lavaMesh.RecalculateBounds();
            _lavaFilter.sharedMesh = _lavaMesh;
            if (_charFilter != null)
            {
                _charFilter.sharedMesh = _lavaMesh;
            }
        }

        private void BuildPlates()
        {
            if (_plateParent == null)
            {
                GameObject parent = new GameObject("Plates");
                _plateParent = parent.transform;
                _plateParent.SetParent(transform, false);
            }

            float half = _angle * 0.5f;
            bool  full = _angle >= FULL_ARC;

            // 간격을 흩뜨린 격자에서 점을 고른다. 경계 근처는 안팎이 섞이게 뽑고, 바깥 점은 판으로 만들지 않고 이웃 판의 모양만 잡아 준다
            List<Vector2> sites = new List<Vector2>(SITE_CAPACITY);
            List<bool>    real  = new List<bool>(SITE_CAPACITY);
            int cells = Mathf.CeilToInt((REFERENCE_RADIUS + GHOST_MARGIN) / PLATE_SPACING) + 1;
            for (int i = -cells; i <= cells; ++i)
            {
                for (int j = -cells; j <= cells; ++j)
                {
                    Vector2 p = new Vector2(
                        (i + Random.Range(-SITE_JITTER, SITE_JITTER)) * PLATE_SPACING,
                        (j + Random.Range(-SITE_JITTER, SITE_JITTER)) * PLATE_SPACING);
                    float sd = SectorDistance(p, half, full);
                    if (sd < -GHOST_MARGIN)
                    {
                        continue;
                    }

                    float d = p.magnitude;
                    float keep = Mathf.Lerp(1f, KEEP_OUTER, Mathf.Clamp01((d / REFERENCE_RADIUS - KEEP_START) / KEEP_SPAN));
                    if (d > HOLE_START)
                    {
                        keep *= Random.Range(HOLE_KEEP_MIN, 1f);   // 군데군데 점을 빼서 큰 판이 섞이게 한다
                    }

                    // 경계 둘레에서 판이 될 확률을 무작위로 흔든다. 일부는 부채꼴 밖에서도 판이 된다
                    float edgeChance = Mathf.InverseLerp(EDGE_OUTSIDE, EDGE_INSIDE, sd + Random.Range(-EDGE_RANDOM, EDGE_RANDOM));
                    bool isReal = (sd > EDGE_INSIDE) ? (Random.value <= keep) : (Random.value <= (keep * Mathf.Clamp01(edgeChance)));
                    if ((!isReal) && (sd > EDGE_INSIDE))
                    {
                        continue;   // 안쪽에서 뺀 점은 큰 판을 만들도록 그냥 없앤다
                    }

                    sites.Add(p);
                    real.Add(isReal);
                }
            }

            if (sites.Count == 0)
            {
                sites.Add(Vector2.up);
                real.Add(true);
            }

            int used = 0;
            for (int s = 0; s < sites.Count; ++s)
            {
                if (!real[s])
                {
                    continue;
                }

                List<Vector2> poly = MakeCell(sites, s);
                if (poly == null)
                {
                    continue;
                }

                Plate plate = GetPlate(used);
                ++used;
                ShapePlate(plate, poly);
            }

            // 폭발로 튀는 작은 판 조각
            Vector2 c0 = Center();
            float fit = FitRadius();
            for (int k = 0; k < CHIP_COUNT; ++k)
            {
                List<Vector2> chip = MakePolygon(Random.Range(CHIP_SIDES_MIN, CHIP_SIDES_MAX), Random.Range(CHIP_SIZE_MIN, CHIP_SIZE_MAX),
                    CHIP_ANGLE_JITTER, CHIP_RADIUS_MIN, CHIP_RADIUS_MAX);

                Plate plate = GetPlate(used);
                ++used;
                ShapePlate(plate, chip);
                Vector2 start = c0 + (Random.insideUnitCircle * fit * CHIP_START_SPREAD);
                Vector2 dir = Random.insideUnitCircle.normalized;
                float speed = Random.Range(CHIP_SPEED_MIN, CHIP_SPEED_MAX);
                plate.Kind = PLATE_CHIP;
                plate.Gone = false;
                plate.Home = new Vector3(start.x, CHIP_START_HEIGHT, start.y);
                plate.Vel = new Vector3(dir.x * speed, Random.Range(CHIP_UP_MIN, CHIP_UP_MAX), dir.y * speed);
                plate.Delay = Random.Range(0f, CHIP_DELAY_MAX);
                plate.SpinAxis = Random.onUnitSphere;
                plate.SpinSpeed = Random.Range(CHIP_SPIN_MIN, CHIP_SPIN_MAX);
                plate.Root.gameObject.SetActive(false);
            }

            // 가장자리에 솟는 두꺼운 바위 덩어리. 부채꼴 바깥까지 삐져나와도 된다
            float span = CenterSpan();
            for (int k = 0; k < RIM_BOULDER_COUNT; ++k)
            {
                List<Vector2> rock = MakePolygon(Random.Range(BOULDER_SIDES_MIN, BOULDER_SIDES_MAX), Random.Range(BOULDER_SIZE_MIN, BOULDER_SIZE_MAX),
                    BOULDER_ANGLE_JITTER, BOULDER_RADIUS_MIN, BOULDER_RADIUS_MAX);

                float ang = full ? Random.Range(0f, TAU) : Random.Range(-half, half) * Mathf.Deg2Rad;
                float dist = REFERENCE_RADIUS * Random.Range(BOULDER_DIST_MIN, BOULDER_DIST_MAX);
                Vector2 pos = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang)) * dist;

                Plate plate = GetPlate(used);
                ++used;
                ShapePlate(plate, rock);
                Vector2 rel = pos - Center();
                Vector2 outDir = (rel.sqrMagnitude > MIN_DIRECTION_SQR) ? rel.normalized : Vector2.up;
                float dd = Mathf.Clamp01(rel.magnitude / span);
                plate.Kind = PLATE_FIXED;
                plate.Gone = false;
                plate.Home = new Vector3(pos.x, 0f, pos.y);
                plate.Delay = (dd * SPREAD_TIME) + Random.Range(0f, BOULDER_DELAY_MAX);
                plate.Lift = Random.Range(BOULDER_LIFT_MIN, BOULDER_LIFT_MAX);
                plate.Tilt = Random.Range(BOULDER_TILT_MIN, BOULDER_TILT_MAX);
                float ax = Random.Range(0f, TAU);
                plate.Axis = new Vector3(Mathf.Cos(ax), 0f, Mathf.Sin(ax));
                plate.Push = new Vector3(outDir.x, 0f, outDir.y) * Random.Range(BOULDER_PUSH_MIN, BOULDER_PUSH_MAX);
                plate.Yaw = Random.Range(-BOULDER_YAW_MAX, BOULDER_YAW_MAX);
                float width = Random.Range(BOULDER_WIDTH_MIN, BOULDER_WIDTH_MAX);
                plate.Scale = new Vector3(width, Random.Range(BOULDER_HEIGHT_MIN, BOULDER_HEIGHT_MAX), width);
                plate.Root.localPosition = new Vector3(plate.Home.x, PLATE_HIDDEN, plate.Home.z);
                plate.Root.gameObject.SetActive(true);
            }

            for (int i = used; i < _plates.Count; ++i)
            {
                _plates[i].Root.gameObject.SetActive(false);
            }

            if (used < _plates.Count)
            {
                _plates.RemoveRange(used, _plates.Count - used);
            }
        }

        // 원점 둘레에 울퉁불퉁한 다각형을 만든다. 칸마다 각도와 반지름이 조금씩 흔들린다
        private static List<Vector2> MakePolygon(int sides, float size, float angleJitter, float radiusMin, float radiusMax)
        {
            List<Vector2> polygon = new List<Vector2>(sides);
            float offset = Random.Range(0f, TAU);
            for (int v = 0; v < sides; ++v)
            {
                float a = offset + (v * TAU / sides) + Random.Range(-angleJitter, angleJitter);
                float r = size * Random.Range(radiusMin, radiusMax);
                polygon.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r));
            }

            return polygon;
        }

        private List<Vector2> MakeCell(List<Vector2> sites, int index)
        {
            Vector2 site = sites[index];
            List<Vector2> poly = new List<Vector2>(CELL_CAPACITY)
            {
                site + new Vector2(-CELL_BOX, -CELL_BOX), site + new Vector2(CELL_BOX, -CELL_BOX),
                site + new Vector2(CELL_BOX, CELL_BOX),   site + new Vector2(-CELL_BOX, CELL_BOX)
            };

            float neighborRange = CELL_BOX * NEIGHBOR_RANGE;
            for (int k = 0; k < sites.Count; ++k)
            {
                if (k == index)
                {
                    continue;
                }

                Vector2 other = sites[k];
                if ((other - site).sqrMagnitude > (neighborRange * neighborRange))
                {
                    continue;
                }

                // 두 점의 수직이등분선 안쪽(site 쪽)만 남긴다
                poly = Clip(poly, (site + other) * 0.5f, (site - other).normalized);
                if (poly.Count < 3)
                {
                    return null;
                }
            }

            return (poly.Count >= 3) ? poly : null;
        }

        // n 방향 반평면(p를 지나고 법선 n)에서 안쪽(dot >= 0)만 남긴다
        private static List<Vector2> Clip(List<Vector2> poly, Vector2 p, Vector2 n)
        {
            List<Vector2> result = new List<Vector2>(poly.Count + 2);
            for (int i = 0; i < poly.Count; ++i)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % poly.Count];
                float da = Vector2.Dot(a - p, n);
                float db = Vector2.Dot(b - p, n);
                if (da >= 0f)
                {
                    result.Add(a);
                }

                if ((da >= 0f) != (db >= 0f))
                {
                    result.Add(a + ((b - a) * (da / (da - db))));
                }
            }

            return result;
        }

        private Plate GetPlate(int index)
        {
            if (index < _plates.Count)
            {
                return _plates[index];
            }

            GameObject go = new GameObject("Plate");
            go.transform.SetParent(_plateParent, false);
            Plate plate = new Plate
            {
                Root   = go.transform,
                Filter = go.AddComponent<MeshFilter>(),
                Mesh   = new Mesh { name = "FanPlate" }
            };

            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            plate.Renderer = renderer;
            renderer.sharedMaterials   = new[] { _plateMaterial, _sideMaterial };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            plate.Filter.sharedMesh = plate.Mesh;
            _plates.Add(plate);
            return plate;
        }

        // 판 하나의 윗면과 옆면을 만든다. 중심을 기준으로 회전하도록 정점은 중심 상대 좌표로 둔다
        private void ShapePlate(Plate plate, List<Vector2> poly)
        {
            Vector2 c = Vector2.zero;
            for (int i = 0; i < poly.Count; ++i)
            {
                c += poly[i];
            }

            c /= poly.Count;

            // 틈이 생기게 중심 쪽으로 조금 줄인다
            float shrink = Random.Range(SHRINK_MIN, SHRINK_MAX);
            int n = poly.Count;
            Vector2[] ring = new Vector2[n];
            for (int i = 0; i < n; ++i)
            {
                ring[i] = (poly[i] - c) * shrink;
            }

            _vertices.Clear();
            _triangles.Clear();
            _sideTriangles.Clear();
            _uvs.Clear();

            // 윗면
            for (int i = 0; i < n; ++i)
            {
                _vertices.Add(new Vector3(ring[i].x, PLATE_THICKNESS, ring[i].y));
                _uvs.Add((c + ring[i]) * PLATE_UV_SCALE);
            }

            _vertices.Add(new Vector3(0f, PLATE_THICKNESS, 0f));
            _uvs.Add(c * PLATE_UV_SCALE);
            for (int i = 0; i < n; ++i)
            {
                _triangles.Add(n); _triangles.Add((i + 1) % n); _triangles.Add(i);
            }

            // 옆면은 윗면과 따로 정점을 두어 각진 음영을 낸다
            for (int i = 0; i < n; ++i)
            {
                int j = (i + 1) % n;
                int b = _vertices.Count;
                _vertices.Add(new Vector3(ring[i].x, PLATE_THICKNESS, ring[i].y));
                _vertices.Add(new Vector3(ring[j].x, PLATE_THICKNESS, ring[j].y));
                _vertices.Add(new Vector3(ring[j].x, 0f, ring[j].y));
                _vertices.Add(new Vector3(ring[i].x, 0f, ring[i].y));
                for (int u = 0; u < 4; ++u)
                {
                    _uvs.Add(c * PLATE_UV_SCALE);   // 옆면은 윗면 무늬의 한 점만 써서 어둡게 둔다
                }

                _sideTriangles.Add(b); _sideTriangles.Add(b + 1); _sideTriangles.Add(b + 2);
                _sideTriangles.Add(b); _sideTriangles.Add(b + 2); _sideTriangles.Add(b + 3);
            }

            plate.Push  = Vector3.zero;
            plate.Yaw   = 0f;
            plate.Scale = Vector3.one;
            plate.Root.localScale = Vector3.one;

            plate.Mesh.Clear();
            plate.Mesh.SetVertices(_vertices);
            plate.Mesh.SetUVs(0, _uvs);
            plate.Mesh.subMeshCount = 2;
            plate.Mesh.SetTriangles(_triangles, 0);
            plate.Mesh.SetTriangles(_sideTriangles, 1);
            plate.Mesh.RecalculateNormals();
            plate.Mesh.RecalculateBounds();

            Vector2 rel = c - Center();
            float d = Mathf.Clamp01(rel.magnitude / CenterSpan());
            plate.Home  = new Vector3(c.x, 0f, c.y);
            plate.Delay = (d * SPREAD_TIME) + Random.Range(0f, PLATE_DELAY_JITTER);

            // 안쪽은 파편으로 날아가 구덩이가 되고, 바깥쪽 판은 바깥 모서리가 들려 테두리가 높아진다
            plate.Gone = (d < GONE_DISTANCE) && (Random.value < GONE_CHANCE);
            plate.Kind = plate.Gone ? PLATE_LAUNCHED : PLATE_FIXED;
            float rim  = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - RIM_START) / RIM_SPAN));
            plate.Lift = Mathf.Lerp(0f, RIM_LIFT_MAX, Mathf.Pow(rim, RIM_LIFT_POWER)) * Random.Range(LIFT_RANDOM_MIN, LIFT_RANDOM_MAX);
            plate.Tilt = Mathf.Lerp(TILT_INNER, TILT_OUTER, rim) * Random.Range(TILT_RANDOM_MIN, TILT_RANDOM_MAX);

            Vector2 radial = (rel.sqrMagnitude > MIN_DIRECTION_SQR) ? rel.normalized : Vector2.up;
            float edge = Mathf.Clamp01((rim - EDGE_FROM) / EDGE_SPAN);
            plate.Push  = new Vector3(radial.x, 0f, radial.y) * (Random.Range(0f, PUSH_MAX) * edge);
            plate.Yaw   = Random.Range(-YAW_MAX, YAW_MAX) * edge;
            float chunk = (edge > CHUNK_EDGE) ? Random.Range(CHUNK_MIN, CHUNK_MAX) : 1f;
            plate.Scale = new Vector3(chunk, 1f + (Random.Range(0f, SCALE_Y_EXTRA) * edge), chunk);

            // 가장자리는 기울이는 방향이 제각각이다
            float jitter = (Random.Range(-AXIS_JITTER_INNER, AXIS_JITTER_INNER) * (1f - rim)) + (Random.Range(-AXIS_JITTER_OUTER, AXIS_JITTER_OUTER) * rim);
            float cs = Mathf.Cos(jitter);
            float sn = Mathf.Sin(jitter);
            plate.Axis  = new Vector3(-radial.y * cs, 0f, radial.x * cs) + new Vector3(radial.x * sn, 0f, radial.y * sn);
            if (plate.Axis.sqrMagnitude < MIN_DIRECTION_SQR)
            {
                plate.Axis = Vector3.right;
            }

            plate.Axis.Normalize();

            if (plate.Kind == PLATE_LAUNCHED)
            {
                // 안쪽 판은 중심에서 바깥으로 날아가 구덩이를 드러낸다
                float outSpeed = Random.Range(LAUNCH_SPEED_MIN, LAUNCH_SPEED_MAX);
                plate.Vel       = new Vector3(radial.x * outSpeed, Random.Range(LAUNCH_UP_MIN, LAUNCH_UP_MAX), radial.y * outSpeed);
                plate.SpinAxis  = Random.onUnitSphere;
                plate.SpinSpeed = Random.Range(LAUNCH_SPIN_MIN, LAUNCH_SPIN_MAX);
                plate.Delay     = Random.Range(0f, LAUNCH_DELAY_MAX);
            }

            // 판마다 밝기를 다르게 하고, 중심에 가까울수록 아래 용암 열기로 붉게 달아오른 색을 준다
            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }

            float tone = Random.Range(TONE_MIN, TONE_MAX);
            float heat = 1f - d;
            plate.Renderer.GetPropertyBlock(_block, 0);
            _block.SetColor(BASE_COLOR_ID, new Color(PLATE_BASE_COLOR.r * tone, PLATE_BASE_COLOR.g * tone, PLATE_BASE_COLOR.b * tone, 1f));
            _block.SetColor(EMISSION_COLOR_ID, PLATE_EMISSION_COLOR * (Mathf.Lerp(HEAT_MIN, 1f, heat) * Random.Range(HEAT_RANDOM_MIN, 1f)));
            plate.Renderer.SetPropertyBlock(_block, 0);
            plate.Root.localPosition = new Vector3(plate.Home.x, PLATE_HIDDEN, plate.Home.z);
            plate.Root.localRotation = Quaternion.identity;
            plate.Root.gameObject.SetActive(true);
        }
    }
}
