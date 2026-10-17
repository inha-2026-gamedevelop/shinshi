// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 서버 규칙(HP 25% 여우불 유도와 봉인 핵 파괴): 차례마다 준비 3초, 추적 4초, 탈출 2초, 정리 1초이고 9초에 낙하와 핵 파괴를 판정한다
    //   생존자 한 명당 핵과 추적 여우불이 하나씩이며 추적 동안 여우불 낙하 위치가 대상을 따라가다 7초에 고정된다. 세 차례 성공하면 월백이 6초 쓰러져 반격을 받는다
    //   하드는 추적 시작 1.5초와 3초에 넓은 휩쓸기가 있고 0.8초 전에 범위를 보여 준다. 두 번째 방향은 첫 방향의 반대다
    // 이 도구는 서버 알림이 없어서 위 시각표를 패턴 시작 기준 초로 계산해 스냅샷을 공급한다. 참가자는 핵으로 걸어가는 가상 캡슐이며 모든 차례를 성공시킨다
    /// <summary> 봉인 핵, 낙하 여우불, 하드 휩쓸기를 차례 시각표대로 재생하는 확인용 도구 </summary>
    public sealed class SealCoreCue : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   SITE_COUNT        = 8;
        private const float SITE_RING         = 11f;
        private const float START_RING        = 3f;
        private const float MIN_CORE_GAP      = 6f;
        private const int   PICK_TRIES        = 64;
        private const float FULL_TURN         = Mathf.PI * 2f;
        private const float SWEEP_BACK        = 180f;
        private const float SWEEP_OFFSET_ANGLE = 30f;
        private const float PLAYER_HEIGHT     = 1f;
        private const int   SWEEP_COUNT       = 2;
        private const int   STEP_OPENED       = 1;
        private const int   STEP_LOCKED       = 2;
        private const int   STEP_RESOLVED     = 3;
        private const int   RANGE_SEGMENTS    = 24;
        private const int   TRIANGLE_VERTICES = 3;
        private const float RANGE_HEIGHT      = 0.05f;
        private const float RANGE_FADE        = 0.4f;
        private const float EFFECT_LIFETIME   = 4f;
        private const float YAW_FROM_X_AXIS   = 90f;
        private static readonly Color RANGE_COLOR = new Color(1f, 0.1f, 0.1f, 0.3f);

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PatternMotionPreview _pattern;
        [SerializeField] private Transform _origin;
        [SerializeField] private string[] _easyLabels;
        [SerializeField] private string[] _hardLabels;
        [SerializeField] private WolbaekSealCorePlayer[] _cores;
        [SerializeField] private WolbaekSealDropPlayer[] _drops;
        [SerializeField] private int _playerCount = 3;
        [SerializeField] private int _turnCount = 3;
        [SerializeField] private float _prepareSeconds = 3f;
        [SerializeField] private float _trackSeconds = 4f;
        [SerializeField] private float _escapeSeconds = 2f;
        [SerializeField] private float _cleanupSeconds = 1f;
        [SerializeField] private float _blastRadius = 1.5f;
        [SerializeField] private float[] _sweepHitSeconds = { 1.5f, 3f };
        [SerializeField] private float _sweepTelegraphSeconds = 0.8f;
        [SerializeField] private float _sweepRange = 16f;
        [SerializeField] private float _sweepHalfAngle = 60f;
        [SerializeField] private GameObject _sweepPrefab;
        [SerializeField] private float _sweepScale = 8f;
        [SerializeField] private float _sweepHeight = 2f;
        [SerializeField] private float _fadeSeconds = 2.5f;

        private ulong      _castId;
        private ulong      _sequence;
        private double     _castStart;
        private bool       _isCasting;
        private bool       _isHard;
        private int        _turn = -1;
        private double     _turnStart;
        private int        _phaseStep;
        private uint[]     _corePlayback;
        private uint[]     _dropPlayback;
        private Vector3[]  _coreCenters;
        private Vector3[]  _startPositions;
        private Vector3[]  _playerPositions;
        private GameObject[] _markers;
        private bool[]     _sweepTelegraphed;
        private bool[]     _sweepStruck;
        private float[]    _sweepAngles;
        private GameObject[] _sweepRanges;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _corePlayback = new uint[_cores.Length];
            _dropPlayback = new uint[_drops.Length];
            _coreCenters = new Vector3[_cores.Length];
            _startPositions = new Vector3[_cores.Length];
            _playerPositions = new Vector3[_cores.Length];
            _markers = new GameObject[_cores.Length];
            _sweepAngles = new float[SWEEP_COUNT];
            _sweepTelegraphed = new bool[SWEEP_COUNT];
            _sweepStruck = new bool[SWEEP_COUNT];
            _sweepRanges = new GameObject[SWEEP_COUNT];
        }

        private void OnEnable()
        {
            _pattern.Started += OnStarted;
        }

        private void OnDisable()
        {
            _pattern.Started -= OnStarted;
            StopAll();
        }

        private void Update()
        {
            if (!_isCasting)
            {
                return;
            }

            double now = Time.timeAsDouble;
            double elapsed = now - _castStart;
            double turnLength = _prepareSeconds + _trackSeconds + _escapeSeconds + _cleanupSeconds;
            double finalResolve = ((_turnCount - 1) * turnLength) + _prepareSeconds + _trackSeconds + _escapeSeconds;

            if (elapsed >= finalResolve + _fadeSeconds)
            {
                StopAll();
                return;
            }

            int wantedTurn = Mathf.Min(_turnCount - 1, (int)(elapsed / turnLength));
            while (_turn < wantedTurn)
            {
                BeginTurn(_turn + 1, _castStart + ((_turn + 1) * turnLength));
            }

            TickTurn(now - _turnStart, now);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnStarted(string label, float strikeSeconds)
        {
            StopAll();

            bool isEasy = Array.IndexOf(_easyLabels, label) >= 0;
            bool isHard = Array.IndexOf(_hardLabels, label) >= 0;

            if (!isEasy && !isHard)
            {
                return;
            }

            ++_castId;
            _isHard = isHard;
            _castStart = Time.timeAsDouble;
            _turn = -1;
            _isCasting = true;
        }

        private void BeginTurn(int index, double startTime)
        {
            StopTurnVisuals();
            _turn = index;
            _turnStart = startTime;
            _phaseStep = 0;
            PickCores();

            for (int i = 0; i < _playerCount; ++i)
            {
                float angle = (i / (float)_playerCount) * FULL_TURN;
                _startPositions[i] = _origin.position + (new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * START_RING);
                _playerPositions[i] = _startPositions[i];
            }

            for (int i = 0; i < _sweepTelegraphed.Length; ++i)
            {
                _sweepTelegraphed[i] = false;
                _sweepStruck[i] = false;
            }

            _sweepAngles[0] = UnityEngine.Random.Range(0, SITE_COUNT) * (FULL_TURN / SITE_COUNT) * Mathf.Rad2Deg;
            _sweepAngles[1] = _sweepAngles[0] + SWEEP_BACK;
            double now = Time.timeAsDouble;

            for (int i = 0; i < _playerCount; ++i)
            {
                ++_sequence;
                _corePlayback[i] = _cores[i].Begin(CoreSnapshot(i, WolbaekSealCorePlayer.EPhase.Dormant, startTime, now), now);
                _dropPlayback[i] = _drops[i].Begin(DropSnapshot(i, WolbaekSealDropPlayer.EPhase.Tracking, startTime, now), now);
            }
        }

        // 차례마다 핵 후보 여덟 곳 중 서로 6m 이상 떨어진 자리를 인원 수만큼 무작위로 고른다
        private void PickCores()
        {
            for (int tries = 0; tries < PICK_TRIES; ++tries)
            {
                bool valid = true;
                int offset = UnityEngine.Random.Range(0, SITE_COUNT);

                for (int i = 0; i < _playerCount; ++i)
                {
                    int site = (offset + (i * (SITE_COUNT / _playerCount))) % SITE_COUNT;
                    float angle = (site / (float)SITE_COUNT) * FULL_TURN;
                    _coreCenters[i] = _origin.position + (new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * SITE_RING);
                }

                for (int a = 0; a < _playerCount && valid; ++a)
                {
                    for (int b = a + 1; b < _playerCount; ++b)
                    {
                        if (Vector3.Distance(_coreCenters[a], _coreCenters[b]) < MIN_CORE_GAP)
                        {
                            valid = false;
                            break;
                        }
                    }
                }

                if (valid)
                {
                    return;
                }
            }
        }

        private void TickTurn(double local, double now)
        {
            float trackStart = _prepareSeconds;
            float lockTime = trackStart + _trackSeconds;
            float resolve = lockTime + _escapeSeconds;

            MovePlayers(local, trackStart, lockTime);

            // 외부 상태 변화는 시각표 순서대로 한 번씩만 입력한다
            if ((_phaseStep < STEP_OPENED) && (local >= trackStart))
            {
                _phaseStep = STEP_OPENED;
                ReconcileCores(WolbaekSealCorePlayer.EPhase.Opened, _turnStart + trackStart, now);
            }

            if ((_phaseStep < STEP_LOCKED) && (local >= lockTime))
            {
                _phaseStep = STEP_LOCKED;
                ReconcileDrops(WolbaekSealDropPlayer.EPhase.Locked, _turnStart + lockTime, now);
            }

            if ((_phaseStep < STEP_RESOLVED) && (local >= resolve))
            {
                _phaseStep = STEP_RESOLVED;
                ReconcileCores(WolbaekSealCorePlayer.EPhase.Destroyed, _turnStart + resolve, now);
                ReconcileDrops(WolbaekSealDropPlayer.EPhase.Resolved, _turnStart + resolve, now);
            }

            if (_phaseStep < STEP_LOCKED)
            {
                // 추적 중에는 낙하 위치가 대상 위치를 그대로 따라간다
                ReconcileDrops(WolbaekSealDropPlayer.EPhase.Tracking, _turnStart, now);
            }

            for (int i = 0; i < _playerCount; ++i)
            {
                _cores[i].Tick(_corePlayback[i], now);
                _drops[i].Tick(_dropPlayback[i], now);
            }

            if (_isHard)
            {
                TickSweeps(local, trackStart);
            }
        }

        // 준비 중에는 시작 자리에 서 있고 추적 4초 동안 담당 핵 위로 걸어가 낙하 위치가 고정될 때 핵 중심에 선다
        private void MovePlayers(double local, float trackStart, float lockTime)
        {
            float t = Mathf.Clamp01((float)((local - trackStart) / (lockTime - trackStart)));
            float eased = Mathf.SmoothStep(0f, 1f, t);

            for (int i = 0; i < _playerCount; ++i)
            {
                _playerPositions[i] = Vector3.Lerp(_startPositions[i], _coreCenters[i], eased);

                if (_markers[i] == null)
                {
                    _markers[i] = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    Destroy(_markers[i].GetComponent<Collider>());
                }

                _markers[i].transform.position = _playerPositions[i] + (Vector3.up * PLAYER_HEIGHT);
            }
        }

        private WolbaekSealCorePlayer.Snapshot CoreSnapshot(int index, WolbaekSealCorePlayer.EPhase phase, double phaseStart, double now)
        {
            return new WolbaekSealCorePlayer.Snapshot(_castId, _sequence, _turn, index, _coreCenters[index], _turnStart, now,
                phase, phaseStart);
        }

        private WolbaekSealDropPlayer.Snapshot DropSnapshot(int index, WolbaekSealDropPlayer.EPhase phase, double phaseStart, double now)
        {
            double lockAt = _turnStart + _prepareSeconds + _trackSeconds;
            double resolveAt = lockAt + _escapeSeconds;
            return new WolbaekSealDropPlayer.Snapshot(_castId, _sequence, _turn, index, _playerPositions[index], _blastRadius,
                _turnStart, lockAt, resolveAt, now, phase, phaseStart);
        }

        private void ReconcileCores(WolbaekSealCorePlayer.EPhase phase, double phaseStart, double now)
        {
            for (int i = 0; i < _playerCount; ++i)
            {
                ++_sequence;
                _cores[i].Reconcile(_corePlayback[i], CoreSnapshot(i, phase, phaseStart, now), now);
            }
        }

        private void ReconcileDrops(WolbaekSealDropPlayer.EPhase phase, double phaseStart, double now)
        {
            for (int i = 0; i < _playerCount; ++i)
            {
                ++_sequence;
                _drops[i].Reconcile(_dropPlayback[i], DropSnapshot(i, phase, phaseStart, now), now);
            }
        }

        // 휩쓸기 범위를 예고 시작부터 판정 직후까지 바닥에 보여 주고 판정 시각에 효과를 낸다
        private void TickSweeps(double local, float trackStart)
        {
            for (int i = 0; i < _sweepHitSeconds.Length; ++i)
            {
                float strike = trackStart + _sweepHitSeconds[i];

                if ((!_sweepTelegraphed[i]) && (local >= (strike - _sweepTelegraphSeconds)))
                {
                    _sweepTelegraphed[i] = true;
                    _sweepRanges[i] = CreateRange(_sweepAngles[i]);
                }

                if ((!_sweepStruck[i]) && (local >= strike))
                {
                    _sweepStruck[i] = true;
                    SpawnSweep(_sweepAngles[i]);

                    if (_sweepRanges[i] != null)
                    {
                        Destroy(_sweepRanges[i], RANGE_FADE);
                        _sweepRanges[i] = null;
                    }
                }
            }
        }

        // 중심 부채꼴 도형을 반투명 붉은색으로 바닥에 깐다, 시험 확인용 표시다
        private GameObject CreateRange(float angleDegrees)
        {
            Vector3[] vertices = new Vector3[RANGE_SEGMENTS + 2];
            int[] triangles = new int[RANGE_SEGMENTS * TRIANGLE_VERTICES];
            vertices[0] = Vector3.zero;

            for (int i = 0; i <= RANGE_SEGMENTS; ++i)
            {
                float a = (-_sweepHalfAngle + ((2f * _sweepHalfAngle) * i / RANGE_SEGMENTS)) * Mathf.Deg2Rad;
                vertices[i + 1] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * _sweepRange;
            }

            for (int i = 0; i < RANGE_SEGMENTS; ++i)
            {
                triangles[i * TRIANGLE_VERTICES] = 0;
                triangles[(i * TRIANGLE_VERTICES) + 1] = i + 2;
                triangles[(i * TRIANGLE_VERTICES) + 2] = i + 1;
            }

            Mesh mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();

            GameObject range = new GameObject("SweepRange");
            range.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = range.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"))
            {
                color = RANGE_COLOR
            };
            range.transform.position = _origin.position + (Vector3.up * RANGE_HEIGHT);
            range.transform.rotation = Quaternion.Euler(0f, -angleDegrees, 0f);
            return range;
        }

        // 서드파티 부채꼴 프리팹은 로컬 -Z 쪽으로 펼쳐지고 한 장이 약 60도라 좌우로 두 장을 겹쳐 쓴다
        private void SpawnSweep(float angleDegrees)
        {
            if (_sweepPrefab == null)
            {
                return;
            }

            for (int side = -1; side <= 1; side += 2)
            {
                Quaternion rotation = Quaternion.Euler(0f, -(angleDegrees + (side * SWEEP_OFFSET_ANGLE)) + YAW_FROM_X_AXIS + SWEEP_BACK, 0f);
                GameObject instance = Instantiate(_sweepPrefab, _origin.position + (Vector3.up * _sweepHeight), rotation);
                instance.transform.localScale = _sweepPrefab.transform.localScale * _sweepScale;
                Destroy(instance, EFFECT_LIFETIME);
            }
        }

        private void StopTurnVisuals()
        {
            for (int i = 0; i < _cores.Length; ++i)
            {
                _cores[i].Stop();
                _drops[i].Stop();
                _corePlayback[i] = 0;
                _dropPlayback[i] = 0;
            }

            for (int i = 0; i < _sweepRanges.Length; ++i)
            {
                if (_sweepRanges[i] != null)
                {
                    Destroy(_sweepRanges[i]);
                    _sweepRanges[i] = null;
                }
            }
        }

        private void StopAll()
        {
            _isCasting = false;
            StopTurnVisuals();

            for (int i = 0; i < _markers.Length; ++i)
            {
                if (_markers[i] != null)
                {
                    Destroy(_markers[i]);
                    _markers[i] = null;
                }
            }
        }
    }
}
