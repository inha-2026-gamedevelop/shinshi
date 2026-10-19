// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    /// <summary> 일반 적 네트워크 개체의 복제 상태, 종류와 체력, 생사, 행동 상태, 공격 예고를 싣는다. 판정은 서버의 EnemyServer가 한다 </summary>
    public sealed class EnemyNetState : NetworkBehaviour
    {
        // 서버 스포너(EnemyNetworkSpawner)가 풀에서 꺼낸 개체에 Bind로 새 적을 묶고 매 틱 SetSnapshot으로 값을 싣는다. Command가 없어
        // 클라이언트는 상태를 바꿀 수 없다. 위치와 방향은 같은 프리팹의 NetworkTransform이 보내므로 여기서 싣지 않는다.
        //
        // 값은 모두 SyncVar라 늦게 들어온 관찰자는 스폰 초기 상태로 지금의 종류, 체력, 진행 중인 예고를 그대로 받는다.
        // 예고는 번호와 시작, 끝 서버 시각을 함께 보내 표현이 서버 시각으로 진행도를 세므로 늦은 접속도 중간부터 그린다.
        // SyncVar 훅은 쓰지 않는다. 표현이 매 프레임 읽어 풀 재사용과 훅 순서에 흔들리지 않는다

        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 서버가 아직 정하지 않은 시각, double.IsNaN으로 확인한다 </summary>
        public const double NO_SERVER_TIME = double.NaN;

        [SyncVar] private int                   _combatId = CombatIds.NONE;
        [SyncVar] private EEnemyRole            _role;
        [SyncVar] private float                 _health;
        [SyncVar] private float                 _maxHealth;
        [SyncVar] private EParticipantLifeState _lifeState = EParticipantLifeState.Alive;
        [SyncVar] private EEnemyState           _behavior  = EEnemyState.Idle;

        [SyncVar] private bool              _hasTelegraph;
        [SyncVar] private ushort            _telegraphSequence;
        [SyncVar] private EEnemyAttackShape _telegraphShape;
        [SyncVar] private Vector3           _telegraphOrigin;
        [SyncVar] private ushort            _telegraphYaw;
        [SyncVar] private float             _telegraphRange;
        [SyncVar] private float             _telegraphWidth;
        [SyncVar] private double            _telegraphStart = NO_SERVER_TIME;
        [SyncVar] private double            _telegraphEnd   = NO_SERVER_TIME;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 전투 대상 번호, 비어 있는 풀 개체는 NONE이다 </summary>
        public int CombatId => _combatId;

        public EEnemyRole Role => _role;

        public float Health    => _health;
        public float MaxHealth => _maxHealth;

        public float HealthRate => (_maxHealth > 0f) ? Mathf.Clamp01(_health / _maxHealth) : 0f;

        public EParticipantLifeState LifeState => _lifeState;
        public EEnemyState           Behavior  => _behavior;

        /// <summary> 마지막으로 시작한 예고의 번호, 새 예고마다 1씩 오르고 0은 건너뛴다 </summary>
        public ushort TelegraphSequence => _telegraphSequence;

        /// <summary> 진행 중이라고 서버가 알린 예고가 있는가 </summary>
        public bool HasTelegraph => _hasTelegraph;

        /****************************************
        *                Methods
        ****************************************/

        // 프리팹 값과 상관없이 계약값 주기를 쓴다. 상태는 값이 바뀔 때만 실려 매 틱 불러도 비용이 늘지 않는다
        private void Awake()
        {
            syncInterval = Constants.Network.ENEMY_SYNC_INTERVAL_SECONDS;
            syncMethod   = SyncMethod.Reliable;
        }

        /// <summary> 진행 중인 예고를 읽는다, 없으면 거짓이다 </summary>
        public bool TryGetTelegraph(out EnemyTelegraphCue cue)
        {
            if (!_hasTelegraph)
            {
                cue = default;
                return false;
            }

            cue = new EnemyTelegraphCue(_telegraphSequence, _telegraphShape, _telegraphOrigin, FacingRules.DecodeYaw(_telegraphYaw),
                _telegraphRange, _telegraphWidth, _telegraphStart, _telegraphEnd);
            return true;
        }

        /// <summary> 다음 예고 번호, 0은 예고 없음 표시로 남겨 두려고 건너뛴다 </summary>
        public static ushort NextSequence(ushort current)
        {
            ushort next = (ushort)(current + 1);
            return (next == 0) ? (ushort)1 : next;
        }

        /****************************************
        *                Server
        ****************************************/

        // 풀에서 꺼내 새 적에 묶을 때 이전 적의 값과 예고를 모두 지운다. 스폰 전에 불러 첫 관찰자부터 새 값을 받는다
        [Server]
        public void Bind(int combatId, EEnemyRole role, float health, float maxHealth)
        {
            _combatId  = combatId;
            _role      = role;
            _health    = health;
            _maxHealth = maxHealth;
            _lifeState = EParticipantLifeState.Alive;
            _behavior  = EEnemyState.Idle;
            ClearTelegraph();
        }

        // 같은 값이면 SyncVar가 더럽혀지지 않으므로 매 틱 불러도 바뀐 값만 전송된다
        [Server]
        public void SetSnapshot(float health, float maxHealth, EParticipantLifeState lifeState)
        {
            _health    = health;
            _maxHealth = maxHealth;
            _lifeState = lifeState;

            if (lifeState != EParticipantLifeState.Alive)
            {
                _behavior = EEnemyState.Dead;
                ClearTelegraph();
            }
        }

        // 죽은 뒤에는 되돌아오지 않는다
        [Server]
        public void SetBehavior(EEnemyState behavior)
        {
            if (_lifeState == EParticipantLifeState.Alive)
            {
                _behavior = behavior;
            }
        }

        /// <summary> 예고를 시작한다, 번호를 올려 같은 모양이 연달아 와도 새 예고로 구분한다 </summary>
        [Server]
        public void BeginTelegraph(EEnemyAttackShape shape, Vector3 origin, Vector3 direction, float range, float width,
            double startServerTime, double endServerTime)
        {
            if (_lifeState != EParticipantLifeState.Alive)
            {
                return;
            }

            _telegraphSequence = NextSequence(_telegraphSequence);
            _telegraphShape    = shape;
            _telegraphOrigin   = origin;
            _telegraphYaw      = FacingRules.EncodeYaw(direction);
            _telegraphRange    = range;
            _telegraphWidth    = width;
            _telegraphStart    = startServerTime;
            _telegraphEnd      = endServerTime;
            _hasTelegraph      = true;
        }

        /// <summary> 진행 중인 예고를 끝낸다, 취소, 타격, 사망 모두 같다 </summary>
        [Server]
        public void EndTelegraph()
        {
            ClearTelegraph();
        }

        private void ClearTelegraph()
        {
            _hasTelegraph   = false;
            _telegraphStart = NO_SERVER_TIME;
            _telegraphEnd   = NO_SERVER_TIME;
        }
    }
}
