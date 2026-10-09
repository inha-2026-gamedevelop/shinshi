// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using EnemyDefinition = Shinshi.Data.EnemyCatalogSO.EnemyDefinition;

namespace Shinshi.AI
{
    // 네트워크 없이 일반 적 여러 마리의 밀집을 눈으로 확인하는 시험 장치다. 빈 오브젝트에 붙이면 근접형과 돌진형을 한꺼번에 세우고
    // WASD 플레이어 하나를 쫓게 한다. 서버 부트스트랩이 하게 될 생성, 틱 호출, 등록, 간격 계산 등록을 대신할 뿐이며
    // 서버 권위 경계나 Command, Rpc를 바꾸지 않는다. 위치는 서버 몸체와 EnemyCrowd 결과만 쓰고 클라이언트 물리는 읽지 않는다.
    // 벽은 Obstacle 레이어 콜라이더가 씬에 있을 때만 돌진을 막는다
    /// <summary> 일반 적 다수 시험 구동기, 같은 자리 스폰과 흩어 뿌리기를 번갈아 눌러 밀집과 떨림 지표를 본다 </summary>
    public sealed class EnemyCrowdTestRig : MonoBehaviour
    {
        /****************************************
        *              Types
        ****************************************/

        private enum ESpawnPattern
        {
            SamePoint,
            Scatter
        }

        private sealed class MeleeEntry
        {
            public EnemyServer     Server;
            public MeleeEnemyBrain Brain;
            public EnemyTestView   View;
            public Vector3         LastStep;
            public int             WindowReversals;
        }

        private sealed class ChargerEntry
        {
            public EnemyServer       Server;
            public ChargerEnemyBrain Brain;
            public EnemyTestView     View;
            public Vector3           LastStep;
            public int               WindowReversals;
        }

        /****************************************
        *              Constants
        ****************************************/

        private const int    PLAYER_ID              = 1;
        private const int    ENEMY_ID_BASE          = 100;
        private const float  PLAYER_SPEED           = 5f;
        private const float  PLAYER_MAX_HP          = 1000000000f; // 밀집 관찰 중 플레이어가 죽어 추격이 멈추지 않게 한다
        private const float  DEFENSE_CONSTANT       = 1000f;       // PlayerConfigSO 초안과 같은 값
        private const float  REDUCTION_CAP          = 0.6f;
        private const float  LINE_OF_SIGHT_HEIGHT   = 0.5f;        // 시험용 벽 검사 높이
        private const float  REVERSAL_MIN_STEP      = 0.005f;      // m, 이보다 작은 틱 이동은 방향 반전으로 세지 않는다
        private const float  REVERSAL_DOT           = -0.5f;       // 연속한 두 틱 이동이 이 값보다 반대를 가리키면 반전이다
        private const int    SCATTER_SEED           = 20261009;
        private const float  TICK_SECONDS           = 1f / 30f;    // 서버 틱처럼 고정 간격으로 돌려 프레임 끊김이 이동량에 섞이지 않게 한다
        private const int    MAX_TICKS_PER_FRAME    = 5;           // 한 프레임에 따라잡는 틱 상한, 넘치면 버린다
        private const float  WINDOW_SECONDS         = 5f;          // 반전 횟수를 끊어 세는 구간 길이
        private const float  GUI_WIDTH              = 430f;
        private const float  GUI_HEIGHT             = 210f;
        private const float  GUI_MARGIN             = 10f;
        private const string HORIZONTAL_AXIS        = "Horizontal";
        private const string VERTICAL_AXIS          = "Vertical";

        /****************************************
        *                Fields
        ****************************************/

        [Header("프리팹")]
        [SerializeField] private EnemyTestView _meleePrefab;
        [SerializeField] private EnemyTestView _chargerPrefab;

        [Header("시험 주입값")]
        [SerializeField] private float _referenceAttack = 644.32f; // Aref, 체험판 숲 기준
        [SerializeField] private float _referenceHealth = 3541f;   // Href, 체험판 숲 기준

        [Header("스폰")]
        [SerializeField] private int     _meleeCount     = 24;
        [SerializeField] private int     _chargerCount   = 4;
        [SerializeField] private Vector3 _spawnCenter    = new Vector3(0f, 0f, 12f);
        [SerializeField] private float   _scatterRadius  = 3f;

