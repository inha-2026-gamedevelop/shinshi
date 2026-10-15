// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 서버 규칙(하드 HP 0% 최후의 영역): 예고 4초 동안 중앙만 남는다고 알리고, 4초에 외곽 바닥을 제거하면서 노란 여우불을 세우고 첫 빨간 묶음을 시작한다
    //   빨간 묶음은 생존자마다 한 개씩 4초 추적, 위치 고정, 2초 뒤 폭발(반경 2m)이고 노란 여우불이 남아 있으면 폭발 직후 다음 묶음이 이어진다
    //   노란 여우불을 모두 부수면 최종 성공이고 그 순간 남은 빨간 폭발은 취소된다. 노란 여우불 수는 시작 생존자 수에 따라 2, 3, 4, 4개다
    // 이 도구는 서버 알림이 없어서 위 시각표를 패턴 시작 기준 초로 계산해 공급한다. 참가자는 중앙을 도는 가상 캡슐이고 노란 여우불은 정해 둔 시각에 부서진다
    /// <summary> 외곽 바닥 제거, 노란 여우불 파괴, 빨간 여우불 묶음을 시각표대로 재생하는 확인용 도구, 오른쪽에 바닥 효과 후보 버튼을 그린다 </summary>
    public sealed class FinalAreaCue : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   MAX_PLAYERS      = 4;
        private const int   RING_SEGMENTS    = 48;
        private const float FULL_TURN        = Mathf.PI * 2f;
        private const float OUTER_RADIUS     = 30f;
        private const float FLOOR_HEIGHT     = 0.06f;
        private const float PLAYER_HEIGHT    = 1f;
        private const float PLAYER_ORBIT     = 5f;
        private const float PLAYER_SPEED     = 0.4f;
        private const float FLIGHT_HEIGHT    = 1.2f;
        private const float YELLOW_RING      = 4f;
        private const float PULSE_SPEED      = 4f;
        private const float HALF             = 0.5f;
        private const int   NONE_INDEX       = -1;
        private const int   TELEGRAPH_GROUP  = 0;
        private const int   COLLAPSE_GROUP   = 1;
        private const int   BURST_GROUP      = 2;
        private const int   GROUP_COUNT      = 3;

        private const float PANEL_WIDTH   = 250f;
        private const float BUTTON_HEIGHT = 24f;
        private const float BUTTON_STEP   = 26f;
        private const float PANEL_OFFSET  = 520f;
        private const float START_Y       = 40f;
        private const string SELECTED_MARK = "> ";
        private const string NONE_TEXT     = "없음";

        private static readonly Color TELEGRAPH_COLOR = new Color(1f, 0.15f, 0.15f, 0.25f);
        private static readonly Color VOID_COLOR      = new Color(0f, 0f, 0f, 0.92f);
        private static readonly string[] GROUP_TITLES = { "바닥 제거 예고 효과", "바닥 제거 순간 효과", "빨간 불 고정 폭발 효과" };

        /****************************************
        *              Nested Types
        ****************************************/

        /// <summary> 바닥 효과 후보 하나의 프리팹과 크기 </summary>
        [Serializable]
        public sealed class Candidate
        {
            [SerializeField] private string     _label;
            [SerializeField] private GameObject _prefab;
            [SerializeField] private float      _scale = 1f;
            [SerializeField] private float      _height = 0.1f;

            public string     Label  => _label;
            public GameObject Prefab => _prefab;
            public float      Scale  => _scale;
            public float      Height => _height;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PatternMotionPreview _pattern;
        [SerializeField] private DeathSequencePreview _death;
        [SerializeField] private Transform _origin;
        [SerializeField] private string[] _patternLabels;
        [SerializeField] private WolbaekFoxfirePlayer[] _flights;
        [SerializeField] private WolbaekFoxfirePlayer[] _hazards;
        [SerializeField] private WolbaekFoxfirePlayer[] _golds;
        [SerializeField] private WolbaekFoxfirePlayer[] _impacts;
        [SerializeField] private Candidate[] _telegraphCandidates;
        [SerializeField] private Candidate[] _collapseCandidates;
        [SerializeField] private Candidate[] _burstCandidates;
        [SerializeField] private int _playerCount = 3;
        [SerializeField] private float _centralRadius = 8f;
        [SerializeField] private float _collapseSeconds = 4f;
        [SerializeField] private float _trackSeconds = 4f;
        [SerializeField] private float _fuseSeconds = 2f;
        [SerializeField] private float _redRadius = 2f;
        [SerializeField] private float[] _yellowDestroySeconds = { 11.5f, 14.5f, 17.5f, 21.5f };
        [SerializeField] private float _effectLifetime = 4f;
        [SerializeField] private bool _showPanel = true;

        private double       _castStart;
        private bool         _isCasting;
        private bool         _collapsed;
        private bool         _deathPlayed;
        private int          _bundle = -1;
        private double       _bundleStart;
        private bool         _locked;
        private bool         _blasted;
        private int          _impactCursor;
        private int          _destroyedCount;
        private uint[]       _flightPlayback;
        private uint[]       _hazardPlayback;
        private uint[]       _goldPlayback;
        private uint[]       _impactPlayback;
        private Vector3[]    _redCenters;
        private Vector3[]    _yellowPositions;
        private Vector3[]    _playerPositions;
        private GameObject[] _markers;
        private GameObject   _telegraphFloor;
        private GameObject   _voidFloor;
        private GameObject[] _fxInstances;
        private int[]        _selected;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _flightPlayback = new uint[_flights.Length];
            _hazardPlayback = new uint[_hazards.Length];
            _goldPlayback = new uint[_golds.Length];
            _impactPlayback = new uint[_impacts.Length];
            _redCenters = new Vector3[MAX_PLAYERS];
            _yellowPositions = new Vector3[_golds.Length];
            _playerPositions = new Vector3[MAX_PLAYERS];
            _markers = new GameObject[MAX_PLAYERS];
            _fxInstances = new GameObject[GROUP_COUNT];
            _selected = new int[GROUP_COUNT];
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

        private void OnGUI()
        {
            if (!_showPanel)
            {
                return;
            }

            float x = Screen.width - PANEL_OFFSET;
            DrawGroup(x, START_Y, TELEGRAPH_GROUP, _telegraphCandidates);
            float collapseY = START_Y + ((_telegraphCandidates.Length + 3) * BUTTON_STEP);
            DrawGroup(x, collapseY, COLLAPSE_GROUP, _collapseCandidates);
            DrawGroup(x, collapseY + ((_collapseCandidates.Length + 3) * BUTTON_STEP), BURST_GROUP, _burstCandidates);
        }

        private void Update()
        {
            if (!_isCasting)
            {
                return;
            }

            double now = Time.timeAsDouble;
            double elapsed = now - _castStart;
            double finalSeconds = _yellowDestroySeconds[_yellowDestroySeconds.Length - 1];

            MovePlayers((float)elapsed);

            if ((!_collapsed) && (elapsed >= _collapseSeconds))
            {
                Collapse(now);
            }

            if (_collapsed)
            {
                TickBundles(elapsed, now);
                TickYellows(elapsed, now);
            }
            else
            {
                PulseTelegraph();
            }

            if (elapsed >= finalSeconds + _effectLifetime)
            {
                StopAll();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void DrawGroup(float x, float y, int group, Candidate[] candidates)
        {
            GUI.Label(new Rect(x, y, PANEL_WIDTH, BUTTON_HEIGHT), GROUP_TITLES[group]);
            y += BUTTON_STEP;

            if (GUI.Button(new Rect(x, y, PANEL_WIDTH, BUTTON_HEIGHT), (_selected[group] == NONE_INDEX ? SELECTED_MARK : string.Empty) + NONE_TEXT))
            {
                _selected[group] = NONE_INDEX;
                Replay();
            }

            for (int i = 0; i < candidates.Length; ++i)
            {
                y += BUTTON_STEP;

                if (GUI.Button(new Rect(x, y, PANEL_WIDTH, BUTTON_HEIGHT), (i == _selected[group] ? SELECTED_MARK : string.Empty) + candidates[i].Label))
                {
                    _selected[group] = i;
                    Replay();
                }
            }
        }

        private void Replay()
        {
            _pattern.PlayByLabel(_patternLabels[0]);
        }

        private void OnStarted(string label, float strikeSeconds)
        {
            StopAll();

            if (Array.IndexOf(_patternLabels, label) < 0)
            {
                return;
            }

            _castStart = Time.timeAsDouble;
            _collapsed = false;
            _deathPlayed = false;
            _bundle = -1;
            _destroyedCount = 0;
            _impactCursor = 0;
            _isCasting = true;
            _death.ResetDeath();

            _telegraphFloor = CreateRing(_centralRadius, OUTER_RADIUS, TELEGRAPH_COLOR);
            SpawnCandidate(TELEGRAPH_GROUP, _telegraphCandidates);
        }

        // 바닥을 제거하는 순간 외곽을 어둡게 가리고 노란 여우불을 세우며 첫 빨간 묶음을 시작한다
        private void Collapse(double now)
        {
            _collapsed = true;

            if (_telegraphFloor != null)
            {
                Destroy(_telegraphFloor);
                _telegraphFloor = null;
            }

            // 바닥이 사라지는 순간 예고 효과도 함께 사라진다
            DestroyEffect(TELEGRAPH_GROUP);

            _voidFloor = CreateRing(_centralRadius, OUTER_RADIUS, VOID_COLOR);
            SpawnCandidate(COLLAPSE_GROUP, _collapseCandidates);

            for (int i = 0; i < _yellowDestroySeconds.Length; ++i)
            {
                float angle = (i / (float)_yellowDestroySeconds.Length) * FULL_TURN;
                Vector3 position = _origin.position + (new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * YELLOW_RING);
                _yellowPositions[i] = position;
                _goldPlayback[i] = _golds[i].BeginTelegraph(position, EWolbaekFoxfireVariant.GoldTarget, now,
                    now + _yellowDestroySeconds[_yellowDestroySeconds.Length - 1] + _effectLifetime, now);
            }
        }

        // 묶음은 6초마다 이어진다, 첫 묶음은 바닥 제거와 같은 순간에 시작한다
        private void TickBundles(double elapsed, double now)
        {
            double bundleLength = _trackSeconds + _fuseSeconds;
            int wanted = (int)((elapsed - _collapseSeconds) / bundleLength);

            if ((wanted > _bundle) && (!AllYellowsDestroyed()))
            {
                BeginBundle(wanted, _castStart + _collapseSeconds + (wanted * bundleLength), now);
            }

            if (_bundle < 0)
            {
                return;
            }

            double local = now - _bundleStart;

            if ((!_locked) && (!_blasted))
            {
                TrackReds();
            }

            if ((!_locked) && (local >= _trackSeconds))
            {
                LockReds(now);
            }

            if ((!_blasted) && (local >= bundleLength))
            {
                BlastReds(now);
            }

            for (int i = 0; i < _hazards.Length; ++i)
            {
                _hazards[i].Tick(_hazardPlayback[i], now);
            }
        }

        private void BeginBundle(int index, double startTime, double now)
        {
            _bundle = index;
            _bundleStart = startTime;
            _locked = false;
            _blasted = false;

            for (int i = 0; i < _playerCount; ++i)
            {
                _flights[i].Stop();
                _flightPlayback[i] = _flights[i].PlayFlight(_playerPositions[i] + (Vector3.up * FLIGHT_HEIGHT), Quaternion.identity, now, now);
            }
        }

        // 추적 중에는 빨간 여우불이 담당 참가자 위로 따라붙는다
        private void TrackReds()
        {
            for (int i = 0; i < _playerCount; ++i)
            {
                Vector3 forward = Vector3.Cross(Vector3.up, _playerPositions[i] - _origin.position);
                Quaternion rotation = (forward.sqrMagnitude > 0f) ? Quaternion.LookRotation(forward.normalized) : Quaternion.identity;
                _flights[i].SetFlightPose(_flightPlayback[i], _playerPositions[i] + (Vector3.up * FLIGHT_HEIGHT), rotation);
                _flights[i].Tick(_flightPlayback[i], Time.timeAsDouble);
            }
        }

        private void LockReds(double now)
        {
            _locked = true;
            double blastTime = _bundleStart + _trackSeconds + _fuseSeconds;

            for (int i = 0; i < _playerCount; ++i)
            {
                _redCenters[i] = _playerPositions[i];
                _flights[i].Stop(_flightPlayback[i]);
                SpawnBurst(_redCenters[i]);
                _hazardPlayback[i] = _hazards[i].BeginTelegraph(_redCenters[i], EWolbaekFoxfireVariant.RedHazard, now, blastTime, now);
                _hazards[i].SetGroundRadius(_hazardPlayback[i], _redRadius);
            }
        }

        // 추적하던 빨간 불이 고정되며 터지는 효과를 낸다, 이어서 빨간 장판 예고가 나타난다
        private void SpawnBurst(Vector3 position)
        {
            if (_selected[BURST_GROUP] == NONE_INDEX)
            {
                return;
            }

            Candidate candidate = _burstCandidates[_selected[BURST_GROUP]];
            GameObject instance = Instantiate(candidate.Prefab, position + (Vector3.up * candidate.Height), Quaternion.identity);
            instance.transform.localScale = candidate.Prefab.transform.localScale * candidate.Scale;
            Destroy(instance, _effectLifetime);
        }

        private void BlastReds(double now)
        {
            _blasted = true;

            for (int i = 0; i < _playerCount; ++i)
            {
                _hazards[i].Stop(_hazardPlayback[i]);
                PlayImpact(_redCenters[i], EWolbaekFoxfireVariant.RedHazard, now);
            }
        }

        // 노란 여우불은 정해 둔 시각에 하나씩 부서지고 마지막이 부서지면 남은 빨간 여우불과 폭발을 모두 취소한다
        private void TickYellows(double elapsed, double now)
        {
            while ((_destroyedCount < _yellowDestroySeconds.Length) && (elapsed >= _yellowDestroySeconds[_destroyedCount]))
            {
                int index = _destroyedCount;
                Vector3 position = _yellowPositions[index];
                _golds[index].Stop(_goldPlayback[index]);
                PlayImpact(position, EWolbaekFoxfireVariant.GoldTarget, now);
                ++_destroyedCount;
            }

            for (int i = 0; i < _golds.Length; ++i)
            {
                _golds[i].Tick(_goldPlayback[i], now);
            }

            for (int i = 0; i < _impacts.Length; ++i)
            {
                _impacts[i].Tick(_impactPlayback[i], now);
            }

            if (AllYellowsDestroyed())
            {
                CancelReds();

                // 마지막 노란 여우불이 부서지는 순간 최종 성공이므로 같은 순간 처치 연출로 이어진다
                if (!_deathPlayed)
                {
                    _deathPlayed = true;
                    _death.PlayDeath();
                }
            }
        }

        private bool AllYellowsDestroyed()
        {
            return _destroyedCount >= _yellowDestroySeconds.Length;
        }

        private void CancelReds()
        {
            for (int i = 0; i < _flights.Length; ++i)
            {
                _flights[i].Stop();
                _hazards[i].Stop();
            }

            _locked = true;
            _blasted = true;
        }

        private void PlayImpact(Vector3 position, EWolbaekFoxfireVariant variant, double now)
        {
            int slot = _impactCursor % _impacts.Length;
            ++_impactCursor;
            _impacts[slot].Stop();
            _impactPlayback[slot] = _impacts[slot].PlayImpact(position, variant, now, now);
        }

        private void MovePlayers(float elapsed)
        {
            for (int i = 0; i < _playerCount; ++i)
            {
                float angle = ((i / (float)_playerCount) * FULL_TURN) + (elapsed * PLAYER_SPEED);
                _playerPositions[i] = _origin.position + (new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * PLAYER_ORBIT);

                if (_markers[i] == null)
                {
                    _markers[i] = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    Destroy(_markers[i].GetComponent<Collider>());
                }

                _markers[i].transform.position = _playerPositions[i] + (Vector3.up * PLAYER_HEIGHT);
            }
        }

        // 제거 예고 동안 외곽 영역을 붉게 깜빡인다
        private void PulseTelegraph()
        {
            if (_telegraphFloor == null)
            {
                return;
            }

            float pulse = HALF + (HALF * Mathf.Sin(Time.time * PULSE_SPEED));
            Color color = TELEGRAPH_COLOR;
            color.a = TELEGRAPH_COLOR.a * (HALF + pulse);
            _telegraphFloor.GetComponent<MeshRenderer>().sharedMaterial.color = color;
        }

        // 안쪽 반경에서 바깥 반경까지의 평평한 고리를 바닥 위에 만든다, 중앙 8m 밖 영역을 나타낸다
        private GameObject CreateRing(float innerRadius, float outerRadius, Color color)
        {
            Vector3[] vertices = new Vector3[(RING_SEGMENTS + 1) * 2];
            int[] triangles = new int[RING_SEGMENTS * 6];

            for (int i = 0; i <= RING_SEGMENTS; ++i)
            {
                float angle = (i / (float)RING_SEGMENTS) * FULL_TURN;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * innerRadius;
                vertices[(i * 2) + 1] = direction * outerRadius;
            }

            for (int i = 0; i < RING_SEGMENTS; ++i)
            {
                int v = i * 2;
                int t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2;
                triangles[t + 4] = v + 1;
                triangles[t + 5] = v + 3;
            }

            Mesh mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();

            GameObject ring = new GameObject("FinalAreaFloor");
            ring.AddComponent<MeshFilter>().sharedMesh = mesh;
            ring.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = color };
            ring.transform.position = _origin.position + (Vector3.up * FLOOR_HEIGHT);
            return ring;
        }

        private void SpawnCandidate(int group, Candidate[] candidates)
        {
            DestroyEffect(group);

            if (_selected[group] == NONE_INDEX)
            {
                return;
            }

            Candidate candidate = candidates[_selected[group]];
            GameObject instance = Instantiate(candidate.Prefab, _origin.position + (Vector3.up * candidate.Height), Quaternion.identity);
            instance.transform.localScale = candidate.Prefab.transform.localScale * candidate.Scale;
            _fxInstances[group] = instance;

            foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                main.loop = group == TELEGRAPH_GROUP;
            }

            Destroy(instance, _collapseSeconds + _effectLifetime);
        }

        private void DestroyEffect(int group)
        {
            if (_fxInstances[group] != null)
            {
                Destroy(_fxInstances[group]);
                _fxInstances[group] = null;
            }
        }

        private void StopAll()
        {
            _isCasting = false;

            for (int i = 0; i < _flights.Length; ++i)
            {
                _flights[i].Stop();
                _hazards[i].Stop();
            }

            for (int i = 0; i < _golds.Length; ++i)
            {
                _golds[i].Stop();
            }

            for (int i = 0; i < _impacts.Length; ++i)
            {
                _impacts[i].Stop();
            }

            for (int i = 0; i < _markers.Length; ++i)
            {
                if (_markers[i] != null)
                {
                    Destroy(_markers[i]);
                    _markers[i] = null;
                }
            }

            for (int group = 0; group < GROUP_COUNT; ++group)
            {
                DestroyEffect(group);
            }

            if (_telegraphFloor != null)
            {
                Destroy(_telegraphFloor);
                _telegraphFloor = null;
            }

            if (_voidFloor != null)
            {
                Destroy(_voidFloor);
                _voidFloor = null;
            }
        }
    }
}
