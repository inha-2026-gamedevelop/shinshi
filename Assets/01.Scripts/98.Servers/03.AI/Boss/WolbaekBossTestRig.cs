// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using BossDefinition = Shinshi.Data.BossPatternCatalogSO.BossDefinition;

namespace Shinshi.AI
{
    // 네트워크 없이 월백 1페이즈를 눈으로 확인하는 시험 장치다. 빈 오브젝트에 붙이면 월백과 WASD 플레이어를 만들고 바로 전투를 연다.
    // 서버 부트스트랩이 하게 될 생성, 틱 호출, 등록을 대신할 뿐이며 서버 권위 경계나 Command, Rpc를 바꾸지 않는다.
    // 지역 기준값은 플레이어 능력치가 아니라 아래 인스펙터 값으로 명시 주입한다. 보스 수치는 BossPatternCatalog 에셋의 값을 그대로 쓴다.
    // 외형은 읽기만 한다. 모델이 없으면 캡슐로 대신하고, 서버 몸체의 위치와 방향을 따라간다
    // 2페이즈는 전환 뒤 인계(CompleteHandoff)를 이 구동기가 대신 불러 이어 간다(끌 수 있다). 2페이즈 P07(워프 후 충격파)을 눈으로 보려는 용도라
    // 별형 빔과 평행 빔의 창구는 연결하지 않았고, 기본으로 2페이즈는 P07만 쓴다(끌 수 있다). 플레이어는 이동 방향을 정면으로 삼고 Space로 정면 방어한다.
    // 워프와 전장 입력은 BossTestRigBody의 로컬 대역이라 서버 몸체의 순간 이동, 충돌, 경로 검사를 대신하지 않는다
    /// <summary> 월백 시험 구동기, 단일 프로세스에서 서버 두뇌와 예고 표현과 대체 외형을 이어 준다 </summary>
    public sealed class WolbaekBossTestRig : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  PLAYER_SPEED     = 6f;
        private const float  PLAYER_MAX_HP    = 30000f;
        private const float  DEFENSE_CONSTANT = 1000f; // PlayerConfigSO 초안과 같은 값
        private const float  REDUCTION_CAP    = 0.6f;
        private const float  LINE_OF_SIGHT_HEIGHT = 0.5f;
        private const float  HIT_TEST_DAMAGE_RATE = 0.03f; // 피격 번쩍임 시험 버튼이 주는 피해, 최대 체력 비율
        private const float  HIT_FLASH_SECONDS = 0.15f;
        private const int    HISTORY_LENGTH   = 8;
        private const float  GUI_WIDTH        = 400f;
        private const float  GUI_MARGIN       = 10f;
        private const string HORIZONTAL_AXIS  = "Horizontal";
        private const string VERTICAL_AXIS    = "Vertical";
        private const float  HALF             = 0.5f;
        private const float  PERCENT          = 100f;
        private const float  GUI_SPACING      = 6f;
        private const float  HP_STEP_RATE     = 0.1f;
        private const float  TRANSITION_MARGIN_RATE = 0.01f; // 전환 직전으로 맞출 때 전환 비율 위에 남길 여유
        private const int    PATTERN_COUNT    = 6;
        private const int    PATTERN_SUFFIX_LENGTH = 2;
        private const string PATTERN_ID_PREFIX = "wolbaek_p0";
        private const float  PHASE_TWO_TEST_DISTANCE = 8f;
        private const float  FACING_MARKER_OFFSET    = 0.6f;
        private const float  FACING_MARKER_SCALE     = 0.25f;
        private const float  DIRECTION_EPSILON       = 0.0001f;
        private const float  WARP_BEHIND_DISTANCE    = 3f;

        // 부적진 기믹 시험 배치와 표시용 값이다. 모두 시험 장치 안에서만 쓴다
        private const int    MAX_TALISMAN_PARTICIPANTS = 4;
        private const float  BOT_SPREAD              = 2f;    // m, 참가자를 처음 세울 때 옆으로 벌리는 거리
        private const float  OUT_OF_SEAT_DISTANCE    = 6f;    // m, 자리 이탈 버튼이 구조물 중심에서 바깥쪽으로 옮기는 거리
        private const float  HP_BEFORE_TALISMAN      = 0.76f; // 75% 기믹 직전
        private const float  GUI_RIGHT_WIDTH         = 440f;
        private const float  LABEL_LIFT              = 1.5f;  // m, 구조물 글자를 바닥에서 띄우는 높이
        private const float  LABEL_WIDTH             = 160f;
        private const float  LABEL_HEIGHT            = 40f;
        private const float  PARTICIPANT_LABEL_WIDTH = 70f;
        private const float  BOSS_EDGE_MARGIN        = 0.1f;  // m, 보스를 가장자리로 옮길 때 경계 안쪽에 남기는 거리

        // 색 결계 기믹 시험 배치와 표시용 값이다. 모두 시험 장치 안에서만 쓴다
        private const float  HP_BEFORE_WARD          = 0.51f; // 50% 기믹 직전
        private const int    WARD_COLOR_COUNT        = 2;
        private const float  WARD_STRAY_DISTANCE     = 5f;    // m, 결계 이탈 버튼이 자기 결계 중심에서 옮기는 거리

        // 여우불 유도와 봉인 핵 기믹 시험 배치와 표시용 값이다. 모두 시험 장치 안에서만 쓴다
        private const float  HP_BEFORE_SEAL          = 0.26f; // 25% 기믹 직전
        private const float  SEAL_CORE_STEP_DISTANCE = 1f;    // m, 핵 옆 버튼이 핵 중심에서 비켜 서는 거리
        private const float  FINAL_HIT_AMOUNT        = 3000f; // 노란 여우불 시험 타격의 방어 전 피해
        private const float  FINAL_OUTSIDE_DISTANCE  = 12f;   // m, 바깥 버튼이 중앙에서 떨어지는 거리, 중앙 반경보다 크다
        private const string FINAL_HIT_ID            = "dev_final_hit";

        private static readonly Color DisconnectedColor = Color.gray;

        /****************************************
        *                Fields
        ****************************************/

        [Header("프리팹")]
        [SerializeField] private BossTelegraphPresenter _presenterPrefab; // 예고 표현 풀을 묶은 프리팹
        [SerializeField] private BossView               _bossViewPrefab;   // 월백 외형과 모션

        [Header("시험 주입값")]
        [SerializeField] private float   _referenceAttack = 644.32f; // Aref, 체험판 숲 기준
        [SerializeField] private float   _referenceHealth = 3541f;   // Href, 체험판 숲 기준
        [SerializeField] private float   _healthScale     = 1f;      // 시작 인원에 따른 체력 배율
        [SerializeField] private Vector3 _playerStart     = new Vector3(0f, 0f, 12f);

        [Header("2페이즈 시험")]
        [SerializeField] private bool _continueToPhaseTwo = true; // 전환 뒤 CompleteHandoff를 이 구동기가 대신 부른다, 운영에서는 서버 연결이 부른다
        [SerializeField] private bool _phaseTwoWarpOnly   = true; // 2페이즈에서 P07 워프 후 충격파만 쓴다

        [Header("부적진 기믹 시험 (75%, 기본 꺼짐)")]
        [SerializeField] private bool  _talismanMode        = false; // 켜면 75%에서 사방 부적진이 발동하고 참가자 대역이 생긴다
        [SerializeField] private bool  _talismanHard        = true;  // 하드는 고리와 직선 공격이 있고 이지는 자리 유지만 있다
        [SerializeField] private int   _talismanParticipants = 2;    // 필수 참가자 수, 1에서 4까지
        [SerializeField] private float _talismanHardReferenceHealth = 4255f; // 하드 Href, 하드 시험의 공격 피해에 쓴다

        [Header("색 결계 기믹 시험 (50%, 기본 꺼짐)")]
        [SerializeField] private bool  _wardMode            = false; // 켜면 50%에서 색 결계가 발동한다, 부적진 시험과 함께 켜면 75%에서 이어진다

        [Header("여우불 유도와 봉인 핵 기믹 시험 (25%, 기본 꺼짐)")]
        [SerializeField] private bool  _sealMode            = false; // 켜면 25%에서 발동한다, 색 결계 시험과 함께 켜면 50%에서 이어진다
        [SerializeField] private bool  _sealHard            = true;  // 하드는 휩쓸기가 있고 반격 중 0%에서 멈추며, 이지는 휩쓸기가 없고 처치 규칙을 따른다

        [Header("최후의 영역 시험 (하드 0%, 기본 꺼짐)")]
        [SerializeField] private bool  _finalMode           = false; // 켜면 25% 봉인(하드)에 이어 0%에서 발동한다, 바닥 제거와 추락은 임시 거리 판정이다

