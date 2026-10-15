// System
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.AI;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary> 플레이어 한 명의 서버 판정, 입력 검증과 서버 권위 이동, 기본 공격, 체력과 사망을 맡는다 </summary>
    public sealed class PlayerServer : IPlayerInputServerHandler, IPlayerSkillServerHandler, ISkillCombatant, IPotionUser,
        IPlayerControlTarget, ICriticalDamageSource, ILifeStealSource, IBossDisplaceTarget, IHitImmunityTarget
    {
        // Command 처리기는 검증과 의도 보관만 하고, 실제 이동과 공격은 CombatServerRuntime의 틱에서 한다.
        // 한 틱에 이동 입력이 여러 개 와도 경로 계산은 마지막 것 한 번만 하고, 처리 순서를 틱 순서에 맞춘다
        // 소유 클라이언트는 PlayerPrediction의 PlayerMovePredictor가 같은 이동 순번, 경과 시간의 위치로 예측 오차를 보정한다

        /****************************************
        *              Constants
        ****************************************/

        // 서버 시각은 0 이상이라 음수가 부활한 적 없음의 표식이다
        public const double NO_REVIVE_TIME = -1.0;

        /****************************************
        *                Fields
        ****************************************/

        private readonly PlayerInputState                 _input;
        private readonly PlayerCombatState                _combat;
        private readonly Transform                        _transform;
        private readonly PlayerInputGate                  _gate;
        private readonly IDamageResolver                  _resolver;
        private readonly ICombatTargetQuery               _query;
        private readonly PlayerConfigSO.BasicAttackSpec   _attack;
        private readonly List<ICombatTarget>              _targetBuffer;
        private readonly NavMeshPath                      _navPath;
        private readonly PathFollower                     _path;
        private readonly MoveRequestThrottle              _moveThrottle;
        private readonly CombatLineOfSight                _lineOfSight;
        private readonly EPlayerClass                     _playerClass;
        private readonly int                              _combatId;
        private readonly int                              _participantId;
        private readonly float                            _moveSpeed;
        private float                                     _attackPower;
        private readonly float                            _defense;
        private readonly float                            _maxEnergy;
        private float                                     _critChance;
        private float                                     _lifeStealRate;
        private readonly float                            _lifeStealCapPerSecond;
        private readonly double                           _attackBufferSeconds;
        private readonly PlayerControlTracker             _control;

        private float                 _health              = 0f;
        private float                 _maxHealth           = 0f;
        private EParticipantLifeState _lifeState           = EParticipantLifeState.Alive;
        private double                _deathTime           = 0.0;
        private Vector3               _forward             = Vector3.forward;
        private bool                  _hasPendingMove      = false;
        private Vector3               _pendingDestination  = Vector3.zero;
        private uint                  _pendingMoveSequence = 0;
        private uint                  _moveSequence        = 0;
        private double                _moveStartTime       = 0.0;
        private PlayerMoveAuthority   _publishedMove;
        private bool                  _hasPendingAttack    = false;
        private ushort                _pendingAimYaw       = 0;
        private double                _nextAttackTime      = 0.0;
        private bool                  _isWindingUp         = false;
        private double                _windupFireAt        = 0.0;
        private Vector3               _windupAim           = Vector3.forward;
        private float                 _windupCoefficient   = 0f;
        private int                   _comboStep           = BasicAttackComboRules.NO_STEP;
        private double                _comboImmuneUntil    = 0.0;
        private double                _grantedImmuneUntil  = 0.0;
        private bool                  _arrowActive         = false;
        private Vector3               _arrowOrigin         = Vector3.zero;
        private Vector3               _arrowAim            = Vector3.forward;
        private float                 _arrowTravelled      = 0f;
        private double                _arrowLastTime       = 0.0;
        private int                   _arrowCastId         = CombatIds.NONE;
        private int                   _arrowActionId       = CombatIds.NONE;
        private float                 _arrowDamage         = 0f;
        private float                 _arrowSpeed          = 0f;
        private EArcherElement        _arrowElement        = EArcherElement.None;
        private readonly float        _dodgeDistance;
        private readonly float        _dodgeDuration;
        private readonly double       _dodgeCooldown;
        private bool                  _hasPendingDodge      = false;
        private ushort                _pendingDodgeYaw      = 0;
        private uint                  _pendingDodgeSequence = 0;
        private double                _pendingDodgeDeadline = 0.0;
        private bool                  _isDodging            = false;
        private Vector3               _dodgeStart           = Vector3.zero;
        private Vector3               _dodgeEnd             = Vector3.zero;
        private double                _dodgeStartTime       = 0.0;
        private double                _nextDodgeTime        = 0.0;
        private bool                  _isDisconnecting      = false;
        private bool                  _isDetached           = false;
        private SkillServer           _skills;
        private ICombatSkillPhase     _skillPhase;
        private bool                  _hasPendingSkill      = false;
        private PlayerSkillInput      _pendingSkill;
        private double                _pendingSkillDeadline = 0.0;
        private bool                  _hasReviveRequest     = false;
        private uint                  _reviveSequence       = 0;
        private double                _nextReviveCheckTime  = 0.0;
        private bool                  _isWaitingSafePoint   = false;
        private Vector3               _deathPosition        = Vector3.zero;
        private bool                  _hasDeathPosition     = false;
        private EPlayerControlKind    _controlKind          = EPlayerControlKind.None;
        private long                  _lifeStealWindow      = LifeStealRules.NO_WINDOW;
        private float                 _lifeStealWindowHealed = 0f;
        private double                _lastReviveTime       = NO_REVIVE_TIME;
        private bool                  _isServerProtected    = false;

        /****************************************
        *              Properties
        ****************************************/

        public PlayerInputState      InputState      => _input;
        public PlayerCombatState     Combat          => _combat;
        public int                   CombatId        => _combatId;
        public int                   ParticipantId   => _participantId;
        public EPlayerClass          PlayerClass     => _playerClass;
        public ECombatFaction        Faction         => ECombatFaction.Player;
        public EParticipantLifeState LifeState       => _lifeState;
        public bool                  IsSummon        => false;
        public int                   OwnerCombatId   => CombatIds.NONE;
        public Vector3               Position        => _transform.position;
        public Vector3               Forward         => _forward;
        public float                 BodyRadius      => Constants.Combat.PLAYER_BODY_RADIUS;
        public float                 Health          => _health;
        public float                 MaxHealth       => _maxHealth;
        public float                 Defense         => _defense;
        public double                DeathTime       => _deathTime;

        // 부활 단계가 승인한 서버 시각, 부활한 적이 없으면 NO_REVIVE_TIME이다. 보스 기믹의 부활 직후 보호가 이 시각에서 잰다
        public double                LastReviveTime  => _lastReviveTime;
        public void ApplyGrowth(in CharacterStats stats)
        {
            _attackPower = stats.Attack;
            _critChance = stats.CritChance;
            _lifeStealRate = stats.LifeSteal;
            _maxHealth = stats.MaxHealth;
            _health = Mathf.Min(_health, _maxHealth);
            _combat.SetHealth(_health, _maxHealth);
        }

        public float                 AttackPower     => _attackPower;

        // 0에서 1 사이, 상한 적용 뒤 값
        public float                 CritChance      => _critChance;
        public float                 MaxEnergy       => _maxEnergy;
        public SkillServer           Skills          => _skills;

        // 연결이 끊겨 소유권만 뗀 채 서버에 남은 상태. 입력은 받지 않지만 피격, 사망, 기술 타이머는 계속 처리한다
        public bool                  IsDetached      => _isDetached;

        // 부활 요청을 받아 두고 틱의 부활 단계가 승인하기를 기다린다
        public bool                  HasReviveRequest    => _hasReviveRequest;
        public double                NextReviveCheckTime => _nextReviveCheckTime;

        // 출정 명단에 마지막으로 알린 생사, 틱 끝에서 LifeState와 다를 때만 다시 알린다
        public EParticipantLifeState ReportedLifeState { get; set; } = EParticipantLifeState.Alive;

        // 기획상 복귀 뒤 무적 시간은 없다. 공격 예고와 실제 범위가 피격을 정하므로 부활 직후에도 거짓이다.
        // 서버 보호(SetServerProtection)가 켜진 동안만 참이다. 판정기를 거치지 않는 KillByServer는 이 값을 보지 않는다
        // TODO(민성): 입장 직후 같은 다른 스폰 보호가 기획에 생기면 그 기간만 무적 상태를 반영한다
        public bool IsInvulnerable  => _isServerProtected;
        public bool IsGuardingFront => false;

        // 기절 중에는 물약을 쓰지 못하고 속박 중에는 쓸 수 있다
        public bool IsControlLocked => PlayerControlRules.BlocksPotion(_control.Evaluate(NetworkTime.time));

        // 마지막 틱에 확정한 제어, 입력 처리기는 시각을 새로 계산해 쓴다
        public EPlayerControlKind ControlKind => _controlKind;

        // 회피는 매 틱 시작점과 끝점 사이의 절대 위치를 다시 쓰므로 그 사이에 밀면 다음 틱에 되돌아간다.
        // 돌진과 후퇴 기술은 시전 때 고정한 원점에서 목표 위치를 계산해 준비 중에 밀려도 이동 거리가 어긋난다.
        // 기술 서버가 이동 기술인지 밖에 알리지 않아 준비와 타격 구간은 기술 종류와 무관하게 막고, 위치를 쓰지 않는 후딜만 받는다
        public bool CanBeDisplaced => (_lifeState == EParticipantLifeState.Alive) && (!_isDodging) && (!IsHitImmune(NetworkTime.time))
            && ((_skills == null) || (_skills.Phase == ESkillPhase.Idle) || (_skills.Phase == ESkillPhase.Recovery));

        // 기술 반격의 120도 방어는 SkillDamageResolver가 적중 원장과 함께 판정한다

        /****************************************
        *                Methods
        ****************************************/

        // OnStartServer 알림에서 한 번 만든다. 할당은 여기서 끝내고 틱과 Command 처리에서는 만들지 않는다.
        // participantId는 인증기가 연결에 결합한 출정 참가자 번호다. 재접속해도 같은 번호가 온다.
        // playerClass는 결합이 API 교환에서 받은 직업이다. 기본 공격 명세와 기술 서버가 모두 이 값을 쓰고, 클라이언트에는
        // 아래에서 복제 상태로 알린다. 상태의 SyncVar를 읽어 정하면 [Server] 호출 순서에 기대게 되므로 인자로 받는다
        // stats는 결합의 출정 전투 준비로 CharacterStatsRules가 계산한 최종 능력치다. 전투 준비가 없는 개발 입장은 기본값이다
        public PlayerServer(PlayerInputState input, PlayerCombatState combat, EPlayerClass playerClass, int combatId,
            int participantId, int sessionEpoch, PlayerConfigSO config, in CharacterStats stats, IDamageResolver resolver,
            ICombatTargetQuery query, CombatLineOfSight lineOfSight, double serverTime)
        {
            _input         = input;
            _combat        = combat;
            _transform     = input.transform;
            _resolver      = resolver;
            _query         = query;
            _lineOfSight   = lineOfSight;
            _playerClass   = playerClass;
            _combatId      = combatId;
            _participantId = participantId;

            _moveSpeed   = stats.MoveSpeed;
            _attackPower = stats.Attack;
            _defense     = stats.Defense;
            _maxHealth   = stats.MaxHealth;
            _maxEnergy   = stats.MaxEnergy;
            _critChance  = stats.CritChance;
            _lifeStealRate         = stats.LifeSteal;
            _lifeStealCapPerSecond = config.LifeStealCapPerSecond;
            _health      = _maxHealth;
            _attack      = config.GetBasicAttack(playerClass);

            // TODO(민성): 회피 기력 비용(DodgeEnergyCost)은 기획에서 0이다. 0이 아니게 바뀌면 기술 자원에서 뺀다
            _dodgeDistance = config.DodgeDistance;
            _dodgeDuration = config.DodgeDuration;
            _dodgeCooldown = config.DodgeCooldown;

            // 보관 깊이가 0이면 쿨다운이 끝난 뒤의 입력만 받는다
            _attackBufferSeconds = (config.InputBufferDepth > 0) ? config.InputBufferSeconds : 0.0;
            _control             = new PlayerControlTracker(config.ControlImmunitySeconds);

            _targetBuffer = new List<ICombatTarget>(Constants.Combat.MAX_COMBAT_TARGETS);
            _navPath      = new NavMeshPath();
            _path         = new PathFollower(Constants.Player.MAX_PATH_CORNERS);
            _moveThrottle = new MoveRequestThrottle(Constants.Player.MAX_MOVE_DESTINATION_DISTANCE,
                Constants.Player.MOVE_REPATH_MIN_INTERVAL_SECONDS, Constants.Player.MOVE_SAME_DESTINATION_EPSILON_SQR);

            _gate = new PlayerInputGate(Constants.Network.PROTOCOL_VERSION, Constants.Network.INPUT_SEND_RATE_MAX,
                Constants.Player.INPUT_RATE_BURST, Constants.Network.INPUT_PAST_TOLERANCE_SECONDS,
                Constants.Network.INPUT_FUTURE_TOLERANCE_SECONDS, Constants.Player.INPUT_VIOLATION_WINDOW_SECONDS,
                Constants.Player.INPUT_VIOLATION_DISCONNECT_THRESHOLD, serverTime);
            _gate.Bind(sessionEpoch, participantId);
            _gate.Restore(input.LastProcessedSequence);

            if (FacingRules.TryGetHorizontalDirection(_transform.forward, out Vector3 initialForward))
            {
                _forward = initialForward;
            }

            _input.SetIdentity(sessionEpoch, participantId);
            _combat.SetPlayerClass(playerClass);
            _combat.SetCombatId(_combatId);
            _combat.SetHealth(_health, _maxHealth);
            _combat.SetLifeState(_lifeState);
            _combat.SetFacingYaw(FacingRules.EncodeYaw(_forward));
            _input.SetMoveSpeed(_moveSpeed);
            PublishMovement(serverTime);
            _input.ServerHandler = this;
        }

        /// <summary> 재접속 유예로 보존할 서버 상태 </summary>
        public ParticipantCombatSnapshot CaptureSnapshot()
        {
            return new ParticipantCombatSnapshot(_health, _lifeState, _deathTime, _nextAttackTime, _gate.LastAcceptedSequence,
                _gate.ViolationCount, _gate.ViolationWindowStart, _nextDodgeTime, _deathPosition, _hasDeathPosition,
                _gate.CaptureRateViolations(), _gate.CaptureTokenBucket(), _lifeStealWindow, _lifeStealWindowHealed,
                _lastReviveTime);
        }

        /// <summary> 유예 안에 돌아온 참가자의 상태를 되돌린다, 만든 직후 스폰 메시지가 나가기 전에 부른다 </summary>
        public void Restore(in ParticipantCombatSnapshot snapshot)
        {
            // 최대 체력이 줄었으면 넘는 몫은 버린다. 회복은 하지 않는다
            _health           = Mathf.Min(snapshot.Health, _maxHealth);
            _lifeState        = snapshot.LifeState;
            _deathTime        = snapshot.DeathTime;
            _deathPosition    = snapshot.DeathPosition;
            _hasDeathPosition = snapshot.HasDeathPosition;
            _nextAttackTime   = snapshot.NextAttackTime;
            _nextDodgeTime    = snapshot.NextDodgeTime;
            _lifeStealWindow       = snapshot.LifeStealWindow;
            _lifeStealWindowHealed = snapshot.LifeStealWindowHealed;
            _lastReviveTime        = snapshot.LastReviveTime;
            _input.SetDodgeReadyAt(_nextDodgeTime);
            if (_lifeState != EParticipantLifeState.Alive)
            {
                _health = 0f;
            }

            _gate.Restore(snapshot.LastSequence);
            _gate.RestoreTokenBucket(snapshot.TokenBucket, NetworkTime.time);
            _gate.RestoreViolations(snapshot.ViolationWindowStart, snapshot.ViolationCount);
            _gate.RestoreRateViolations(snapshot.RateViolations, NetworkTime.time);
            _input.SetLastProcessedSequence(snapshot.LastSequence);
            _combat.SetHealth(_health, _maxHealth);
            _combat.SetLifeState(_lifeState);
        }

        /// <summary> 상태가 서버에서 멈출 때 부른다, 상태가 더 이상 이 개체를 부르지 않게 끊는다 </summary>
        public void Shutdown(bool preserveSkills = false)
        {
            _input.ServerHandler = null;
            _input.SkillServerHandler = null;
            if (preserveSkills)
            {
                _skills?.Detach();
            }
            else
            {
                _skills?.Shutdown();
            }
            _hasPendingMove      = false;
            _hasPendingAttack    = false;
            _isWindingUp         = false;
            _arrowActive         = false;
            _hasPendingSkill     = false;
            _hasPendingDodge     = false;
            _isDodging           = false;
            _path.Stop();
            _moveThrottle.ClearActive();
            _targetBuffer.Clear();
        }

        /// <summary> 연결이 끊겨 개체만 남길 때 부른다, 보관한 입력과 진행 중 이동을 버린다. 체력과 타이머는 그대로 둔다 </summary>
        public void Detach()
        {
            if (_isDetached)
            {
                return;
            }

            _isDetached          = true;
            _input.ServerHandler = null;
            _input.SkillServerHandler = null;
            _hasPendingMove      = false;
            _hasPendingAttack    = false;
            _isWindingUp         = false;
            _hasPendingSkill     = false;
            _hasReviveRequest    = false;
            _hasPendingDodge     = false;
            _path.Stop();
            _moveThrottle.ClearActive();
            SetWaitingSafePoint(false);
        }

        /// <summary> 같은 참가자의 새 연결이 남은 개체를 넘겨받은 뒤 부른다, 회복이나 쿨다운 초기화 없이 입력만 다시 받는다 </summary>
        public void Reattach()
        {
            if (!_isDetached)
            {
                return;
            }

            // 입력 위반 누적은 게이트에 남아 있으므로 새 연결의 첫 입력부터 같은 기준으로 센다. 끊기 표시만 내린다.
            // 내리지 않으면 이전 연결이 위반으로 끊긴 캐릭터는 새 연결의 첫 정상 지연 한 번에도 다시 끊긴다
            _gate.ClearDisconnect();
            _isDetached          = false;
            _isDisconnecting     = false;
            _input.ServerHandler = this;
            if (_skills != null)
            {
                _input.SkillServerHandler = this;
            }

            ReplicateSkills();
        }

        /****************************************
        *            Input Handlers
        ****************************************/

        public void HandleMove(in PlayerMoveInput input)
        {
            if (!TryAcceptHeader(input.Header, out _))
            {
                return;
            }

            uint sequence = input.Header.Sequence;
            if (_lifeState != EParticipantLifeState.Alive)
            {
                _input.SendInputRejected(sequence, ERejectReason.Dead);
                return;
            }

            // 속박과 기절은 경로 이동을 막는다. 보관해 두지 않고 거절해 소유 예측이 서 있는 서버 위치로 맞춘다
            if (PlayerControlRules.BlocksMovement(CurrentControl()))
            {
                _input.SendInputRejected(sequence, ERejectReason.Disabled);
                return;
            }

            Vector3 destination = input.Destination;
            if (!IsFinite(destination))
            {
                _input.SendInputRejected(sequence, ERejectReason.InvalidTarget);
                return;
            }

            EMoveAdmission admission = _moveThrottle.CheckDestination(_transform.position, destination, _path.IsMoving);
            if (admission == EMoveAdmission.TooFar)
            {
                _input.SendInputRejected(sequence, ERejectReason.OutOfRange);
                return;
            }

            // 이동은 기본 공격 준비를 취소한다. 준비 동작이 이동으로 끊기는 공격은 이미 시작한 준비도 버린다
            _hasPendingAttack = false;
            if (_attack.CancelOnMove)
            {
                _isWindingUp = false;
            }

            // 움직이면 콤보 동작은 끝난 것이다. 단계는 이어 가되 동작에 붙은 피격이상 면역은 거둔다
            _comboImmuneUntil = 0.0;

            // 진행 중인 경로와 같은 곳이면 다시 계산하지 않는다. 앞서 보관한 다른 목적지도 이 의도로 덮는다
            if (admission == EMoveAdmission.SameDestination)
            {
                _hasPendingMove = false;
                return;
            }

            _pendingDestination  = destination;
            _pendingMoveSequence = sequence;
            _hasPendingMove      = true;
        }

        public void HandleAttack(in PlayerAttackInput input)
        {
            if (!TryAcceptHeader(input.Header, out double requestTime))
            {
                return;
            }

            uint sequence = input.Header.Sequence;
            if (_lifeState != EParticipantLifeState.Alive)
            {
                _input.SendInputRejected(sequence, ERejectReason.Dead);
                return;
            }

            // 기절은 제자리 공격도 막는다. 속박은 막지 않는다
            if (PlayerControlRules.BlocksAction(CurrentControl()))
            {
                _input.SendInputRejected(sequence, ERejectReason.Disabled);
                return;
            }

            if ((_skills != null) && (_skills.BlocksMovement))
            {
                if (_skills.IsGuardWindow)
                {
                    _skills.Cancel();
                }
                else
                {
                    _input.SendInputRejected(sequence, ERejectReason.WrongPhase);
                    return;
                }
            }

            if ((_skills != null) && (_skills.BlocksMovement))
            {
                _input.SendInputRejected(sequence, ERejectReason.WrongPhase);
                return;
            }

            // 허용 창으로 자른 요청 시각 기준으로 남은 쿨다운이 보관 시간 안이면 받아 두고 쿨다운이 끝나는 틱에 쓴다
            if ((_nextAttackTime - requestTime) > _attackBufferSeconds)
            {
                _input.SendInputRejected(sequence, ERejectReason.Cooldown);
                return;
            }

            // 보관은 한 개다. 새 조준이 앞선 대기 입력을 덮는다
            _pendingAimYaw    = input.AimYaw;
            _hasPendingAttack = true;
        }

        public void HandleDodge(in PlayerDodgeInput input)
        {
            if (!TryAcceptHeader(input.Header, out double requestTime))
            {
                return;
            }

            uint sequence = input.Header.Sequence;
            if (_lifeState != EParticipantLifeState.Alive)
            {
                _input.SendInputRejected(sequence, ERejectReason.Dead);
                return;
            }

            if (PlayerControlRules.BlocksDodge(CurrentControl()))
            {
                _input.SendInputRejected(sequence, ERejectReason.Disabled);
                return;
            }

            if (IsSkillLockingDodge())
            {
                _input.SendInputRejected(sequence, ERejectReason.WrongPhase);
                return;
            }

            // 한 행동당 다음 행동 하나만 보관한다. 회피 중이거나 이미 받아 둔 회피가 있으면 쿨다운으로 어차피 막힌다
            if ((_hasPendingDodge) || ((_nextDodgeTime - requestTime) > _attackBufferSeconds))
            {
                _input.SendInputRejected(sequence, ERejectReason.Cooldown);
                return;
            }

            // 보관 시간에 틱 하나를 더한다. 받은 직후 틱의 서버 시각이 수신 시각보다 한 틱까지 늦을 수 있다
            _pendingDodgeYaw      = input.AimYaw;
            _pendingDodgeSequence = sequence;
            _pendingDodgeDeadline = NetworkTime.time + _attackBufferSeconds + (1.0 / Constants.Network.SERVER_TICK_RATE);
            _hasPendingDodge      = true;

            // 상충 입력은 회피가 먼저다
            _hasPendingAttack = false;
        }

        // 입력 처리기는 틱 사이에 불린다. 종료 시각이 이미 지났으면 틱 전이라도 풀린 것으로 보고 복제와 기술 서버까지 맞춘다
        private EPlayerControlKind CurrentControl()
        {
            RefreshControl(NetworkTime.time);
            return _controlKind;
        }

        // 기술이 실제로 타격 중이면 회피로 끊지 못한다. 준비와 후딜, 반격 방어 창은 끊을 수 있다
        private bool IsSkillLockingDodge()
        {
            return (_skills != null) && (_skills.LocksDodge);
        }

        // 부활은 요청만 받아 두고 실제 승인은 틱의 부활 단계(종료 판정 뒤)가 한다. 같은 틱의 전멸이 부활보다 먼저다
        public void HandleRevive(in PlayerInputHeader header)
        {
            if (!TryAcceptHeader(in header, out _))
            {
                return;
            }

            if (_lifeState == EParticipantLifeState.Alive)
            {
                _input.SendInputRejected(header.Sequence, ERejectReason.WrongPhase);
                return;
            }

            if (_lifeState == EParticipantLifeState.Spectating)
            {
                _input.SendInputRejected(header.Sequence, ERejectReason.NoRevives);
                return;
            }

            // 보관은 한 개다. 새 요청이 앞선 대기 요청의 순번을 덮지만 재평가 시각은 유지한다.
            // 연타로 시각을 0으로 되돌리면 안전 지점 탐색(NavMesh 샘플)을 매 틱 다시 돌리게 된다
            _nextReviveCheckTime = NextReviveCheckOnRequest(_hasReviveRequest, _nextReviveCheckTime);
            _reviveSequence      = header.Sequence;
            _hasReviveRequest    = true;
        }

        /// <summary> 부활 요청을 받을 때의 재평가 시각, 대기 요청이 있으면 그 시각을 유지하고 없으면 바로 평가한다 </summary>
        public static double NextReviveCheckOnRequest(bool hasPendingRequest, double pendingCheckTime)
        {
            return hasPendingRequest ? pendingCheckTime : 0.0;
        }

        /// <summary> 부활 단계가 거절했을 때 부른다, 요청을 지우고 사유를 소유자에게 알린다 </summary>
        public void RejectRevive(ERejectReason reason)
        {
            if (!_hasReviveRequest)
            {
                return;
            }

            _hasReviveRequest = false;
            SetWaitingSafePoint(false);
            _input.SendInputRejected(_reviveSequence, reason);
        }

        /// <summary> 안전 지점이 없어 다음 재평가 시각까지 미룬다, 횟수는 쓰지 않고 소유자에게 대기 중임을 알린다 </summary>
        public void DeferRevive(double nextCheckTime)
        {
            _nextReviveCheckTime = nextCheckTime;
            SetWaitingSafePoint(true);
        }

        /// <summary> 일반전 복귀 지점, 서버가 기록한 사망 위치 그대로다. 기록이 무효면 죽은 자리 근처의 걸을 수 있는 지점으로 대신하고 못 찾으면 거짓 </summary>
        public bool TryFindNormalRevivePoint(out Vector3 point)
        {
            if (ReviveLocationRules.Decide(false, _hasDeathPosition, _deathPosition) == EReviveLocationPlan.DeathPosition)
            {
                point = _deathPosition;
                return true;
            }

            // TODO(민성): 지형 소실과 범위 밖 좌표의 최종 처리는 기획이 아직 정하지 않았다. 지금은 NavMesh 근처 지점으로 대신한다
            Debug.LogWarning($"[Combat] 사망 위치가 무효라 근처 NavMesh 지점으로 부활시킨다, participantId={_participantId}");
            return TrySampleNearbyWalkable(out point);
        }

        /// <summary> 협동 보스 복귀 지점을 찾는다. 지정 지점이 있으면 선택기로 고르고, 없으면 죽은 자리 근처의 걸을 수 있는 지점이다. 못 찾으면 거짓 </summary>
        public bool TryFindRevivePoint(RevivePointSelector selector, double serverTime, out Vector3 point)
        {
            if ((selector != null) && (selector.CandidateCount > 0))
            {
                return selector.TrySelect(_transform.position, serverTime, out point);
            }

            return TrySampleNearbyWalkable(out point);
        }

        private bool TrySampleNearbyWalkable(out Vector3 point)
        {
            if (NavMesh.SamplePosition(_transform.position, out NavMeshHit hit, Constants.Combat.REVIVE_SAFE_POINT_SEARCH_RADIUS,
                NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }

            point = _transform.position;
            return false;
        }

        /// <summary> 부활 단계가 승인한 뒤 부른다, 지점으로 옮기고 체력 비율만큼 되살린다. serverTime은 승인한 서버 시각이다 </summary>
        public void Revive(Vector3 point, float healthRate, double serverTime)
        {
            if (_lifeState == EParticipantLifeState.Alive)
            {
                return;
            }

            _lifeState          = EParticipantLifeState.Alive;
            _lastReviveTime     = serverTime;
            _health             = PlayerLifeRules.ReviveHealth(_maxHealth, healthRate);
            _deathTime          = 0.0;
            _hasDeathPosition   = false;
            _hasReviveRequest   = false;
            _isDodging          = false;
            _transform.position = point;
            SetWaitingSafePoint(false);
            _path.Stop();
            _moveThrottle.ClearActive();
            _combat.SetHealth(_health, _maxHealth);
            _combat.SetLifeState(_lifeState);
        }

        /// <summary> 보스 공용 부활이 바닥난 뒤 사망했을 때 관전으로 옮긴다, 대기 중인 요청은 NoRevives로 거절한다 </summary>
        public void EnterSpectating()
        {
            if (_lifeState != EParticipantLifeState.Dead)
            {
                return;
            }

            RejectRevive(ERejectReason.NoRevives);
            SetWaitingSafePoint(false);
            _lifeState = EParticipantLifeState.Spectating;
            _combat.SetLifeState(_lifeState);
        }

        // 바뀔 때만 복제 상태를 건드린다. 재평가마다 같은 값을 쓰지 않으려는 것이다
        private void SetWaitingSafePoint(bool waiting)
        {
            if (_isWaitingSafePoint == waiting)
            {
                return;
            }

            _isWaitingSafePoint = waiting;
            _input.SetWaitingSafePoint(waiting);
        }

        // talismans는 출정 고정 부적을 합성해 둔 창구다. null이면 원래 기술 수치다
        public void ConfigureSkills(SkillCatalogSO catalog, PlayerConfigSO player, CombatTargetRegistry registry,
            ICombatSkillPhase phase, double now, SkillServer retained = null, ITalismanModifierResolver talismans = null)
        {
            _skillPhase = phase;
            // 보존된 기술 서버는 같은 직업일 때만 넘어온다(CombatServerRuntime이 걸러 낸다)
            if (retained != null)
            {
                _skills = retained;
                _skills.Rebind(this);
            }
            else
            {
                _skills = new SkillServer(this, _playerClass, _participantId, catalog, player, registry, _resolver, _lineOfSight, now,
                    talismans, null, _maxEnergy);
            }
            _skills.SetControl(_controlKind);
            _input.SkillServerHandler = this;
            ReplicateSkills();
        }

        public void HandleSkill(in PlayerSkillInput input)
        {
            if (!TryAcceptHeader(input.Header, out _, true))
            {
                return;
            }
            if ((_skills == null) || (_skillPhase == null))
            {
                _input.SendSkillResult(input.Header.Sequence, CombatIds.NONE, ERejectReason.Disabled);
                return;
            }
            if (_isDodging)
            {
                _input.SendSkillResult(input.Header.Sequence, CombatIds.NONE, ERejectReason.WrongPhase);
                return;
            }
            double now = NetworkTime.time;

            // 기술 서버가 기절이면 시전을, 속박이면 이동 기술을 거절하도록 이 시각의 제어를 먼저 맞춘다
            RefreshControl(now);
            if (!_skills.TryCast(input.Kind, input.Slot, input.Point, input.AimYaw, now,
                _skillPhase.AllowsCombat, out ERejectReason reason))
            {
                double readyAt = _skills.NextActionAt;
                if ((reason == ERejectReason.Cooldown) && (input.Kind == ESkillRequestKind.Regular))
                {
                    readyAt = _skills.GetReadyAt(input.Slot);
                }
                else if ((reason == ERejectReason.Cooldown) && (input.Kind == ESkillRequestKind.Ascension))
                {
                    readyAt = _skills.AscensionReadyAt;
                }
                if ((_skillPhase.AllowsCombat) && (_lifeState == EParticipantLifeState.Alive)
                    && ((reason == ERejectReason.Cooldown) || ((reason == ERejectReason.WrongPhase) && (_skills.Phase != ESkillPhase.Idle)))
                    && (_attackBufferSeconds > 0.0) && (readyAt >= now) && ((readyAt - now) <= _attackBufferSeconds))
                {
                    if (_hasPendingSkill)
                    {
                        _input.SendSkillResult(_pendingSkill.Header.Sequence, CombatIds.NONE, ERejectReason.WrongPhase);
                    }
                    _pendingSkill = input;
                    _pendingSkillDeadline = now + _attackBufferSeconds;
                    _hasPendingSkill = true;
                    _hasPendingAttack = false;
                    return;
                }
                _input.SendSkillResult(input.Header.Sequence, CombatIds.NONE, reason);
                return;
            }
            CommitSkillInput(input.Header.Sequence);
        }

        private void CommitSkillInput(uint sequence)
        {
            if (_hasPendingSkill)
            {
                _input.SendSkillResult(_pendingSkill.Header.Sequence, CombatIds.NONE, ERejectReason.WrongPhase);
                _hasPendingSkill = false;
            }
            _hasPendingAttack = false;
            _isWindingUp = false;
            ResetCombo();
            _hasPendingMove = false;
            _path.Stop();
            _moveThrottle.ClearActive();
            ReplicateSkills();
            _input.SendSkillResult(sequence, _skills.CastInstanceId, ERejectReason.None);
        }

        /// <summary> 물약 요청의 입력 머리를 다른 입력과 같은 게이트로 검증한다, 통과하면 참 </summary>
        public bool TryAcceptPotionInput(in PlayerInputHeader header)
        {
            return TryAcceptHeader(in header, out _);
        }

        // 회복, 최대 체력을 넘는 몫은 버린다. 물약은 저장 확정 뒤 PotionServer가, 생명력 흡수는 ReceiveLifeSteal이 부른다
        public void Heal(float amount)
        {
            if ((_lifeState != EParticipantLifeState.Alive) || (!(amount > 0f)))
            {
                return;
            }

            _health = Mathf.Min(_maxHealth, _health + amount);
            _combat.SetHealth(_health, _maxHealth);
        }

        // 죽었거나 부활을 기다리는 동안 남은 지속 기술이 준 피해는 회복을 만들지 않는다.
        // 구간 누계는 부활해도 지우지 않는다. 지우면 죽었다 살아나는 것으로 초당 상한을 다시 채울 수 있다
        public float ReceiveLifeSteal(float appliedDamage, double serverTime)
        {
            if (_lifeState != EParticipantLifeState.Alive)
            {
                return 0f;
            }

            float healed = LifeStealRules.Grant(appliedDamage, _lifeStealRate, _health, _maxHealth, _lifeStealCapPerSecond,
                serverTime, ref _lifeStealWindow, ref _lifeStealWindowHealed);
            Heal(healed);
            return healed;
        }

        private bool TryAcceptHeader(in PlayerInputHeader header, out double requestTime, bool reportSkill = false)
        {
            requestTime = 0.0;
            if ((_isDisconnecting) || (_isDetached))
            {
                return false;
            }

            EInputVerdict verdict = _gate.Check(in header, NetworkTime.time, out requestTime);
            if (verdict == EInputVerdict.Accepted)
            {
                _input.SetLastProcessedSequence(header.Sequence);
                return true;
            }

            if (reportSkill)
            {
                // 기존 코드값 안에서 헤더 거절을 전달한다. 구체적인 입력 위반은 게이트가 누적한다
                ERejectReason reason = ERejectReason.InvalidTarget;
                switch (verdict)
                {
                    case EInputVerdict.ProtocolMismatch:
                        reason = ERejectReason.VersionMismatch;
                        break;
                    case EInputVerdict.SessionMismatch:
                        reason = ERejectReason.SessionMismatch;
                        break;
                    case EInputVerdict.ParticipantMismatch:
                        reason = ERejectReason.NotEligible;
                        break;
                    case EInputVerdict.RateLimited:
                        reason = ERejectReason.NoCapacity;
                        break;
                    case EInputVerdict.StaleSequence:
                        reason = ERejectReason.StaleRevision;
                        break;
                }
                _input.SendSkillResult(header.Sequence, CombatIds.NONE, reason);
            }
            else if (verdict == EInputVerdict.RateLimited)
            {
                // 지연이 뭉쳐 몰려 온 입력은 정상 클라이언트도 주기 초과로 버려진다. 알리지 않으면 소유 예측은 오지 않는 응답을
                // 기다리다 시간 초과 뒤 서버 위치로 스냅한다. 순번 역행과 시각 오류는 변조 클라이언트만 만들므로 알리지 않는다
                _input.SendInputRejected(header.Sequence, ERejectReason.RateLimited);
            }

            if (_gate.ShouldDisconnect)
            {
                Kick(verdict);
            }

            return false;
        }

        // 연결 종료는 드물어서 이때만 문자열을 만든다
        private void Kick(EInputVerdict verdict)
        {
            _isDisconnecting = true;
            Debug.LogWarning($"[Combat] 입력 위반으로 연결을 끊는다, combatId={_combatId}, verdict={verdict}, violations={_gate.ViolationCount}, rateViolations={_gate.RateViolationCount}");
            _input.DisconnectOwner();
        }

        /****************************************
        *                 Tick
        ****************************************/

        // 경로 계산은 참가자별 최소 간격과 틱 예산을 모두 통과해야 한다. 못 하면 의도를 남겨 두고 다음 틱에 다시 본다
        public void TickMovement(float deltaTime, double serverTime, PathRequestBudget pathBudget)
        {
            if (_lifeState != EParticipantLifeState.Alive)
            {
                return;
            }

            // 틱마다 가장 먼저 제어 종료를 반영한다. 풀린 틱부터 이동과 회피를 다시 받는다
            RefreshControl(serverTime);

            // 회피는 기술 준비와 후딜을 끊으므로 기술의 이동 막기보다 먼저 본다
            if (TickDodge(deltaTime, serverTime))
            {
                return;
            }

            if ((_skills != null) && (_skills.BlocksMovement))
            {
                return;
            }

            // 걸리는 순간 경로와 보관 이동을 지웠다. 여기서는 그 뒤에 새로 들어온 의도가 없는지만 막는다
            if (PlayerControlRules.BlocksMovement(_controlKind))
            {
                return;
            }

            if ((_hasPendingMove) && (_moveThrottle.IsRepathReady(serverTime)) && (pathBudget.TryConsume()))
            {
                _hasPendingMove = false;
                bool hasPath = MovePathRules.TryBeginPath(_transform.position, _pendingDestination, _navPath, _path);
                _moveThrottle.MarkAttempt(serverTime, hasPath, _pendingDestination);
                if (hasPath)
                {
                    // 이 틱에서 바로 한 칸 나아가므로 이동은 틱 구간의 시작에 시작한 것이다
                    _moveSequence  = _pendingMoveSequence;
                    _moveStartTime = serverTime - deltaTime;
                }
                else
                {
                    _input.SendInputRejected(_pendingMoveSequence, ERejectReason.InvalidTarget);
                }
            }

            if (!_path.IsMoving)
            {
                return;
            }

            Vector3 next = _path.Step(_transform.position, _moveSpeed * deltaTime, out Vector3 direction, out bool moved);
            _transform.position = next;

            if ((moved) && (direction != Vector3.zero))
            {
                SetFacing(direction);
            }
        }

        public void TickAttack(double serverTime)
        {
            TickWindup(serverTime);
            TickArrow(serverTime);

            // 회피 중에 받은 공격은 버리지 않고 회피가 끝난 틱에 쓴다
            if ((!_hasPendingAttack) || (_isWindingUp) || (_lifeState != EParticipantLifeState.Alive) || (_isDodging)
                || ((_skills != null) && (_skills.BlocksMovement)) || (PlayerControlRules.BlocksAction(_controlKind)))
            {
                return;
            }

            if (serverTime < _nextAttackTime)
            {
                return;
            }

            // 쉰 시간은 다음 공격이 가능해진 시각부터 잰다. 콤보가 없는 직업은 늘 0단이다
            int step = BasicAttackComboRules.SelectStep(_comboStep, serverTime - _nextAttackTime, _attack.ComboResetSeconds,
                _attack.ComboStepCount);
            float coefficient = _attack.GetDamageCoefficient(step);
            float windup      = _attack.GetWindup(step);

            // 궁수 바람 속성은 단계 주기만 줄이고 준비 시간과 단계 순서는 그대로 둔다
            float intervalScale = (_skills != null) ? _skills.BasicAttackIntervalScale : 1f;

            _comboStep        = step;
            _hasPendingAttack = false;
            _nextAttackTime   = serverTime + (_attack.GetInterval(step) * intervalScale);
            _comboImmuneUntil = _attack.IsHitImmune(step) ? _nextAttackTime : 0.0;
            _path.Stop();
            _moveThrottle.ClearActive();

            Vector3 aim = FacingRules.DecodeYaw(_pendingAimYaw);
            SetFacing(aim);
            _combat.BumpAttackSequence((byte)step);
            if (windup > 0f)
            {
                // 동작은 승인 시점에 시작하고 판정은 준비 시간 뒤에 한다
                _isWindingUp       = true;
                _windupFireAt      = serverTime + windup;
                _windupAim         = aim;
                _windupCoefficient = coefficient;
                return;
            }

            ResolveBasicAttack(aim, serverTime, coefficient);
        }

        /// <summary> 기절, 속박, 밀림을 받지 않는 시각까지 늘린다. 피해는 그대로 받는다. 기술처럼 콤보 밖에서 거는 면역이다 </summary>
        public void GrantHitImmunity(double untilServerTime)
        {
            _grantedImmuneUntil = System.Math.Max(_grantedImmuneUntil, untilServerTime);
        }

        /// <summary> 콤보 동작이나 기술이 건 피격이상 면역 중이면 참 </summary>
        public bool IsHitImmune(double serverTime)
        {
            return (serverTime < _comboImmuneUntil) || (serverTime < _grantedImmuneUntil);
        }

        // 회피, 기술, 사망, 기절로 끊기면 다음 기본 공격은 1단부터다
        private void ResetCombo()
        {
            _comboStep        = BasicAttackComboRules.NO_STEP;
            _comboImmuneUntil = 0.0;
        }

        // 준비 중 회피, 사망, 기술 시전, 이동(취소형 공격)은 이 상태를 지운다. 여기서는 남은 방해만 확인하고 시각이 되면 판정한다
        private void TickWindup(double serverTime)
        {
            if (!_isWindingUp)
            {
                return;
            }

            if ((_lifeState != EParticipantLifeState.Alive) || (_isDodging) || ((_skills != null) && (_skills.BlocksMovement))
                || (PlayerControlRules.BlocksAction(_controlKind)))
            {
                _isWindingUp = false;
                return;
            }

            if (serverTime < _windupFireAt)
            {
                return;
            }

            _isWindingUp = false;
            ResolveBasicAttack(_windupAim, _windupFireAt, _windupCoefficient);
        }

        // 날아가는 기본 공격 화살. 이미 나간 화살은 시전자가 회피하거나 죽어도 사거리까지 이어 간다
        private void TickArrow(double serverTime)
        {
            if (!_arrowActive)
            {
                return;
            }

            float distance = Mathf.Min(_attack.Range - _arrowTravelled,
                _arrowSpeed * (float)System.Math.Max(0.0, serverTime - _arrowLastTime));
            float clear     = _lineOfSight.GetClearDistance(_arrowOrigin, _arrowAim, distance);
            float halfWidth = _attack.ProjectileWidth * Constants.Combat.SKILL_HALF_WIDTH_SCALE;
            bool  wallCut   = clear < distance;

            int   hitIndex = -1;
            float hitAlong = float.MaxValue;
            int   count    = _query.GetLivingInRadius(_arrowOrigin, clear + halfWidth, ECombatFaction.Enemy, true, _targetBuffer);
            for (int i = 0; i < count; ++i)
            {
                Vector3 position = _targetBuffer[i].Position;
                if ((!BasicAttackRules.TryProjectOnRay(_arrowOrigin, _arrowAim, position, clear, halfWidth, out float along))
                    || (!BasicAttackRules.IsWithinHeight(_arrowOrigin, position, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE)))
                {
                    continue;
                }

                // 벽과 대상이 같은 거리면 벽이 먼저다
                if ((wallCut) && (along >= clear))
                {
                    continue;
                }

                if ((along < hitAlong) || ((along == hitAlong) && (_targetBuffer[i].CombatId < _targetBuffer[hitIndex].CombatId)))
                {
                    hitAlong = along;
                    hitIndex = i;
                }
            }

            if (hitIndex >= 0)
            {
                ICombatTarget target  = _targetBuffer[hitIndex];
                var           request = new DamageRequest(_combatId, target.CombatId, _arrowCastId, _arrowActionId, _arrowDamage,
                    EDamageFlags.None, _arrowOrigin, Constants.Combat.BASIC_ATTACK_SOURCE_ID);
                DamageResult result = _resolver.Apply(in request);
                _arrowActive = false;

                // 속성 스택과 화상, 둔화는 화살이 나갈 때의 속성으로 판정한다. 날아가는 중에 속성을 바꿔도 이 화살은 그대로다
                _skills?.OnArrowHit(_arrowElement, target, in result, serverTime);
                if (result.CountsAsHit)
                {
                    _combat.BumpArrowHit(target.Position, _arrowElement);
                }
            }
            else
            {
                _arrowOrigin    += _arrowAim * clear;
                _arrowTravelled += clear;
                _arrowLastTime   = serverTime;
                if ((wallCut) || (_arrowTravelled >= _attack.Range))
                {
                    _arrowActive = false;
                }
            }

            // 반환된 참조는 이번 틱에만 쓴다
            _targetBuffer.Clear();
        }

        // 이번 틱의 위치를 회피가 정했으면 참이다. 경로 이동과 기술의 이동 막기는 이 틱에 보지 않는다
        private bool TickDodge(float deltaTime, double serverTime)
        {
            if (_isDodging)
            {
                double elapsed = serverTime - _dodgeStartTime;
                _transform.position = DodgeRules.GetPosition(_dodgeStart, _dodgeEnd, elapsed, _dodgeDuration);
                if (elapsed >= _dodgeDuration)
                {
                    _isDodging = false;
                }

                return true;
            }

            if (!_hasPendingDodge)
            {
                return false;
            }

            // 보관 중에 제어가 걸리면 걸리는 순간 지웠으므로 여기는 닿지 않는 안전 장치다
            if (PlayerControlRules.BlocksDodge(_controlKind))
            {
                _hasPendingDodge = false;
                _input.SendInputRejected(_pendingDodgeSequence, ERejectReason.Disabled);
                return false;
            }

            if (serverTime > _pendingDodgeDeadline)
            {
                _hasPendingDodge = false;
                _input.SendInputRejected(_pendingDodgeSequence, IsSkillLockingDodge() ? ERejectReason.WrongPhase : ERejectReason.Cooldown);
                return false;
            }

            // 보관 시간 안에서 쿨다운이 끝나거나 기술 타격이 끝나기를 기다린다
            if ((serverTime < _nextDodgeTime) || (IsSkillLockingDodge()))
            {
                return false;
            }

            _hasPendingDodge = false;
            return BeginDodge(deltaTime, serverTime);
        }

        private bool BeginDodge(float deltaTime, double serverTime)
        {
            Vector3 direction = FacingRules.DecodeYaw(_pendingDodgeYaw);
            if (!DodgeRules.TryGetWallClampedEnd(_transform.position, direction, _dodgeDistance, out Vector3 start, out Vector3 wallEnd))
            {
                _input.SendInputRejected(_pendingDodgeSequence, ERejectReason.InvalidTarget);
                return false;
            }

            Vector3 flat         = wallEnd - start;
            flat.y               = 0f;
            float   wallDistance = flat.magnitude;
            float   clear        = ClampByEnemyBodies(start, direction, wallDistance);
            Vector3 end          = (wallDistance > 0f) ? Vector3.Lerp(start, wallEnd, clear / wallDistance) : start;

            // 승인한 뒤에만 기술을 끊는다. 확정한 기력과 쿨다운은 돌려주지 않는다
            if ((_skills != null) && (_skills.BlocksMovement))
            {
                CancelSkillForDodge();
            }

            _combat.BumpDodgeSequence();

            _hasPendingMove   = false;
            _hasPendingAttack = false;
            _isWindingUp      = false;
            ResetCombo();
            _path.Stop();
            _moveThrottle.ClearActive();

            // 이 틱에서 바로 한 칸 나아가므로 틱 구간의 시작에 시작한 것이다. 쿨다운도 그때부터 센다
            _isDodging      = true;
            _dodgeStart     = start;
            _dodgeEnd       = end;
            _dodgeStartTime = serverTime - deltaTime;
            _nextDodgeTime  = _dodgeStartTime + _dodgeCooldown;
            _input.SetDodgeReadyAt(_nextDodgeTime);

            // 예측은 회피를 이동 순번 하나로 보고 같은 기준으로 맞춘다
            _moveSequence  = _pendingDodgeSequence;
            _moveStartTime = _dodgeStartTime;

            SetFacing(direction);
            _transform.position = DodgeRules.GetPosition(start, end, deltaTime, _dodgeDuration);
            if (deltaTime >= _dodgeDuration)
            {
                _isDodging = false;
            }

            return true;
        }

        // 적 몸체 직전에서 멈춘다. 아군과 신령 몸체는 막지 않는다
        private float ClampByEnemyBodies(Vector3 start, Vector3 direction, float distance)
        {
            float searchRadius = distance + Constants.Combat.PLAYER_BODY_RADIUS + Constants.Combat.MAX_BODY_RADIUS;
            int   count        = _query.GetLivingInRadius(start, searchRadius, ECombatFaction.Enemy, true, _targetBuffer);
            for (int i = 0; i < count; ++i)
            {
                ICombatTarget target = _targetBuffer[i];
                if (!BasicAttackRules.IsWithinHeight(start, target.Position, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE))
                {
                    continue;
                }

                distance = DodgeRules.GetClearDistance(start, direction, distance, target.Position,
                    Constants.Combat.PLAYER_BODY_RADIUS + target.BodyRadius);
            }

            // 반환된 참조는 이번 틱에만 쓴다
            _targetBuffer.Clear();
            return distance;
        }

        /// <summary> 틱 끝 위치를 소유 클라이언트 예측의 기준으로 보낸다, 이동, 공격, 기술 처리가 모두 끝난 뒤 부른다 </summary>
        public void PublishMovement(double serverTime)
        {
            bool    isMoving = (_path.IsMoving) || (_isDodging);
            Vector3 position = _transform.position;

            // 관찰자용 공개 속도는 Transform 차분이 아니라 서버가 직접 공개한다(NETWORK_AUTHORITY 5.5 함정5).
            // 아래 조기 반환과 무관하게 매번 최신 값으로 둔다
            float locomotionSpeed = _isDodging ? (_dodgeDistance / _dodgeDuration) : ((_path.IsMoving) ? _moveSpeed : 0f);
            _combat.SetLocomotionSpeed(locomotionSpeed);

            // 멈춘 채 그대로면 경과 시간도 멈춘 시점 값으로 둔다. 매 틱 바꾸면 서 있는 동안에도 계속 전송된다
            if ((_publishedMove.IsValid) && (!isMoving) && (!_publishedMove.IsMoving)
                && (_publishedMove.MoveSequence == _moveSequence) && (_publishedMove.Position == position))
            {
                return;
            }

            float elapsed = (_moveSequence != 0) ? (float)(serverTime - _moveStartTime) : 0f;
            _publishedMove = new PlayerMoveAuthority(position, _moveSequence, elapsed, isMoving);
            _input.SetMoveAuthority(in _publishedMove);
        }

        // 맞힐 대상이 없어도 휘두르는 모습은 보여야 하므로 호출 전에 동작 순번을 올려 둔다
        private void ResolveBasicAttack(Vector3 aim, double serverTime, float coefficient)
        {
            Vector3 origin   = _transform.position;
            float   range    = _attack.Range;
            bool    isRanged = _attack.ArcAngle <= 0f;

            if ((isRanged) && (_attack.ProjectileSpeed > 0f))
            {
                _arrowActive    = true;
                _arrowOrigin    = origin;
                _arrowAim       = aim;
                _arrowTravelled = 0f;
                _arrowLastTime  = serverTime;
                _arrowCastId    = _resolver.IssueInstanceId();
                _arrowActionId  = _resolver.IssueInstanceId();
                _arrowDamage    = _attackPower * coefficient;
                _arrowElement   = (_skills != null) ? _skills.ArcherElement : EArcherElement.None;
                _arrowSpeed     = _attack.ProjectileSpeed * ((_skills != null) ? _skills.BasicProjectileSpeedScale : 1f);
                return;
            }

            float   radius   = isRanged ? (range + Constants.Combat.BASIC_ATTACK_RAY_HALF_WIDTH) : range;

            int count = _query.GetLivingInRadius(origin, radius, ECombatFaction.Enemy, true, _targetBuffer);
            if (count == 0)
            {
                return;
            }

            // 치명타는 공통 DamageResolver가 대상별로 판정한다
            // TODO(민성): 시작 인원 피해 배율의 적용 위치가 기획에서 정해지면 여기서 곱한다
            float rawDamage = _attackPower * coefficient;
            int   castId    = _resolver.IssueInstanceId();
            int   actionId  = _resolver.IssueInstanceId();

            if (isRanged)
            {
                ApplyFirstOnRay(origin, aim, range, rawDamage, castId, actionId);
            }
            else
            {
                ApplyArc(origin, aim, range, rawDamage, castId, actionId);
            }

            // 반환된 참조는 이번 틱에만 쓴다
            _targetBuffer.Clear();
        }

        private void ApplyArc(Vector3 origin, Vector3 aim, float range, float rawDamage, int castId, int actionId)
        {
            for (int i = 0; i < _targetBuffer.Count; ++i)
            {
                ICombatTarget target   = _targetBuffer[i];
                Vector3       position = target.Position;
                if (!BasicAttackRules.IsInArc(origin, aim, position, range, _attack.ArcAngle))
                {
                    continue;
                }

                if (!BasicAttackRules.IsWithinHeight(origin, position, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE))
                {
                    continue;
                }

                // 부채꼴 안 대상 수만큼 선을 긋는다. 지형만 보므로 앞선 적이 뒤의 적을 가리지 않는다
                if (_lineOfSight.IsBlocked(origin, position))
                {
                    continue;
                }

                var request = new DamageRequest(_combatId, target.CombatId, castId, actionId, rawDamage,
                    EDamageFlags.None, origin, Constants.Combat.BASIC_ATTACK_SOURCE_ID);
                _resolver.Apply(in request);
            }
        }

        // 원거리 기본 공격은 첫 적에서 사라지고 지형에 막힌 거리 너머는 맞히지 않는다. 조준선 하나만 쏘아 막힌 거리를 잰다
        // TODO(민성): 투사체 속도가 정해지면 투사체로 바꾼다
        private void ApplyFirstOnRay(Vector3 origin, Vector3 aim, float range, float rawDamage, int castId, int actionId)
        {
            int   firstIndex    = -1;
            float firstDistance = float.MaxValue;
            float clearRange    = _lineOfSight.GetClearDistance(origin, aim, range);

            for (int i = 0; i < _targetBuffer.Count; ++i)
            {
                Vector3 position = _targetBuffer[i].Position;
                if (!BasicAttackRules.TryProjectOnRay(origin, aim, position, clearRange,
                    Constants.Combat.BASIC_ATTACK_RAY_HALF_WIDTH, out float along))
                {
                    continue;
                }

                if (!BasicAttackRules.IsWithinHeight(origin, position, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE))
                {
                    continue;
                }

                if (along < firstDistance)
                {
                    firstDistance = along;
                    firstIndex    = i;
                }
            }

            if (firstIndex < 0)
            {
                return;
            }

            var request = new DamageRequest(_combatId, _targetBuffer[firstIndex].CombatId, castId, actionId, rawDamage,
                EDamageFlags.None, origin, Constants.Combat.BASIC_ATTACK_SOURCE_ID);
            _resolver.Apply(in request);
        }

        private void SetFacing(Vector3 direction)
        {
            _forward = direction;
            _combat.SetFacingYaw(FacingRules.EncodeYaw(direction));
        }

        public void SetSkillPosition(Vector3 position) { _transform.position = position; }
        public void SetSkillFacing(Vector3 direction) { SetFacing(direction); }

        /// <summary> 회피가 서버에서 승인된 뒤에만 호출한다, 취소만 보내 후딜을 공짜로 지우지 않는다 </summary>
        public void CancelSkillForDodge()
        {
            if (_hasPendingSkill)
            {
                _input.SendSkillResult(_pendingSkill.Header.Sequence, CombatIds.NONE, ERejectReason.WrongPhase);
                _hasPendingSkill = false;
            }
            _skills?.Cancel();
            ReplicateSkills();
        }

        public void StopSkillCombat()
        {
            if (_hasPendingSkill)
            {
                _input.SendSkillResult(_pendingSkill.Header.Sequence, CombatIds.NONE, ERejectReason.WrongPhase);
            }
            _hasPendingSkill = false;
            _skills?.Shutdown();
            ReplicateSkills();
        }

        public void TickSkills(float deltaTime, double serverTime, PathRequestBudget budget)
        {
            _skills?.Tick(serverTime, deltaTime, budget);
            if ((!_hasPendingSkill) || (_skills == null))
            {
                return;
            }
            if ((_skills.Phase != ESkillPhase.Idle) && (serverTime <= _pendingSkillDeadline))
            {
                return;
            }
            _hasPendingSkill = false;
            if (serverTime > _pendingSkillDeadline)
            {
                _input.SendSkillResult(_pendingSkill.Header.Sequence, CombatIds.NONE, ERejectReason.TooEarly);
                return;
            }
            if (!_skills.TryCast(_pendingSkill.Kind, _pendingSkill.Slot, _pendingSkill.Point, _pendingSkill.AimYaw, serverTime,
                _skillPhase.AllowsCombat, out ERejectReason reason))
            {
                // 쿨다운을 기다리는 의도는 보관 창 동안 유지한다. 실제 커밋 시 전부 다시 검증한다
                if ((reason == ERejectReason.Cooldown) && (serverTime <= _pendingSkillDeadline))
                {
                    _hasPendingSkill = true;
                    return;
                }
                _input.SendSkillResult(_pendingSkill.Header.Sequence, CombatIds.NONE, reason);
                return;
            }
            CommitSkillInput(_pendingSkill.Header.Sequence);
        }

        public void ReplicateSkills()
        {
            if (_skills == null)
            {
                return;
            }
            _combat.SetSkillResources(_skills.Energy, _skills.MaxEnergy, _skills.Resonance);
            _combat.SetClassStacks((byte)_skills.ClassStacks);
            _combat.SetArcherElement(_skills.ArcherElement);
            _combat.SetSkillExecution(_skills.Phase, _skills.CastInstanceId, _skills.ActionInstanceId, (byte)_skills.Slot,
                _skills.Kind, _skills.Origin, _skills.Aim, _skills.Point, _skills.ResolveAt);
            _input.SetSkillCooldowns(_skills.GetReadyAt(Constants.Combat.SKILL_SLOT_Q), _skills.GetReadyAt(Constants.Combat.SKILL_SLOT_W),
                _skills.GetReadyAt(Constants.Combat.SKILL_SLOT_E), _skills.GetReadyAt(Constants.Combat.SKILL_SLOT_R), _skills.AscensionReadyAt);
        }

        /****************************************
        *                Damage
        ****************************************/

        public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
        {
            if (_lifeState != EParticipantLifeState.Alive)
            {
                return;
            }

            if (!isLethal)
            {
                _health = Mathf.Max(0f, _health - appliedAmount);
                _combat.SetHealth(_health, _maxHealth);
                return;
            }

            ConfirmDeath(serverTime);
        }

        // 추락 같은 서버 사망 확정이다. 방어, 반격, 무적이 개입하지 않아야 하므로 피해 판정기를 거치지 않고 피해 통지, 미터, 흡혈도 만들지 않는다.
        // 서버 보호 중에도 죽인다. 보호가 막을지는 부르는 쪽이 정한다
        /// <summary> 판정기를 거치지 않고 사망을 확정한다, 살아 있을 때만 참이다 </summary>
        public bool KillByServer(double serverTime)
        {
            if (_lifeState != EParticipantLifeState.Alive)
            {
                return false;
            }

            ConfirmDeath(serverTime);
            return true;
        }

        /// <summary> 서버 보호를 켜거나 끈다, 켜진 동안 피해 판정기가 이 참가자를 무적으로 본다 </summary>
        public void SetServerProtection(bool isProtected)
        {
            _isServerProtected = isProtected;
        }

        // 치명타(ApplyDamage)와 서버 사망 확정(KillByServer)이 같이 쓰는 사망 정리다. 살아 있는지는 부르는 쪽이 확인한다
        private void ConfirmDeath(double serverTime)
        {
            _health = 0f;
            _combat.SetHealth(_health, _maxHealth);

            // 보스 공용 부활이 바닥났으면 틱의 부활 단계가 관전으로 옮긴다
            _lifeState        = EParticipantLifeState.Dead;
            _deathTime        = serverTime;

            // 사망 순간의 서버 좌표, 일반전 부활이 이 자리를 그대로 쓴다. 사망 뒤 기술 이동이 위치를 옮겨도 바뀌지 않는다
            _deathPosition    = _transform.position;
            _hasDeathPosition = true;
            _hasPendingMove   = false;
            _hasPendingAttack = false;
            _isWindingUp      = false;
            ResetCombo();
            _grantedImmuneUntil = 0.0;
            if (_hasPendingSkill)
            {
                _input.SendSkillResult(_pendingSkill.Header.Sequence, CombatIds.NONE, ERejectReason.Dead);
            }
            _hasPendingSkill  = false;
            if (_hasPendingDodge)
            {
                _input.SendInputRejected(_pendingDodgeSequence, ERejectReason.Dead);
            }
            _hasPendingDodge  = false;
            _isDodging        = false;
            _path.Stop();
            _moveThrottle.ClearActive();
            _combat.SetLifeState(_lifeState);

            // 죽으면 제어와 면역을 함께 지운다. 부활한 뒤에는 새 제어를 바로 받는다
            _control.Clear();
            _controlKind = EPlayerControlKind.None;
            _combat.SetControl(EPlayerControlKind.None, 0.0);
            _skills?.SetControl(EPlayerControlKind.None);
            _skills?.OnOwnerDeath();
        }

        /****************************************
        *             Displacement
        ****************************************/

        // 보스 틱은 이동, 기술 틱 뒤이고 PublishMovement 앞이라 이 틱의 위치를 다시 쓰는 계층이 없고, 틱 끝 위치가 바뀌었으므로
        // 서 있던 대상도 권위 값이 다시 나간다. 그래서 시각은 여기서 쓰지 않는다
        public void Displace(Vector3 position, double serverTime)
        {
            if ((!CanBeDisplaced) || (!IsFinite(position)))
            {
                return;
            }

            // 밀린 자리에서 남은 모서리로 곧장 가면 경로가 벽을 스칠 수 있어 순간 이동처럼 경로를 버린다.
            // 소유 예측은 서버가 멈춘 것을 권위 값만으로는 알지 못해 옛 경로를 끝까지 가므로 그 이동의 거절을 보내 멈추게 한다.
            // 아직 경로를 만들지 않은 보관 이동은 다음 틱에 밀린 자리에서 시작하면 되므로 남긴다
            if (_path.IsMoving)
            {
                _path.Stop();
                _moveThrottle.ClearActive();
                _input.SendInputRejected(_moveSequence, ERejectReason.Disabled);
            }

            _transform.position = position;
        }

        /****************************************
        *                Control
        ****************************************/

        /// <summary> 기절이나 속박을 건다, 피해를 적용한 뒤 적 두뇌가 부른다. 걸린 순간 몸이 움직이는 행동과 기절이면 공격과 시전도 끊는다 </summary>
        public EPlayerControlResult ApplyControl(EPlayerControlKind kind, float seconds, double serverTime)
        {
            if (_lifeState != EParticipantLifeState.Alive)
            {
                return EPlayerControlResult.NotAlive;
            }

            // 콤보 동작이나 기술의 피격이상 면역 중에는 걸지 않는다. 직후 제어 면역과 같이 피해는 이미 받았다
            if (IsHitImmune(serverTime))
            {
                return EPlayerControlResult.Immune;
            }

            EPlayerControlResult result = _control.TryApply(kind, seconds, serverTime);
            if ((result != EPlayerControlResult.Applied) && (result != EPlayerControlResult.Extended))
            {
                return result;
            }

            if (result == EPlayerControlResult.Applied)
            {
                BeginControl(kind);
            }

            RefreshControl(serverTime);

            // 종류가 그대로여도 종료 시각만 늘 수 있어 표시를 다시 쓴다
            _combat.SetControl(_controlKind, _control.GetEndsAt(_controlKind));
            return result;
        }

        // 새로 걸린 종류가 끊는 것. 속박과 기절 모두 이동, 회피, 보관 이동을 끊고, 기절은 공격 준비와 보관 입력까지 끊는다.
        // 기술은 RefreshControl이 기술 서버를 통해 끊는다. 비용과 쿨다운은 돌려주지 않는다
        private void BeginControl(EPlayerControlKind kind)
        {
            _hasPendingMove = false;
            _isDodging      = false;
            _path.Stop();
            _moveThrottle.ClearActive();
            if (_hasPendingDodge)
            {
                _input.SendInputRejected(_pendingDodgeSequence, ERejectReason.Disabled);
                _hasPendingDodge = false;
            }

            if (kind != EPlayerControlKind.Stun)
            {
                return;
            }

            bool wasWindingUp = _isWindingUp;
            _isWindingUp      = false;
            _hasPendingAttack = false;
            ResetCombo();
            if (_hasPendingSkill)
            {
                _input.SendSkillResult(_pendingSkill.Header.Sequence, CombatIds.NONE, ERejectReason.Disabled);
                _hasPendingSkill = false;
            }

            if (wasWindingUp)
            {
                _combat.BumpActionCancelSequence();
            }
        }

        // 지금 시각의 제어로 복제 상태와 기술 서버를 맞춘다, 바뀔 때만 일한다. 기절이 새로 걸려 기술이 끊겼으면 표현 취소 신호를 올린다
        private void RefreshControl(double now)
        {
            EPlayerControlKind kind = _control.Evaluate(now);
            if (kind == _controlKind)
            {
                return;
            }

            _controlKind = kind;
            _combat.SetControl(kind, _control.GetEndsAt(kind));
            if ((_skills != null) && (_skills.SetControl(kind)))
            {
                _combat.BumpActionCancelSequence();
                ReplicateSkills();
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return (!float.IsNaN(value.x)) && (!float.IsInfinity(value.x))
                && (!float.IsNaN(value.y)) && (!float.IsInfinity(value.y))
                && (!float.IsNaN(value.z)) && (!float.IsInfinity(value.z));
        }
    }
}
