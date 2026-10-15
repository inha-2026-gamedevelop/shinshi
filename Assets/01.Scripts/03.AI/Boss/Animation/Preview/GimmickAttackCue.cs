// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 서버 규칙(하드 부적진 압박 공격): 전환이 끝난 시각부터 고리와 직선이 번갈아 나온다. 예고 1.2초 뒤 즉발이고 공격 간격은 2초다
    //   고리는 안쪽(7m에서 10m)이나 바깥쪽(10m에서 13m) 띠 중 무작위로 하나이고, 직선은 폭 3m로 살아 있는 참가자 구조물 방향으로 뻗는다
    // 이 도구는 서버 알림이 없어서 같은 시각표를 직접 계산하고, 시작 모션, 고리 모션, 직선 모션, 성공 후 모션, 고리 효과, 직선 효과를
    // 후보 중에서 골라 비교할 수 있게 오른쪽에 묶음별 후보 버튼을 그린다. 후보를 고르면 하드 부적진이 처음부터 다시 재생된다
    /// <summary> 부적진 기믹의 월백 모션과 압박 공격 효과 후보를 골라 시각표대로 재생하는 확인용 도구 </summary>
    public sealed class GimmickAttackCue : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int START_GROUP       = 0;
        private const int RING_MOTION_GROUP = 1;
        private const int LINE_MOTION_GROUP = 2;
        private const int SUCCESS_GROUP     = 3;
        private const int RING_VFX_GROUP    = 4;
        private const int LINE_VFX_GROUP    = 5;
        private const int NO_GROUP          = -1;

        private const int   MAX_ATTACKS     = 8;
        private const int   BEAM_SLOTS      = 4;
        private const int   RING_PARITY     = 0;
        private const int   DIRECTION_COUNT = 4;
        private const float QUARTER_TURN    = 90f;
        private const float MOTION_BLEND    = 0.05f;
        private const float FOLLOW_BLEND    = 0.15f;
        private const float RING_HEIGHT     = 0.1f;
        private const float BEAM_ACTIVE     = 1.4f;
        private const float BEAM_LINGER     = 0.5f;
        private const float HALF            = 0.5f;
        private const uint  BEAM_REVISION   = 1;
        private const int   BEAM_LINE_ID    = 0;

        private const float PANEL_WIDTH   = 250f;
        private const float BUTTON_WIDTH  = 240f;
        private const float BUTTON_HEIGHT = 24f;
        private const float BUTTON_STEP   = 26f;
        private const float START_Y       = 330f;
        private const float OPTION_INDENT = 12f;
        private const float SCALE_STEP    = 1.25f;

        private const float RANGE_HEIGHT    = 0.05f;
        private const int   RING_SEGMENTS   = 96;
        private const float RANGE_FADE      = 0.4f;
        private const float TELEGRAPH_ALPHA = 0.3f;
        private const float TELEGRAPH_PEAK  = 0.5f;
        private const float STRIKE_ALPHA    = 0.75f;

        private const string RANGE_SHADER  = "Sprites/Default";
        private const string RANGE_NAME    = "AttackRangeIndicator";
        private const string RANGE_ON_TEXT  = "판정 범위 표시: 켬";
        private const string RANGE_OFF_TEXT = "판정 범위 표시: 끔";

        private static readonly Color RANGE_COLOR = new Color(1f, 0.2f, 0.25f, 1f);

        private const string OPENED_MARK   = "v ";
        private const string CLOSED_MARK   = "> ";
        private const string SELECTED_MARK = "* ";
        private const string HEADER_FORMAT = "{0}{1}: {2}";
        private const string SCALE_FORMAT  = "크기 {0:F2}";
        private const string SMALLER_TEXT  = "줄이기";
        private const string LARGER_TEXT   = "키우기";
        private const string HEIGHT_FORMAT = "높이 {0:F2}";
        private const string LOWER_TEXT    = "낮추기";
        private const string HIGHER_TEXT   = "높이기";

        /****************************************
        *              Nested Types
        ****************************************/

        /// <summary> 후보 하나, 모션이면 상태 이름과 힘 순간과 재생 속도를 쓰고 효과면 프리팹과 크기를 쓴다 </summary>
        [Serializable]
        public sealed class Option
        {
            [SerializeField] private string     _label;
            [SerializeField] private string     _stateName;
            [SerializeField] private float      _peakSeconds;
            [SerializeField] private float      _speed = 1f;
            [SerializeField] private GameObject _prefab;
            [SerializeField] private float      _scale = 1f;
            [SerializeField] private float      _lifetime = 3f;
            [SerializeField] private string     _followStateName;
            [SerializeField] private float      _followSeconds;
            [SerializeField] private string     _finishStateName;
            [SerializeField] private float      _finishSeconds;
            [SerializeField] private float      _enterBlendSeconds;
            [SerializeField] private float      _finishBlendSeconds;

            public string     Label           => _label;
            public string     StateName       => _stateName;
            public float      PeakSeconds     => _peakSeconds;
            public float      Speed           => _speed;
            public GameObject Prefab          => _prefab;
            public float      Scale           => _scale;
            public float      Lifetime        => _lifetime;
            public string     FollowStateName => _followStateName;
            public float      FollowSeconds   => _followSeconds;
            public string     FinishStateName => _finishStateName;
            public float      FinishSeconds   => _finishSeconds;
            public float      EnterBlend      => _enterBlendSeconds;
            public float      FinishBlend     => _finishBlendSeconds;
        }

        /// <summary> 같은 역할의 후보 묶음 </summary>
        [Serializable]
        public sealed class Group
        {
            [SerializeField] private string   _title;
            [SerializeField] private Option[] _options;
            [SerializeField] private int      _selected;

            public string   Title    => _title;
            public Option[] Options  => _options;
            public int      Selected { get => _selected; set => _selected = value; }
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PatternMotionPreview _pattern;
        [SerializeField] private Animator _animator;
        [SerializeField] private Transform _origin;
        [SerializeField] private string[] _motionLabels;
        [SerializeField] private string[] _attackLabels;
        [SerializeField] private string _replayLabel;
        [SerializeField] private Group[] _groups;
        [SerializeField] private int _attackCount = 4;
        [SerializeField] private float _firstTelegraphSeconds = 3f;
        [SerializeField] private float _intervalSeconds = 2f;
        [SerializeField] private float _telegraphSeconds = 1.2f;
        [SerializeField] private float _recoverySeconds = 0.5f;
        [SerializeField] private float _innerRingMin = 7f;
        [SerializeField] private float _ringSplit = 10f;
        [SerializeField] private float _outerRingMax = 13f;
        [SerializeField] private float _lineLength = 18f;
        [SerializeField] private float _lineWidth = 3f;
        [SerializeField] private float _lineHeight = 1f;
        [SerializeField] private float _tileSpacing = 6f;
        [SerializeField] private bool _showRange = true;

        private int                 _expandedGroup = NO_GROUP;
        private float[]             _scaleMultipliers;
        private float[]             _heightMultipliers;
        private double              _castStart;
        private bool                _isAttackCast;
        private bool[]              _ringOuter;
        private Vector3[]           _lineDirection;
        private bool[]              _motionStarted;
        private bool[]              _struck;
        private bool[]              _speedReset;
        private ulong               _castId;
        private WolbaekBeamPlayer[] _beamPlayers;
        private uint[]              _beamIds;
        private double[]            _beamEnds;
        private string              _followState;
        private double              _followTime;
        private string              _finishState;
        private double              _finishTime;
        private float               _finishBlend;
        private GameObject[]        _rangeObjects;
        private Material[]          _rangeMaterials;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _scaleMultipliers = new float[_groups.Length];
            _heightMultipliers = new float[_groups.Length];

            for (int i = 0; i < _scaleMultipliers.Length; ++i)
            {
                _scaleMultipliers[i] = 1f;
                _heightMultipliers[i] = 1f;
            }

            _ringOuter = new bool[MAX_ATTACKS];
            _lineDirection = new Vector3[MAX_ATTACKS];
            _motionStarted = new bool[MAX_ATTACKS];
            _struck = new bool[MAX_ATTACKS];
            _speedReset = new bool[MAX_ATTACKS];
            _beamPlayers = new WolbaekBeamPlayer[BEAM_SLOTS];
            _beamIds = new uint[BEAM_SLOTS];
            _beamEnds = new double[BEAM_SLOTS];
            _rangeObjects = new GameObject[MAX_ATTACKS];
            _rangeMaterials = new Material[MAX_ATTACKS];
        }

        private void OnEnable()
        {
            _pattern.Started += OnStarted;
            _pattern.Struck += OnStruck;
        }

        private void OnDisable()
        {
            _pattern.Started -= OnStarted;
            _pattern.Struck -= OnStruck;
            _isAttackCast = false;
            ClearBeams();
            ClearRanges();
        }

        private void Update()
        {
            double now = Time.timeAsDouble;

            if (_isAttackCast)
            {
                UpdateAttacks(now, now - _castStart);
            }

            UpdateBeams(now);
            UpdateFollow(now);
        }

        private void OnGUI()
        {
            float x = Screen.width - PANEL_WIDTH;
            float y = START_Y;

            if (GUI.Button(new Rect(x, y, BUTTON_WIDTH, BUTTON_HEIGHT), _showRange ? RANGE_ON_TEXT : RANGE_OFF_TEXT))
            {
                _showRange = !_showRange;
            }

            y += BUTTON_STEP;

            for (int g = 0; g < _groups.Length; ++g)
            {
                Group group = _groups[g];
                string header = string.Format(HEADER_FORMAT, (g == _expandedGroup) ? OPENED_MARK : CLOSED_MARK, group.Title,
                    group.Options[group.Selected].Label);

                if (GUI.Button(new Rect(x, y, BUTTON_WIDTH, BUTTON_HEIGHT), header))
                {
                    _expandedGroup = (g == _expandedGroup) ? NO_GROUP : g;
                }

                y += BUTTON_STEP;

                if (g == _expandedGroup)
                {
                    y = DrawOptions(g, x, y);
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private float DrawOptions(int groupIndex, float x, float y)
        {
            Group group = _groups[groupIndex];

            for (int i = 0; i < group.Options.Length; ++i)
            {
                string text = ((i == group.Selected) ? SELECTED_MARK : string.Empty) + group.Options[i].Label;

                if (GUI.Button(new Rect(x + OPTION_INDENT, y, BUTTON_WIDTH - OPTION_INDENT, BUTTON_HEIGHT), text))
                {
                    group.Selected = i;
                    _scaleMultipliers[groupIndex] = 1f;
                    _heightMultipliers[groupIndex] = 1f;
                    _pattern.PlayByLabel(_replayLabel);
                }

                y += BUTTON_STEP;
            }

            if (((groupIndex == RING_VFX_GROUP) || (groupIndex == LINE_VFX_GROUP)) && (group.Options[group.Selected].Prefab != null))
            {
                GUI.Label(new Rect(x + OPTION_INDENT, y, BUTTON_WIDTH * HALF, BUTTON_HEIGHT),
                    string.Format(SCALE_FORMAT, group.Options[group.Selected].Scale * _scaleMultipliers[groupIndex]));

                if (GUI.Button(new Rect(x + (BUTTON_WIDTH * HALF), y, BUTTON_WIDTH * HALF * HALF, BUTTON_HEIGHT), SMALLER_TEXT))
                {
                    _scaleMultipliers[groupIndex] /= SCALE_STEP;
                    _pattern.PlayByLabel(_replayLabel);
                }

                if (GUI.Button(new Rect(x + (BUTTON_WIDTH * HALF * 1.5f), y, BUTTON_WIDTH * HALF * HALF, BUTTON_HEIGHT), LARGER_TEXT))
                {
                    _scaleMultipliers[groupIndex] *= SCALE_STEP;
                    _pattern.PlayByLabel(_replayLabel);
                }

                y += BUTTON_STEP;

                GUI.Label(new Rect(x + OPTION_INDENT, y, BUTTON_WIDTH * HALF, BUTTON_HEIGHT),
                    string.Format(HEIGHT_FORMAT, _heightMultipliers[groupIndex]));

                if (GUI.Button(new Rect(x + (BUTTON_WIDTH * HALF), y, BUTTON_WIDTH * HALF * HALF, BUTTON_HEIGHT), LOWER_TEXT))
                {
                    _heightMultipliers[groupIndex] /= SCALE_STEP;
                    _pattern.PlayByLabel(_replayLabel);
                }

                if (GUI.Button(new Rect(x + (BUTTON_WIDTH * HALF * 1.5f), y, BUTTON_WIDTH * HALF * HALF, BUTTON_HEIGHT), HIGHER_TEXT))
                {
                    _heightMultipliers[groupIndex] *= SCALE_STEP;
                    _pattern.PlayByLabel(_replayLabel);
                }

                y += BUTTON_STEP;
            }

            return y;
        }

        private Option Selected(int groupIndex)
        {
            Group group = _groups[groupIndex];
            return group.Options[group.Selected];
        }

        private void OnStarted(string label, float strikeSeconds)
        {
            // 새 패턴이 시작되면 진행 중이던 압박 공격과 효과를 끊는다
            _isAttackCast = false;
            _followState = null;
            _finishState = null;
            ClearBeams();
            ClearRanges();

            if (Array.IndexOf(_motionLabels, label) >= 0)
            {
                PlayMotion(Selected(START_GROUP), 1f);
            }

            if (Array.IndexOf(_attackLabels, label) < 0)
            {
                return;
            }

            ++_castId;
            _castStart = Time.timeAsDouble;
            _isAttackCast = true;
            int count = Mathf.Min(_attackCount, MAX_ATTACKS);

            for (int i = 0; i < count; ++i)
            {
                _ringOuter[i] = UnityEngine.Random.value >= HALF;
                float angle = UnityEngine.Random.Range(0, DIRECTION_COUNT) * QUARTER_TURN * Mathf.Deg2Rad;
                _lineDirection[i] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                _motionStarted[i] = false;
                _struck[i] = false;
                _speedReset[i] = false;
                CreateRange(i);
            }
        }

        // 성공 판정 시각에 쓰러지는 모션을 재생한다
        private void OnStruck(string label, int shotIndex)
        {
            if (Array.IndexOf(_motionLabels, label) >= 0)
            {
                Option success = Selected(SUCCESS_GROUP);
                PlayMotion(success, 1f);
                ScheduleFollow(success, Time.timeAsDouble);
            }
        }

        // 쓰러진 뒤 이어지는 반복 모션과 일어나는 마무리 모션을 정해 둔 시각에 이어 재생한다
        private void ScheduleFollow(Option motion, double now)
        {
            _followState = motion.FollowStateName;
            _followTime = now + motion.FollowSeconds;
            _finishState = motion.FinishStateName;
            _finishTime = now + motion.FinishSeconds;
            _finishBlend = (motion.FinishBlend > 0f) ? motion.FinishBlend : FOLLOW_BLEND;
        }

        private void UpdateFollow(double now)
        {
            if ((!string.IsNullOrEmpty(_followState)) && (now >= _followTime))
            {
                _animator.CrossFade(_followState, FOLLOW_BLEND, 0, 0f);
                _followState = null;
            }

            if ((!string.IsNullOrEmpty(_finishState)) && (now >= _finishTime))
            {
                _animator.CrossFade(_finishState, _finishBlend, 0, 0f);
                _finishState = null;
            }
        }

        private void UpdateAttacks(double now, double elapsed)
        {
            int count = Mathf.Min(_attackCount, MAX_ATTACKS);

            for (int i = 0; i < count; ++i)
            {
                bool isRing = (i % 2) == RING_PARITY;
                Option motion = Selected(isRing ? RING_MOTION_GROUP : LINE_MOTION_GROUP);
                double strike = _firstTelegraphSeconds + (i * _intervalSeconds) + _telegraphSeconds;

                // 힘이 실리는 순간이 예고 시간보다 길면 재생 속도를 올려 판정 시각에 맞춘다
                float speed = (motion.PeakSeconds > _telegraphSeconds) ? motion.PeakSeconds / _telegraphSeconds : 1f;
                double lead = motion.PeakSeconds / speed;

                UpdateRange(i, elapsed, strike);

                if ((!_motionStarted[i]) && (elapsed >= (strike - lead)))
                {
                    _motionStarted[i] = true;
                    PlayMotion(motion, speed);
                }

                if ((!_struck[i]) && (elapsed >= strike))
                {
                    _struck[i] = true;
                    SpawnAttack(i, isRing, now);
                }

                if ((!_speedReset[i]) && (elapsed >= (strike + _recoverySeconds)))
                {
                    _speedReset[i] = true;
                    _animator.speed = 1f;
                }
            }
        }

        private void PlayMotion(Option motion, float speed)
        {
            if (string.IsNullOrEmpty(motion.StateName))
            {
                return;
            }

            _animator.speed = speed * motion.Speed;
            _animator.CrossFade(motion.StateName, (motion.EnterBlend > 0f) ? motion.EnterBlend : MOTION_BLEND, 0, 0f);
        }

        private void SpawnAttack(int index, bool isRing, double now)
        {
            if (isRing)
            {
                SpawnRing(index);
                return;
            }

            SpawnLine(index, now);
        }

        // 효과 크기는 고리 바깥 반경에 비례한다, 후보의 크기 값은 반경 1m당 배율이다
        private void SpawnRing(int index)
        {
            Option effect = Selected(RING_VFX_GROUP);

            if (effect.Prefab == null)
            {
                return;
            }

            float outer = _ringOuter[index] ? _outerRingMax : _ringSplit;
            Vector3 position = _origin.position + (Vector3.up * RING_HEIGHT);
            GameObject instance = Instantiate(effect.Prefab, position, Quaternion.identity);
            float scale = effect.Scale * _scaleMultipliers[RING_VFX_GROUP] * outer;
            instance.transform.localScale = Vector3.Scale(effect.Prefab.transform.localScale, new Vector3(scale, scale * _heightMultipliers[RING_VFX_GROUP], scale));
            Destroy(instance, effect.Lifetime);
        }

        private void SpawnLine(int index, double now)
        {
            Option effect = Selected(LINE_VFX_GROUP);

            if (effect.Prefab == null)
            {
                return;
            }

            Vector3 direction = _lineDirection[index];
            Vector3 start = _origin.position + (Vector3.up * _lineHeight);

            // 직사각형 빔 프리팹은 도형을 직접 받고, 나머지 효과는 직선을 따라 일정 간격으로 나열한다
            if (effect.Prefab.GetComponent<WolbaekBeamPlayer>() != null)
            {
                SpawnBeam(effect, start, start + (direction * _lineLength), now);
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(direction);

            for (float distance = _tileSpacing * HALF; distance < _lineLength; distance += _tileSpacing)
            {
                GameObject instance = Instantiate(effect.Prefab, start + (direction * distance), rotation);
                float tileScale = effect.Scale * _scaleMultipliers[LINE_VFX_GROUP];
                instance.transform.localScale = Vector3.Scale(effect.Prefab.transform.localScale, new Vector3(tileScale, tileScale * _heightMultipliers[LINE_VFX_GROUP], tileScale));
                Destroy(instance, effect.Lifetime);
            }
        }

        private void SpawnBeam(Option effect, Vector3 start, Vector3 end, double now)
        {
            int slot = FindFreeBeamSlot(now);

            if (slot < 0)
            {
                return;
            }

            GameObject instance = Instantiate(effect.Prefab);
            WolbaekBeamPlayer player = instance.GetComponent<WolbaekBeamPlayer>();
            uint id = player.BeginCast(_castId, now);
            WolbaekBeamShape shape = new WolbaekBeamShape(start, end, _lineWidth);
            double end0 = now + BEAM_ACTIVE;
            player.Warn(id, BEAM_LINE_ID, BEAM_REVISION, shape, now, now, end0, now);
            player.ConfirmActive(id, BEAM_LINE_ID, BEAM_REVISION, now, end0, now);
            _beamPlayers[slot] = player;
            _beamIds[slot] = id;
            _beamEnds[slot] = end0 + BEAM_LINGER;
        }

        private int FindFreeBeamSlot(double now)
        {
            for (int i = 0; i < BEAM_SLOTS; ++i)
            {
                if ((_beamPlayers[i] == null) || (now > _beamEnds[i]))
                {
                    if (_beamPlayers[i] != null)
                    {
                        Destroy(_beamPlayers[i].gameObject);
                        _beamPlayers[i] = null;
                    }

                    return i;
                }
            }

            return -1;
        }

        private void UpdateBeams(double now)
        {
            for (int i = 0; i < BEAM_SLOTS; ++i)
            {
                if (_beamPlayers[i] == null)
                {
                    continue;
                }

                if (now > _beamEnds[i])
                {
                    Destroy(_beamPlayers[i].gameObject);
                    _beamPlayers[i] = null;
                    continue;
                }

                _beamPlayers[i].Tick(_beamIds[i], now);
            }
        }

        // 서버가 판정할 영역을 바닥에 반투명 도형으로 보여 준다, 고리는 띠 모양이고 직선은 폭 있는 직사각형이다
        private void CreateRange(int index)
        {
            bool isRing = (index % 2) == RING_PARITY;
            Mesh mesh;
            Quaternion rotation = Quaternion.identity;

            if (isRing)
            {
                float inner = _ringOuter[index] ? _ringSplit : _innerRingMin;
                float outer = _ringOuter[index] ? _outerRingMax : _ringSplit;
                mesh = BuildRingMesh(inner, outer);
            }
            else
            {
                mesh = BuildLineMesh(_lineLength, _lineWidth);
                rotation = Quaternion.LookRotation(_lineDirection[index]);
            }

            GameObject range = new GameObject(RANGE_NAME);
            range.transform.SetPositionAndRotation(_origin.position + (Vector3.up * RANGE_HEIGHT), rotation);
            range.AddComponent<MeshFilter>().sharedMesh = mesh;
            Material material = new Material(Shader.Find(RANGE_SHADER));
            range.AddComponent<MeshRenderer>().sharedMaterial = material;
            range.SetActive(false);
            _rangeObjects[index] = range;
            _rangeMaterials[index] = material;
        }

        private void UpdateRange(int index, double elapsed, double strike)
        {
            GameObject range = _rangeObjects[index];

            if (range == null)
            {
                return;
            }

            double start = strike - _telegraphSeconds;
            bool isVisible = _showRange && (elapsed >= start) && (elapsed <= (strike + RANGE_FADE));
            range.SetActive(isVisible);

            if (!isVisible)
            {
                return;
            }

            float alpha;

            if (elapsed < strike)
            {
                alpha = Mathf.Lerp(TELEGRAPH_ALPHA, TELEGRAPH_PEAK, (float)((elapsed - start) / _telegraphSeconds));
            }
            else
            {
                alpha = Mathf.Lerp(STRIKE_ALPHA, 0f, (float)((elapsed - strike) / RANGE_FADE));
            }

            Color color = RANGE_COLOR;
            color.a = alpha;
            _rangeMaterials[index].color = color;
        }

        private static Mesh BuildRingMesh(float inner, float outer)
        {
            Vector3[] vertices = new Vector3[(RING_SEGMENTS + 1) * 2];
            int[] triangles = new int[RING_SEGMENTS * 6];

            for (int s = 0; s <= RING_SEGMENTS; ++s)
            {
                float angle = Mathf.PI * 2f * s / RING_SEGMENTS;
                vertices[s * 2] = new Vector3(Mathf.Cos(angle) * inner, 0f, Mathf.Sin(angle) * inner);
                vertices[(s * 2) + 1] = new Vector3(Mathf.Cos(angle) * outer, 0f, Mathf.Sin(angle) * outer);
            }

            for (int s = 0; s < RING_SEGMENTS; ++s)
            {
                int v = s * 2;
                int t = s * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 1;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 2;
            }

            Mesh mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            return mesh;
        }

        private static Mesh BuildLineMesh(float length, float width)
        {
            float half = width * HALF;
            Mesh mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-half, 0f, 0f), new Vector3(half, 0f, 0f), new Vector3(-half, 0f, length), new Vector3(half, 0f, length) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            return mesh;
        }

        private void ClearRanges()
        {
            for (int i = 0; i < MAX_ATTACKS; ++i)
            {
                if (_rangeObjects[i] != null)
                {
                    Destroy(_rangeObjects[i].GetComponent<MeshFilter>().sharedMesh);
                    Destroy(_rangeMaterials[i]);
                    Destroy(_rangeObjects[i]);
                    _rangeObjects[i] = null;
                    _rangeMaterials[i] = null;
                }
            }
        }

        private void ClearBeams()
        {
            for (int i = 0; i < BEAM_SLOTS; ++i)
            {
                if (_beamPlayers[i] != null)
                {
                    Destroy(_beamPlayers[i].gameObject);
                    _beamPlayers[i] = null;
                }
            }
        }
    }
}