        private CombatTargetRegistry _registry;
        private DamageResolver       _resolver;
        private EnemyCrowd           _crowd;
        private EnemyTestRigPlayer   _player;
        private Transform            _playerBody;
        private MeleeEntry[]         _melees;
        private ChargerEntry[]       _chargers;
        private EnemyDefinition      _meleeDefinition;
        private EnemyDefinition      _chargerDefinition;
        private CombatLineOfSight    _lineOfSight;
        private bool                 _ready;

        private double _serverTime;
        private float  _tickAccumulator;

        private float _windowElapsed;
        private int   _windowReversals;
        private int   _lastWindowReversals;
        private int   _lastWindowWorstId;
        private int   _lastWindowWorstCount;

        private float _worstOverlapNow;
        private float _worstOverlapEver;
        private float _maxTickStep;
        private int   _reversals;
        private float _elapsed;

        /****************************************
        *              Unity Event
        ****************************************/

        // GameDB는 Awake가 아니라 Start 이후에 읽는다
        private void Start()
        {
            if ((_meleePrefab == null) || (_chargerPrefab == null))
            {
                Debug.LogError("[Enemy] 근접 또는 돌진 프리팹이 비어 있다", this);
                enabled = false;
                return;
            }

            _meleeDefinition   = GameDB.Enemies.FindByRole(EEnemyRole.Melee);
            _chargerDefinition = GameDB.Enemies.FindByRole(EEnemyRole.Charger);
            if ((_meleeDefinition == null) || (_chargerDefinition == null))
            {
                Debug.LogError("[Enemy] 근접 또는 돌진 산귀 정의가 없다");
                enabled = false;
                return;
            }

            int obstacleMask = CombatLineOfSight.ResolveLayerMask(Constants.Combat.OBSTACLE_LAYER_NAME);
            _lineOfSight     = new CombatLineOfSight(obstacleMask, LINE_OF_SIGHT_HEIGHT);

            GameObject body = TestRigDisplay.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "PlayerBody";
            Destroy(body.GetComponent<Collider>());
            _playerBody = body.transform;
            _playerBody.SetParent(transform, false);

            Build(ESpawnPattern.SamePoint);
        }

        private void Update()
        {
            if (!_ready)
            {
                return;
            }

            _tickAccumulator += Time.deltaTime;

            int ticks = 0;
            while ((_tickAccumulator >= TICK_SECONDS) && (ticks < MAX_TICKS_PER_FRAME))
            {
                _tickAccumulator -= TICK_SECONDS;
                ++ticks;
                TickServer();
            }

            if (_tickAccumulator >= TICK_SECONDS)
            {
                _tickAccumulator = 0f;
            }

            double now = _serverTime;
            for (int i = 0; i < _melees.Length; ++i)
            {
                MeleeEntry entry = _melees[i];
                entry.View.Apply(entry.Server.Position, entry.Server.Forward, entry.Brain.State,
                    entry.Brain.TelegraphProgress(now), entry.Brain.AttackRange, entry.Brain.AttackArcAngle);
            }

            for (int i = 0; i < _chargers.Length; ++i)
            {
                ChargerEntry entry = _chargers[i];
                entry.View.ApplyDash(entry.Server.Position, entry.Server.Forward, entry.Brain.State,
                    entry.Brain.TelegraphProgress(now), entry.Brain.DashOrigin, entry.Brain.AimDirection,
                    entry.Brain.DashLength, entry.Brain.DashWidth);
            }

            _playerBody.position = _player.Position + Vector3.up;
            _playerBody.gameObject.SetActive(_player.LifeState == EParticipantLifeState.Alive);
        }

        private void OnGUI()
        {
            if (!_ready)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(GUI_MARGIN, TestRigDisplay.GUI_TOP, GUI_WIDTH, GUI_HEIGHT), GUI.skin.box);
            GUILayout.Label($"Melee {_melees.Length}  Charger {_chargers.Length}  Crowd {_crowd.Count}  Time {_elapsed:F1}s");
            GUILayout.Label($"Overlap now {_worstOverlapNow:F3}m  worst {_worstOverlapEver:F3}m");
            GUILayout.Label($"Max step/tick {_maxTickStep:F3}m  Reversals total {_reversals}");
            GUILayout.Label($"Last {WINDOW_SECONDS:F0}s reversals {_lastWindowReversals}  worst enemy #{_lastWindowWorstId} x{_lastWindowWorstCount}");

