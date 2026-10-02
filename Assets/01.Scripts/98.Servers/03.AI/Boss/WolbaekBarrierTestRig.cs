// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using BossDefinition = Shinshi.Data.BossPatternCatalogSO.BossDefinition;

namespace Shinshi.AI
{
    // 네트워크 없이 월백 50% 결계 전개를 눈으로 확인하는 시험 장치다. 기존 시험 구동기(WolbaekBossTestRig)는 건드리지 않고
    // 같은 서버 클래스를 따로 이어 붙인다. 서버 부트스트랩이 하게 될 생성, 틱 호출, 등록을 대신할 뿐 권위 경계를 바꾸지 않는다
    // 전개가 끝나면 두뇌의 인계(CompleteHandoff)를 부른다. 2페이즈 공격은 아직 없어 두뇌는 준비 대기 상태에서 멈춘다
    /// <summary> 월백 결계 전개 시험 구동기, 두뇌의 인계 대기 상태를 읽어 결계 시퀀스를 구동한다 </summary>
    public sealed class WolbaekBarrierTestRig : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  PLAYER_SPEED     = 6f;
        private const float  PLAYER_MAX_HP    = 30000f;
        private const float  DEFENSE_CONSTANT = 1000f;
        private const float  REDUCTION_CAP    = 0.6f;
        private const float  LINE_OF_SIGHT_HEIGHT = 0.5f;
        private const float  GUI_WIDTH        = 400f;
        private const float  GUI_MARGIN       = 10f;
        private const float  GUI_SPACING      = 6f;
        private const float  HALF             = 0.5f;
        private const float  PERCENT          = 100f;
        private const float  HP_STEP_RATE     = 0.1f;
        private const float  TRANSITION_MARGIN_RATE = 0.01f;
        private const string HORIZONTAL_AXIS  = "Horizontal";
        private const string VERTICAL_AXIS    = "Vertical";

        /****************************************
        *                Fields
        ****************************************/

        [Header("프리팹")]
        [SerializeField] private BossTelegraphPresenter _presenterPrefab;
        [SerializeField] private BossView               _bossViewPrefab;

        [Header("결계 표현")]
        [SerializeField] private Material _barrierMaterial; // 장판 셰이더 머티리얼

        [Header("시험 주입값")]
        [SerializeField] private float   _referenceAttack = 644.32f;
        [SerializeField] private float   _referenceHealth = 3541f;
        [SerializeField] private Vector3 _playerStart     = new Vector3(0f, 0f, 12f);
        [SerializeField] private BossBarrierSettings _barrier = new BossBarrierSettings();

        private CombatTargetRegistry    _registry;
        private DamageResolver          _resolver;
        private BossTestRigBody         _body;
        private BossBrain               _brain;
        private BossProjectileManager   _projectiles;
        private BossAreaManager         _areas;
        private BossBarrierDirector     _director;
        private BossBarrierLocalBridge  _bridge;
        private BossBarrierView         _barrierView;
        private EnemyTestRigPlayer      _player;
        private BossTelegraphPresenter  _presenter;
        private BossView                _bossView;
        private Transform               _playerBody;
        private BossDefinition          _boss;
        private bool                    _isHandoffDone;

        /****************************************
        *              Unity Event
        ****************************************/

        // GameDB는 Awake가 아니라 Start 이후에 읽는다
        private void Start()
        {
            if ((_presenterPrefab == null) || (_bossViewPrefab == null) || (_barrierMaterial == null))
            {
                Debug.LogError("[Boss] 결계 시험 구동기의 프리팹이나 머티리얼이 비어 있다", this);
                enabled = false;
                return;
            }

            _presenter = Instantiate(_presenterPrefab, transform);
            if (_presenter.TryGetComponent(out BossTelegraphSampleInput sample))
            {
                sample.enabled = false;
            }

            _presenter.SetServerClock(ReadClock);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "PlayerBody";
            Destroy(body.GetComponent<Collider>());
            _playerBody = body.transform;
            _playerBody.SetParent(transform, false);

            _bossView = Instantiate(_bossViewPrefab, transform);
            _bossView.Bind(_presenter, ReadClock);

            GameObject ring = new GameObject("BossBarrierView");
            ring.transform.SetParent(transform, false);
            ring.AddComponent<MeshFilter>();
            ring.AddComponent<MeshRenderer>().sharedMaterial = _barrierMaterial;
            _barrierView = ring.AddComponent<BossBarrierView>();
            _barrierView.SetServerClock(ReadClock);

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
            }

            _resolver.BeginTick(now);
            _body.Brain = _brain;
            _brain.Tick(now);

            // 두뇌가 전투를 끝냈으면(사망, 전투 종료) 결계도 정리한다. 그 틱에는 새로 시작하지 않는다
            if (_brain.FlowState == EBossFlowState.Combat)
            {
                _director.Tick(now, _brain.PhaseState == EBossPhaseState.AwaitingHandoff);

                // 결계 전개가 끝나면 두뇌에 인계를 알린다. 2페이즈 공격이 준비되지 않았으면 두뇌는 준비 대기 상태로 기다린다
                if (_director.IsCompleted && (!_isHandoffDone))
                {
                    _isHandoffDone = _brain.CompleteHandoff();
                }
            }
            else
            {
                _director.Cancel(_brain.IsBossDeathRecorded ? EBossInterruptReason.Death : EBossInterruptReason.CombatEnd);
            }