        private CombatTargetRegistry    _registry;
        private DamageResolver          _resolver;
        private BossTestRigBody         _body;
        private BossBrain               _brain;
        private BossProjectileManager   _projectiles;
        private BossAreaManager         _areas;
        private BossBeamManager         _beams;
        private BossStarManager         _stars;
        private RigPlayer               _player;
        private BossTelegraphPresenter  _presenter;
        private BossView                _bossView;
        private BossHitFlashView        _hitFlash;
        private Transform               _playerBody;
        private Renderer                _playerRenderer;
        private BossDefinition          _boss;
        private WolbaekTalismanFormation _talisman;
        private WolbaekTalismanDevView   _talismanView;
        private WolbaekColorWard         _ward;
        private WolbaekColorWardDevView  _wardView;
        private WolbaekFoxfireSeal       _seal;
        private WolbaekFoxfireSealDevView _sealView;
        private WolbaekFinalArea         _final;
        private WolbaekFinalAreaDevView  _finalView;
        private WolbaekFinalDevJudge     _finalJudge;
        private bool                    _requestedFinal;
        private BossGimmickDeathPolicy   _deathPolicy;
        private bool                    _requestedWard;
        private bool                    _requestedSeal;
        private bool                    _requestedSealHard;
        private RigDisplacer             _displacer;
        private readonly List<TalismanMember> _members = new List<TalismanMember>();
        private Vector2                 _panelScroll;
        private bool                    _rebuildRequested;
        private bool                    _requestedMode;
        private bool                    _requestedHard;
        private int                     _requestedParticipants;
        private float                   _lastPlayerHealth;
        private float                   _flashUntil;
        private string                  _lastPatternId = string.Empty;
        private readonly List<string>   _history = new List<string>();
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        /****************************************
        *               Test Doubles
        ****************************************/

        // 플레이어 대역, 이동 방향을 정면으로 삼고 정면 방어를 켤 수 있다. EnemyTestRigPlayer는 정면이 고정이라 후방 착지를 볼 수 없어 따로 둔다
        private sealed class RigPlayer : IDamageable
        {
            public int                   CombatId        { get; }
            public ECombatFaction        Faction         => ECombatFaction.Player;
            public EParticipantLifeState LifeState       { get; private set; } = EParticipantLifeState.Alive;
            public bool                  IsSummon        => false;
            public int                   OwnerCombatId   => CombatIds.NONE;
            public Vector3               Position        { get; set; }
            public Vector3               Forward         { get; set; } = Vector3.forward;
            public float                 BodyRadius      => 0f;
            public float                 Health          { get; private set; }
            public float                 MaxHealth       { get; }
            public float                 Defense         => 0f;
            public bool                  IsInvulnerable  => false;
            public bool                  IsGuardingFront { get; set; }

            public RigPlayer(int combatId, Vector3 position, float maxHealth)
            {
                CombatId  = combatId;
                Position  = position;
                MaxHealth = maxHealth;
                Health    = maxHealth;
            }

            public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
            {
                if (isLethal)
                {
                    Health    = 0f;
                    LifeState = EParticipantLifeState.Dead;
                    return;
                }

                Health = Mathf.Max(0f, Health - appliedAmount);
            }

            /// <summary> 시험 버튼용 즉사 </summary>
            public void Kill()
            {
                Health    = 0f;
                LifeState = EParticipantLifeState.Dead;
            }

            /// <summary> 시험 버튼용 부활, 체력을 가득 채운다 </summary>
            public void Revive()
            {
                Health    = MaxHealth;
                LifeState = EParticipantLifeState.Alive;
            }
        }

        // 부적진 기믹의 필수 참가자 한 명, 첫 참가자는 WASD 플레이어고 나머지는 버튼으로만 움직이는 대역이다
        private sealed class TalismanMember
        {
            public RigPlayer  Player;
            public Transform  Body;
            public Renderer   Renderer;
            public bool       Connected = true;
            public bool       Revivable = true;
            public bool       RevivePending;
        }

        private sealed class RigRoster : IBossParticipantRoster, IBossParticipantHealth
        {
            private readonly List<TalismanMember> _members;

            public RigRoster(List<TalismanMember> members)
            {
                _members = members;
            }

            public int CollectParticipants(List<int> combatIds)
            {
                for (int i = 0; i < _members.Count; ++i)
                {
                    combatIds.Add(_members[i].Player.CombatId);
                }

                return combatIds.Count;
            }

            public bool TryGetMaxHealth(int combatId, out float maxHealth)
            {
                for (int i = 0; i < _members.Count; ++i)
                {
                    if (_members[i].Player.CombatId == combatId)
                    {
                        maxHealth = _members[i].Player.MaxHealth;
                        return true;
                    }
                }

                maxHealth = 0f;
                return false;
            }

            public bool TryGetParticipant(int combatId, out BossParticipantInfo info)
            {
                for (int i = 0; i < _members.Count; ++i)
                {
                    TalismanMember member = _members[i];
                    if (member.Player.CombatId == combatId)
                    {
                        info = new BossParticipantInfo(member.Player.LifeState, member.Connected, member.Revivable, member.Player.Position);
                        return true;
                    }
                }

                info = default;
                return false;
            }
        }

        // 맞은 시험 대역을 전장 경계 안에서만 민다. 운영 서버의 플레이어 이동 밀림은 이 대역이 대신하지 못한다
        private sealed class RigDisplacer : IBossHitDisplacer
        {
            private readonly BossTestRigBody                _body;
            private readonly Dictionary<int, RigPlayer>     _targets = new Dictionary<int, RigPlayer>();

            public RigDisplacer(BossTestRigBody body)
            {
                _body = body;
            }

            public void Register(RigPlayer player)
            {
                _targets[player.CombatId] = player;
            }

            public float TryPush(int combatId, Vector3 direction, float maxDistance)
            {
                if ((!_targets.TryGetValue(combatId, out RigPlayer target)) || (maxDistance <= 0f))
                {
                    return 0f;
                }

                float distance = Mathf.Clamp(_body.GetClearDistance(target.Position, direction, maxDistance, target.BodyRadius), 0f, maxDistance);
                target.Position += direction * distance;
                return distance;
            }
        }

        // 켜져 있으면 2페이즈에서 P07 외의 정의를 만들지 않는다, 선택 단계에서 후보로 남지 않으므로 P07만 뽑힌다
        private sealed class PhaseTwoFilter : IBossPatternFactory, IBossPatternAvailability
        {
            private readonly IBossPatternFactory _inner;
            private readonly Func<bool>          _isWarpOnly;

            public PhaseTwoFilter(IBossPatternFactory inner, Func<bool> isWarpOnly)
            {
                _inner      = inner;
                _isWarpOnly = isWarpOnly;
            }

            public bool CanCreate(BossPatternCatalogSO.PatternDefinition definition)
            {
                return (!IsHidden(definition)) && ((!(_inner is IBossPatternAvailability availability)) || availability.CanCreate(definition));
            }

            public IBossPattern Create(BossPatternCatalogSO.PatternDefinition definition)
            {
                return IsHidden(definition) ? null : _inner.Create(definition);
            }

            private bool IsHidden(BossPatternCatalogSO.PatternDefinition definition)
            {
                return _isWarpOnly() && (definition.Phase > 1) && (definition.DefinitionId != WolbaekPatternFactory.P2_P07_ID);
            }
        }

        /****************************************
        *              Unity Event
        ****************************************/

        // GameDB는 Awake가 아니라 Start 이후에 읽는다
        private void Start()
        {
            if ((_presenterPrefab == null))
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

            GameObject body = TestRigDisplay.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "PlayerBody";
            Destroy(body.GetComponent<Collider>());
            _playerBody     = body.transform;
            _playerRenderer = body.GetComponent<Renderer>();
            _playerBody.SetParent(transform, false);

            // 정면 표시, 캡슐은 앞뒤가 같아 후방 착지를 구분할 수 없다
            GameObject marker = TestRigDisplay.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "FacingMarker";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(_playerBody, false);
            marker.transform.localPosition = new Vector3(0f, 0f, FACING_MARKER_OFFSET);
            marker.transform.localScale    = Vector3.one * FACING_MARKER_SCALE;

            CreateBossView();
            if (_bossView == null)
            {
                return;
            }

            Build();
        }