            if (GUILayout.Button("Respawn same point"))
            {
                Build(ESpawnPattern.SamePoint);
            }

            if (GUILayout.Button("Respawn scatter"))
            {
                Build(ESpawnPattern.Scatter);
            }

            if (GUILayout.Button("Reset metrics"))
            {
                ResetMetrics();
            }

            GUILayout.EndArea();
        }

        /****************************************
        *                Methods
        ****************************************/

        // 서버 틱 한 번. 입력은 프레임마다 읽지만 위치와 두뇌는 고정 간격으로만 움직인다
        private void TickServer()
        {
            _serverTime += TICK_SECONDS;
            _elapsed    += TICK_SECONDS;

            if (_player.LifeState == EParticipantLifeState.Alive)
            {
                Vector3 move = new Vector3(Input.GetAxisRaw(HORIZONTAL_AXIS), 0f, Input.GetAxisRaw(VERTICAL_AXIS));
                _player.Position += move.normalized * (PLAYER_SPEED * TICK_SECONDS);
            }

            _resolver.BeginTick(_serverTime);

            for (int i = 0; i < _melees.Length; ++i)
            {
                MeleeEntry entry  = _melees[i];
                Vector3    before = entry.Server.Position;

                entry.Brain.Tick(_serverTime);
                if (RecordStep(before, entry.Server.Position, false, ref entry.LastStep))
                {
                    ++entry.WindowReversals;
                }
            }

            for (int i = 0; i < _chargers.Length; ++i)
            {
                ChargerEntry entry  = _chargers[i];
                Vector3      before = entry.Server.Position;

                entry.Brain.Tick(_serverTime);
                if (RecordStep(before, entry.Server.Position, entry.Brain.State == EEnemyState.Dash, ref entry.LastStep))
                {
                    ++entry.WindowReversals;
                }
            }

            _worstOverlapNow  = _crowd.MeasureWorstOverlap();
            _worstOverlapEver = Mathf.Max(_worstOverlapEver, _worstOverlapNow);

            _windowElapsed += TICK_SECONDS;
            if (_windowElapsed >= WINDOW_SECONDS)
            {
                RollWindow();
            }
        }

        // 구간 끝에서 개체별 반전 횟수를 모아 보여 주고 다시 센다
        private void RollWindow()
        {
            _lastWindowReversals  = 0;
            _lastWindowWorstId    = 0;
            _lastWindowWorstCount = 0;

            for (int i = 0; i < _melees.Length; ++i)
            {
                CollectWindow(_melees[i].Server.CombatId, ref _melees[i].WindowReversals);
            }

            for (int i = 0; i < _chargers.Length; ++i)
            {
                CollectWindow(_chargers[i].Server.CombatId, ref _chargers[i].WindowReversals);
            }

            _windowElapsed = 0f;
        }

        private void CollectWindow(int combatId, ref int count)
        {
            _lastWindowReversals += count;
            if (count > _lastWindowWorstCount)
            {
                _lastWindowWorstCount = count;
                _lastWindowWorstId    = combatId;
            }

            count = 0;
        }

