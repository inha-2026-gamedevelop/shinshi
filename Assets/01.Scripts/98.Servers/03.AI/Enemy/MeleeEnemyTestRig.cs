// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using EnemyDefinition = Shinshi.Data.EnemyCatalogSO.EnemyDefinition;

namespace Shinshi.AI
{
    // 네트워크 없이 서버 두뇌를 눈으로 확인하는 시험 장치다. 빈 오브젝트에 붙이면 적 한 마리와 WASD 플레이어를 만든다.
    // 서버 부트스트랩이 하게 될 생성, 틱 호출, 등록을 대신할 뿐이며 서버 권위 경계나 Command, Rpc를 바꾸지 않는다.
    // 지역 기준값은 플레이어 능력치가 아니라 아래 인스펙터 값으로 명시 주입한다
    /// <summary> 근접 산귀 한 마리 시험 구동기, 단일 프로세스에서 서버 두뇌와 대체 외형을 이어 준다 </summary>
    public sealed class MeleeEnemyTestRig : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    ENEMY_ID         = 100;
        private const int    PLAYER_ID        = 1;
        private const float  PLAYER_SPEED     = 5f;
        private const float  PLAYER_MAX_HP    = 100000f;
        private const float  DEFENSE_CONSTANT = 1000f; // PlayerConfigSO 초안과 같은 값
        private const float  REDUCTION_CAP    = 0.6f;
        private const float  GUI_WIDTH        = 340f;
        private const float  HIT_DAMAGE       = 100f; // 피격 모션 시험 버튼이 주는 피해
        private const float  GUI_HEIGHT       = 150f;
        private const float  GUI_MARGIN       = 10f;
        private const string HORIZONTAL_AXIS  = "Horizontal";
        private const string VERTICAL_AXIS    = "Vertical";

        /****************************************
        *                Fields
        ****************************************/

        [Header("프리팹")]
        [SerializeField] private EnemyTestView _enemyPrefab; // 몸체 판정 CapsuleCollider와 외형을 가진 시험 프리팹

        [Header("시험 주입값")]
        [SerializeField] private float   _referenceAttack = 644.32f; // Aref, 체험판 숲 기준
        [SerializeField] private float   _referenceHealth = 3541f;   // Href, 체험판 숲 기준
        [SerializeField] private Vector3 _enemyStart      = new Vector3(0f, 0f, 8f);

        private EnemyServer     _enemy;
        private MeleeEnemyBrain _brain;
        private EnemyTestRigPlayer       _player;
        private DamageResolver  _resolver;
        private EnemyTestView   _enemyView;
        private Transform       _playerBody;
        private float           _lastEnemyHealth;

        /****************************************
        *              Unity Event
        ****************************************/

        // GameDB는 Awake가 아니라 Start 이후에 읽는다
        private void Start()
        {
            EnemyDefinition definition = GameDB.Enemies.FindByRole(EEnemyRole.Melee);
            if (definition == null)
            {
                Debug.LogError("[Enemy] 근접 산귀 정의가 없다");
                enabled = false;
                return;
            }

            var registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger   = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            _resolver = new DamageResolver(registry, ledger, DEFENSE_CONSTANT, REDUCTION_CAP);

            if (_enemyPrefab == null)
            {
                Debug.LogError("[Enemy] 적 프리팹이 비어 있다", this);
                enabled = false;
                return;
            }

            // 프리팹이 두 반경의 원본이다. 감지와 피격 반경이 서버 몸체 반경(돌진 벽 정지 거리 기준)이고 간격 반경은 적끼리 간격이다
            if (!_enemyPrefab.TryGetBodyRadii(out float bodyRadius, out float separationRadius))
            {
                Debug.LogError("[Enemy] 프리팹에 감지 또는 간격 콜라이더 참조가 비어 있다", _enemyPrefab);
                enabled = false;
                return;
            }

            _player = new EnemyTestRigPlayer(PLAYER_ID, transform.position, PLAYER_MAX_HP);
            _enemy  = new EnemyServer(definition, ENEMY_ID, transform.position + _enemyStart, -_enemyStart,
                _referenceAttack, bodyRadius, 1f, separationRadius);
            registry.Register(_player);
            registry.Register(_enemy);
            _brain = new MeleeEnemyBrain(_enemy, definition, _referenceHealth, registry, _resolver);
            _lastEnemyHealth = _enemy.Health;

            _enemyView = Instantiate(_enemyPrefab, _enemy.Position, Quaternion.identity, transform);

            GameObject body = TestRigDisplay.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "PlayerBody";
            Destroy(body.GetComponent<Collider>());
            _playerBody = body.transform;
            _playerBody.SetParent(transform, false);
        }

        private void Update()
        {
            if (_brain == null)
            {
                return;
            }

            double now = Time.timeAsDouble;

            if (_player.LifeState == EParticipantLifeState.Alive)
            {
                Vector3 move = new Vector3(Input.GetAxisRaw(HORIZONTAL_AXIS), 0f, Input.GetAxisRaw(VERTICAL_AXIS));
                _player.Position += move.normalized * (PLAYER_SPEED * Time.deltaTime);
            }

            _resolver.BeginTick(now);
            _brain.Tick(now);

            _playerBody.position = _player.Position + Vector3.up;
            _playerBody.gameObject.SetActive(_player.LifeState == EParticipantLifeState.Alive);
            _enemyView.Apply(_enemy.Position, _enemy.Forward, _brain.State, _brain.TelegraphProgress(now),
                _brain.AttackRange, _brain.AttackArcAngle);

            if ((_enemy.Health < _lastEnemyHealth) && (_enemy.Health > 0f))
            {
                _enemyView.PlayHit();
            }

            _lastEnemyHealth = _enemy.Health;
        }

        private void OnGUI()
        {
            if (_brain == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(GUI_MARGIN, TestRigDisplay.GUI_TOP, GUI_WIDTH, GUI_HEIGHT), GUI.skin.box);
            GUILayout.Label($"State {_brain.State}  Attacks {_brain.ExecutedCount}  LastHit {_brain.LastHitCount}");
            GUILayout.Label($"Enemy HP {_enemy.Health:F0}/{_enemy.MaxHealth:F0}  Player HP {_player.Health:F0}");

            if (GUILayout.Button("Hit enemy"))
            {
                _enemy.ApplyDamage(HIT_DAMAGE, false, Time.timeAsDouble);
            }

            if (GUILayout.Button("Kill enemy"))
            {
                _enemy.ApplyDamage(0f, true, Time.timeAsDouble);
            }

            if (GUILayout.Button("Kill player"))
            {
                _player.ApplyDamage(0f, true, Time.timeAsDouble);
            }

            GUILayout.EndArea();
        }
    }
}