        private void Update()
        {
            if (_brain == null)
            {
                return;
            }

            // 운영에서는 서버가 내려주는 체력 SyncVar가 하게 될 일을 몸체 체력으로 대신한다
            _hitFlash?.Observe(_body.Health);

            // 시험 모드 변경은 GUI 이벤트 도중에 적용하면 컨트롤 수가 어긋나므로 요청만 받아 두고 여기서 적용한다
            if (_rebuildRequested)
            {
                _rebuildRequested     = false;
                _talismanMode         = _requestedMode;
                _wardMode             = _requestedWard;
                _sealMode             = _requestedSeal;
                _sealHard             = _requestedSealHard;
                _finalMode            = _requestedFinal;
                _talismanHard         = _requestedHard;
                _talismanParticipants = _requestedParticipants;
                Build();
                return;
            }

            double now = ReadClock();

            if (_player.LifeState == EParticipantLifeState.Alive)
            {
                Vector3 move = new Vector3(Input.GetAxisRaw(HORIZONTAL_AXIS), 0f, Input.GetAxisRaw(VERTICAL_AXIS));
                _player.Position += move.normalized * (PLAYER_SPEED * Time.deltaTime);
                if (move.sqrMagnitude > DIRECTION_EPSILON)
                {
                    _player.Forward = move.normalized;
                }

                _player.IsGuardingFront = Input.GetKey(KeyCode.Space);
            }

            // 전환 뒤 인계 대기는 서버 연결이 CompleteHandoff를 불러야 풀린다. 이 구동기가 그 자리를 대신한다
            if (_continueToPhaseTwo && (_brain.PhaseState == EBossPhaseState.AwaitingHandoff))
            {
                _brain.CompleteHandoff();
            }

            ApplyPendingRevives();

            _resolver.BeginTick(now);
            _body.Brain = _brain;
            _brain.Tick(now);
            _talismanView?.Update();
            _wardView?.Update();
            _sealView?.Update();
            _finalView?.Update();

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
            GUILayout.Label($"Flow {_brain.FlowState}  Phase {_brain.Phase} ({_brain.PhaseState})");
            GUILayout.Label($"Attack {_brain.AttackState}  Pattern {_brain.ActivePatternId}");
            GUILayout.Label($"Boss HP {_body.Health:F0}/{_body.MaxHealth:F0} ({_body.HealthRate * 100f:F1}%)  " +
                $"전환 {_boss.PhaseTransitionHealthRate * PERCENT:F0}%");
            GUILayout.Label($"Player HP {_player.Health:F0}/{_player.MaxHealth:F0}  장판 {_presenter.AreaCount}  " +
                $"투사체 {_presenter.ProjectileCount}");

            Vector3 toPlayer = _player.Position - _body.Position;
            toPlayer.y = 0f;
            GUILayout.Label($"보스-플레이어 거리 {toPlayer.magnitude:F1}m  보스 ({_body.Position.x:F1}, {_body.Position.z:F1})  " +
                $"플레이어 ({_player.Position.x:F1}, {_player.Position.z:F1}) 정면 ({_player.Forward.x:F1}, {_player.Forward.z:F1})");
            GUILayout.Label("WASD 이동(이동 방향이 정면), Space 정면 방어");

            if (GUILayout.Button("보스에게 피해 주기 (최대 체력 3%)"))
            {
                _body.ApplyDamage(_body.MaxHealth * HIT_TEST_DAMAGE_RATE, false, ReadClock());
            }

            if (_brain.PhaseState == EBossPhaseState.AwaitingHandoff)
            {
                GUILayout.Label("전환 뒤 인계 대기");
                if ((!_continueToPhaseTwo) && GUILayout.Button("CompleteHandoff 호출"))
                {
                    _brain.CompleteHandoff();
                }
            }
            else if (_brain.PhaseState != EBossPhaseState.Active)
            {
                GUILayout.Label($"2페이즈 {_brain.PhaseState}: 실행할 수 있는 패턴이 없다");
            }

            _continueToPhaseTwo = GUILayout.Toggle(_continueToPhaseTwo, "전환 뒤 CompleteHandoff 자동 호출");
            _phaseTwoWarpOnly   = GUILayout.Toggle(_phaseTwoWarpOnly, "2페이즈는 P07 워프만 사용");

            bool wantTalisman = GUILayout.Toggle(_talismanMode, "75% 사방 부적진 시험 (바꾸면 전투를 새로 시작)");
            if (wantTalisman != _talismanMode)
            {
                RequestRebuild(wantTalisman, _talismanHard, _talismanParticipants);
            }

            bool wantWard = GUILayout.Toggle(_wardMode, "50% 색 결계 시험 (바꾸면 전투를 새로 시작)");
            if (wantWard != _wardMode)
            {
                RequestWardRebuild(wantWard, _talismanParticipants);
            }

            bool wantSeal = GUILayout.Toggle(_sealMode, "25% 여우불 유도와 봉인 핵 시험 (바꾸면 전투를 새로 시작)");
            if (wantSeal != _sealMode)
            {
                RequestSealRebuild(wantSeal, _sealHard, _talismanParticipants);
            }

            bool wantSealHard = GUILayout.Toggle(_sealHard, "  25% 시험은 하드 (휩쓸기 있음, 반격 중 0%에서 멈춤)");
            if (wantSealHard != _sealHard)
            {
                RequestSealRebuild(_sealMode, wantSealHard, _talismanParticipants);
            }

            bool wantFinal = GUILayout.Toggle(_finalMode, "하드 0% 최후의 영역 시험 (25% 하드 봉인을 함께 켜고 새로 시작, 바닥은 임시 거리 판정)");
            if (wantFinal != _finalMode)
            {
                RequestFinalRebuild(wantFinal, _talismanParticipants);
            }

            GUILayout.Space(GUI_SPACING);
            GUILayout.Label("패턴 실행 횟수");
            for (int i = 1; i <= PATTERN_COUNT; ++i)
            {
                string id = PATTERN_ID_PREFIX + i;
                _counts.TryGetValue(id, out int count);
                GUILayout.Label($"  {id}  {count}");
            }

            _counts.TryGetValue(WolbaekPatternFactory.P2_P07_ID, out int warpCount);
            GUILayout.Label($"  {WolbaekPatternFactory.P2_P07_ID}  {warpCount}");

            GUILayout.Space(GUI_SPACING);
            GUILayout.Label("최근 순서 " + string.Join(" > ", _history));

            GUILayout.Space(GUI_SPACING);
            if (GUILayout.Button("Boss HP -10%"))
            {
                DamageBossByRate(HP_STEP_RATE);
            }

            if (GUILayout.Button("Boss HP를 전환 직전(51%)으로"))
            {
                DamageBossToRate(_boss.PhaseTransitionHealthRate + TRANSITION_MARGIN_RATE);
            }

            if (GUILayout.Button("플레이어를 보스 정면 8m로 옮기기"))
            {
                _player.Position = _body.Position + (Vector3.forward * PHASE_TWO_TEST_DISTANCE);
            }

            if (GUILayout.Button("Kill boss"))
            {
                _body.ApplyDamage(0f, true, ReadClock());
            }

            if (GUILayout.Button("Reset"))
            {
                Build();
            }

            GUILayout.EndArea();

            if ((_talisman != null) || (_ward != null) || (_seal != null) || (_final != null))
            {
                DrawRightPanel();
                DrawStructureLabels();
                DrawWardLabels();
            }
        }

        private void OnDestroy()
        {
            if (_presenter != null)
            {
                _presenter.ClearAll();
            }

            _talismanView?.ClearAll();
            _wardView?.ClearAll();
            _sealView?.ClearAll();
            _finalView?.ClearAll();
        }

        /****************************************
        *            Talisman Test
        ****************************************/

        private void RequestRebuild(bool mode, bool isHard, int participants)
        {
            _rebuildRequested      = true;
            _requestedMode         = mode;
            _requestedHard         = isHard;
            _requestedWard         = _wardMode;
            _requestedSeal         = _sealMode;
            _requestedSealHard     = _sealHard;
            _requestedFinal        = _finalMode;
            _requestedParticipants = Mathf.Clamp(participants, 1, MAX_TALISMAN_PARTICIPANTS);
        }

        private void RequestWardRebuild(bool ward, int participants)
        {
            _rebuildRequested      = true;
            _requestedMode         = _talismanMode;
            _requestedHard         = _talismanHard;
            _requestedWard         = ward;
            _requestedSeal         = _sealMode;
            _requestedSealHard     = _sealHard;
            _requestedFinal        = _finalMode;
            _requestedParticipants = Mathf.Clamp(participants, 1, MAX_TALISMAN_PARTICIPANTS);
        }