        // 서버 세계를 통째로 새로 만든다. 이전 외형은 지우고 등록부, 간격 계산, 피해 계산기를 새로 쓴다
        private void Build(ESpawnPattern pattern)
        {
            ClearEnemies();

            if (!_meleePrefab.TryGetBodyRadii(out float meleeBodyRadius, out float meleeSeparationRadius)
                || !_chargerPrefab.TryGetBodyRadii(out float chargerBodyRadius, out float chargerSeparationRadius))
            {
                Debug.LogError("[Enemy] 프리팹에 감지 또는 간격 콜라이더 참조가 비어 있다", this);
                _ready = false;
                enabled = false;
                return;
            }

            _registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            _resolver = new DamageResolver(_registry, ledger, DEFENSE_CONSTANT, REDUCTION_CAP);
            _crowd    = new EnemyCrowd();

            _player = new EnemyTestRigPlayer(PLAYER_ID, transform.position, PLAYER_MAX_HP);
            _registry.Register(_player);

            var random = new System.Random(SCATTER_SEED);
            int id     = ENEMY_ID_BASE;

            _melees = new MeleeEntry[Mathf.Max(0, _meleeCount)];
            for (int i = 0; i < _melees.Length; ++i)
            {
                Vector3     start  = PickStart(pattern, random);
                EnemyServer server = new EnemyServer(_meleeDefinition, id++, start, transform.position - start,
                    _referenceAttack, meleeBodyRadius, 1f, meleeSeparationRadius);

                _registry.Register(server);
                _crowd.Add(server);

                _melees[i] = new MeleeEntry
                {
                    Server = server,
                    Brain  = new MeleeEnemyBrain(server, _meleeDefinition, _referenceHealth, _registry, _resolver,
                        MeleeEnemyBrain.PROTOTYPE_RECOVER_SECONDS, _crowd),
                    View   = Instantiate(_meleePrefab, start, Quaternion.identity, transform)
                };
            }

            _chargers = new ChargerEntry[Mathf.Max(0, _chargerCount)];
            for (int i = 0; i < _chargers.Length; ++i)
            {
                Vector3     start  = PickStart(pattern, random);
                EnemyServer server = new EnemyServer(_chargerDefinition, id++, start, transform.position - start,
                    _referenceAttack, chargerBodyRadius, 1f, chargerSeparationRadius);

                _registry.Register(server);
                _crowd.Add(server);

                var movement = new EnemyTestRigMovement(server, _lineOfSight);
                _chargers[i] = new ChargerEntry
                {
                    Server = server,
                    Brain  = new ChargerEnemyBrain(server, _chargerDefinition, _referenceHealth, _registry, _resolver,
                        movement, ChargerEnemyBrain.PROTOTYPE_RECOVER_SECONDS, _crowd),
                    View   = Instantiate(_chargerPrefab, start, Quaternion.identity, transform)
                };
            }

            _serverTime      = Time.timeAsDouble;
            _tickAccumulator = 0f;
            ResetMetrics();
            _ready = true;
        }

        private void ClearEnemies()
        {
            if (_melees != null)
            {
                for (int i = 0; i < _melees.Length; ++i)
                {
                    Destroy(_melees[i].View.gameObject);
                }
            }

            if (_chargers != null)
            {
                for (int i = 0; i < _chargers.Length; ++i)
                {
                    Destroy(_chargers[i].View.gameObject);
                }
            }
        }

        private Vector3 PickStart(ESpawnPattern pattern, System.Random random)
        {
            Vector3 center = transform.position + _spawnCenter;
            if (pattern == ESpawnPattern.SamePoint)
            {
                return center;
            }

            // 반지름의 제곱근으로 뽑아야 원판 안에서 고르게 퍼진다
            double angle  = random.NextDouble() * Math.PI * 2.0;
            double radius = Math.Sqrt(random.NextDouble()) * _scatterRadius;
            return center + new Vector3((float)(Math.Cos(angle) * radius), 0f, (float)(Math.Sin(angle) * radius));
        }

        private void ResetMetrics()
        {
            _worstOverlapNow  = 0f;
            _worstOverlapEver = 0f;
            _maxTickStep      = 0f;
            _reversals        = 0;
            _elapsed          = 0f;
            _windowElapsed    = 0f;
            _lastWindowReversals  = 0;
            _lastWindowWorstId    = 0;
            _lastWindowWorstCount = 0;
        }

        // 틱마다 몸체가 가장 크게 움직인 거리와, 뚜렷하게 움직이다 반대로 꺾은 횟수를 센다. 떨림이면 뒤쪽 값이 계속 늘어난다
        private bool RecordStep(Vector3 before, Vector3 after, bool isDashing, ref Vector3 lastStep)
        {
            // 돌진은 정의된 속도로 가는 확정 이동이라 떨림 지표에서 뺀다
            if (isDashing)
            {
                lastStep = Vector3.zero;
                return false;
            }

            Vector3 step = after - before;
            step.y = 0f;

            float length = step.magnitude;
            _maxTickStep = Mathf.Max(_maxTickStep, length);

            bool reversed = (length > REVERSAL_MIN_STEP) && (lastStep.magnitude > REVERSAL_MIN_STEP)
                && (Vector3.Dot(step.normalized, lastStep.normalized) < REVERSAL_DOT);
            if (reversed)
            {
                ++_reversals;
            }

            if (length > REVERSAL_MIN_STEP)
            {
                lastStep = step;
            }

            return reversed;
        }
    }
}