            _bossView.SetPose(_body.Position, _body.Forward);
            _bossView.SetDefeated(_body.LifeState != EParticipantLifeState.Alive);
            _playerBody.position = _player.Position + Vector3.up;
            _playerBody.gameObject.SetActive(_player.LifeState == EParticipantLifeState.Alive);
        }

        private void OnGUI()
        {
            if (_brain == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(GUI_MARGIN, GUI_MARGIN, GUI_WIDTH, Screen.height - (GUI_MARGIN * 2f)), GUI.skin.box);
            GUILayout.Label($"Flow {_brain.FlowState}  Phase {_brain.Phase} ({_brain.PhaseState})  Attack {_brain.AttackState}");
            GUILayout.Label($"Boss HP {_body.HealthRate * PERCENT:F1}%  전환 {_boss.PhaseTransitionHealthRate * PERCENT:F0}%");
            GUILayout.Label($"결계 {_director.State}  위험 구역 활성 {_director.IsHazardActive}  완료 {_director.IsCompleted}");
            GUILayout.Label($"표시 {_barrierView.Phase}  알림 시작 {_bridge.StartedCount} 활성 {_bridge.ActivatedCount} " +
                $"완료 {_bridge.CompletedCount} 정리 {_bridge.ClearedCount}");
            GUILayout.Label($"Player HP {_player.Health:F0}/{_player.MaxHealth:F0}  보스 위치 {_body.Position}");
            GUILayout.Label("임시값 안내: 결계 시간, 비율, 피해는 모두 미확정 임시값이다");

            if (_director.IsCompleted)
            {
                GUILayout.Label($"전개 완료, 인계 {_isHandoffDone}  2페이즈 공격이 준비되지 않았으면 준비 대기(AwaitingReadiness)다");
            }

            GUILayout.Space(GUI_SPACING);
            if (GUILayout.Button("Boss HP를 전환 직전(51%)으로"))
            {
                DamageBossToRate(_boss.PhaseTransitionHealthRate + TRANSITION_MARGIN_RATE);
            }

            if (GUILayout.Button("Boss HP -10%"))
            {
                _body.ApplyDamage(_body.MaxHealth * HP_STEP_RATE, false, ReadClock());
            }

            if (GUILayout.Button("중복 시작 시도"))
            {
                _director.Tick(ReadClock(), true);
            }

            if (GUILayout.Button("전투 종료 요청(중도 취소)"))
            {
                _brain.RequestCombatEnd();
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

        // 보스와 플레이어와 결계를 새로 만든다. 처음과 Reset이 같이 쓴다
        private void Build()
        {
            if (_director != null)
            {
                _director.Reset();
            }

            _presenter.ClearAll();
            _bossView.ResetView();
            _barrierView.Clear();
            _isHandoffDone = false;

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
            float   arenaRadius = _boss.ArenaDiameter * HALF;

            // 결계 전개 위치로 이동하는 모습이 보이도록 보스는 전장 중심에서 떨어진 곳에서 시작한다
            Vector3 bossStart = center + (Vector3.back * (arenaRadius * HALF));
            _body = new BossTestRigBody(_registry.IssueCombatId(), bossStart, Vector3.forward,
                _boss.HealthRef * _referenceAttack, _boss.Defense, Constants.Combat.BOSS_BODY_RADIUS,
                center, arenaRadius, lineOfSight);
            _player = new EnemyTestRigPlayer(_registry.IssueCombatId(), center + _playerStart, PLAYER_MAX_HP);
            _registry.Register(_player);
            _registry.Register(_body);

            var patternBridge = new BossTelegraphLocalBridge(_presenter, _body, ReadClock, () => _brain.ActivePatternId);
            _projectiles = new BossProjectileManager(_registry, _body, patternBridge);
            _areas       = new BossAreaManager(_registry, patternBridge);

            var dependencies = new BossBrainDependencies
            {
                Catalog  = catalog,
                Self     = _body,
                Vitals   = _body,
                Targets  = _registry,
                Damage   = _resolver,
                Patterns = new WolbaekPatternFactory(_referenceHealth, patternBridge, patternBridge, patternBridge,
                    patternBridge, _body, _projectiles, patternBridge, _areas, patternBridge),
                Effects  = new BossEffectComposite(_projectiles, _areas),
                Control  = _body,
                Random   = new SeededBossRandom(Environment.TickCount)
            };

            _brain      = new BossBrain(Constants.Combat.DEV_BOSS_ID, dependencies);
            _body.Brain = _brain;

            _bridge   = new BossBarrierLocalBridge(_barrierView);
            _director = new BossBarrierDirector(_barrier, _body, _body, _registry, _resolver, center, arenaRadius,
                _referenceHealth, _bridge);

            if (!_brain.Begin(ReadClock()))
            {
                Debug.LogError("[Boss] 전투를 열지 못했다");
                enabled = false;
                return;
            }

            Debug.Log($"[Boss] 월백 결계 시험 시작, 전장 반지름={arenaRadius}, 안전 구역 반지름={_barrier.GetInnerRadius(arenaRadius):F2}");
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
