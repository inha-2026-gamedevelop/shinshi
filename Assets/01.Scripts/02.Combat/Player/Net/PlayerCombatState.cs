// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

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

        // 남은 보호막, 체력바 흰 구간 표시 전용이다. 흡수와 만료는 서버 처리기가 자기 상태로 판정한다
        [SyncVar(hook = nameof(OnShieldChanged))]
        private float _shield = 0f;

        [SyncVar(hook = nameof(OnLifeStateChanged))]
        private EParticipantLifeState _lifeState = EParticipantLifeState.Alive;

        // 시각용 자식 피벗만 이 방향으로 돌린다. 루트 회전은 복제하지 않는다
        [SyncVar]
        private ushort _facingYaw = 0;

        // 서버가 PlayerServer를 만들 때 세션 결합의 직업(API 교환 응답)으로 정한다. 값 0이 무사라 직렬화 초기값만 무사이고,
        // 서버 판정은 이 값을 읽지 않고 PlayerServer가 받은 직업을 쓴다
        [SyncVar(hook = nameof(OnPlayerClassChanged))]
        private EPlayerClass _playerClass = EPlayerClass.Warrior;
        [SyncVar] private int _combatId;

        // DB 인증 닉네임과 정규 v2 외형, 명시적 레거시 캐릭터는 둘 다 null이다(TryResolve가 직업 기본값에 대응시킨다).
        // 최초 스폰 전과 기존 개체 재결합 양쪽에서 서버만 SetPublicIdentity로 채운다
        [SyncVar(hook = nameof(OnCharacterNameChanged))]
        private string _characterName;
        [SyncVar(hook = nameof(OnAppearanceChanged))]
        private CharacterAppearance _appearance;

        // 머리 위 이름 위에 띄우는 길드 이름. 토큰 교환 응답이 준 값이며 길드가 없으면 null이다
        [SyncVar] private string _guildName;

        // 관찰자용 공개 이동 속도, Transform 차분이 아니라 서버가 직접 공개한다(NETWORK_AUTHORITY 5.5 함정5)
        [SyncVar] private float _locomotionSpeed;

        [SyncVar] private float _energy;
        [SyncVar] private float _maxEnergy;
        [SyncVar] private float _resonance;
        // 마검사, 궁수 스택, 다른 직업은 늘 0이다. 표시 전용이며 서버 판정은 기술 서버가 가진 값을 쓴다
        [SyncVar] private byte _classStacks;

        // 궁수 기본 공격에 지금 실린 속성, 다른 직업은 늘 None이다. 화살 표현과 입력 간격 짐작에만 쓴다
        [SyncVar] private EArcherElement _archerElement;

        // 궁수 기본 공격 화살이 적을 맞힐 때마다 서버가 1씩 올린다(255 다음 0). 위치와 속성은 그 화살의 값이며 적중 효과 표시에만 쓴다.
        // 한 복제 주기 안에 여러 발이 맞으면 마지막 적중을 표시한다
        [SyncVar] private byte           _arrowHitSequence;
        [SyncVar] private Vector3        _arrowHitPosition;
        [SyncVar] private EArcherElement _arrowHitElement;
        [SyncVar] private bool           _arrowHitIsHeavy;
        [SyncVar] private ESkillPhase _skillPhase;
        [SyncVar] private int _castInstanceId;
        [SyncVar] private int _actionInstanceId;
        [SyncVar] private byte _skillSlot;
        [SyncVar] private ESkillRequestKind _skillKind;
        [SyncVar] private Vector3 _skillOrigin;
        [SyncVar] private Vector3 _skillAim;
        [SyncVar] private Vector3 _skillPoint;
        [SyncVar] private double _skillResolveAt;

        // 관찰자에게 기본 공격과 회피 모션을 보이기 위한 표현 전용 순번이다. 서버가 행동을 시작할 때마다 1씩 올리고(255 다음 0)
        // 클라이언트는 값이 바뀌었는지만 본다. 서버 판정은 이 값을 읽지 않는다
        [SyncVar] private byte _attackSequence;
        [SyncVar] private byte _dodgeSequence;

        // 서버 콤보 기본 공격이면 이번 순번이 쓴 단계(0부터)다. 콤보가 없는 직업은 늘 0이다. 순번과 같은 갱신에 실려 함께 바뀐다
        [SyncVar] private byte _attackComboStep;

        // 순번을 올린 서버 시각과 공개 이동 속도가 정지에서 움직임으로 바뀐 마지막 서버 시각, 표현 전용이며 서버 판정은 읽지 않는다.
        // 두 값의 차로 이동이 기본 공격 발사 전에 시작했는지(준비 취소) 후에 시작했는지를 수신 순서와 지터 없이 가른다
        [SyncVar] private double _attackStartedAt;
        [SyncVar] private double _locomotionStartedAt;

        // 서버가 기절로 진행 중인 기본 공격 준비나 기술을 끊거나, 이동이 기본 공격 준비를 끊을 때마다 1씩 올린다(255 다음 0).
        // 회피와 사망에는 각자 신호가 있어 올리지 않는다.
        // 클라이언트 표현은 값이 바뀌면 아직 나가지 않은 발사와 타격 재생을 버린다. 서버 판정은 이 값을 읽지 않는다
        [SyncVar] private byte _actionCancelSequence;

        // 지금 적용 중인 제어와 그 종료 서버 시각, 표시용이다. 판정은 서버 처리기가 자기 상태로 한다
        [SyncVar] private long _totalExperience;
        public long TotalExperience => _totalExperience;
        [SyncVar] private EPlayerControlKind _controlKind;
        [SyncVar] private double _controlEndsAt;

        /****************************************
        *              Properties
        ****************************************/

        public float                 Health      => _health;
        public float                 MaxHealth   => _maxHealth;
        public float                 Shield      => _shield;
        public EParticipantLifeState LifeState   => _lifeState;
        public ushort                FacingYaw   => _facingYaw;
        public EPlayerClass          PlayerClass => _playerClass;
        public int CombatId => _combatId;
        // 머리 위 이름. DB 인증 닉네임이며 이름이 없는 레거시 캐릭터면 빈 값이다
        public string DisplayName => _characterName ?? string.Empty;
        public string GuildName => _guildName ?? string.Empty;
        public float Energy => _energy;
        public float MaxEnergy => _maxEnergy;
        public float Resonance => _resonance;
        public byte ClassStacks => _classStacks;
        public EArcherElement ArcherElement => _archerElement;
        public byte ArrowHitSequence => _arrowHitSequence;
        public Vector3 ArrowHitPosition => _arrowHitPosition;
        public EArcherElement ArrowHitElement => _arrowHitElement;
        public bool ArrowHitIsHeavy => _arrowHitIsHeavy;
        public ESkillPhase SkillPhase => _skillPhase;
        public int CastInstanceId => _castInstanceId;
        public int ActionInstanceId => _actionInstanceId;
        public byte SkillSlot => _skillSlot;
        public ESkillRequestKind SkillKind => _skillKind;
        public Vector3 SkillOrigin => _skillOrigin;
        public Vector3 SkillAim => _skillAim;
        public Vector3 SkillPoint => _skillPoint;
        public double SkillResolveAt => _skillResolveAt;
        public string CharacterName => _characterName;
        public CharacterAppearance Appearance => _appearance;
        public float LocomotionSpeed => _locomotionSpeed;
        public byte AttackSequence => _attackSequence;
        public byte AttackComboStep => _attackComboStep;
        public double AttackStartedAt => _attackStartedAt;
        public double LocomotionStartedAt => _locomotionStartedAt;
        public byte DodgeSequence => _dodgeSequence;
        public byte ActionCancelSequence => _actionCancelSequence;
        public EPlayerControlKind ControlKind => _controlKind;
        public double ControlEndsAt => _controlEndsAt;

        // 전용 서버에서는 hook이 불리지 않으므로 표현과 UI만 구독한다
        public event Action<float, float>           HealthChanged;
        public event Action<float>                 ShieldChanged;
        public event Action<EParticipantLifeState> LifeStateChanged;
        public event Action<EPlayerClass>          PlayerClassChanged;
        public event Action<string>                CharacterNameChanged;
        public event Action<CharacterAppearance>   AppearanceChanged;

        /****************************************
        *                Server
        ****************************************/

        [Server]
        public void SetTotalExperience(long totalExperience)
        {
            _totalExperience = totalExperience;
        }

        [Server]
        public void SetHealth(float health, float maxHealth)
        {
            _maxHealth = maxHealth;
            _health    = health;
        }

        // 같은 값이면 SyncVar가 더럽혀지지 않으므로 바뀔 때만 전송된다
        [Server]
        public void SetShield(float shield)
        {
            _shield = shield;
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

        // 최초 스폰 전(CombatServerRuntime.AddPlayer)과 기존 개체 재결합(TryReattachPlayer) 양쪽에서 부른다.
        // 클라이언트가 보낸 값이 아니라 DB 인증 토큰 claims/binding이 가진 값만 쓴다
        [Server]
        public void SetPublicIdentity(string characterName, CharacterAppearance appearance)
        {
            _characterName = characterName;
            _appearance    = appearance;
        }

        [Server]
        public void SetGuildName(string guildName)
        {
            _guildName = guildName;
        }

        // Transform 차분이 아니라 서버가 직접 공개한다. PlayerServer.PublishMovement가 조기 반환 앞에서 부른다
        [Server]
        public void SetLocomotionSpeed(float locomotionSpeed)
        {
            if ((_locomotionSpeed <= 0f) && (locomotionSpeed > 0f))
            {
                _locomotionStartedAt = NetworkTime.time;
            }

            _locomotionSpeed = locomotionSpeed;
        }

        // 같은 값이면 SyncVar가 더럽혀지지 않으므로 매 틱 불러도 바뀔 때만 전송된다
        [Server]
        public void SetClassStacks(byte stacks)
        {
            _classStacks = stacks;
        }

        // 같은 값이면 SyncVar가 더럽혀지지 않으므로 매 틱 불러도 바뀔 때만 전송된다
        [Server]
        public void SetArcherElement(EArcherElement element)
        {
            _archerElement = element;
        }

        [Server]
        public void BumpArrowHit(Vector3 position, EArcherElement element, bool isHeavy = false)
        {
            _arrowHitPosition = position;
            _arrowHitElement  = element;
            _arrowHitIsHeavy  = isHeavy;
            _arrowHitSequence = unchecked((byte)(_arrowHitSequence + 1));
        }

        [Server]
        public void SetSkillResources(float energy, float maxEnergy, float resonance)
        {
            _energy = energy;
            _maxEnergy = maxEnergy;
            _resonance = resonance;
        }

        [Server]
        public void BumpAttackSequence(byte comboStep)
        {
            _attackComboStep = comboStep;
            _attackStartedAt = NetworkTime.time;
            _attackSequence  = unchecked((byte)(_attackSequence + 1));
        }

        [Server]
        public void BumpDodgeSequence()
        {
            _dodgeSequence = unchecked((byte)(_dodgeSequence + 1));
        }

        [Server]
        public void BumpActionCancelSequence()
        {
            _actionCancelSequence = unchecked((byte)(_actionCancelSequence + 1));
        }

        // 같은 값이면 SyncVar가 더럽혀지지 않으므로 매 틱 불러도 바뀔 때만 전송된다
        [Server]
        public void SetControl(EPlayerControlKind kind, double endsAt)
        {
            _controlKind   = kind;
            _controlEndsAt = (kind == EPlayerControlKind.None) ? 0.0 : endsAt;
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

        private void OnShieldChanged(float previous, float next)
        {
            ShieldChanged?.Invoke(next);
        }

        private void OnLifeStateChanged(EParticipantLifeState previous, EParticipantLifeState next)
        {
            LifeStateChanged?.Invoke(next);
        }

        private void OnPlayerClassChanged(EPlayerClass previous, EPlayerClass next)
        {
            PlayerClassChanged?.Invoke(next);
        }

        private void OnCharacterNameChanged(string previous, string next)
        {
            CharacterNameChanged?.Invoke(next);
        }

        private void OnAppearanceChanged(CharacterAppearance previous, CharacterAppearance next)
        {
            AppearanceChanged?.Invoke(next);
        }
    }
}
