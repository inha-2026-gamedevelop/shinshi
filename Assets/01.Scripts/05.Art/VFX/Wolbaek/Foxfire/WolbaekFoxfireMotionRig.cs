// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;

namespace Shinshi.Art
{
    /// <summary> 원본의 각 불꽃 구간과 꽃잎을 GPU skinning으로 움직이며 정점 배열을 복사하지 않는다 </summary>
    public sealed class WolbaekFoxfireMotionRig : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float TAU = Mathf.PI * 2f;
        private const float WAVE_HEIGHT_FREQUENCY = 5.4f;
        private const float SECOND_WAVE_RATIO = 1.73f;
        private const float TWIST_DEGREES = 13f;
        private const float PETAL_TURN_DEGREES = 165f;
        private const float PETAL_MIN_SCALE = 0.08f;
        private const float IMPACT_DELAY_RANGE = 0.18f;
        private const float IMPACT_MIN_SCALE = 0.015f;
        private const float IMPACT_FRAGMENT_LIFE_FLOOR = 0.68f;
        private const float WAVE_VERTICAL_RATIO     = 0.22f;
        private const float CORE_PATH_INFLUENCE     = 0.35f;
        private const float TAIL_TAPER              = 0.18f;
        private const float PETAL_BASE_SPEED        = 0.35f;
        private const float PETAL_SPEED_VARIATION   = 0.22f;
        private const float PETAL_X_WAVE_RATIO      = 0.51f;
        private const float PETAL_Z_WAVE_RATIO      = 0.67f;
        private const float PETAL_BASE_RISE         = 0.75f;
        private const float PETAL_DRIFT_RISE_RATIO  = 3f;
        private const float PETAL_Y_TURN_RATIO      = 0.53f;
        private const float PETAL_Z_TURN_RATIO      = 0.3f;
        private const float IMPACT_BASE_RISE        = 0.7f;

        [SerializeField] private Transform[] _bones = null;
        [SerializeField] private Vector3[] _restPositions = null;
        // x=구간 좌표, y=원본 조각 위상, z=꼬리 여부, w=조각별 변화량
        [SerializeField] private Vector4[] _motionData = null;
        [SerializeField] private bool _flight = false;
        [SerializeField] private bool _petals = false;
        [SerializeField] private bool _impact = false;
        [SerializeField] private float _sampleDelay = 0f;
        [SerializeField] private float _opacityMultiplier = 1f;

        /****************************************
        *              Properties
        ****************************************/

        public float OpacityMultiplier => _opacityMultiplier;
        public int BoneCount => _bones == null ? 0 : _bones.Length;

        /****************************************
        *                Methods
        ****************************************/

        public void Configure(Transform[] bones, Vector3[] restPositions, Vector4[] motionData,
            bool flight, bool petals, bool impact, float sampleDelay = 0f, float opacityMultiplier = 1f)
        {
            _bones = bones;
            _restPositions = restPositions;
            _motionData = motionData;
            _flight = flight;
            _petals = petals;
            _impact = impact;
            _sampleDelay = sampleDelay;
            _opacityMultiplier = opacityMultiplier;
        }

        public void ResetPose()
        {
            if (_bones == null)
            {
                return;
            }

            for (int i = 0; i < _bones.Length; ++i)
            {
                _bones[i].localPosition = _restPositions[i];
                _bones[i].localRotation = Quaternion.identity;
                _bones[i].localScale = Vector3.one;
            }
        }

