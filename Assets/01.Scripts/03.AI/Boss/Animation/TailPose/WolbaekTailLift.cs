// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 모델의 기본 자세에서 꼬리가 등 뒤로 뻗으며 아래로 처져 있고, 클립에는 꼬리 곡선이 없어서 모든 모션에서 그 자세가 그대로 나온다
    // 그래서 꼬리 마디 10개를 각자 몸 좌우 축으로 조금씩 더 돌려 꼬리를 위로 말아 올린 자세를 먼저 만든다. 꼬리는 원본 하나와 복제 여덟 개를 같은 뿌리에서 부채꼴로 벌려 쓴다
    // 그 자세를 목표로 마디를 입자 사슬로 시뮬레이션해서 천처럼 움직인다. 뿌리 입자는 몸에 붙어 있고 나머지는 관성으로 뒤처지다 스프링으로 따라오며, 끝으로 갈수록 느슨하다
    // 숨 쉬듯 이는 물결은 입자의 목표 위치를 흔드는 힘으로만 쓰고, 몸이 움직일수록 커진다. 시뮬레이션 결과는 마디 회전으로 되돌려 쓴다
    // 매 프레임 기본 회전에서 다시 시작하므로 회전 값이 누적되지 않는다
    /// <summary> 월백 꼬리를 말아 올리고 부채꼴로 펼친 뒤, 천처럼 몸을 따라 흔들리게 하는 표현 전용 컴포넌트 </summary>
    public sealed class WolbaekTailLift : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TAIL_PREFIX    = "tail_";
        private const int    TAIL_COUNT     = 10;   // 꼬리 하나의 마디 수
        private const int    MIDDLE_DIVISOR = 2;    // 꼬리 개수의 가운데 번호를 구한다

        // 꼬리 번호에 곱해 속도, 폭 편차를 흩는 값, 서로 다른 무리수에 가깝게 잡아 두 값이 같은 패턴으로 반복되지 않게 한다
        private const float  TAIL_SPEED_SEED = 2.399f;
        private const float  TAIL_AMP_SEED   = 1.618f;
        private const float  MAX_STEP        = 0.033f;  // 프레임이 길게 끊겨도 스프링이 튀지 않게 한 번에 진행하는 최대 시간
        private const float  MIN_LENGTH      = 0.00001f;
        private const float  MIN_DIVISOR     = 0.0001f; // 0으로 나누지 않게 하는 아래 한계
        private const float  TWO_PI          = Mathf.PI * 2f;

        // 부채꼴의 가운데(원본)가 배열 가운데에 오도록 순서대로 넣는다
        [SerializeField] private Transform[]    _tailRoots;
        [SerializeField] private float          _fanStepDegrees = 20f;   // 꼬리 사이의 벌어짐 각도

        [SerializeField] private float          _degreesPerBone = 12f;   // 마디 하나가 더 도는 각도, 양수일수록 꼬리가 위로 올라간다

        // 가로축은 꼬리 뿌리(0)에서 끝(1)까지, 세로축은 마디별 각도에 곱하는 비율이다
        [SerializeField] private AnimationCurve _weightByBone   = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        // 입자 사슬: 값이 작을수록 느슨하고 천천히 따라온다. 뿌리는 단단하게, 끝은 느슨하게 두면 천처럼 끝이 뒤늦게 휘감긴다
        [SerializeField] private float _rootStiffness = 180f;   // 목표 위치로 끌어당기는 세기(뿌리 쪽)
        [SerializeField] private float _tipStiffness  = 40f;    // (끝 쪽)
        [SerializeField] private float _rootDamping   = 12f;    // 속도가 줄어드는 정도(뿌리 쪽), 작을수록 여러 번 출렁인다
        [SerializeField] private float _tipDamping    = 5f;     // (끝 쪽)
        [SerializeField] private float _maxDistance   = 0.6f;   // 끝 마디가 목표 자세에서 벗어날 수 있는 최대 거리(미터), 급가속에도 꼬리가 몸에서 멀리 휘둘리지 않게 한다

        // 숨 쉬듯 이는 물결, 목표 위치를 위아래로 흔드는 힘이라 시뮬레이션이 뒤늦게 따라가며 출렁인다
        [SerializeField] private float _waveAmplitude    = 0.3f;   // 맨 끝 마디가 목표에서 벗어나는 최대 거리(미터)
        [SerializeField] private float _sideWaveRatio    = 0.25f;  // 위아래 물결 대비 옆으로 도는 물결의 비율, 0이면 위아래로만 흔들린다
        [SerializeField] private float _waveRootWeight   = 0f;     // 뿌리 마디의 물결 비율, 0이면 뿌리는 목표에 그대로 붙는다
        [SerializeField] private float _waveTipBias      = 1.5f;   // 클수록 물결이 끝쪽에 몰린다, 1이면 뿌리에서 끝까지 일정하게 커진다
        [SerializeField] private float _waveSpeed        = 2f;     // 초당 왕복 횟수
        [SerializeField] private float _wavePhasePerBone = 0.7f;  // 마디 하나가 늦는 위상(라디안), 0.7이면 꼬리 전체에 물결이 한 번 들어가고 클수록 촘촘해진다
        [SerializeField] private float _wavePhasePerTail = 2.4f;  // 꼬리끼리 박자가 어긋나는 위상(라디안), 이웃 꼬리가 같은 물결을 쓰지 않게 크게 흩는다

        // 한 가지 사인파만 쓰면 규칙적으로 왕복해서 기계적으로 보이므로, 더 빠른 파동을 섞어 불규칙하게 만든다
        [SerializeField] private float _flutterSpeedRatio = 2.3f;  // 기본 파동 대비 섞는 파동의 속도 배수
        [SerializeField] private float _flutterMix        = 0.35f; // 섞는 파동의 비율

        // 꼬리마다 속도와 폭, 스프링 세기를 조금씩 달리해 같은 동작이 반복되어 보이지 않게 한다, 0이면 모두 같다
        [SerializeField, Range(0f, 0.6f)] private float _speedVariance     = 0.35f;
        [SerializeField, Range(0f, 0.6f)] private float _amplitudeVariance = 0.3f;

        // 몸이 멈춰 있을 때는 숨 쉬듯 느리고 약하게, 움직일수록 빠르고 크게 흔들린다
        [SerializeField, Range(0f, 1f)] private float _idleActivity      = 0.12f;  // 멈췄을 때 물결 폭 비율
        [SerializeField, Range(0f, 1f)] private float _idleSpeedScale    = 0.4f;   // 멈췄을 때 물결 속도 비율
        [SerializeField] private float _fullActivitySpeed  = 4f;    // 이 속도(초당 미터) 이상이면 물결이 최대가 된다
        [SerializeField] private float _velocitySmoothTime = 0.25f; // 속도를 부드럽게 따라가는 시간(초), 길수록 천천히 켜지고 꺼진다
        [SerializeField] private float _teleportDistance   = 3f;    // 한 프레임에 이만큼 넘게 움직이면 순간이동으로 보고 꼬리를 제자리로 되돌린다

        private Transform    _anchor;         // 꼬리가 붙은 몸 본, 이 본의 움직임으로 속도를 잰다
        private Transform[]  _bones;          // 꼬리 번호 * TAIL_COUNT + 마디 번호
        private Quaternion[] _baseLocal;
        private Vector3[]    _axisInParent;
        private Vector3[]    _upInParent;
        private float[]      _degrees;
        private float[]      _phase;          // 꼬리마다 누적한 물결 위상, 속도가 바뀌어도 끊기지 않게 시간이 아니라 누적해서 쓴다
        private Vector3[]    _animatedPosition;   // 말아 올린 자세에서의 마디 위치, 입자가 따라갈 목표
        private Vector3[]    _position;           // 시뮬레이션 입자 위치
        private Vector3[]    _velocity;
        private Vector3      _lastAnchorPosition;
        private Vector3      _smoothedVelocity;
        private float        _speedRatio;     // 0이면 멈춤, 1이면 최대 속도
        private bool         _hasLastPosition;
        private bool         _needsReset = true;
        private int          _tailCount;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _tailCount = (_tailRoots != null) ? _tailRoots.Length : 0;

            if (_tailCount == 0)
            {
                Debug.LogWarning("월백 꼬리: 꼬리 뿌리가 비어 있어 꺼집니다", this);
                enabled = false;
                return;
            }

            int total     = _tailCount * TAIL_COUNT;
            _bones        = new Transform[total];
            _baseLocal    = new Quaternion[total];
            _axisInParent = new Vector3[total];
            _upInParent   = new Vector3[total];
            _degrees      = new float[TAIL_COUNT];

            _phase            = new float[_tailCount];
            _animatedPosition = new Vector3[total];
            _position         = new Vector3[total];
            _velocity         = new Vector3[total];

            // 보스가 여럿이어도 꼬리가 같은 박자로 흔들리지 않게 한다
            float startPhase = Random.value * TWO_PI;

            for (int t = 0; t < _tailCount; ++t)
            {
                _phase[t] = startPhase + (t * _wavePhasePerTail);

                Transform bone = _tailRoots[t];

                for (int i = 0; i < TAIL_COUNT; ++i)
                {
                    if ((bone == null) || (bone.parent == null))
                    {
                        Debug.LogWarning("월백 꼬리: 꼬리 본을 찾지 못해 꺼집니다 " + t + "번 " + (i + 1) + "마디", this);
                        enabled = false;
                        return;
                    }

                    int index = (t * TAIL_COUNT) + i;

                    _bones[index]        = bone;
                    _baseLocal[index]    = bone.localRotation;
                    _axisInParent[index] = bone.parent.InverseTransformDirection(transform.right);
                    _upInParent[index]   = bone.parent.InverseTransformDirection(transform.up);

                    bone = FindNextBone(bone);
                }
            }

            _anchor = _tailRoots[0].parent;

            RebuildDegrees();
        }

        private void OnEnable()
        {
            // 꺼졌다 켜지는 동안 몸이 어디로든 갔을 수 있으니, 켜질 때 꼬리를 제자리에서 다시 시작한다
            _needsReset      = true;
            _hasLastPosition = false;
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;

            TrackBody(deltaTime);
            PoseLifted();
            ReadLiftedPositions();

            if (_needsReset)
            {
                ResetParticles();
            }
            else if (deltaTime > 0f)
            {
                Simulate(deltaTime);
            }

            WriteRotations();
        }

        private void OnValidate()
        {
            // 도메인 리로드 뒤에는 배열이 빈 채로 복원될 수 있어 길이까지 본다
            if ((_degrees != null) && (_degrees.Length == TAIL_COUNT))
            {
                RebuildDegrees();
            }
        }

        /****************************************
        *               Private
        ****************************************/

        // 몸의 속도를 재서 물결의 크기와 빠르기를 정한다
        private void TrackBody(float deltaTime)
        {
            Vector3 position = _anchor.position;
            Vector3 velocity = Vector3.zero;

            if (_hasLastPosition && (deltaTime > 0f))
            {
                Vector3 delta = position - _lastAnchorPosition;

                // 워프처럼 순간이동하는 프레임은 속도로 치지 않고, 꼬리도 새 자리에서 다시 시작한다
                if (delta.sqrMagnitude >= (_teleportDistance * _teleportDistance))
                {
                    _needsReset = true;
                }
                else
                {
                    velocity = delta / deltaTime;
                }
            }

            _lastAnchorPosition = position;
            _hasLastPosition    = true;

            if (deltaTime > 0f)
            {
                float follow = 1f - Mathf.Exp(-deltaTime / Mathf.Max(_velocitySmoothTime, MIN_DIVISOR));
                _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, velocity, follow);
                _speedRatio       = Mathf.Clamp01(_smoothedVelocity.magnitude / Mathf.Max(_fullActivitySpeed, MIN_DIVISOR));
            }
        }

        // 모든 마디를 말아 올린 기본 자세로 돌려 놓는다, 시뮬레이션의 목표가 되는 자세다
        private void PoseLifted()
        {
            int middle = _tailCount / MIDDLE_DIVISOR;

            for (int t = 0; t < _tailCount; ++t)
            {
                for (int i = 0; i < TAIL_COUNT; ++i)
                {
                    int        index    = (t * TAIL_COUNT) + i;
                    Quaternion rotation = Quaternion.AngleAxis(_degrees[i], _axisInParent[index]) * _baseLocal[index];

                    if (i == 0)
                    {
                        // 부채꼴로 벌리는 건 뿌리 마디 하나에만 준다, 자식은 따라서 같이 돌아간다
                        rotation = Quaternion.AngleAxis((t - middle) * _fanStepDegrees, _upInParent[index]) * rotation;
                    }

                    _bones[index].localRotation = rotation;
                }
            }
        }

        private void ReadLiftedPositions()
        {
            for (int i = 0; i < _bones.Length; ++i)
            {
                _animatedPosition[i] = _bones[i].position;
            }
        }

        private void ResetParticles()
        {
            for (int i = 0; i < _bones.Length; ++i)
            {
                _position[i] = _animatedPosition[i];
                _velocity[i] = Vector3.zero;
            }

            _needsReset = false;
        }

        // 입자 사슬을 한 걸음 진행한다
        private void Simulate(float deltaTime)
        {
            float   step     = Mathf.Min(deltaTime, MAX_STEP);
            float   speedMul = Mathf.Lerp(_idleSpeedScale, 1f, _speedRatio);
            float   activity = Mathf.Lerp(_idleActivity, 1f, _speedRatio);
            int     middle   = _tailCount / MIDDLE_DIVISOR;
            Vector3 up       = transform.up;
            Vector3 back     = -transform.forward;

            for (int t = 0; t < _tailCount; ++t)
            {
                // 꼬리 번호로 정한 값이라 프레임마다 바뀌지 않고, 꼬리끼리는 서로 겹치지 않게 흩어진다
                float speedScale = 1f + (_speedVariance * Mathf.Sin(t * TAIL_SPEED_SEED));
                float ampScale   = 1f + (_amplitudeVariance * Mathf.Sin(t * TAIL_AMP_SEED));
                float stiffScale = speedScale * speedScale;

                _phase[t] += deltaTime * _waveSpeed * speedScale * speedMul * TWO_PI;

                float phase = _phase[t];

                // 꼬리가 뻗은 수평 방향의 옆쪽, 옆으로 도는 물결에 쓴다
                Vector3 tailDirection = Quaternion.AngleAxis((t - middle) * _fanStepDegrees, up) * back;
                Vector3 side          = Vector3.Cross(up, tailDirection);

                int first = t * TAIL_COUNT;

                // 뿌리 입자는 몸에 붙어 있다
                _position[first] = _animatedPosition[first];
                _velocity[first] = Vector3.zero;

                for (int i = 1; i < TAIL_COUNT; ++i)
                {
                    int   index    = first + i;
                    float along    = i / (float)(TAIL_COUNT - 1);
                    float envelope = Mathf.Lerp(_waveRootWeight, 1f, Mathf.Pow(along, _waveTipBias));
                    float lag      = i * _wavePhasePerBone;
                    float wave     = Mathf.Sin(phase - lag)
                                   + (_flutterMix * Mathf.Sin((phase * _flutterSpeedRatio) - (lag * _flutterSpeedRatio)));
                    float turn     = Mathf.Cos(phase - lag) * _sideWaveRatio;

                    Vector3 target = _animatedPosition[index]
                                   + (((up * wave) + (side * turn)) * (_waveAmplitude * envelope * ampScale * activity));

                    float stiffness = Mathf.Lerp(_rootStiffness, _tipStiffness, along) * stiffScale;
                    float damping   = Mathf.Lerp(_rootDamping, _tipDamping, along);

                    Vector3 velocity = _velocity[index];
                    velocity += (target - _position[index]) * (stiffness * step);
                    velocity *= Mathf.Exp(-damping * step);

                    Vector3 moved  = _position[index] + (velocity * step);
                    Vector3 away   = moved - _animatedPosition[index];
                    float   limit  = _maxDistance * along;

                    if (away.sqrMagnitude > (limit * limit))
                    {
                        moved = _animatedPosition[index] + (away.normalized * limit);
                    }

                    _position[index] = moved;
                    _velocity[index] = velocity;
                }

                // 마디 길이는 변하지 않으므로 뿌리부터 차례로 길이를 맞춰 준다
                for (int i = 1; i < TAIL_COUNT; ++i)
                {
                    int     index  = first + i;
                    float   length = (_animatedPosition[index] - _animatedPosition[index - 1]).magnitude;
                    Vector3 offset = _position[index] - _position[index - 1];
                    float   size   = offset.magnitude;

                    if (size > MIN_LENGTH)
                    {
                        Vector3 fixedPosition = _position[index - 1] + (offset * (length / size));

                        // 길이를 맞추며 움직인 만큼을 속도에도 반영해 입자가 늘어났다 튕기지 않게 한다
                        _velocity[index] += (fixedPosition - _position[index]) / step;
                        _position[index]  = fixedPosition;
                    }
                }
            }
        }

        // 입자 위치를 마디 회전으로 되돌려 쓴다, 뿌리부터 차례로 돌려야 자식 위치가 따라서 바뀐다
        private void WriteRotations()
        {
            for (int t = 0; t < _tailCount; ++t)
            {
                int first = t * TAIL_COUNT;

                for (int i = 0; i < TAIL_COUNT - 1; ++i)
                {
                    Transform bone    = _bones[first + i];
                    Vector3   current = _bones[first + i + 1].position - bone.position;
                    Vector3   wanted  = _position[first + i + 1] - bone.position;

                    if ((current.sqrMagnitude > MIN_LENGTH) && (wanted.sqrMagnitude > MIN_LENGTH))
                    {
                        bone.rotation = Quaternion.FromToRotation(current, wanted) * bone.rotation;
                    }
                }
            }
        }

        private void RebuildDegrees()
        {
            for (int i = 0; i < TAIL_COUNT; ++i)
            {
                float t = i / (float)(TAIL_COUNT - 1);
                _degrees[i] = _degreesPerBone * _weightByBone.Evaluate(t);
            }
        }

        private static Transform FindNextBone(Transform bone)
        {
            for (int i = 0; i < bone.childCount; ++i)
            {
                Transform child = bone.GetChild(i);

                if (child.name.StartsWith(TAIL_PREFIX))
                {
                    return child;
                }
            }

            return null;
        }
    }
}
