// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 네트워크 없이 무철 1페이즈 여섯 패턴(2페이즈 모드를 켜면 2페이즈 여섯 패턴도)을 눈으로 확인하는 시험 장치다. 빈 오브젝트에 붙이면 무철 대체 외형과 WASD 플레이어를 만들고 바로 전투를 연다.
    // 서버 부트스트랩이 하게 될 생성, 틱 호출, 등록을 대신할 뿐이며 서버 권위 경계나 Command, Rpc를 바꾸지 않는다.
    // 보스 수치는 운영 BossPatternCatalog 에셋이 아니라 MucheolDevCatalog의 임시 시작값을 쓴다. 운영 에셋은 무철을 비활성으로 두고 있다.
    // 월백 시험 장치와 같은 예고 표현 연결부(BossTelegraphLocalBridge)와 시험용 몸체(BossTestRigBody)를 읽기만 하고 재사용한다.
    // 2페이즈 모드(_phaseTwoMode)는 1페이즈 시험과 별도 모드다. 꺼 두면 카탈로그, 팩토리, 화면이 이전과 같다. 켜면 체력 55%에서 2페이즈로 넘어가고
    // 전환 뒤 인계(CompleteHandoff)는 이 구동기가 서버 연결 대신 부른다(자동 부름은 끌 수 있다). 운영에서는 서버 연결이 불러야 한다.
    // 하드 P07 스위치(_hardP07Mode)는 2페이즈 모드 안의 별도 시험 스위치다. 꺼 두면 후보, 팩토리, 대상이 이전과 같고 P07은 어디에도 없다.
    // 켜면 하드 전용 전장 공격(mucheol_ph2_p07)이 2페이즈 후보로 섞이고, 방어력과 체력을 하드 시험값으로 낮춘 플레이어에 더해
    // 테스트용 동료와 소환물이 실제로 등록되어 같은 피해 경로와 밀림을 받는다. 밀림은 이 구동기 안의 로컬 대역(BossTestRigDisplacer)이 하며
    // 운영 서버의 플레이어와 동료 이동, 네트워크 동기화는 이 장면이 증명하지 않는다.
    // 무철 외형 모델이 없어 캡슐로 대신한다. 무철 기믹 1은 공용 두뇌 연결이 승인 전이라 이 장면에서 동작하지 않는다
    /// <summary> 무철 시험 구동기, 단일 프로세스에서 서버 두뇌와 예고 표현과 대체 외형을 이어 준다 </summary>
    public sealed class MucheolBossTestRig : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  PLAYER_SPEED     = 6f;
        private const float  PLAYER_MAX_HP    = 30000f;
        private const float  DEFENSE_CONSTANT = 1000f; // PlayerConfigSO 초안과 같은 값
        private const float  REDUCTION_CAP    = 0.6f;
        private const float  LINE_OF_SIGHT_HEIGHT = 0.5f;
        private const float  HIT_FLASH_SECONDS = 0.15f;
        private const int    HISTORY_LENGTH   = 8;
        private const float  GUI_WIDTH        = 440f;
        private const float  GUI_MARGIN       = 10f;
        private const float  GUI_SPACING      = 6f;
        private const string HORIZONTAL_AXIS  = "Horizontal";
        private const string VERTICAL_AXIS    = "Vertical";
        private const float  HALF             = 0.5f;
        private const float  PERCENT          = 100f;
        private const float  HP_STEP_RATE     = 0.1f;
        private const int    PATTERN_COUNT    = 6;
        private const int    PATTERN_SUFFIX_LENGTH = 2;
        private const string PATTERN_ID_PREFIX = "mucheol_p0";
        private const string PHASE_TWO_ID_PREFIX = "mucheol_ph2_p0";
        private const string PHASE_TWO_MARK   = "ph2";
        private const string PHASE_TWO_LABEL  = "2-";
        private const float  HP_BEFORE_TRANSITION = 0.56f;  // 55% 전환 직전
        private const float  HP_AT_TRANSITION     = 0.549f; // 55% 이하, 부동소수 경계를 피해 살짝 아래로 둔다
        private const float  HP_BEFORE_ENHANCED   = 0.26f;  // 25% 초과
        private const float  HP_AT_ENHANCED       = 0.249f; // 25% 이하(강화 구간), 같은 이유로 살짝 아래다

        // 외형은 반경이 BOSS_BODY_RADIUS인 캡슐이다. 캡슐 기본 높이 2m, 반경 0.5m에 맞춰 배율을 정한다
        private const float  CAPSULE_BASE_RADIUS = 0.5f;
        private const float  CAPSULE_HEIGHT_SCALE = 2f;
        private const float  NOSE_HEIGHT      = 2f;
        private const float  NOSE_WIDTH       = 0.6f;
        private const float  NOSE_LENGTH      = 1.2f;
        private const int    COMBO_KEY_OFFSET = 1000000; // 2연타의 둘째 예고 번호를 첫째와 겹치지 않게 띄운다
        private const float  BOUNDARY_TOLERANCE = 0.05f; // m, 경계에 붙었다고 보는 오차
        private const float  BOUNDARY_MARGIN    = 0.1f;  // m, 돌진이 전장 경계에 막힐 때 경계 안쪽에 남기는 거리
        private const float  CLAMP_DETECT_EPSILON = 0.001f; // m, 막혔다고 보기 전에 허용하는 부동소수 오차
        private const float  BODY_TURN_RATE     = 360f;  // 도/초, 몸이 서버 방향으로 돌 때 도착 시간을 넉넉히 잡을 때의 최고 속도 기준
        private const float  BODY_MIN_HORIZON   = 0.06f; // 초, 몸이 아주 작은 각도도 이보다 빠르게 돌지 않게 하는 최소 시간

        // 하드 P07 시험 장치. 모두 시험 배치용 값이다
        private const string P07_ID_SUFFIX      = "07";
        private const float  EXTRA_BODY_SCALE   = 0.7f;   // 동료와 소환물 캡슐은 플레이어보다 작게 그린다
        private const float  PLACE_DISTANCE     = 8f;     // m, 보스에서 시험 대상을 세우는 거리
        private const float  PLACE_SPREAD       = 2f;     // m, 시험 대상끼리 옆으로 벌리는 거리
        private const int    LANE_SIDES         = 2;      // 시험 대상을 좌우로 번갈아 세우는 줄 수
        private const float  DISTURB_DISTANCE   = 3f;     // m, 예고 중에 보스를 옮기는 거리
        private const float  DISTURB_YAW        = 90f;    // 도, 예고 중에 보스를 돌리는 각도
        private const float  LOW_HEALTH_RATE    = 0.3f;   // 체력을 낮추는 시험 비율

        private static readonly Color BossColor = new Color(0.45f, 0.1f, 0.55f);

        /****************************************
        *                Fields
        ****************************************/

        [Header("프리팹")]
        [SerializeField] private BossTelegraphPresenter _presenterPrefab; // 예고 표현 풀을 묶은 프리팹, 월백 시험과 같은 것을 쓴다

        [Header("시험 주입값")]
        // m, 전장 지름. 씬의 바닥 원판 지름과 같게 맞춰야 보스가 바닥 밖으로 나가지 않는다(씬의 바닥은 지름 44).
        // 경계는 몸체 반경을 뺀 20m라 중심에서 18m 돌진이 경계에 닿지 않는다. 시험 몸체는 경계에 정확히 닿아 끝나면 안쪽 돌진도 0m가 되어
        // 이 리그는 이동 창구에서 경계 안쪽 0.1m에 서게 한다. 그래도 붙으면 Reset으로 푼다
        [SerializeField] private float   _arenaDiameter   = 44f;
        [SerializeField] private float   _referenceAttack = 644.32f; // Aref, 체험판 숲 기준
        [SerializeField] private float   _referenceHealth = 3541f;   // Href, 체험판 숲 기준
        [SerializeField] private float   _healthScale     = 1f;      // 시작 인원에 따른 체력 배율
        [SerializeField] private Vector3 _playerStart     = new Vector3(0f, 0f, 12f);

        [Header("2페이즈 모드")]
        [SerializeField] private bool    _phaseTwoMode        = false; // 켜면 체력 55%에서 2페이즈로 넘어가고 mucheol_ph2 패턴이 후보가 된다
        [SerializeField] private float   _startHealthRate     = 1f;    // 시작 체력 비율, 2페이즈 시험은 낮춰서 시작할 수 있다
        [SerializeField] private bool    _autoCompleteHandoff = true;  // 전환 뒤 인계 대기에서 CompleteHandoff를 이 구동기가 대신 부른다

        [Header("하드 P07 시험 (2페이즈 모드 안의 스위치, 기본 꺼짐)")]
        [SerializeField] private bool    _hardP07Mode          = false; // 켜면 P07이 2페이즈 후보가 되고 하드 시험 대상이 등록된다
        [SerializeField] private float   _hardPlayerHealth     = 1500f; // PlayerConfig 기본 체력, 임시 시험값
        [SerializeField] private float   _hardPlayerDefense    = 100f;  // PlayerConfig 기본 방어력, 임시 시험값
        [SerializeField] private float   _hardCompanionRate    = 0.6f;  // 동료 최대 체력 / 주인 최대 체력, 임시 시험값
        [SerializeField] private float   _hardSummonHealth     = 1500f; // 소환물 체력, 임시 시험값

        private CombatTargetRegistry    _registry;
        private DamageResolver          _resolver;
        private BossTestRigBody         _body;
        private BossBrain               _brain;
        private BossTestRigCombatant    _player;
        private BossTestRigDisplacer    _displacer;
        private BattlefieldVisual       _battlefield;
        private readonly List<ExtraTargetView> _extras = new List<ExtraTargetView>();
        private bool                    _hardChangeRequested;
        private Vector2                 _guiScroll;
        private bool                    _hardRequestedValue;
        private BossTelegraphPresenter  _presenter;
        private MucheolDevCatalog       _catalog;
        private ForcedPatternRule       _forced;
        private ComboVisual             _combo;
        private ChargeOutcomeLog        _chargeLog;
        private SequenceVisual          _sequence;
        private MultiDashVisual         _multiDash;
        private readonly MucheolYawMotion _bodyMotion = new MucheolYawMotion(); // 화면의 몸이 서버 방향을 따라 도는 계산
        private double                  _bodyDeadline;    // 돌진 시작 시각, 몸이 그때까지 서버 방향에 닿아야 한다
        private float                   _bodyLagDegrees;  // 서버 방향과 화면의 몸 방향의 차이
        private Transform               _bossBody;
        private Transform               _bossNose;
        private Transform               _playerBody;
        private Renderer                _playerRenderer;
        private float                   _lastPlayerHealth;
        private float                   _flashUntil;
        private string                  _lastPatternId = string.Empty;
        private readonly List<string>   _history = new List<string>();
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            if (_presenterPrefab == null)
            {
                Debug.LogError("[Boss] 예고 표현 프리팹이 비어 있다", this);
                enabled = false;
                return;
            }

            _presenter = Instantiate(_presenterPrefab, transform);

            // 프리팹에 같이 묶인 샘플 입력은 가짜 알림을 흘리므로 끈다
            if (_presenter.TryGetComponent(out BossTelegraphSampleInput sample))
            {
                sample.enabled = false;
            }

            _presenter.SetServerClock(ReadClock);

            GameObject player = TestRigDisplay.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "PlayerBody";
            Destroy(player.GetComponent<Collider>());
            _playerBody     = player.transform;
            _playerRenderer = player.GetComponent<Renderer>();
            _playerBody.SetParent(transform, false);

            CreateBossStandIn();
            Build();
        }

        private void Update()
        {
            if (_brain == null)
            {
                return;
            }

            // 스위치는 GUI 이벤트 도중에 바꾸면 컨트롤 수가 어긋나 오류가 나므로 요청만 받아 두고 여기서 적용한다
            if (_hardChangeRequested)
            {
                _hardChangeRequested = false;
                _hardP07Mode         = _hardRequestedValue;
                Build();
                return;
            }

            double now = ReadClock();

            if (_player.LifeState == EParticipantLifeState.Alive)
            {
                Vector3 move = new Vector3(Input.GetAxisRaw(HORIZONTAL_AXIS), 0f, Input.GetAxisRaw(VERTICAL_AXIS));
                _player.Position += move.normalized * (PLAYER_SPEED * Time.deltaTime);
            }

            _resolver.BeginTick(now);
            _body.Brain = _brain;
            _brain.Tick(now);

            // 전환 뒤 인계 대기는 서버 연결이 CompleteHandoff를 불러야 풀린다. 이 구동기가 그 자리를 대신한다
            if (_phaseTwoMode && _autoCompleteHandoff && (_brain.PhaseState == EBossPhaseState.AwaitingHandoff))
            {
                _brain.CompleteHandoff();
            }

            TrackPattern();
            ApplyView();
        }

        private void OnGUI()
        {
            if (_brain == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(GUI_MARGIN, TestRigDisplay.GUI_TOP, GUI_WIDTH, Screen.height - TestRigDisplay.GUI_TOP - GUI_MARGIN), GUI.skin.box);
            _guiScroll = GUILayout.BeginScrollView(_guiScroll);
            GUILayout.Label($"무철 시험 ({(_phaseTwoMode ? (_hardP07Mode ? "2페이즈 모드 + 하드 P07" : "2페이즈 모드") : "1페이즈")}, 임시 수치, 운영 에셋 아님)  Flow {_brain.FlowState}  Phase {_brain.Phase}");
            GUILayout.Label($"Attack {_brain.AttackState}  Pattern {_brain.ActivePatternId}  PhaseState {_brain.PhaseState}");
            GUILayout.Label($"Boss HP {_body.Health:F0}/{_body.MaxHealth:F0} ({_body.HealthRate * PERCENT:F1}%)");
            GUILayout.Label($"Player HP {_player.Health:F0}/{_player.MaxHealth:F0}");
            for (int i = 0; i < _extras.Count; ++i)
            {
                GUILayout.Label(_extras[i].Describe());
            }

            // 패널 아래쪽은 화면 밖으로 잘리기 쉬워 스위치를 맨 위에 둔다
            if (_phaseTwoMode)
            {
                bool wantHard = GUILayout.Toggle(_hardP07Mode, "하드 P07 시험 스위치 (바꾸면 전투를 새로 시작)");
                if (wantHard != _hardP07Mode)
                {
                    _hardChangeRequested = true;
                    _hardRequestedValue  = wantHard;
                }
            }

            GUILayout.Space(GUI_SPACING);
            GUILayout.Label("패턴 실행 횟수");
            for (int i = 1; i <= PATTERN_COUNT; ++i)
            {
                string id = PATTERN_ID_PREFIX + i;
                _counts.TryGetValue(id, out int count);
                if (_phaseTwoMode)
                {
                    _counts.TryGetValue(PHASE_TWO_ID_PREFIX + i, out int countTwo);
                    GUILayout.Label($"  {id}  {count}      {PHASE_TWO_ID_PREFIX + i}  {countTwo}");
                }
                else
                {
                    GUILayout.Label($"  {id}  {count}");
                }
            }

            if (IsHardActive)
            {
                _counts.TryGetValue(MucheolPhase2PatternFactory.P07_ID, out int countSeven);
                GUILayout.Label($"  {MucheolPhase2PatternFactory.P07_ID}  {countSeven}");
            }

            GUILayout.Space(GUI_SPACING);
            GUILayout.Label("최근 순서 " + string.Join(" > ", _history));
            GUILayout.Label($"몸 방향 차이: 서버 방향과 {_bodyLagDegrees:F1}도");
            GUILayout.Label("P05 마지막 타: " + _combo.LastEvent);
            GUILayout.Label("P06 마지막 결과: " + _chargeLog.LastText);
            if (_phaseTwoMode)
            {
                GUILayout.Label("2페이즈 순차 타격 마지막 타: " + _sequence.LastEvent);
                GUILayout.Label("2페이즈 연속 돌진 마지막 시전: " + _multiDash.LastEvent);
            }

            if (IsHardActive)
            {
                GUILayout.Label("P07 전장 공격 마지막: " + _battlefield.LastEvent);
                GUILayout.Label("P07 마지막 밀림: " + _battlefield.LastPush);
            }

            if (IsBossOnArenaBoundary())
            {
                GUILayout.Label("보스가 전장 경계에 붙었다. 시험 몸체는 경계 위에서 돌진이 0m가 되므로 Reset으로 푼다");
            }

            GUILayout.Space(GUI_SPACING);
            GUILayout.Label("다음 선택으로 강제 (현재 시전이 끝난 뒤)");
            GUILayout.BeginHorizontal();
            for (int i = 1; i <= PATTERN_COUNT; ++i)
            {
                string id = PATTERN_ID_PREFIX + i;
                if (GUILayout.Button("P0" + i))
                {
                    _forced.PendingId = id;
                }
            }

            GUILayout.EndHorizontal();
            if (_phaseTwoMode)
            {
                GUILayout.BeginHorizontal();
                for (int i = 1; i <= PATTERN_COUNT; ++i)
                {
                    if (GUILayout.Button(PHASE_TWO_LABEL + "P0" + i))
                    {
                        _forced.PendingId = PHASE_TWO_ID_PREFIX + i;
                    }
                }

                GUILayout.EndHorizontal();

                if (_hardP07Mode)
                {
                    if (GUILayout.Button(PHASE_TWO_LABEL + "P" + P07_ID_SUFFIX + " (하드 전장 공격)"))
                    {
                        _forced.PendingId = MucheolPhase2PatternFactory.P07_ID;
                    }
                }
            }

            if (!string.IsNullOrEmpty(_forced.PendingId))
            {
                GUILayout.Label("강제 대기: " + _forced.PendingId);
            }

            GUILayout.Space(GUI_SPACING);
            if (GUILayout.Button("Boss HP -10%"))
            {
                _body.ApplyDamage(_body.MaxHealth * HP_STEP_RATE, false, ReadClock());
            }

            if (_phaseTwoMode)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("HP 56%"))
                {
                    LowerHealthTo(HP_BEFORE_TRANSITION);
                }

                if (GUILayout.Button("HP 54.9% (전환)"))
                {
                    LowerHealthTo(HP_AT_TRANSITION);
                }

                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("HP 26%"))
                {
                    LowerHealthTo(HP_BEFORE_ENHANCED);
                }

                if (GUILayout.Button("HP 24.9% (강화)"))
                {
                    LowerHealthTo(HP_AT_ENHANCED);
                }

                GUILayout.EndHorizontal();

                _autoCompleteHandoff = GUILayout.Toggle(_autoCompleteHandoff, "전환 뒤 CompleteHandoff 자동 호출 (운영에서는 서버 연결이 호출)");
                if ((!_autoCompleteHandoff) && (_brain.PhaseState == EBossPhaseState.AwaitingHandoff) && GUILayout.Button("CompleteHandoff 호출"))
                {
                    _brain.CompleteHandoff();
                }

                if (_hardP07Mode)
                {
                    DrawHardTools();
                }
            }

            if (GUILayout.Button("Kill boss"))
            {
                _body.ApplyDamage(0f, true, ReadClock());
            }

            if (GUILayout.Button("Reset"))
            {
                Build();
            }

            GUILayout.Space(GUI_SPACING);
            GUILayout.Label("기믹 1(돌진 유도)은 공용 두뇌 연결 승인 전이라 이 장면에서 동작하지 않음");
            if (_phaseTwoMode)
            {
                GUILayout.Label("2-P06 정의의 실행 구간은 세 번(3.45초)에 고정: 공용 공격 FSM이 실행 시간을 하나로 고정하는 제약. 두 번 달리면 남는 구간 동안 선다");
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // 하드 P07 확인용 배치 도구. 위치만 바꾸고 판정은 건드리지 않는다
        private void DrawHardTools()
        {
            GUILayout.Label("P07 시험 배치 (보스 정면 기준)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("보스 중앙"))
            {
                PlaceBoss(false);
            }

            if (GUILayout.Button("보스 가장자리(+x)"))
            {
                PlaceBoss(true);
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("대상 안전지대(등 뒤)"))
            {
                PlaceTargets(-_body.Forward);
            }

            if (GUILayout.Button("대상 공격 영역(정면)"))
            {
                PlaceTargets(_body.Forward);
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("예고 중 보스 이동 + 회전"))
            {
                DisturbBoss();
            }

            if (GUILayout.Button("대상 체력 가득"))
            {
                SetTargetHealth(1f);
            }

            if (GUILayout.Button("대상 체력 낮춤"))
            {
                SetTargetHealth(LOW_HEALTH_RATE);
            }

            GUILayout.EndHorizontal();
        }

        private void PlaceBoss(bool atEdge)
        {
            Vector3 center = transform.position;
            float   limit  = (_arenaDiameter * HALF) - Constants.Combat.BOSS_BODY_RADIUS - BOUNDARY_MARGIN;

            _body.Move(_body.Position, atEdge ? (center + (Vector3.right * limit)) : center, Constants.Combat.BOSS_BODY_RADIUS);
        }

        // 보스에서 direction 쪽으로 PLACE_DISTANCE 떨어진 줄에 플레이어와 동료와 소환물을 옆으로 벌려 세운다
        private void PlaceTargets(Vector3 direction)
        {
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            Vector3 spot = _body.Position + (direction * PLACE_DISTANCE);

            _player.Position = spot;
            for (int i = 0; i < _extras.Count; ++i)
            {
                float lane = (i % LANE_SIDES == 0) ? -PLACE_SPREAD : PLACE_SPREAD;
                _extras[i].Target.Position = spot + (side * (lane * ((i / LANE_SIDES) + 1)));
            }
        }

        // 예고가 도는 중에 보스의 위치와 방향을 바꿔, 예고와 판정이 시작 순간의 값에 남는지 눈으로 본다
        private void DisturbBoss()
        {
            Vector3 turned = Quaternion.AngleAxis(DISTURB_YAW, Vector3.up) * _body.Forward;

            _body.Move(_body.Position, _body.Position + (turned * DISTURB_DISTANCE), Constants.Combat.BOSS_BODY_RADIUS);
            _body.Face(turned);
        }

        private void SetTargetHealth(float rate)
        {
            _player.SetHealth(_player.MaxHealth * rate);
            for (int i = 0; i < _extras.Count; ++i)
            {
                _extras[i].Target.SetHealth(_extras[i].Target.MaxHealth * rate);
            }
        }

        private void OnDestroy()
        {
            if (_presenter != null)
            {
                _presenter.ClearAll();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 하드 P07은 2페이즈 모드 안에서만 뜻이 있다
        private bool IsHardActive => _phaseTwoMode && _hardP07Mode;

        private double ReadClock()
        {
            return Time.timeAsDouble;
        }

        // 돌진 예고가 시작될 때 몸이 돌진 시작 시각까지 서버 방향에 닿도록 마감을 정한다
        private void SetBodyDeadline(double deadline)
        {
            _bodyDeadline = deadline;
        }

        // 서버 방향이 바로 바뀌는 돌진(P06, 2페이즈 연속 돌진)은 예고 동안 몸이 서버 방향(_body.Forward)으로 최단 경로를 따라 부드럽게 돌아
        // 돌진 시작 시각까지 닿는다. 서버 방향이 이미 부드럽게 도는 패턴은 지연 없이 서버 방향 그대로 보인다.
        // 서버의 조준, 이동, 판정은 이 표현과 상관없이 서버 방향을 쓴다
        private Vector3 FollowBodyFacing()
        {
            double now  = ReadClock();
            float  goal = Mathf.Atan2(_body.Forward.x, _body.Forward.z) * Mathf.Rad2Deg;

            if (_bodyDeadline > now)
            {
                _bodyMotion.Advance(now, goal, _bodyDeadline, BODY_TURN_RATE, BODY_MIN_HORIZON);
            }
            else
            {
                _bodyMotion.Reset(goal, now);
            }

            float yaw = _bodyMotion.Yaw;
            _bodyLagDegrees = Mathf.Abs(Mathf.DeltaAngle(yaw, goal));

            float radians = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        // 보스와 플레이어와 지속물 관리자를 새로 만든다. 처음과 Reset이 같이 쓴다
        private void Build()
        {
            _presenter.ClearAll();

            _catalog = new MucheolDevCatalog(_arenaDiameter, _phaseTwoMode, IsHardActive);
            _catalog.TryGetBoss(MucheolPatternFactory.BOSS_ID, out var boss);

            _registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            _resolver = new DamageResolver(_registry, ledger, DEFENSE_CONSTANT, REDUCTION_CAP);

            int obstacleMask = CombatLineOfSight.ResolveLayerMask(Constants.Combat.OBSTACLE_LAYER_NAME);
            var lineOfSight  = new CombatLineOfSight(obstacleMask, LINE_OF_SIGHT_HEIGHT);

            Vector3 center = transform.position;
            _body = new BossTestRigBody(_registry.IssueCombatId(), center, Vector3.forward,
                boss.HealthRef * _referenceAttack * _healthScale, boss.Defense, Constants.Combat.BOSS_BODY_RADIUS,
                center, boss.ArenaDiameter * HALF, lineOfSight);
            _player = IsHardActive
                ? new BossTestRigCombatant(_registry.IssueCombatId(), center + _playerStart, _hardPlayerHealth, _hardPlayerDefense, 0f)
                : new BossTestRigCombatant(_registry.IssueCombatId(), center + _playerStart, PLAYER_MAX_HP, 0f, 0f);
            _registry.Register(_player);
            _registry.Register(_body);

            _displacer = new BossTestRigDisplacer(_body);
            _displacer.Register(_player);
            RebuildExtraTargets();

            var bridge  = new BossTelegraphLocalBridge(_presenter, _body, ReadClock);
            var dashTap = new DashDeadlineTap(bridge, ReadClock, () => _brain.ActivePatternId, SetBodyDeadline);
            _combo     = new ComboVisual(_presenter, _body, ReadClock);
            _chargeLog = new ChargeOutcomeLog();

            // 팩토리 인자 순서: 부채꼴(P01, P02), 이동 창구와 돌진(P03, P06), 원형(P04), 콤보 데이터와 표현(P05), 충돌 회복(P06)
            var factory = new MucheolPatternFactory(_referenceHealth, bridge, new BoundaryMarginMovement(_body), dashTap, bridge,
                new MucheolComboSpec(new MucheolComboHit(5f, 120f, 0.14f, 0f), new MucheolComboHit(5f, 70f, 0.24f, 0.5f)), _combo,
                new MucheolChargeSpec(5f), null, _chargeLog);

            // 2페이즈 모드는 1페이즈 팩토리 뒤에 2페이즈 팩토리를 묶는다. 꺼 두면 1페이즈 팩토리 하나다
            IBossPatternFactory patterns = factory;
            _sequence  = null;
            _multiDash = null;
            _battlefield = null;
            if (_phaseTwoMode)
            {
                _sequence  = new SequenceVisual(_presenter, _body, ReadClock, () => _brain.ActivePatternId);
                _multiDash = new MultiDashVisual(_presenter, _body, ReadClock, () => _brain.ActivePatternId, SetBodyDeadline);
                _battlefield = new BattlefieldVisual(_presenter, _body, ReadClock);

                // 밀림 창구는 하드 스위치가 켜졌을 때만 준다. 꺼 두면 P07은 정의도 없고 팩토리도 만들지 않는다
                var phaseTwo = new MucheolPhase2PatternFactory(_referenceHealth, MucheolDevCatalog.CreatePhaseTwoSpec(), _body, _catalog,
                    new BoundaryMarginMovement(_body), _sequence, _multiDash, IsHardActive ? _displacer : null, _battlefield);
                patterns = new MucheolPatternFactoryChain(factory, phaseTwo);
            }

            var dependencies = new BossBrainDependencies
            {
                Catalog  = _catalog,
                Self     = _body,
                Vitals   = _body,
                Targets  = _registry,
                Damage   = _resolver,
                Patterns = patterns,
                Effects  = new BossEffectComposite(), // 무철 1페이즈에는 투사체나 장판 같은 지속물이 없다
                Control  = _body,
                Random   = new SeededBossRandom(Environment.TickCount)
            };

            _brain  = new BossBrain(MucheolPatternFactory.BOSS_ID, dependencies);
            _forced = new ForcedPatternRule(_catalog);
            _brain.AddSelectionRule(_forced);
            _body.Brain = _brain;

            double now = ReadClock();
            _bodyMotion.Reset(0f, now);
            _bodyDeadline = 0.0;
            if (!_brain.Begin(now))
            {
                Debug.LogError("[Boss] 전투를 열지 못했다");
                enabled = false;
                return;
            }

            _lastPlayerHealth = _player.Health;
            _lastPatternId    = string.Empty;
            _history.Clear();
            _counts.Clear();

            if (_phaseTwoMode && (_startHealthRate < 1f))
            {
                LowerHealthTo(_startHealthRate);
            }

            Debug.Log($"[Boss] 무철 {(_phaseTwoMode ? "2페이즈 모드" : "1페이즈")} 시험 시작, maxHealth={_body.MaxHealth:F0}, 전장 지름={boss.ArenaDiameter}");
        }

        // 체력을 목표 비율까지 깎는다. 몸체에는 회복이 없어 지금보다 높은 비율로는 올리지 못하고, 그때는 Reset으로 다시 만든다
        private void LowerHealthTo(float rate)
        {
            float target = _body.MaxHealth * rate;
            if (_body.Health > target)
            {
                _body.ApplyDamage(_body.Health - target, false, ReadClock());
            }
        }

        // 하드 스위치가 켜져 있으면 플레이어 옆에 테스트용 동료와 소환물을 등록한다. 둘 다 소환물(IsSummon)이라 보스 타깃 선택에서는 빠지고
        // 범위 피해는 받는다. 방어력은 플레이어와 같게 두었고, 이 값은 실제 동료 능력치가 아니라 임시 시험값이다
        private void RebuildExtraTargets()
        {
            for (int i = 0; i < _extras.Count; ++i)
            {
                _extras[i].Destroy();
            }

            _extras.Clear();
            if (!IsHardActive)
            {
                return;
            }

            Vector3 spot = _player.Position;
            AddExtraTarget("동료", Color.green, _player.MaxHealth * _hardCompanionRate, spot + (Vector3.left * PLACE_SPREAD));
            AddExtraTarget("소환물", Color.cyan, _hardSummonHealth, spot + (Vector3.right * PLACE_SPREAD));
        }

        private void AddExtraTarget(string label, Color color, float health, Vector3 position)
        {
            var target = new BossTestRigCombatant(_registry.IssueCombatId(), position, health, _hardPlayerDefense, 0f,
                isSummon: true, ownerCombatId: _player.CombatId);
            _registry.Register(target);
            _displacer.Register(target);
            _extras.Add(new ExtraTargetView(label, target, color, transform));
        }

        // 시험 몸체의 경계는 전장 반지름에서 몸체 반경을 뺀 거리다. 거기에 정확히 닿아 끝나면 이후 돌진이 0m가 된다
        private bool IsBossOnArenaBoundary()
        {
            Vector3 offset = _body.Position - transform.position;
            offset.y = 0f;
            float limit = (_arenaDiameter * HALF) - Constants.Combat.BOSS_BODY_RADIUS;
            return offset.magnitude >= (limit - BOUNDARY_TOLERANCE);
        }

        // 무철 외형 모델이 없어 캡슐과 앞쪽을 알리는 막대로 대신한다
        private void CreateBossStandIn()
        {
            GameObject capsule = TestRigDisplay.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "MucheolStandIn";
            Destroy(capsule.GetComponent<Collider>());
            float radiusScale = Constants.Combat.BOSS_BODY_RADIUS / CAPSULE_BASE_RADIUS;
            capsule.transform.SetParent(transform, false);
            capsule.transform.localScale = new Vector3(radiusScale, CAPSULE_HEIGHT_SCALE, radiusScale);
            capsule.GetComponent<Renderer>().material.color = BossColor;
            _bossBody = capsule.transform;

            GameObject nose = TestRigDisplay.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "MucheolFacing";
            Destroy(nose.GetComponent<Collider>());
            nose.transform.SetParent(transform, false);
            nose.transform.localScale = new Vector3(NOSE_WIDTH, NOSE_WIDTH, NOSE_LENGTH);
            nose.GetComponent<Renderer>().material.color = Color.yellow;
            _bossNose = nose.transform;
        }

        // 패턴이 바뀔 때마다 순서와 횟수를 남긴다
        private void TrackPattern()
        {
            string id = _brain.ActivePatternId;
            if ((id == _lastPatternId) || (string.IsNullOrEmpty(id)))
            {
                _lastPatternId = id;
                return;
            }

            _lastPatternId = id;
            _counts.TryGetValue(id, out int count);
            _counts[id] = count + 1;

            string suffix = id.Substring(id.Length - PATTERN_SUFFIX_LENGTH);
            _history.Add(id.Contains(PHASE_TWO_MARK) ? (PHASE_TWO_LABEL + suffix) : suffix);
            if (_history.Count > HISTORY_LENGTH)
            {
                _history.RemoveAt(0);
            }
        }

        private void ApplyView()
        {
            bool bossAlive = _body.LifeState == EParticipantLifeState.Alive;
            float bodyHeight = CAPSULE_HEIGHT_SCALE;

            Vector3 facing = FollowBodyFacing();

            _bossBody.position = _body.Position + (Vector3.up * bodyHeight);
            _bossBody.rotation = Quaternion.LookRotation(facing, Vector3.up);
            _bossBody.gameObject.SetActive(bossAlive);

            _bossNose.position = _body.Position + (facing * Constants.Combat.BOSS_BODY_RADIUS) + (Vector3.up * NOSE_HEIGHT);
            _bossNose.rotation = _bossBody.rotation;
            _bossNose.gameObject.SetActive(bossAlive);

            _playerBody.position = _player.Position + Vector3.up;
            _playerBody.gameObject.SetActive(_player.LifeState == EParticipantLifeState.Alive);

            for (int i = 0; i < _extras.Count; ++i)
            {
                _extras[i].Apply();
            }

            // 맞으면 잠깐 붉게 보여 판정이 났음을 알린다
            if (_player.Health < _lastPlayerHealth)
            {
                _flashUntil = Time.time + HIT_FLASH_SECONDS;
            }

            _lastPlayerHealth = _player.Health;
            _playerRenderer.material.color = (Time.time < _flashUntil) ? Color.red : Color.white;
        }

        /****************************************
        *            Helper Classes
        ****************************************/

        // 시험 몸체는 돌진이 전장 경계에 정확히 닿아 끝나면 그 자리에서 안쪽 돌진도 0m가 된다. 몸체를 고치지 않고(월백 시험과 같이 쓰는 코드)
        // 이 리그가 패턴에 주는 이동 창구에서 경계에 막히는 거리를 조금 줄여, 보스가 경계 위가 아니라 안쪽에 서게 한다
        private sealed class BoundaryMarginMovement : IBossMovement
        {
            private readonly BossTestRigBody _body;

            public BoundaryMarginMovement(BossTestRigBody body)
            {
                _body = body;
            }

            // 최대 거리보다 짧게 막혔다는 것은 경계나 벽에 걸렸다는 뜻이다. 몸체의 거리 계산은 막힘이 없어도 부동소수 오차(1e-7 안팎)만큼
            // 짧게 돌려주는 값이 있어서, 오차 허용 없이 비교하면 매 틱 막힌 것으로 오인해 돌진이 일찍 멈춘다
            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                float clear = _body.GetClearDistance(origin, direction, maxDistance, bodyRadius);
                return (clear < (maxDistance - CLAMP_DETECT_EPSILON)) ? Mathf.Max(0f, clear - BOUNDARY_MARGIN) : clear;
            }

            public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
            {
                Vector3 delta    = to - from;
                float   distance = delta.magnitude;
                if (distance <= 0f)
                {
                    return _body.Move(from, to, bodyRadius);
                }

                Vector3 direction = delta / distance;
                float   allowed   = GetClearDistance(from, direction, distance, bodyRadius);
                return _body.Move(from, from + (direction * allowed), bodyRadius);
            }
        }

        // 버튼으로 누른 패턴을 다음 선택에 한 번 강제한다. 선택 규칙은 일반 가중 선택보다 먼저 평가된다
        private sealed class ForcedPatternRule : IBossSelectionRule
        {
            private readonly MucheolDevCatalog _catalog;

            public string PendingId = string.Empty;

            public ForcedPatternRule(MucheolDevCatalog catalog)
            {
                _catalog = catalog;
            }

            public bool TrySelect(BossBlackboard blackboard, out PatternDefinition pattern)
            {
                pattern = null;
                if (string.IsNullOrEmpty(PendingId))
                {
                    return false;
                }

                pattern   = _catalog.Find(PendingId);
                PendingId = string.Empty;
                return pattern != null;
            }
        }

        // P06 결과를 화면 글자로 보인다. 돌진 표현은 월백 돌진과 같은 연결부가 맡는다
        private sealed class ChargeOutcomeLog : IBossChargeOutcomeListener
        {
            public string LastText = "아직 없음";

            public void OnChargeEnded(int castInstanceId, EBossChargeEnd end, BossChargeCollision collision, float recoverySeconds)
            {
                LastText = (end == EBossChargeEnd.Collision)
                    ? $"충돌({collision.Kind}) 간 거리 {collision.TravelDistance:F1}m, 회복 요청 {recoverySeconds:F1}초 (공용 FSM은 아직 안 읽음)"
                    : $"정상 종료, 회복 {recoverySeconds:F1}초";
            }
        }

        // 2페이즈 순차 타격을 예고 표현으로 보인다. 타마다 예고 하나씩이고 같은 시전의 예고는 시전 번호를 묶음으로 달아 서로 지우지 않는다.
        // 부채꼴, 직사각형, 원형 모두 시전 시작 때 한꺼번에 시작하고 각자 (예고 + 지연) 뒤에 확정된다
        private sealed class SequenceVisual : IMucheolSequenceTelegraphListener
        {
            private readonly BossTelegraphPresenter _presenter;
            private readonly BossTestRigBody        _body;
            private readonly Func<double>           _clock;
            private readonly Func<string>           _activePatternId;

            private IReadOnlyList<MucheolSequenceStep> _steps;

            public string LastEvent = "아직 없음";

            public SequenceVisual(BossTelegraphPresenter presenter, BossTestRigBody body, Func<double> clock, Func<string> activePatternId)
            {
                _presenter       = presenter;
                _body            = body;
                _clock           = clock;
                _activePatternId = activePatternId;
            }

            private static int Key(int castInstanceId, int index)
            {
                return (index == 0) ? castInstanceId : (castInstanceId + (COMBO_KEY_OFFSET * index));
            }

            private static Vector3 StepDirection(Vector3 aim, MucheolSequenceStep step)
            {
                return Quaternion.AngleAxis(step.AimOffsetDegrees, Vector3.up) * aim;
            }

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float telegraphSeconds,
                IReadOnlyList<MucheolSequenceStep> steps)
            {
                _steps = steps;
                _body.Face(direction);
                _presenter.enabled = true;

                string patternId = _activePatternId();
                for (int i = 0; i < steps.Count; ++i)
                {
                    MucheolSequenceStep step = steps[i];
                    float seconds = telegraphSeconds + step.DelaySeconds;
                    BossTelegraphStart start;

                    if (step.Shape == EMucheolSequenceShape.Fan)
                    {
                        start = BossTelegraphStart.Fan(_body.CombatId, Key(castInstanceId, i), patternId, origin,
                            StepDirection(direction, step), step.Range, step.ArcAngle, _clock(), seconds, castInstanceId);
                    }
                    else if (step.Shape == EMucheolSequenceShape.Line)
                    {
                        start = new BossTelegraphStart(_body.CombatId, Key(castInstanceId, i), castInstanceId, patternId,
                            EBossTelegraphShape.Line, origin, StepDirection(direction, step), step.Range, step.Width, 0f, _clock(), seconds);
                    }
                    else
                    {
                        start = BossTelegraphStart.Circle(_body.CombatId, Key(castInstanceId, i), patternId, origin, step.Range,
                            _clock(), seconds, castInstanceId);
                    }

                    _presenter.BeginTelegraph(in start);
                }

                LastEvent = $"예고 시작, 타 {steps.Count}개";
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction)
            {
                _body.Face(direction);
                for (int i = 0; i < _steps.Count; ++i)
                {
                    if (_steps[i].Shape != EMucheolSequenceShape.Circle)
                    {
                        _presenter.UpdateAim(Key(castInstanceId, i), StepDirection(direction, _steps[i]), _steps[i].Range, _clock());
                    }
                }
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction)
            {
                for (int i = 0; i < _steps.Count; ++i)
                {
                    if (_steps[i].Shape != EMucheolSequenceShape.Circle)
                    {
                        _presenter.LockDirection(Key(castInstanceId, i), StepDirection(direction, _steps[i]), _steps[i].Range);
                    }
                }
            }

            public void OnStepHit(int castInstanceId, int stepIndex, int hitCount)
            {
                _presenter.Strike(Key(castInstanceId, stepIndex), _clock());
                LastEvent = $"{stepIndex + 1}타 확정({_steps[stepIndex].Shape}), 맞은 대상 {hitCount}명";
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                _presenter.CancelGroup(castInstanceId);
                LastEvent = $"취소({reason})";
            }
        }

        // 1페이즈 돌진(P03, P06)의 알림을 그대로 표현 연결부에 넘기면서, 예고 시작부터 대상 쪽으로 바로 조준하는 P06은 돌진 시작 시각(예고 끝)을
        // 몸 회전의 마감으로 알린다. P03은 서버 방향이 이미 부드럽게 돌아 마감이 필요 없다
        private sealed class DashDeadlineTap : IBossDashTelegraphListener
        {
            private readonly IBossDashTelegraphListener _inner;
            private readonly Func<double>               _clock;
            private readonly Func<string>               _activePatternId;
            private readonly Action<double>             _onDeadline;

            public DashDeadlineTap(IBossDashTelegraphListener inner, Func<double> clock, Func<string> activePatternId,
                Action<double> onDeadline)
            {
                _inner           = inner;
                _clock           = clock;
                _activePatternId = activePatternId;
                _onDeadline      = onDeadline;
            }

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float length, float width,
                float telegraphSeconds)
            {
                if (_activePatternId() == MucheolPatternFactory.P06_ID)
                {
                    _onDeadline(_clock() + telegraphSeconds);
                }

                _inner.OnTelegraphStarted(castInstanceId, origin, direction, length, width, telegraphSeconds);
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction, float length)
            {
                _inner.OnAimChanged(castInstanceId, direction, length);
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction, float length)
            {
                _inner.OnDirectionLocked(castInstanceId, direction, length);
            }

            public void OnDashStarted(int castInstanceId, Vector3 origin, Vector3 direction, float distance, float seconds, float speed,
                double startServerTime)
            {
                _inner.OnDashStarted(castInstanceId, origin, direction, distance, seconds, speed, startServerTime);
            }

            public void OnDashStopped(int castInstanceId, Vector3 position, bool stoppedByWall, double stopServerTime)
            {
                _inner.OnDashStopped(castInstanceId, position, stoppedByWall, stopServerTime);
            }

            public void OnHit(int castInstanceId, int hitCount)
            {
                _inner.OnHit(castInstanceId, hitCount);
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                _inner.OnCancelled(castInstanceId, reason);
            }
        }

        // 2페이즈 연속 돌진을 예고 표현의 돌진으로 보인다. 돌진마다 예고 하나씩이고(첫 돌진은 시전 시작, 이후는 재조준 시작)
        // 같은 시전의 예고는 시전 번호를 묶음으로 달아 서로 지우지 않는다
        private sealed class MultiDashVisual : IMucheolMultiDashListener
        {
            private readonly BossTelegraphPresenter _presenter;
            private readonly BossTestRigBody        _body;
            private readonly Func<double>           _clock;
            private readonly Func<string>           _activePatternId;
            private readonly Action<double>         _onDeadline;

            private int _planned;

            public string LastEvent = "아직 없음";

            public MultiDashVisual(BossTelegraphPresenter presenter, BossTestRigBody body, Func<double> clock, Func<string> activePatternId,
                Action<double> onDeadline)
            {
                _presenter       = presenter;
                _body            = body;
                _clock           = clock;
                _activePatternId = activePatternId;
                _onDeadline      = onDeadline;
            }

            private static int Key(int castInstanceId, int dashIndex)
            {
                return (dashIndex == 0) ? castInstanceId : (castInstanceId + (COMBO_KEY_OFFSET * dashIndex));
            }

            private void BeginDashTelegraph(int castInstanceId, int dashIndex, Vector3 origin, Vector3 direction, float length,
                float width, float seconds)
            {
                _body.Face(direction);
                _onDeadline(_clock() + seconds);
                _presenter.enabled = true;

                var start = new BossTelegraphStart(_body.CombatId, Key(castInstanceId, dashIndex), castInstanceId, _activePatternId(),
                    EBossTelegraphShape.Dash, origin, direction, length, width, 0f, _clock(), seconds);
                _presenter.BeginTelegraph(in start);
            }

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float length, float width,
                float telegraphSeconds, int dashCount)
            {
                _planned = dashCount;
                BeginDashTelegraph(castInstanceId, 0, origin, direction, length, width, telegraphSeconds);
                LastEvent = $"예고 시작, 계획 {dashCount}회";
            }

            public void OnAimChanged(int castInstanceId, int dashIndex, Vector3 direction, float length)
            {
                _body.Face(direction);
                _presenter.UpdateAim(Key(castInstanceId, dashIndex), direction, length, _clock());
            }

            public void OnDirectionLocked(int castInstanceId, int dashIndex, Vector3 direction, float length)
            {
                _body.Face(direction);
                _presenter.LockDirection(Key(castInstanceId, dashIndex), direction, length);
            }

            public void OnReaimStarted(int castInstanceId, int dashIndex, Vector3 origin, Vector3 direction, float length, float width,
                float reaimSeconds)
            {
                BeginDashTelegraph(castInstanceId, dashIndex, origin, direction, length, width, reaimSeconds);
                LastEvent = $"{dashIndex + 1}번째 돌진 재조준";
            }

            public void OnDashStarted(int castInstanceId, int dashIndex, Vector3 origin, Vector3 direction, float distance, float seconds,
                float speed, double startServerTime)
            {
                _presenter.BeginDash(Key(castInstanceId, dashIndex), distance, seconds, speed, startServerTime);
                LastEvent = $"{dashIndex + 1}/{_planned}번째 돌진 시작, 거리 {distance:F1}m";
            }

            public void OnDashStopped(int castInstanceId, int dashIndex, Vector3 position, bool stoppedByWall,
                double stopServerTime)
            {
                _presenter.StopDash(Key(castInstanceId, dashIndex), position, stoppedByWall);
                LastEvent = $"{dashIndex + 1}번째 돌진 정지{(stoppedByWall ? "(벽)" : string.Empty)}";
            }

            public void OnHit(int castInstanceId, int dashIndex, int hitCount)
            {
                LastEvent = $"{dashIndex + 1}번째 돌진 적중 {hitCount}명";
            }

            public void OnTruncated(int castInstanceId, int plannedDashCount, int completedDashCount)
            {
                LastEvent = $"실행 구간이 짧아 잘림, 계획 {plannedDashCount}회 중 {completedDashCount}회 완료";
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                _presenter.CancelGroup(castInstanceId);
                LastEvent = $"취소({reason})";
            }
        }

        // 하드 P07 전장 공격을 예고 표현의 부채꼴로 보인다. 기존 부채꼴 연결부(BossTelegraphLocalBridge)는 부채꼴 알림을 월백 P02 ID로
        // 그리고 조준 갱신 알림을 받는 계약이라 쓰지 않는다. 예고는 시작 때 한 번 그리고 이후 방향을 바꾸지 않는다
        private sealed class BattlefieldVisual : IMucheolBattlefieldListener
        {
            private readonly BossTelegraphPresenter _presenter;
            private readonly BossTestRigBody        _body;
            private readonly Func<double>           _clock;
            private readonly List<string>           _pushes = new List<string>();

            public string LastEvent = "아직 없음";
            public string LastPush  = "아직 없음";

            public BattlefieldVisual(BossTelegraphPresenter presenter, BossTestRigBody body, Func<double> clock)
            {
                _presenter = presenter;
                _body      = body;
                _clock     = clock;
            }

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float radius, float arcAngle,
                float telegraphSeconds)
            {
                _pushes.Clear();
                LastPush = "아직 없음";
                _presenter.enabled = true;

                BossTelegraphStart start = BossTelegraphStart.Fan(_body.CombatId, castInstanceId, MucheolPhase2PatternFactory.P07_ID,
                    origin, direction, radius, arcAngle, _clock(), telegraphSeconds);
                _presenter.BeginTelegraph(in start);

                LastEvent = $"예고 시작, 저장 위치 {origin}, 저장 방향 {direction}, 반경 {radius:F0}m, 각도 {arcAngle:F0}도";
            }

            public void OnPushed(int castInstanceId, int combatId, float distance)
            {
                _pushes.Add($"#{combatId} {distance:F2}m");
                LastPush = string.Join(", ", _pushes);
            }

            public void OnHit(int castInstanceId, int hitCount)
            {
                _presenter.Strike(castInstanceId, _clock());
                LastEvent = $"타격 확정, 피해가 적용된 대상 {hitCount}명";
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                _presenter.Cancel(castInstanceId);
                LastEvent = $"취소({reason})";
            }
        }

        // 하드 시험에서 플레이어 곁에 세우는 동료와 소환물의 대역과 화면의 캡슐
        private sealed class ExtraTargetView
        {
            private readonly string    _label;
            private readonly Color     _color;
            private readonly Transform _body;
            private readonly Renderer  _renderer;

            private float _lastHealth;
            private float _flashUntil;

            public BossTestRigCombatant Target { get; }

            public ExtraTargetView(string label, BossTestRigCombatant target, Color color, Transform parent)
            {
                _label = label;
                Target = target;
                _color = color;

                GameObject capsule = TestRigDisplay.CreatePrimitive(PrimitiveType.Capsule);
                capsule.name = "Test" + label;
                UnityEngine.Object.Destroy(capsule.GetComponent<Collider>());
                capsule.transform.SetParent(parent, false);
                capsule.transform.localScale = Vector3.one * EXTRA_BODY_SCALE;

                _body       = capsule.transform;
                _renderer   = capsule.GetComponent<Renderer>();
                _lastHealth = target.Health;
            }

            public string Describe()
            {
                return $"{_label} HP {Target.Health:F0}/{Target.MaxHealth:F0} 위치 {Target.Position}";
            }

            public void Apply()
            {
                _body.position = Target.Position + (Vector3.up * EXTRA_BODY_SCALE);
                _body.gameObject.SetActive(Target.LifeState == EParticipantLifeState.Alive);

                if (Target.Health < _lastHealth)
                {
                    _flashUntil = Time.time + HIT_FLASH_SECONDS;
                }

                _lastHealth = Target.Health;
                _renderer.material.color = (Time.time < _flashUntil) ? Color.red : _color;
            }

            public void Destroy()
            {
                if (_body != null)
                {
                    UnityEngine.Object.Destroy(_body.gameObject);
                }
            }
        }

        // P05 2연타를 예고 표현의 부채꼴로 보인다. 타마다 부채꼴 하나씩이고 둘째는 첫째보다 지연만큼 늦게 확정된다
        private sealed class ComboVisual : IBossComboTelegraphListener
        {
            private readonly BossTelegraphPresenter _presenter;
            private readonly BossTestRigBody        _body;
            private readonly Func<double>           _clock;

            private IReadOnlyList<MucheolComboHit> _hits;

            public string LastEvent = "아직 없음";

            public ComboVisual(BossTelegraphPresenter presenter, BossTestRigBody body, Func<double> clock)
            {
                _presenter = presenter;
                _body      = body;
                _clock     = clock;
            }

            private static int Key(int castInstanceId, int index)
            {
                return (index == 0) ? castInstanceId : (castInstanceId + (COMBO_KEY_OFFSET * index));
            }

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float telegraphSeconds,
                IReadOnlyList<MucheolComboHit> hits)
            {
                _hits = hits;
                _body.Face(direction);
                _presenter.enabled = true;

                for (int i = 0; i < hits.Count; ++i)
                {
                    BossTelegraphStart start = BossTelegraphStart.Fan(_body.CombatId, Key(castInstanceId, i), MucheolPatternFactory.P05_ID,
                        origin, direction, hits[i].Range, hits[i].ArcAngle, _clock(), telegraphSeconds + hits[i].DelaySeconds, castInstanceId);
                    _presenter.BeginTelegraph(in start);
                }

                LastEvent = $"예고 시작, 타 {hits.Count}개";
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction)
            {
                _body.Face(direction);
                for (int i = 0; i < _hits.Count; ++i)
                {
                    _presenter.UpdateAim(Key(castInstanceId, i), direction, _hits[i].Range, _clock());
                }
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction)
            {
                for (int i = 0; i < _hits.Count; ++i)
                {
                    _presenter.LockDirection(Key(castInstanceId, i), direction, _hits[i].Range);
                }
            }

            public void OnHit(int castInstanceId, int hitIndex, int hitCount)
            {
                _presenter.Strike(Key(castInstanceId, hitIndex), _clock());
                LastEvent = $"{hitIndex + 1}타 확정, 맞은 대상 {hitCount}명";
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                _presenter.CancelGroup(castInstanceId);
                LastEvent = $"취소({reason})";
            }
        }
    }
}