        internal void Sample(Transform owner, WolbaekFoxfirePath path, double now, float age,
            float flowCycle, float bend, float tailLag, float drift, float rise, float expansion,
            float lifetime, float fadeSeconds, float spread)
        {
            float time = Mathf.Max(0f, age - _sampleDelay);
            float waveTime = time * TAU / flowCycle;
            Quaternion inverse = Quaternion.Inverse(owner.rotation);
            for (int i = 0; i < _bones.Length; ++i)
            {
                Vector3 rest = _restPositions[i];
                Vector4 data = _motionData[i];
                float phase = waveTime - rest.y * WAVE_HEIGHT_FREQUENCY + data.y;
                Vector3 displacement = new Vector3(Mathf.Sin(phase), Mathf.Sin(phase * SECOND_WAVE_RATIO) * WAVE_VERTICAL_RATIO,
                    Mathf.Cos(phase * SECOND_WAVE_RATIO)) * bend * data.w;
                Vector3 position = rest + displacement;
                Quaternion rotation = Quaternion.Euler(Mathf.Cos(phase) * TWIST_DEGREES * data.x,
                    Mathf.Sin(phase * SECOND_WAVE_RATIO) * TWIST_DEGREES * data.x, 0f);
                float scale = 1f;

                if (_flight)
                {
                    float delay = Mathf.Clamp01(-rest.z / WolbaekFoxfirePath.REST_LENGTH) * tailLag;
                    Vector3 point;
                    Quaternion heading;
                    path.Sample(now - delay - _sampleDelay, out point, out heading);
                    Vector3 crossSection = new Vector3(rest.x, rest.y, Mathf.Max(0f, rest.z));
                    Vector3 followed = owner.InverseTransformPoint(point + heading * crossSection);
                    float influence = data.z > 0f ? path.MovementStrength : CORE_PATH_INFLUENCE * path.MovementStrength;
                    position = Vector3.LerpUnclamped(rest, followed, influence) + displacement;
                    rotation = Quaternion.SlerpUnclamped(Quaternion.identity, inverse * heading, influence) * rotation;
                    // 원본 끝단 alpha와 폭에 더해 구간별 시간 위상이 다른 가늘어짐을 보존한다.
                    scale = 1f - Mathf.Clamp01(-rest.z / WolbaekFoxfirePath.REST_LENGTH) * TAIL_TAPER
                        * (0.5f + 0.5f * Mathf.Sin(phase));
                }

                if (_petals)
                {
                    float speed = PETAL_BASE_SPEED + data.w * PETAL_SPEED_VARIATION;
                    float cycle = Mathf.Repeat(time * speed + data.y / TAU, 1f);
                    float birth = Mathf.Repeat(data.y / TAU, 1f);
                    position += new Vector3(Mathf.Cos(phase * PETAL_X_WAVE_RATIO) * drift,
                        (cycle - birth) * (PETAL_BASE_RISE + drift * PETAL_DRIFT_RISE_RATIO),
                        Mathf.Sin(phase * PETAL_Z_WAVE_RATIO) * drift);
                    rotation *= Quaternion.Euler(time * PETAL_TURN_DEGREES * data.w,
                        time * PETAL_TURN_DEGREES * PETAL_Y_TURN_RATIO,
                        Mathf.Sin(phase) * PETAL_TURN_DEGREES * PETAL_Z_TURN_RATIO);
                    scale *= Mathf.Max(PETAL_MIN_SCALE, Mathf.Sin(cycle * Mathf.PI));
                }

                if (_impact)
                {
                    float piece = Mathf.Repeat(data.y / TAU, 1f);
                    float delay = data.z < 0f ? 0f : IMPACT_DELAY_RANGE * (0.5f + piece);
                    float fragmentAge = Mathf.Max(0f, time - delay);
                    float progress = DOVirtual.EasedValue(0f, 1f,
                        Mathf.Clamp01(fragmentAge / expansion), Ease.OutCubic);
                    Vector3 radial = new Vector3(rest.x, 0f, rest.z);
                    position = rest * progress + displacement * progress
                        + radial * fragmentAge * spread * data.w
                        + Vector3.up * fragmentAge * rise * (IMPACT_BASE_RISE + data.w);
                    float end = lifetime * (IMPACT_FRAGMENT_LIFE_FLOOR + (1f - IMPACT_FRAGMENT_LIFE_FLOOR) * piece);
                    float localFade = Mathf.Clamp01((end - time) / fadeSeconds);
                    scale *= time < delay ? 0f : (IMPACT_MIN_SCALE + (1f - IMPACT_MIN_SCALE) * progress) * localFade;
                }

                _bones[i].localPosition = position;
                _bones[i].localRotation = rotation;
                _bones[i].localScale = Vector3.one * scale;
            }
        }
    }

    /// <summary> 외부 Tick 시계의 고정 크기 경로 버퍼, 재사용·순간이동에서 이전 궤적을 폐기한다 </summary>
    internal sealed class WolbaekFoxfirePath
    {
        internal const float REST_LENGTH = 2.95f;
        private const int CAPACITY = 256;
        private const float MIN_TIME = 0.0001f;
        private const float SPEED_WINDOW = 0.65f;
        private const float MAX_TIME_GAP_LAG_MULTIPLIER = 2f;
        private const float DIRECTION_EPSILON_SQUARED = 0.000001f;
        private readonly Vector3[] _positions = new Vector3[CAPACITY];
        private readonly Quaternion[] _rotations = new Quaternion[CAPACITY];
        private readonly double[] _times = new double[CAPACITY];
        private int _next;
        private int _count;
        private float _speed;
        private float _lag;
        private bool _hasMovingFrame;

        internal int Count => _count;
        internal float MovementStrength => Mathf.Clamp01(_speed * _lag / REST_LENGTH);

        internal void Clear()
        {
            _count = 0;
            _next = 0;
            _speed = 0f;
            _hasMovingFrame = false;
        }

        internal void Record(Vector3 position, Quaternion rotation, double now, float lag, float teleportDistance)
        {
            _lag = lag;
            if (_count > 0)
            {
                int last = (_next + CAPACITY - 1) % CAPACITY;
                if (((position - _positions[last]).sqrMagnitude > teleportDistance * teleportDistance)
                    || (now - _times[last] > lag * MAX_TIME_GAP_LAG_MULTIPLIER))
                {
                    Clear();
                }
                else if (now == _times[last])
                {
                    _positions[last] = position;
                    _rotations[last] = rotation;
                    AverageSpeed(now);
                    return;
                }
                else
                {
                    Vector3 delta = position - _positions[last];
                    Quaternion previous = _rotations[last];
                    if (delta.sqrMagnitude > DIRECTION_EPSILON_SQUARED)
                    {
                        // 경로 접선에 이전 frame을 최소 회전으로 운반해 수직/곡선 구간의 뒤집힘을 피한다.
                        rotation = Quaternion.FromToRotation(previous * Vector3.forward, delta.normalized) * previous;
                        if (!_hasMovingFrame)
                        {
                            // 첫 이동 전의 정지 샘플은 접선이 없다. 첫 접선으로 seed를 맞춰
                            // 지연 구간이 seed 경계를 통과할 때 초기 head 방향으로 꺾이지 않게 한다.
                            for (int i = 0; i < _count; ++i)
                            {
                                _rotations[(last - i + CAPACITY) % CAPACITY] = rotation;
                            }
                            _hasMovingFrame = true;
                        }
                    }
                    else
                    {
                        rotation = previous;
                    }
                }
            }

            _positions[_next] = position;
            _rotations[_next] = rotation;
            _times[_next] = now;
            _next = (_next + 1) % CAPACITY;
            _count = Mathf.Min(CAPACITY, _count + 1);
            AverageSpeed(now);
        }

        private void AverageSpeed(double now)
        {
            int newest = (_next + CAPACITY - 1) % CAPACITY;
            int recent = newest;
            float distance = 0f;
            double oldestTime = now;
            // 20Hz pose/60Hz Tick에서 변위 없는 한 프레임을 정지로 해석하면 꼬리가 rest로 튄다.
            // 최근 경로 길이의 시간 평균으로 늦게 따라오는 꼬리를 유지하고 정지 후에도 부드럽게 감쇠한다.
            for (int i = 1; i < _count; ++i)
            {
                int older = (newest - i + CAPACITY) % CAPACITY;
                distance += (_positions[recent] - _positions[older]).magnitude;
                oldestTime = _times[older];
                if (now - oldestTime >= SPEED_WINDOW)
                {
                    break;
                }
                recent = older;
            }
            _speed = distance / Mathf.Max(MIN_TIME, (float)(now - oldestTime));
        }

        internal void Sample(double time, out Vector3 position, out Quaternion rotation)
        {
            int newest = (_next + CAPACITY - 1) % CAPACITY;
            int recent = newest;
            for (int i = 1; i < _count; ++i)
            {
                int older = (newest - i + CAPACITY) % CAPACITY;
                if (_times[older] <= time)
                {
                    float factor = Mathf.Clamp01((float)((time - _times[older]) / (_times[recent] - _times[older])));
                    position = Vector3.LerpUnclamped(_positions[older], _positions[recent], factor);
                    rotation = Quaternion.SlerpUnclamped(_rotations[older], _rotations[recent], factor);
                    return;
                }

                recent = older;
            }

            rotation = _rotations[recent];
            // 짧은 이력은 현재 세대의 최초 방향으로만 연장한다. 이전 세대/순간이동 궤적은 섞지 않는다.
            position = _positions[recent] + rotation * Vector3.forward
                * (float)(time - _times[recent]) * REST_LENGTH / _lag;
        }
    }
}
