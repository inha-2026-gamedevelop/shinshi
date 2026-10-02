// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 모든 관찰자에게 복제하는 플레이어 전투 상태, 값은 서버 처리기만 바꾼다 </summary>
    public class PlayerCombatState : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnHealthChanged))]
        private float _health = 0f;

        [SyncVar(hook = nameof(OnMaxHealthChanged))]
        private float _maxHealth = 0f;

        [SyncVar(hook = nameof(OnLifeStateChanged))]
        private EParticipantLifeState _lifeState = EParticipantLifeState.Alive;

        // 시각용 자식 피벗만 이 방향으로 돌린다. 루트 회전은 복제하지 않는다
        [SyncVar]
        private ushort _facingYaw = 0;

        // 서버가 PlayerServer를 만들 때 세션 결합의 직업(API 교환 응답)으로 정한다. 값 0이 무사라 직렬화 초기값만 무사이고,
        // 서버 판정은 이 값을 읽지 않고 PlayerServer가 받은 직업을 쓴다
        [SyncVar]
        private EPlayerClass _playerClass = EPlayerClass.Warrior;
        [SyncVar] private int _combatId;

        [SyncVar] private float _energy;
        [SyncVar] private float _maxEnergy;
        [SyncVar] private float _resonance;
        [SyncVar] private ESkillPhase _skillPhase;
        [SyncVar] private int _castInstanceId;
        [SyncVar] private int _actionInstanceId;
        [SyncVar] private byte _skillSlot;
        [SyncVar] private ESkillRequestKind _skillKind;
        [SyncVar] private Vector3 _skillOrigin;
        [SyncVar] private Vector3 _skillAim;
        [SyncVar] private Vector3 _skillPoint;
        [SyncVar] private double _skillResolveAt;

        /****************************************
        *              Properties
        ****************************************/

        public float                 Health      => _health;
        public float                 MaxHealth   => _maxHealth;
        public EParticipantLifeState LifeState   => _lifeState;
        public ushort                FacingYaw   => _facingYaw;
        public EPlayerClass          PlayerClass => _playerClass;
        public int CombatId => _combatId;
        public float Energy => _energy;
        public float MaxEnergy => _maxEnergy;
        public float Resonance => _resonance;
        public ESkillPhase SkillPhase => _skillPhase;
        public int CastInstanceId => _castInstanceId;
        public int ActionInstanceId => _actionInstanceId;
        public byte SkillSlot => _skillSlot;
        public ESkillRequestKind SkillKind => _skillKind;
        public Vector3 SkillOrigin => _skillOrigin;
        public Vector3 SkillAim => _skillAim;
        public Vector3 SkillPoint => _skillPoint;
        public double SkillResolveAt => _skillResolveAt;

        // 전용 서버에서는 hook이 불리지 않으므로 표현과 UI만 구독한다
        public event Action<float, float>           HealthChanged;
        public event Action<EParticipantLifeState> LifeStateChanged;

        /****************************************
        *                Server
        ****************************************/

        [Server]
        public void SetHealth(float health, float maxHealth)
        {
            _maxHealth = maxHealth;
            _health    = health;
        }

        [Server]
        public void SetLifeState(EParticipantLifeState lifeState)
        {
            _lifeState = lifeState;
        }

        // SyncVar는 같은 값이면 더럽히지 않으므로 매 틱 불러도 바뀔 때만 전송된다
        [Server]
        public void SetFacingYaw(ushort yaw)
        {
            _facingYaw = yaw;
        }

        [Server]
        public void SetPlayerClass(EPlayerClass playerClass)
        {
            _playerClass = playerClass;
        }

        [Server]
        public void SetCombatId(int combatId) { _combatId = combatId; }

        [Server]
        public void SetSkillResources(float energy, float maxEnergy, float resonance)
        {
            _energy = energy;
            _maxEnergy = maxEnergy;
            _resonance = resonance;
        }

        [Server]
        public void SetSkillExecution(ESkillPhase phase, int castId, int actionId, byte slot, ESkillRequestKind kind,
            Vector3 origin, Vector3 aim, Vector3 point, double resolveAt)
        {
            _skillPhase = phase;
            _castInstanceId = castId;
            _actionInstanceId = actionId;
            _skillSlot = slot;
            _skillKind = kind;
            _skillOrigin = origin;
            _skillAim = aim;
            _skillPoint = point;
            _skillResolveAt = resolveAt;
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        private void OnHealthChanged(float previous, float next)
        {
            HealthChanged?.Invoke(next, _maxHealth);
        }

        private void OnMaxHealthChanged(float previous, float next)
        {
            HealthChanged?.Invoke(_health, next);
        }

        private void OnLifeStateChanged(EParticipantLifeState previous, EParticipantLifeState next)
        {
            LifeStateChanged?.Invoke(next);
        }
    }
}
