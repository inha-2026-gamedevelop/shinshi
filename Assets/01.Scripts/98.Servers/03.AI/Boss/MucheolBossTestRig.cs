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
    // 네트워크 없이 무철 1페이즈 여섯 패턴을 눈으로 확인하는 시험 장치다. 빈 오브젝트에 붙이면 무철 대체 외형과 WASD 플레이어를 만들고 바로 전투를 연다.
    // 서버 부트스트랩이 하게 될 생성, 틱 호출, 등록을 대신할 뿐이며 서버 권위 경계나 Command, Rpc를 바꾸지 않는다.
    // 보스 수치는 운영 BossPatternCatalog 에셋이 아니라 MucheolDevCatalog의 임시 시작값을 쓴다. 운영 에셋은 무철을 비활성으로 두고 있다.
    // 월백 시험 장치와 같은 예고 표현 연결부(BossTelegraphLocalBridge)와 시험용 몸체(BossTestRigBody)를 읽기만 하고 재사용한다.
    // 무철 외형 모델이 없어 캡슐로 대신한다. 무철 기믹 1은 공용 두뇌 연결이 승인 전이라 이 장면에서 동작하지 않는다
    /// <summary> 무철 1페이즈 시험 구동기, 단일 프로세스에서 서버 두뇌와 예고 표현과 대체 외형을 이어 준다 </summary>
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

        private CombatTargetRegistry    _registry;
        private DamageResolver          _resolver;
        private BossTestRigBody         _body;
        private BossBrain               _brain;
        private EnemyTestRigPlayer      _player;
        private BossTelegraphPresenter  _presenter;
        private MucheolDevCatalog       _catalog;
        private ForcedPatternRule       _forced;
        private ComboVisual             _combo;
        private ChargeOutcomeLog        _chargeLog;
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

            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
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

            double now = ReadClock();

            if (_player.LifeState == EParticipantLifeState.Alive)
            {
                Vector3 move = new Vector3(Input.GetAxisRaw(HORIZONTAL_AXIS), 0f, Input.GetAxisRaw(VERTICAL_AXIS));
                _player.Position += move.normalized * (PLAYER_SPEED * Time.deltaTime);
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

            GUILayout.BeginArea(new Rect(GUI_MARGIN, GUI_MARGIN, GUI_WIDTH, Screen.height - (GUI_MARGIN * 2f)), GUI.skin.box);
            GUILayout.Label($"무철 시험 (임시 수치, 운영 에셋 아님)  Flow {_brain.FlowState}  Phase {_brain.Phase}");
            GUILayout.Label($"Attack {_brain.AttackState}  Pattern {_brain.ActivePatternId}");
            GUILayout.Label($"Boss HP {_body.Health:F0}/{_body.MaxHealth:F0} ({_body.HealthRate * PERCENT:F1}%)");
            GUILayout.Label($"Player HP {_player.Health:F0}/{_player.MaxHealth:F0}");

            GUILayout.Space(GUI_SPACING);
            GUILayout.Label("패턴 실행 횟수");
            for (int i = 1; i <= PATTERN_COUNT; ++i)
            {
                string id = PATTERN_ID_PREFIX + i;
                _counts.TryGetValue(id, out int count);
                GUILayout.Label($"  {id}  {count}");
            }

            GUILayout.Space(GUI_SPACING);
            GUILayout.Label("최근 순서 " + string.Join(" > ", _history));
            GUILayout.Label("P05 마지막 타: " + _combo.LastEvent);
            GUILayout.Label("P06 마지막 결과: " + _chargeLog.LastText);

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
            if (!string.IsNullOrEmpty(_forced.PendingId))
            {
                GUILayout.Label("강제 대기: " + _forced.PendingId);
            }

            GUILayout.Space(GUI_SPACING);
            if (GUILayout.Button("Boss HP -10%"))
            {
                _body.ApplyDamage(_body.MaxHealth * HP_STEP_RATE, false, ReadClock());
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

            _catalog = new MucheolDevCatalog(_arenaDiameter);
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
            _player = new EnemyTestRigPlayer(_registry.IssueCombatId(), center + _playerStart, PLAYER_MAX_HP);
            _registry.Register(_player);
            _registry.Register(_body);

            var bridge = new BossTelegraphLocalBridge(_presenter, _body, ReadClock);
            _combo     = new ComboVisual(_presenter, _body, ReadClock);
            _chargeLog = new ChargeOutcomeLog();

            // 팩토리 인자 순서: 부채꼴(P01, P02), 이동 창구와 돌진(P03, P06), 원형(P04), 콤보 데이터와 표현(P05), 충돌 회복(P06)
            var factory = new MucheolPatternFactory(_referenceHealth, bridge, new BoundaryMarginMovement(_body), bridge, bridge,
                new MucheolComboSpec(new MucheolComboHit(5f, 120f, 0.14f, 0f), new MucheolComboHit(5f, 70f, 0.24f, 0.5f)), _combo,
                new MucheolChargeSpec(2.5f), null, _chargeLog);

            var dependencies = new BossBrainDependencies
            {
                Catalog  = _catalog,
                Self     = _body,
                Vitals   = _body,
                Targets  = _registry,
                Damage   = _resolver,
                Patterns = factory,
                Effects  = new BossEffectComposite(), // 무철 1페이즈에는 투사체나 장판 같은 지속물이 없다
                Control  = _body,
                Random   = new SeededBossRandom(Environment.TickCount)
            };

            _brain  = new BossBrain(MucheolPatternFactory.BOSS_ID, dependencies);
            _forced = new ForcedPatternRule(_catalog);
            _brain.AddSelectionRule(_forced);
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
            Debug.Log($"[Boss] 무철 1페이즈 시험 시작, maxHealth={_body.MaxHealth:F0}, 전장 지름={boss.ArenaDiameter}");
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
            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "MucheolStandIn";
            Destroy(capsule.GetComponent<Collider>());
            float radiusScale = Constants.Combat.BOSS_BODY_RADIUS / CAPSULE_BASE_RADIUS;
            capsule.transform.SetParent(transform, false);
            capsule.transform.localScale = new Vector3(radiusScale, CAPSULE_HEIGHT_SCALE, radiusScale);
            capsule.GetComponent<Renderer>().material.color = BossColor;
            _bossBody = capsule.transform;

            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
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

            _history.Add(id.Substring(id.Length - PATTERN_SUFFIX_LENGTH));
            if (_history.Count > HISTORY_LENGTH)
            {
                _history.RemoveAt(0);
            }
        }

        private void ApplyView()
        {
            bool bossAlive = _body.LifeState == EParticipantLifeState.Alive;
            float bodyHeight = CAPSULE_HEIGHT_SCALE;

            _bossBody.position = _body.Position + (Vector3.up * bodyHeight);
            _bossBody.rotation = Quaternion.LookRotation(_body.Forward, Vector3.up);
            _bossBody.gameObject.SetActive(bossAlive);

            _bossNose.position = _body.Position + (_body.Forward * Constants.Combat.BOSS_BODY_RADIUS) + (Vector3.up * NOSE_HEIGHT);
            _bossNose.rotation = _bossBody.rotation;
            _bossNose.gameObject.SetActive(bossAlive);

            _playerBody.position = _player.Position + Vector3.up;
            _playerBody.gameObject.SetActive(_player.LifeState == EParticipantLifeState.Alive);

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