        // 최후의 영역은 25% 하드 봉인의 인계로 시작하므로 켜면 봉인도 하드로 함께 켠다
        private void RequestFinalRebuild(bool final, int participants)
        {
            _rebuildRequested      = true;
            _requestedMode         = _talismanMode;
            _requestedHard         = _talismanHard;
            _requestedWard         = _wardMode;
            _requestedSeal         = final || _sealMode;
            _requestedSealHard     = final || _sealHard;
            _requestedFinal        = final;
            _requestedParticipants = Mathf.Clamp(participants, 1, MAX_TALISMAN_PARTICIPANTS);
        }

        private void RequestSealRebuild(bool seal, bool isHard, int participants)
        {
            _rebuildRequested      = true;
            _requestedMode         = _talismanMode;
            _requestedHard         = _talismanHard;
            _requestedWard         = _wardMode;
            _requestedSeal         = seal;
            _requestedSealHard     = isHard;
            _requestedFinal        = _finalMode;
            _requestedParticipants = Mathf.Clamp(participants, 1, MAX_TALISMAN_PARTICIPANTS);
        }

        // 오른쪽 패널, 켜진 기믹마다 진행과 참가자 조작과 배정과 최근 알림을 보인다
        private void DrawRightPanel()
        {
            Rect area = new Rect(Screen.width - GUI_RIGHT_WIDTH - GUI_MARGIN, TestRigDisplay.GUI_TOP, GUI_RIGHT_WIDTH,
                Screen.height - TestRigDisplay.GUI_TOP - GUI_MARGIN);
            GUILayout.BeginArea(area, GUI.skin.box);
            _panelScroll = GUILayout.BeginScrollView(_panelScroll);

            if (_talisman != null)
            {
                DrawTalismanSection();
            }

            if (_ward != null)
            {
                DrawWardSection();
            }

            if (_seal != null)
            {
                DrawSealSection();
            }

            if (_final != null)
            {
                DrawFinalSection();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawTalismanSection()
        {
            double now = ReadClock();

            GUILayout.Label($"부적진 {(_talismanHard ? "하드" : "이지")}  상태 {_talisman.State}  필수 {_talisman.RequiredCount}명");
            GUILayout.Label($"Boss HP {_body.HealthRate * PERCENT:F1}%  무적 {_talisman.IsDamageBlocked}  일반 선택 차단 {_talisman.BlocksSelection}");
            GUILayout.Label(DescribeProgress(now));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("HP 76%로"))
            {
                DamageBossToRate(HP_BEFORE_TALISMAN);
            }

            if (GUILayout.Button("Boss HP -10%"))
            {
                DamageBossByRate(HP_STEP_RATE);
            }

            if (GUILayout.Button("모두 자리로"))
            {
                SeatEveryone();
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("보스 가장자리(+x)로"))
            {
                MoveBossToEdge();
            }

            if (GUILayout.Button("보스 중앙으로"))
            {
                _body.Move(_body.Position, transform.position, _body.BodyRadius);
            }

            GUILayout.EndHorizontal();
            GUILayout.Label($"보스 위치 ({_body.Position.x:F1}, {_body.Position.z:F1})  중앙까지 {Vector3.Distance(new Vector3(_body.Position.x, 0f, _body.Position.z), new Vector3(transform.position.x, 0f, transform.position.z)):F1}m");

            GUILayout.BeginHorizontal();
            GUILayout.Label("참가 인원", GUILayout.Width(PARTICIPANT_LABEL_WIDTH));
            for (int count = 1; count <= MAX_TALISMAN_PARTICIPANTS; ++count)
            {
                if (GUILayout.Toggle(_talismanParticipants == count, count.ToString(), GUI.skin.button) && (_talismanParticipants != count))
                {
                    RequestRebuild(true, _talismanHard, count);
                }
            }

            GUILayout.EndHorizontal();

            bool wantHard = GUILayout.Toggle(_talismanHard, "하드 (고리와 직선 공격), 끄면 이지 (공격 없음)");
            if (wantHard != _talismanHard)
            {
                RequestRebuild(true, wantHard, _talismanParticipants);
            }

            GUILayout.Space(GUI_SPACING);
            for (int i = 0; i < _members.Count; ++i)
            {
                DrawMemberRow(i);
            }

            GUILayout.Space(GUI_SPACING);
            for (int i = 0; i < _talisman.Structures.Count; ++i)
            {
                WolbaekTalismanStructure structure = _talisman.Structures[i];
                GUILayout.Label($"구조물 방위 {structure.DirectionIndex}  {structure.Color}  문양 {structure.SymbolId}  배정 {DescribeOwner(structure.AssignedCombatId)}");
            }

            GUILayout.Space(GUI_SPACING);
            for (int i = _talismanView.Log.Count - 1; i >= 0; --i)
            {
                GUILayout.Label(_talismanView.Log[i]);
            }

            GUILayout.Space(GUI_SPACING);
            GUILayout.Label("미연결: 상시 소형 낙하 여우불은 아직 구현되지 않아 멈추고 재개하는 자리만 있다");
            GUILayout.Label("경계 멈춤과 무적은 시험 몸체에서만 닿는다. 운영 보스 몸체와 피해 판정기 연결은 별도 작업이다");
        }

        private string DescribeProgress(double now)
        {
            switch (_talisman.State)
            {
                case EWolbaekTalismanState.Transition:
                    return $"전환 시간 {Math.Max(0d, _talisman.TransitionEndServerTime - now):F1}초 남음, 월백이 중앙으로 이동 중";
                case EWolbaekTalismanState.Active:
                    return _talisman.HasHold
                        ? $"전원 자리 유지 {now - _talisman.HoldStartServerTime:F1}초 / {WolbaekTalismanDevCatalog.HOLD_SECONDS:F0}초"
                        : "전원이 자기 자리에 모이면 유지 시간이 시작된다";
                case EWolbaekTalismanState.Counter:
                    return $"반격 시간 {Math.Max(0d, _talisman.CounterEndServerTime - now):F1}초 남음";
                case EWolbaekTalismanState.Halted:
                    return $"정지 - {_talisman.HaltReason}. 끝내는 방법은 정해지지 않아 Reset으로만 풀린다";
                default:
                    return string.Empty;
            }
        }

        private void DrawMemberRow(int index)
        {
            TalismanMember member = _members[index];
            GUILayout.BeginHorizontal();
            GUILayout.Label($"P{index} {(index == 0 ? "(WASD)" : string.Empty)} {member.Player.LifeState}  {(member.Connected ? "접속" : "끊김")}  " +
                $"{(member.Revivable ? string.Empty : "부활불가")}  HP {member.Player.Health:F0}", GUILayout.Width(GUI_RIGHT_WIDTH * 0.5f));

            if (GUILayout.Button("사망"))
            {
                member.Player.Kill();
            }

            if (GUILayout.Button("부활"))
            {
                member.Player.Revive();
            }

            if (GUILayout.Button("접속"))
            {
                member.Connected = !member.Connected;
            }

            if (GUILayout.Button("이탈"))
            {
                MoveOutOfSeat(index);
            }

            if (GUILayout.Button("복귀"))
            {
                MoveToSeat(index);
            }

            if (GUILayout.Button("부활불가"))
            {
                member.Revivable = !member.Revivable;
            }

            GUILayout.EndHorizontal();
        }

        private string DescribeOwner(int combatId)
        {
            for (int i = 0; i < _members.Count; ++i)
            {
                if (_members[i].Player.CombatId == combatId)
                {
                    return "P" + i;
                }
            }

            return "없음";
        }

        // 구조물 위에 색과 문양 번호와 배정된 참가자를 글자로 띄운다. 문양 그림은 확정되지 않았다
        private void DrawStructureLabels()
        {
            Camera view = Camera.main;
            if ((view == null) || (_talisman == null))
            {
                return;
            }

            for (int i = 0; i < _talisman.Structures.Count; ++i)
            {
                WolbaekTalismanStructure structure = _talisman.Structures[i];
                Vector3 screen = view.WorldToScreenPoint(structure.Center + (Vector3.up * LABEL_LIFT));
                if (screen.z <= 0f)
                {
                    continue;
                }

                Rect rect = new Rect(screen.x - (LABEL_WIDTH * HALF), Screen.height - screen.y - (LABEL_HEIGHT * HALF), LABEL_WIDTH, LABEL_HEIGHT);
                GUI.Label(rect, $"{structure.Color} / 문양 {structure.SymbolId}\n{DescribeOwner(structure.AssignedCombatId)}");
            }
        }

        /****************************************
        *              Ward Test
        ****************************************/

        // 두뇌 시작 전에 색 결계 기믹을 얹는다. 부적진이 같이 켜져 있으면 그 기믹을 앞선 기믹으로 넘겨 75% 반격 중 50% 도달을 이어 받게 하고
        // 시험 몸체의 상한 계약은 두 기믹을 합성해 읽게 한다. 결계 후보는 시험용 임시 배치이고 운영 좌표가 아니다
        private void AttachWard()
        {
            if (!_wardMode)
            {
                return;
            }

            _wardView = new WolbaekColorWardDevView(transform, ReadClock, ReadMemberPosition, transform.position,
                _boss.ArenaDiameter * HALF);
            RigRoster roster = new RigRoster(_members);

            _ward = new WolbaekColorWard(WolbaekColorWardDevCatalog.CreateSpec(), _body, _body, roster, roster, _body,
                new WolbaekWardDevSites(transform.position), _resolver, _registry, new SeededBossRandom(Environment.TickCount),
                _displacer, _talisman, _wardView);

            _brain.AddGimmick(_ward);
            _body.DamageCap = (_talisman != null) ? new BossDamageCapComposite(_talisman, _ward) : (IBossDamageCap)_ward;
        }

        // 쓰러졌거나 명단에 없으면 거짓이다, 연결 띠 표시가 쓴다
        private bool ReadMemberPosition(int combatId, out Vector3 position)
        {
            for (int i = 0; i < _members.Count; ++i)
            {
                if ((_members[i].Player.CombatId == combatId) && (_members[i].Player.LifeState == EParticipantLifeState.Alive))
                {
                    position = _members[i].Player.Position;
                    return true;
                }
            }

            position = Vector3.zero;
            return false;
        }

        // 부활 요청은 색 결계의 광역 공격이 끝날 때까지 보류한다, 운영에서는 세션이 같은 창구를 읽어야 한다
        private void ApplyPendingRevives()
        {
            if ((_ward != null) && (!_ward.IsReviveWindowOpen))
            {
                return;
            }

            for (int i = 0; i < _members.Count; ++i)
            {
                if (_members[i].RevivePending)
                {
                    _members[i].RevivePending = false;
                    _members[i].Player.Revive();
                    ApplyFinalRevive(_members[i].Player);
                }
            }
        }

        private void DrawWardSection()
        {
            double now = ReadClock();

            GUILayout.Label("---- 50% 색 결계 (시험용 임시 후보 좌표) ----");
            GUILayout.Label($"상태 {_ward.State}  필수 {_ward.RequiredCount}명  Boss HP {_body.HealthRate * PERCENT:F1}%");
            GUILayout.Label($"무적 {_ward.IsDamageBlocked}  일반 선택 차단 {_ward.BlocksSelection}  부활 창구 {(_ward.IsReviveWindowOpen ? "열림" : "닫힘")}");
            GUILayout.Label(DescribeWardProgress(now));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("HP 51%로"))
            {
                DamageBossToRate(HP_BEFORE_WARD);
            }

            if (GUILayout.Button("Boss HP -10%"))
            {
                DamageBossByRate(HP_STEP_RATE);
            }

            if (GUILayout.Button("모두 중앙으로"))
            {
                for (int i = 0; i < _members.Count; ++i)
                {
                    _members[i].Player.Position = transform.position;
                }
            }

            if (GUILayout.Button("모두 자기 결계로"))
            {
                for (int i = 0; i < _members.Count; ++i)
                {
                    MoveToWard(i, Vector3.zero);
                }
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("참가 인원", GUILayout.Width(PARTICIPANT_LABEL_WIDTH));
            for (int count = 1; count <= MAX_TALISMAN_PARTICIPANTS; ++count)
            {
                if (GUILayout.Toggle(_talismanParticipants == count, count.ToString(), GUI.skin.button) && (_talismanParticipants != count))
                {
                    RequestWardRebuild(true, count);
                }
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(GUI_SPACING);
            for (int i = 0; i < _members.Count; ++i)
            {
                DrawWardMemberRow(i, now);
            }

            GUILayout.Space(GUI_SPACING);
            for (int i = _wardView.Log.Count - 1; i >= 0; --i)
            {
                GUILayout.Label(_wardView.Log[i]);
            }

            GUILayout.Space(GUI_SPACING);
            GUILayout.Label("미연결: 운영 보스 몸체와 피해 판정기와 세션과 Net은 연결되지 않았다. 후보 좌표와 부활 위치는 맵 검증 전이다");
        }

        private string DescribeWardProgress(double now)
        {
            switch (_ward.State)
            {
                case EWolbaekWardState.Gather:
                    return _ward.HasHold
                        ? $"중앙 연속 집결 {now - _ward.HoldStartServerTime:F1}초 / {WolbaekColorWardDevCatalog.GATHER_HOLD_SECONDS:F0}초"
                        : "필수 참가자 전원이 중앙 결계(반경 5m)에 모이면 연속 집결이 시작된다";
                case EWolbaekWardState.Search:
                    return $"지점 {_ward.StageIndex + 1}/{WolbaekColorWardDevCatalog.STAGE_COUNT} 탐색, 첫 광역 공격까지 {Math.Max(0d, _ward.Stage.FirstStrikeServerTime - now):F1}초";
                case EWolbaekWardState.Strike:
                    return $"지점 {_ward.StageIndex + 1}/{WolbaekColorWardDevCatalog.STAGE_COUNT} 광역 공격 중, 다음 공격 {_ward.NextStrikeIndex + 1}번";
                case EWolbaekWardState.Halted:
                    return $"정지 - {_ward.HaltReason}. 끝내는 방법은 정해지지 않아 Reset으로만 풀린다";
                case EWolbaekWardState.Failed:
                    return "실패 - 필수 참가자 전멸";
                case EWolbaekWardState.Completed:
                    return "성공 또는 종료, 무적 해제";
                default:
                    return string.Empty;
            }
        }

        private void DrawWardMemberRow(int index, double now)
        {
            TalismanMember member = _members[index];
            string color = _ward.TryGetColor(member.Player.CombatId, out EWolbaekWardColor assigned) ? assigned.ToString() : "-";
            string grace = _ward.IsInGrace(member.Player.CombatId, now) ? " 합류유예" : string.Empty;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"P{index} {(index == 0 ? "(WASD)" : string.Empty)} {color} {member.Player.LifeState} " +
                $"{(member.Connected ? "접속" : "끊김")}{(member.Revivable ? string.Empty : " 부활불가")}" +
                $"{(member.RevivePending ? " 부활대기" : string.Empty)}{grace}", GUILayout.Width(GUI_RIGHT_WIDTH * 0.42f));

            if (GUILayout.Button("사망"))
            {
                member.Player.Kill();
            }

            if (GUILayout.Button("부활"))
            {
                member.RevivePending = true;
            }

            if (GUILayout.Button("접속"))
            {
                member.Connected = !member.Connected;
            }

            if (GUILayout.Button("중앙"))
            {
                member.Player.Position = transform.position;
            }

            if (GUILayout.Button("결계"))
            {
                MoveToWard(index, Vector3.zero);
            }

            if (GUILayout.Button("+5m"))
            {
                MoveToWard(index, Vector3.right * WARD_STRAY_DISTANCE);
            }

            if (GUILayout.Button("부활불가"))
            {
                member.Revivable = !member.Revivable;
            }

            GUILayout.EndHorizontal();
        }

        private void MoveToWard(int index, Vector3 offset)
        {
            if ((!_wardView.TryGetStage(out WolbaekWardStage stage))
                || (!_ward.TryGetColor(_members[index].Player.CombatId, out EWolbaekWardColor color)))
            {
                return;
            }

            _members[index].Player.Position = stage.GetCenter(color) + offset;
        }

        // 결계 위에 색과 배정된 참가자를 글자로 띄운다. 문양 그림은 확정되지 않았다
        private void DrawWardLabels()
        {
            Camera view = Camera.main;
            if ((view == null) || (_ward == null) || (!_wardView.TryGetStage(out WolbaekWardStage stage)))
            {
                return;
            }

            for (int color = 0; color < WARD_COLOR_COUNT; ++color)
            {
                EWolbaekWardColor ward = (EWolbaekWardColor)color;
                Vector3 screen = view.WorldToScreenPoint(stage.GetCenter(ward) + (Vector3.up * LABEL_LIFT));
                if (screen.z <= 0f)
                {
                    continue;
                }

                string owners = string.Empty;
                for (int i = 0; i < _members.Count; ++i)
                {
                    if (_ward.TryGetColor(_members[i].Player.CombatId, out EWolbaekWardColor assigned) && (assigned == ward))
                    {
                        owners += "P" + i + " ";
                    }
                }

                Rect rect = new Rect(screen.x - (LABEL_WIDTH * HALF), Screen.height - screen.y - (LABEL_HEIGHT * HALF), LABEL_WIDTH, LABEL_HEIGHT);
                GUI.Label(rect, $"{ward} / 문양 {color}\n{owners}");
            }
        }

        /****************************************
        *              Seal Test
        ****************************************/

        // 두뇌 시작 전에 여우불 유도와 봉인 핵 기믹을 얹는다. 앞선 기믹은 색 결계, 없으면 부적진이다. 켜진 기믹의 상한 계약은 모두 합성해
        // 시험 몸체가 읽게 한다. 하드는 반격 중 0%에서 사망을 보류해 0% 기믹의 인계를 기다리는데, 0% 기믹은 없어 그 상태에서 멈춘다. 핵 후보는 임시 배치다
        private void AttachSeal()
        {
            if (!_sealMode)
            {
                return;
            }

            _sealView = new WolbaekFoxfireSealDevView(transform, ReadClock, ReadMemberPosition);
            RigRoster roster = new RigRoster(_members);
            IBossGimmick preceding = (_ward != null) ? (IBossGimmick)_ward : _talisman;

            _seal = new WolbaekFoxfireSeal(WolbaekFoxfireSealDevCatalog.CreateSpec(_sealHard), _body, _body, roster, roster, _body,
                new WolbaekSealDevSites(transform.position), _resolver, _registry, new SeededBossRandom(Environment.TickCount),
                _displacer, preceding, _sealView);

            _brain.AddGimmick(_seal);
            _deathPolicy.AddHold(_seal);

            List<IBossDamageCap> caps = new List<IBossDamageCap>();
            if (_talisman != null)
            {
                caps.Add(_talisman);
            }

            if (_ward != null)
            {
                caps.Add(_ward);
            }

            caps.Add(_seal);
            _body.DamageCap = (caps.Count == 1) ? caps[0] : new BossDamageCapComposite(caps.ToArray());
        }

        private void DrawSealSection()
        {
            double now = ReadClock();

            GUILayout.Label("---- 25% 여우불 유도와 봉인 핵 (시험용 임시 핵 후보) ----");
            GUILayout.Label($"상태 {_seal.State}  단계 {_seal.Step}  {(_sealHard ? "하드" : "이지")}  성공 {_seal.SuccessCount}/{WolbaekFoxfireSealDevCatalog.REQUIRED_SUCCESSES}  Boss HP {_body.HealthRate * PERCENT:F1}%");
            GUILayout.Label($"무적 {_seal.IsDamageBlocked}  일반 선택 차단 {_seal.BlocksSelection}  사망 보류 {_seal.IsDeathHeld}");
            GUILayout.Label(DescribeSealProgress(now));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("HP 26%로"))
            {
                DamageBossToRate(HP_BEFORE_SEAL);
            }

            if (GUILayout.Button("Boss HP -10%"))
            {
                DamageBossByRate(HP_STEP_RATE);
            }

            if (GUILayout.Button("모두 중앙으로"))
            {
                for (int i = 0; i < _members.Count; ++i)
                {
                    _members[i].Player.Position = transform.position;
                }
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("참가 인원", GUILayout.Width(PARTICIPANT_LABEL_WIDTH));
            for (int count = 1; count <= MAX_TALISMAN_PARTICIPANTS; ++count)
            {
                if (GUILayout.Toggle(_talismanParticipants == count, count.ToString(), GUI.skin.button) && (_talismanParticipants != count))
                {
                    RequestSealRebuild(true, _sealHard, count);
                }
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(GUI_SPACING);
            for (int i = 0; i < _members.Count; ++i)
            {
                DrawSealMemberRow(i);
            }

            GUILayout.Space(GUI_SPACING);
            for (int i = _sealView.Log.Count - 1; i >= 0; --i)
            {
                GUILayout.Label(_sealView.Log[i]);
            }

            GUILayout.Space(GUI_SPACING);
            GUILayout.Label("미연결: 운영 보스 몸체와 피해 판정기와 세션과 Net은 연결되지 않았다. 핵 후보와 휩쓸기 범위는 임시 값이고 맵 검증 전이다. 하드 0% 기믹은 없다");
        }

        /****************************************
        *              Final Test
        ****************************************/

        // 두뇌 시작 전에 최후의 영역 기믹을 얹는다. 앞선 기믹은 25% 봉인이며 그 인계로 시작한다. 바닥 제거와 낙하는 중앙 거리만 보는 임시 판정이고
        // 노란 여우불 위치도 임시 배치다. 운영의 바닥 구조와 서버 추락 판정과 세션 연결은 이 구동기에 없다
        private void AttachFinal()
        {
            if ((!_finalMode) || (_seal == null))
            {
                return;
            }

            Vector3 center      = transform.position;
            float   arenaRadius = _boss.ArenaDiameter * HALF;

            _finalView  = new WolbaekFinalAreaDevView(transform, center, arenaRadius, ReadMemberPosition);
            RigRoster roster = new RigRoster(_members);
            _finalJudge = new WolbaekFinalDevJudge(center, WolbaekFinalAreaDevCatalog.CENTRAL_RADIUS, roster, KillMember, _registry);

            _final =new WolbaekFinalArea(WolbaekFinalAreaDevCatalog.CreateSpec(), _body, _body, roster, roster, _body,
                new WolbaekFinalDevSites(center), _finalJudge, _finalJudge, _finalJudge, _finalJudge, _finalJudge, _resolver,
                _registry, _seal, _finalView);

            _brain.AddGimmick(_final);
            _deathPolicy.AddHold(_final);

            List<IBossDamageCap> caps = new List<IBossDamageCap>();
            if (_talisman != null)
            {
                caps.Add(_talisman);
            }

            if (_ward != null)
            {
                caps.Add(_ward);
            }

            caps.Add(_seal);
            caps.Add(_final);
            _body.DamageCap = new BossDamageCapComposite(caps.ToArray());
        }

        // 구동기의 참가자 대역을 서버 사망 처리한다, 살아 있었을 때만 참이다
        private bool KillMember(int combatId)
        {
            for (int i = 0; i < _members.Count; ++i)
            {
                if ((_members[i].Player.CombatId == combatId) && (_members[i].Player.LifeState == EParticipantLifeState.Alive))
                {
                    _members[i].Player.Kill();
                    return true;
                }
            }

            return false;
        }

        // 부활 시각을 기믹에 알리고, 최후의 영역 중이면 남은 중앙 바닥의 안전 지점으로 옮긴다
        private void ApplyFinalRevive(RigPlayer player)
        {
            if ((_finalJudge == null) || (_final == null))
            {
                return;
            }

            _finalJudge.NoteRevive(player.CombatId, ReadClock());
            if (_final.TryGetSafeRevivePoint(player.CombatId, out Vector3 point))
            {
                player.Position = point;
            }
        }

        // 플레이어 대역이 진짜 피해 판정기를 거쳐 노란 여우불을 때린다, 대상 등록과 피격 경로를 눈으로 확인하려는 것이다
        private void HitYellow(int index, float amount)
        {
            if ((index < 0) || (index >= _final.YellowBodies.Count) || _final.YellowBodies[index].IsDestroyed)
            {
                return;
            }

            DamageRequest request = new DamageRequest(_player.CombatId, _final.YellowBodies[index].CombatId, _resolver.IssueInstanceId(),
                _resolver.IssueInstanceId(), amount, EDamageFlags.None, _player.Position, FINAL_HIT_ID);
            _resolver.Apply(in request);
        }

        private void HitAllYellows(float amount)
        {
            for (int i = 0; i < _final.YellowBodies.Count; ++i)
            {
                HitYellow(i, amount);
            }
        }

        private void DrawFinalSection()
        {
            GUILayout.Label("---- 하드 0% 최후의 영역 (시험용 임시 바닥 판정) ----");
            GUILayout.Label($"상태 {_final.State}  단계 {_final.Step}  Boss HP {_body.HealthRate * PERCENT:F1}%  묶음 {_final.BundleCount}  " +
                $"노란 {_final.YellowRemaining}/{_final.YellowCount}  외곽 바닥 {(_final.IsOuterFloorRemoved ? "제거(표시만)" : "있음")}");
            GUILayout.Label($"무적 {_final.IsDamageBlocked}  일반 선택 차단 {_final.BlocksSelection}  사망 보류 {_final.IsDeathHeld}  " +
                $"최종 성공 신호 {_final.IsFinalSucceeded}  승리 보호 호출 {_finalJudge.VictoryProtected}  보스 {_brain.FlowState}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("HP 26%로"))
            {
                DamageBossToRate(HP_BEFORE_SEAL);
            }

            if (GUILayout.Button("Boss HP -10%"))
            {
                DamageBossByRate(HP_STEP_RATE);
            }

            if (GUILayout.Button("HP 0%로"))
            {
                DamageBossToRate(0f);
            }

            if (GUILayout.Button("모두 중앙으로"))
            {
                for (int i = 0; i < _members.Count; ++i)
                {
                    _members[i].Player.Position = transform.position;
                }
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("노란 1번에 3000"))
            {
                HitYellow(0, FINAL_HIT_AMOUNT);
            }

            if (GUILayout.Button("광역 3000 (모든 노란)"))
            {
                HitAllYellows(FINAL_HIT_AMOUNT);
            }

            if (GUILayout.Button("노란 모두 파괴"))
            {
                HitAllYellows(WolbaekFinalAreaDevCatalog.YELLOW_HEALTH);
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("참가 인원", GUILayout.Width(PARTICIPANT_LABEL_WIDTH));
            for (int count = 1; count <= MAX_TALISMAN_PARTICIPANTS; ++count)
            {
                if (GUILayout.Toggle(_talismanParticipants == count, count.ToString(), GUI.skin.button) && (_talismanParticipants != count))
                {
                    RequestFinalRebuild(true, count);
                }
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(GUI_SPACING);
            for (int i = 0; i < _members.Count; ++i)
            {
                DrawFinalMemberRow(i);
            }

            GUILayout.Space(GUI_SPACING);
            for (int i = _finalView.Log.Count - 1; i >= 0; --i)
            {
                GUILayout.Label(_finalView.Log[i]);
            }

            GUILayout.Space(GUI_SPACING);
            GUILayout.Label("미연결: 운영 바닥 구조와 서버 추락 판정과 안전 부활 위치와 노란 여우불 배치와 세션과 Net은 연결되지 않았다. 바닥 제거는 표시만 바뀌고 추락은 중앙 거리 임시 판정이다. 승리 보호도 대역이라 실제로 지키지 않는다");
        }

        private void DrawFinalMemberRow(int index)
        {
            TalismanMember member = _members[index];

            GUILayout.BeginHorizontal();
            GUILayout.Label($"P{index} {(index == 0 ? "(WASD)" : string.Empty)} {member.Player.LifeState} " +
                $"{(member.Connected ? "접속" : "끊김")}{(member.RevivePending ? " 부활대기" : string.Empty)}",
                GUILayout.Width(GUI_RIGHT_WIDTH * 0.42f));

            if (GUILayout.Button("사망"))
            {
                member.Player.Kill();
            }

            if (GUILayout.Button("부활"))
            {
                member.RevivePending = true;
            }

            if (GUILayout.Button("접속"))
            {
                member.Connected = !member.Connected;
            }

            if (GUILayout.Button("중앙"))
            {
                member.Player.Position = transform.position;
            }

            if (GUILayout.Button("바깥"))
            {
                member.Player.Position = transform.position + (Vector3.right * FINAL_OUTSIDE_DISTANCE);
            }

            GUILayout.EndHorizontal();
        }

        private string DescribeSealProgress(double now)
        {
            switch (_seal.State)
            {
                case EWolbaekSealState.Dormant:
                    return "HP가 25%에 닿으면 발동한다";
                case EWolbaekSealState.Active:
                    return $"차례 {_seal.TurnIndex + 1}, 핵 {_seal.Cores.Count}개, 판정까지 {Math.Max(0d, _seal.Turn.ResolveServerTime - now):F1}초, 전체 남은 {Math.Max(0d, _seal.LimitEndServerTime - now):F1}초";
                case EWolbaekSealState.Counter:
                    return $"반격 {Math.Max(0d, _seal.CounterEndServerTime - now):F1}초 남음";
                case EWolbaekSealState.NextBoundaryPending:
                    return "반격 끝, 다음 경계(0%)에서 사망 보류 중. 0% 기믹이 없어 인계를 기다리며 Reset으로만 풀린다";
                case EWolbaekSealState.Halted:
                    return $"정지 - {_seal.HaltReason}. 끝내는 방법은 정해지지 않아 Reset으로만 풀린다";
                case EWolbaekSealState.Failed:
                    return $"실패 - {_seal.FailReason}";
                case EWolbaekSealState.Completed:
                    return "반격까지 마침, 일반전 재개";
                default:
                    return string.Empty;
            }
        }

        private void DrawSealMemberRow(int index)
        {
            TalismanMember member = _members[index];

            GUILayout.BeginHorizontal();
            GUILayout.Label($"P{index} {(index == 0 ? "(WASD)" : string.Empty)} {member.Player.LifeState} " +
                $"{(member.Connected ? "접속" : "끊김")}{(member.RevivePending ? " 부활대기" : string.Empty)}",
                GUILayout.Width(GUI_RIGHT_WIDTH * 0.42f));

            if (GUILayout.Button("사망"))
            {
                member.Player.Kill();
            }

            if (GUILayout.Button("부활"))
            {
                member.RevivePending = true;
            }

            if (GUILayout.Button("접속"))
            {
                member.Connected = !member.Connected;
            }

            if (GUILayout.Button("중앙"))
            {
                member.Player.Position = transform.position;
            }

            if (GUILayout.Button("핵 위"))
            {
                MoveToCore(index, Vector3.zero);
            }

            if (GUILayout.Button("핵 옆"))
            {
                MoveToCore(index, Vector3.right * SEAL_CORE_STEP_DISTANCE);
            }

            GUILayout.EndHorizontal();
        }

        // 참가자 번호와 같은 번호의 핵으로 옮긴다, 핵이 그보다 적으면 마지막 핵으로 옮긴다
        private void MoveToCore(int index, Vector3 offset)
        {
            int count = _sealView.CoreCount;
            if (count == 0)
            {
                return;
            }

            if (_sealView.TryGetCoreCenter(Mathf.Min(index, count - 1), out Vector3 center))
            {
                _members[index].Player.Position = center + offset;
            }
        }

        // 75% 직전에 중앙 밖에서 시작하는 경우를 보려는 시험 도구다, 전장 경계 안에서만 움직인다
        private void MoveBossToEdge()
        {
            Vector3 edge = transform.position + (Vector3.right * ((_boss.ArenaDiameter * HALF) - _body.BodyRadius - BOSS_EDGE_MARGIN));
            _body.Move(_body.Position, edge, _body.BodyRadius);
        }

        private void MoveToSeat(int index)
        {
            if (!TryGetSeat(index, out Vector3 center))
            {
                return;
            }

            _members[index].Player.Position = center;
        }

        private void MoveOutOfSeat(int index)
        {
            if (!TryGetSeat(index, out Vector3 center))
            {
                return;
            }

            Vector3 outward = new Vector3(center.x, 0f, center.z).normalized;
            _members[index].Player.Position = center + (outward * OUT_OF_SEAT_DISTANCE);
        }

        private void SeatEveryone()
        {
            for (int i = 0; i < _members.Count; ++i)
            {
                MoveToSeat(i);
            }
        }

        private bool TryGetSeat(int index, out Vector3 center)
        {
            int combatId = _members[index].Player.CombatId;
            for (int i = 0; i < _talisman.Structures.Count; ++i)
            {
                if (_talisman.Structures[i].AssignedCombatId == combatId)
                {
                    center = _talisman.Structures[i].Center;
                    return true;
                }
            }

            center = Vector3.zero;
            return false;
        }

        /****************************************
        *                Methods
        ****************************************/

        private double ReadClock()
        {
            return Time.timeAsDouble;
        }

        // 보스와 플레이어와 지속물 관리자를 새로 만든다. 처음과 Reset이 같이 쓴다
        private void Build()
        {
            // 새 전투는 시전 번호를 다시 1부터 매긴다. 끝난 번호 기록까지 비우지 않으면 새 예고가 이전 시전으로 오인되어 버려진다
            _presenter.ResetNetworkSession();
            _bossView.ResetView();
            ClearTalismanTest();

            var catalog = new BossPatternCatalogSource(GameDB.BossPatterns);
            if ((!catalog.TryGetBoss(Constants.Combat.DEV_BOSS_ID, out _boss)) || (!catalog.IsTuned(Constants.Combat.DEV_BOSS_ID)))
            {
                Debug.LogError("[Boss] 월백 정의가 없거나 수치가 지정되지 않았다");
                enabled = false;
                return;
            }

            _registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            _resolver = new DamageResolver(_registry, ledger, DEFENSE_CONSTANT, REDUCTION_CAP);

            int obstacleMask = CombatLineOfSight.ResolveLayerMask(Constants.Combat.OBSTACLE_LAYER_NAME);
            var lineOfSight  = new CombatLineOfSight(obstacleMask, LINE_OF_SIGHT_HEIGHT);

            Vector3 center = transform.position;
            _body = new BossTestRigBody(_registry.IssueCombatId(), center, Vector3.forward,
                _boss.HealthRef * _referenceAttack * _healthScale, _boss.Defense, Constants.Combat.BOSS_BODY_RADIUS,
                center, _boss.ArenaDiameter * HALF, lineOfSight);
            _player = new RigPlayer(_registry.IssueCombatId(), center + _playerStart, PLAYER_MAX_HP);
            _registry.Register(_player);
            _registry.Register(_body);
            BuildTalismanParticipants();

            var bridge = new BossTelegraphLocalBridge(_presenter, _body, ReadClock, () => (_brain != null) ? _brain.ActivePatternId : null,
                _bossView);
            _projectiles = new BossProjectileManager(_registry, _body, bridge);
            _areas       = new BossAreaManager(_registry, bridge);
            _beams       = new BossBeamManager(_registry, bridge);
            _stars       = new BossStarManager(_registry, bridge);

            _deathPolicy = new BossGimmickDeathPolicy(new ImmediateBossDeathPolicy());

            var dependencies = new BossBrainDependencies
            {
                Catalog  = catalog,
                Self     = _body,
                Vitals   = _body,
                Targets  = _registry,
                Damage   = _resolver,
                Patterns = new PhaseTwoFilter(new WolbaekPatternFactory(_referenceHealth, bridge, bridge, bridge, bridge, _body,
                    _projectiles, bridge, _areas, bridge, sweepListener: bridge, beams: _beams, arena: _body, beamListener: bridge,
                    stars: _stars, starListener: bridge, warp: _body), () => _phaseTwoWarpOnly),
                Effects  = new BossEffectComposite(_projectiles, _areas, _beams, _stars),
                Control  = _body,
                Random   = new SeededBossRandom(Environment.TickCount),
                DeathPolicy = _deathPolicy
            };

            _brain      = new BossBrain(Constants.Combat.DEV_BOSS_ID, dependencies);
            _body.Brain = _brain;
            AttachTalisman();
            AttachWard();
            AttachSeal();
            AttachFinal();

            double now = ReadClock();
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
            Debug.Log($"[Boss] 월백 시험 시작, maxHealth={_body.MaxHealth:F0}, 전장 지름={_boss.ArenaDiameter}");
        }

        // 이전 시험의 참가자 대역과 도형을 치운다. 첫 참가자(WASD 플레이어)의 몸은 Start가 한 번만 만든다
        private void ClearTalismanTest()
        {
            for (int i = 0; i < _members.Count; ++i)
            {
                if (_members[i].Body != _playerBody)
                {
                    Destroy(_members[i].Body.gameObject);
                }
            }

            _members.Clear();
            _talismanView?.ClearAll();
            _talismanView = null;
            _talisman     = null;
            _wardView?.ClearAll();
            _wardView     = null;
            _ward         = null;
            _sealView?.ClearAll();
            _sealView     = null;
            _seal         = null;
            _finalView?.ClearAll();
            _finalView    = null;
            _final        = null;
            _finalJudge   = null;
        }

        // 시험 모드가 켜져 있으면 WASD 플레이어 옆에 참가자 대역을 세운다. 대역도 피해 대상으로 등록해 같은 판정 경로로 맞는다
        private void BuildTalismanParticipants()
        {
            _displacer = new RigDisplacer(_body);
            _displacer.Register(_player);
            _members.Add(new TalismanMember { Player = _player, Body = _playerBody, Renderer = _playerRenderer });

            if ((!_talismanMode) && (!_wardMode) && (!_sealMode) && (!_finalMode))
            {
                return;
            }

            int count = Mathf.Clamp(_talismanParticipants, 1, MAX_TALISMAN_PARTICIPANTS);
            for (int i = 1; i < count; ++i)
            {
                Vector3 position = _player.Position + (Vector3.right * (BOT_SPREAD * i));
                RigPlayer bot = new RigPlayer(_registry.IssueCombatId(), position, PLAYER_MAX_HP);
                _registry.Register(bot);
                _displacer.Register(bot);

                GameObject body = TestRigDisplay.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "TalismanBot" + i;
                Destroy(body.GetComponent<Collider>());
                body.transform.SetParent(transform, false);
                _members.Add(new TalismanMember { Player = bot, Body = body.transform, Renderer = body.GetComponent<Renderer>() });
            }
        }

        // 두뇌 시작 전에 기믹을 얹고 시험 몸체에 경계 멈춤과 무적을 연결한다
        private void AttachTalisman()
        {
            if (!_talismanMode)
            {
                return;
            }

            _talismanView = new WolbaekTalismanDevView(transform, ReadClock);
            WolbaekTalismanSpec spec = WolbaekTalismanDevCatalog.CreateSpec(_talismanHard, _boss.PhaseTransitionHealthRate);
            float href = _talismanHard ? _talismanHardReferenceHealth : _referenceHealth;

            // 하드 공격이 없으면 밀림 창구는 쓰이지 않지만 이 대역은 항상 넘겨도 무해하다
            _talisman = new WolbaekTalismanFormation(spec, _body, href, _body, new RigRoster(_members), _body, _body, _resolver,
                _registry, new SeededBossRandom(Environment.TickCount), _displacer, null, _talismanView);

            _brain.AddGimmick(_talisman);
            _body.DamageCap = _talisman;
        }

        private void CreateBossView()
        {
            if (_bossViewPrefab == null)
            {
                Debug.LogError("[Boss] 보스 외형 프리팹이 비어 있다", this);
                enabled = false;
                return;
            }

            _bossView = Instantiate(_bossViewPrefab, transform);
            _bossView.Bind(_presenter, ReadClock);

            _hitFlash = _bossView.GetComponent<BossHitFlashView>();
            if (_hitFlash == null)
            {
                _hitFlash = _bossView.gameObject.AddComponent<BossHitFlashView>();
            }
        }

        // 패턴이 바뀔 때마다 순서와 횟수를 남긴다, 가중 선택이 여섯 패턴을 고르게 쓰는지 눈으로 보려는 것이다
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

            if (id == WolbaekPatternFactory.P2_P07_ID)
            {
                // 워프는 시전 시작 틱에 끝나 있다. 착지 위치와 대상 정면, 후방 3m 지점을 남겨 눈으로 확인하게 한다
                Vector3 expectedBehind = _player.Position - (_player.Forward * WARP_BEHIND_DISTANCE);
                Vector3 toPlayer = _player.Position - _body.Position;
                toPlayer.y = 0f;
                Debug.Log($"[Boss] P07 워프 착지 보스 ({_body.Position.x:F2}, {_body.Position.z:F2}), 플레이어 ({_player.Position.x:F2}, " +
                    $"{_player.Position.z:F2}) 정면 ({_player.Forward.x:F2}, {_player.Forward.z:F2}), 후방 3m ({expectedBehind.x:F2}, {expectedBehind.z:F2}), " +
                    $"착지-플레이어 {toPlayer.magnitude:F2}m");
                _body.Face(toPlayer);
            }

            _history.Add(id.Substring(id.Length - PATTERN_SUFFIX_LENGTH));
            if (_history.Count > HISTORY_LENGTH)
            {
                _history.RemoveAt(0);
            }
        }

        private void ApplyView()
        {
            _bossView.SetPose(_body.Position, _body.Forward);
            _bossView.SetDefeated(_body.LifeState != EParticipantLifeState.Alive);

            _playerBody.position = _player.Position + Vector3.up;
            _playerBody.rotation = Quaternion.LookRotation(_player.Forward, Vector3.up);
            _playerBody.gameObject.SetActive(_player.LifeState == EParticipantLifeState.Alive);

            // 참가자 대역은 접속이 끊기면 회색으로 보인다
            for (int i = 1; i < _members.Count; ++i)
            {
                TalismanMember member = _members[i];
                member.Body.position = member.Player.Position + Vector3.up;
                member.Body.gameObject.SetActive(member.Player.LifeState == EParticipantLifeState.Alive);
                member.Renderer.material.color = member.Connected ? Color.white : DisconnectedColor;
            }

            // 맞으면 잠깐 붉게 보여 판정이 났음을 알린다
            if (_player.Health < _lastPlayerHealth)
            {
                _flashUntil = Time.time + HIT_FLASH_SECONDS;
            }

            _lastPlayerHealth = _player.Health;
            _playerRenderer.material.color = (Time.time < _flashUntil) ? Color.red : Color.white;
        }

        private void DamageBossByRate(float rate)
        {
            _body.ApplyDamage(_body.MaxHealth * rate, false, ReadClock());
        }

        private void DamageBossToRate(float rate)
        {
            float target = _body.MaxHealth * rate;
            if (_body.Health > target)
            {
                _body.ApplyDamage(_body.Health - target, false, ReadClock());
            }
        }
    }
}
