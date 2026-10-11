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

        private CombatTargetRegistry    _registry;
        private DamageResolver          _resolver;
        private BossTestRigBody         _body;
        private BossBrain               _brain;
        private BossProjectileManager   _projectiles;
        private BossAreaManager         _areas;
        private RigPlayer               _player;
        private BossTelegraphPresenter  _presenter;
        private BossView                _bossView;
        private Transform               _playerBody;
        private Renderer                _playerRenderer;
        private BossDefinition          _boss;
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

            _resolver.BeginTick(now);
            _body.Brain = _brain;
            _brain.Tick(now);

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

        private double ReadClock()
        {
            return Time.timeAsDouble;
        }

        // 보스와 플레이어와 지속물 관리자를 새로 만든다. 처음과 Reset이 같이 쓴다
        private void Build()
        {
            _presenter.ClearAll();
            _bossView.ResetView();

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

            var bridge = new BossTelegraphLocalBridge(_presenter, _body, ReadClock);
            _projectiles = new BossProjectileManager(_registry, _body, bridge);
            _areas       = new BossAreaManager(_registry, bridge);

            var dependencies = new BossBrainDependencies
            {
                Catalog  = catalog,
                Self     = _body,
                Vitals   = _body,
                Targets  = _registry,
                Damage   = _resolver,
                Patterns = new PhaseTwoFilter(new WolbaekPatternFactory(_referenceHealth, bridge, bridge, bridge, bridge, _body,
                    _projectiles, bridge, _areas, bridge, arena: _body, warp: _body), () => _phaseTwoWarpOnly),
                Effects  = new BossEffectComposite(_projectiles, _areas),
                Control  = _body,
                Random   = new SeededBossRandom(Environment.TickCount)
            };

            _brain      = new BossBrain(Constants.Combat.DEV_BOSS_ID, dependencies);
            _body.Brain = _brain;

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
