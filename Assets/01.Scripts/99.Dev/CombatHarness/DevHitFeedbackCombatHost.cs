// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Dev
{
    /// <summary> 네트워크 없이 실제 서버 판정(PlayerServer, SkillServer, DamageResolver)을 한 프로세스에서 돌려 타격감 시험 플레이어에 직업 기술과 기본 공격을 붙인다 </summary>
    [RequireComponent(typeof(DevHitFeedbackPlayer))]
    public sealed class DevHitFeedbackCombatHost : MonoBehaviour, ICombatSkillPhase
    {
        // 전송 계층만 빠진다. Mirror의 서버 활성 표식을 켜서 [Server] 메서드와 NetworkTime이 동작하게 하고, 연결이 없는 개체의 TargetRpc는
        // 보내지 않고 돌아오도록 이미 막혀 있다. 판정 결과는 PlayerCombatState에 그대로 써서 실제 클라이언트 표현(모션, 효과)이 같은 경로로 읽는다.
        // 이동은 시험 플레이어의 지면 걷기를 그대로 쓰고, 기술이 만드는 위치 이동(돌진, 순간 이동, 후퇴)만 서버 판정이 정한다.
        // 이 컴포넌트는 에디터 전용 어셈블리에 있어 빌드에 들어가지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const int    PARTICIPANT_ID       = 1;
        private const int    SESSION_EPOCH        = 1;
        private const int    TARGET_CAPACITY      = 256;
        private const float  MONSTER_SCAN_SECONDS = 0.25f;
        private const float  ATTACK_REPEAT_SECONDS = 0.1f;   // 공격 버튼을 누르고 있을 때 입력을 다시 보내는 주기
        private const float  FREEZE_FRAMES_PER_SECOND = 60f;
        private const float  HEAVY_DAMAGE_RATIO   = 2f;      // 공격력의 이 배수 이상 피해는 강타 연출을 쓴다
        private const int    HEAVY_SHOT_STEP      = 2;       // 강공격 효과를 쓰는 콤보 단계(0부터)
        private const float  HEAVY_SHOT_SECONDS   = 1f;      // 강공격 화살이 맞기까지 기다려 주는 시간
        private const float  ATTACK_LOCK_MARGIN   = 0.05f;   // 초, 준비 시간이 끝난 뒤에도 이만큼 더 선 채로 둔다
        private const float  MIN_ASSIST_DISTANCE  = 0.01f;   // m, 이보다 가까운 몬스터는 방향을 정할 수 없어 보정에서 뺀다
        private const float  PLAYER_DAMAGE_RATIO  = 1f;     // 몬스터에게 한 번 맞을 때 최대 체력에서 깎는 몫을 맞을 수 있는 횟수로 나눈다

        [Header("직업")]
        [SerializeField] private EPlayerClass _playerClass = EPlayerClass.Mageknight;

        [Header("능력치")]
        [SerializeField, Min(1f)] private float _maxHealth = 100f;

        [Header("시험 편의")]
        [SerializeField] private bool _invincible = true;
        [SerializeField] private bool _noCooldown = true;

        [Header("데이터 덮어쓰기 (이 시험 씬의 메모리에서만, 0이면 그대로)")]
        // 릴리즈 데이터 파일은 바꾸지 않고 실행 중에만 값을 바꿨다가 종료할 때 되돌린다
        [SerializeField, Min(0f)] private float _skillWRange = 0f;

        [Header("원거리 기본 공격 (이 시험 씬에서만)")]
        // 커서 방향에서 이 각도 안에 있는 가장 가까운 각도의 몬스터 중심으로 기본 공격 방향을 당긴다. 서버 코드는 그대로고 보내는 방향만 바꾼다
        [SerializeField] private bool _aimAssist = true;
        [SerializeField, Range(0f, 30f)] private float _aimAssistHalfAngle = 8f;

        [Header("궁수 콤보 시험 (이 시험 씬에서만)")]
        // 켜면 2타를 연발(1타 모션을 빠르게 반복)로 바꾸고 3타는 강공격 효과의 단발로 쓴다. 궁수일 때만 쓴다
        [SerializeField] private bool _archerComboTrial = true;
        [SerializeField, Range(2, 8)] private int _burstShots = 3;
        [SerializeField, Min(0.02f)] private float _burstGap = 0.12f;
        [SerializeField, Min(1f)] private float _burstAnimSpeedScale = 2.5f;

        [Header("마검사 콤보 시험 (이 시험 씬에서만)")]
        // 켜면 기본 공격을 1타 단발, 2타 3연타, 3타 5연타, 4타 강타 단발의 4단 콤보로 바꾼다. 마검사일 때만 쓴다. 데이터 파일은 바꾸지 않는다
        [SerializeField] private bool _mageknightComboTrial = true;

        [Header("W 검파 타격감")]
        // 검파 타격을 강한 연출(흔들림, 경직, 불똥)로 낼지. 스택이 적은 약한 피해도 화면 흔들림이 보이게 한다
        [SerializeField] private bool _skillWHeavyFeedback = true;
        // 맞을 때마다 날아가는 검파 본체를 이 프레임 수만큼 세운다(플레이어는 굳지 않는다)
        [SerializeField, Min(0)] private int _skillWBodyFreezeFrames  = 4;
        // 한 번 쏜 검파가 맞아서 서는 시간의 합 상한. 길어지면 본체가 실제 판정보다 한참 뒤처진다
        [SerializeField, Min(0)] private int _skillWFreezeBudgetFrames = 12;

        [Header("카메라")]
        // 마검사 R 타격의 화면 흔들림 세기 배율(1이 기본)
        [SerializeField, Min(0f)] private float _skillRShakeScale = 1.2f;
        // 마검사 R 섬광 일섬 시전 동안 화면을 당기는 비율(시야각 기준), 0이면 쓰지 않는다
        [SerializeField, Range(0f, 0.6f)] private float _skillRZoomAmount     = 0.2f;
        // 당기고 풀 때의 감쇠 시간. 클수록 부드럽고 느리게 따라간다
        [SerializeField, Min(0.01f)]      private float _skillRZoomInSmooth   = 0.12f;
        [SerializeField, Min(0.01f)]      private float _skillRZoomOutSmooth  = 0.2f;

        [Header("타격감")]
        // 장판, 화상처럼 시전이 끝난 뒤 틱마다 들어오는 지속 피해에도 타격감(경직, 흔들림, 불똥)을 낼지. 끄면 피해는 들어가지만 연출은 첫 타격에만 나온다
        [SerializeField] private bool _feedbackOnTickDamage = true;

        private DevHitFeedbackPlayer _player;
        private PlayerInputState     _input;
        private PlayerCombatState    _combat;
        private PlayerServer         _server;
        private CombatTargetRegistry _registry;
        private DamageResolver       _resolver;
        private PathRequestBudget    _budget;
        private CharacterStats       _stats;
        private CharacterController  _controller;

        private readonly Dictionary<int, DevMonsterCombatTarget>               _targets    = new Dictionary<int, DevMonsterCombatTarget>();
        private readonly Dictionary<DevHitFeedbackMonster, DevMonsterCombatTarget> _byMonster = new Dictionary<DevHitFeedbackMonster, DevMonsterCombatTarget>();
        private readonly List<DevHitFeedbackMonster> _staleMonsters = new List<DevHitFeedbackMonster>();

        private readonly HashSet<string> _tickSources = new HashSet<string>();
        private SourceTrackingResolver   _tracker;

        private MageknightVfxView _waveView = null;
        private int    _waveFreezeCastId = CombatIds.NONE;
        private int    _waveFreezeSpentFrames = 0;
        private readonly List<System.Action> _restoreOverrides = new List<System.Action>();
        private DevArcherComboTrial _comboTrial = null;
        private DevMageknightComboTrial _mageComboTrial = null;
        private byte  _lastAttackSequence = 0;
        private float _attackLockUntil    = 0f;
        private float _heavyShotUntil     = 0f;
        private FieldInfo _readyAtField = null;
        private FieldInfo _energyField  = null;

        private int    _lastCastId     = CombatIds.NONE;
        private uint   _sequence       = 0;
        private float  _nextScanTime   = 0f;
        private float  _nextAttackSend = 0f;
        private ushort _lastYaw        = 0;
        private bool   _hasYaw         = false;
        private bool   _isReady        = false;

        public bool AllowsCombat => true;

        public PlayerServer Server => _server;

        public EPlayerClass PlayerClass => _playerClass;

        /// <summary> 이번 프레임에 보정 조준이 잡은 몬스터의 중심점, 잡지 못했으면 거짓 </summary>
        public bool HasAssistTarget { get; private set; }

        public Vector3 AssistPoint { get; private set; }

        /// <summary> 보정 후 기본 공격이 나가는 수평 방향, 보정이 없으면 커서 방향이다 </summary>
        public Vector3 AttackAim { get; private set; } = Vector3.forward;

        public float AimAssistHalfAngle => _aimAssist ? _aimAssistHalfAngle : 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _player     = GetComponent<DevHitFeedbackPlayer>();
            _controller = GetComponent<CharacterController>();
            _input      = GetComponent<PlayerInputState>();
            _combat     = GetComponent<PlayerCombatState>();
            _waveView   = GetComponent<MageknightVfxView>();

            if ((_input == null) || (_combat == null))
            {
                Debug.LogError("[DevCombatHost] PlayerInputState와 PlayerCombatState가 씬 플레이어에 필요하다", this);
                enabled = false;
                return;
            }

            if (!NetworkServer.active)
            {
                Debug.LogError("[DevCombatHost] DevLocalNetworkServer가 씬에 필요하다", this);
                enabled = false;
                return;
            }

            GameDBBootstrap.EnsureInitialized();
            ApplyDataOverrides();

            PlayerConfigSO config = GameDB.Player;
            _registry = new CombatTargetRegistry(TARGET_CAPACITY);
            var ledger = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            _resolver = new DamageResolver(_registry, ledger, config.DefenseReductionConstant, config.DamageReductionCap,
                config.CritDamageMultiplier, null, null, config.PlayerDefenseReductionConstant);
            _resolver.DamageApplied += OnDamageApplied;

            _tracker = new SourceTrackingResolver(new SkillDamageResolver(_resolver, _registry, ledger));
            IDamageResolver skillResolver = _tracker;
            CollectTickSources();
            var lineOfSight   = new CombatLineOfSight(0, Constants.Combat.LINE_OF_SIGHT_HEIGHT);
            _budget           = new PathRequestBudget(Constants.Player.PATH_REQUESTS_PER_TICK);
            _stats            = new CharacterStats(config.BaseAttack, _maxHealth, 0f, 0f, config.BaseMaxEnergy, config.MoveSpeed);

            double now = NetworkTime.time;
            _server = new PlayerServer(_input, _combat, _playerClass, _registry.IssueCombatId(), PARTICIPANT_ID, SESSION_EPOCH, config,
                in _stats, skillResolver, _registry, lineOfSight, now);
            _registry.Register(_server);
            _server.ConfigureSkills(GameDB.Skills, config, _registry, this, now);

            // 기술 서버의 내비는 내비 메시를 읽는다. 이 씬에는 없으므로 물리 충돌체 기반 대역으로 바꾼다
            typeof(SkillServer).GetField("_navigation", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_server.Skills, new DevSkillNavigation(transform));

            // HUD 바인더가 소유 플레이어로 인식하게 한다. 소유 표식은 내부 설정자라 반사로 켠다
            if (TryGetComponent(out NetworkIdentity identity))
            {
                typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isOwned), BindingFlags.Public | BindingFlags.Instance)
                    .GetSetMethod(true).Invoke(identity, new object[] { true });
            }

            _server.SetServerProtection(_invincible);
            _player.SetInvincible(_invincible);
            _player.SetAttackInputEnabled(false);
            _player.Damaged += OnPlayerDamaged;
            SetUpComboTrial(GetComponentInChildren<ArcherVfxView>(true));
            if ((_mageknightComboTrial) && (_playerClass == EPlayerClass.Mageknight))
            {
                // 추가 타의 효과는 원본 효과 뷰가 쓰는 기본 공격 프리팹을 그대로 가져다 따로 재생한다
                GameObject basicPrefab = (_waveView != null)
                    ? typeof(MageknightVfxView).GetField("_basicPrefab", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(_waveView) as GameObject
                    : null;
                _mageComboTrial = new DevMageknightComboTrial(_server, _combat, transform, basicPrefab);
            }

            _isReady = true;
        }

        private void OnDestroy()
        {
            if (_player != null)
            {
                _player.Damaged -= OnPlayerDamaged;
            }

            if (_resolver != null)
            {
                _resolver.DamageApplied -= OnDamageApplied;
            }

            _server?.Shutdown();
            _mageComboTrial?.Dispose();
            for (int i = 0; i < _restoreOverrides.Count; ++i)
            {
                _restoreOverrides[i]();
            }

            _restoreOverrides.Clear();
        }

        private void Update()
        {
            if (!_isReady)
            {
                return;
            }

            double now = NetworkTime.time;
            ScanMonsters();
            if (_noCooldown)
            {
                ClearCooldowns();
            }

            SendInputs(now);

            // 기술이 위치를 직접 옮기므로 캐릭터 컨트롤러가 덮어쓰지 않게 이 틱 동안만 끈다
            bool controllerWasOn = (_controller != null) && (_controller.enabled);
            if (controllerWasOn)
            {
                _controller.enabled = false;
            }

            _budget.BeginTick();
            _combat.SetLocomotionSpeed((Time.time < _attackLockUntil) ? 0f : new Vector2(_player.Velocity.x, _player.Velocity.z).magnitude);
            _server.TickSkills(Time.deltaTime, now, _budget);
            _server.TickAttack(now);
            _server.ReplicateSkills();

            if (controllerWasOn)
            {
                _controller.enabled = true;
            }

            _comboTrial?.AfterAttackTick(now, AttackAim);
            _mageComboTrial?.AfterAttackTick(now, AttackAim);
            LockMovementWhileAttacking();
            _player.SetMovementBlocked((_server.Skills.BlocksMovement) || (Time.time < _attackLockUntil));
            ApplyFacing();
            ApplySkillCamera();
        }

        // 실제 서버는 공격을 승인하면 이동을 멈추고 준비 중에 움직이면 공격을 취소한다. 이 씬은 이동을 서버가 아니라 플레이어가 하므로,
        // 승인된 공격마다 준비 시간 동안 세워 둔다. 걷는 채로 승인하면 화살 효과가 이동 취소로 사라지기 때문이다
        private void LockMovementWhileAttacking()
        {
            byte sequence = _combat.AttackSequence;
            if (sequence == _lastAttackSequence)
            {
                return;
            }

            _lastAttackSequence = sequence;
            PlayerConfigSO.BasicAttackSpec spec = GameDB.Player.GetBasicAttack(_playerClass);
            int step = _combat.AttackComboStep;
            if (((_playerClass == EPlayerClass.Archer) && (step == HEAVY_SHOT_STEP)) || (spec.IsHeavy(step)))
            {
                _heavyShotUntil = Time.time + HEAVY_SHOT_SECONDS;
            }

            // 여러 번 치는 단계는 마지막 타까지 세워 둔다
            float hold = spec.GetWindup(step) + (Mathf.Max(0, spec.GetShotCount(step) - 1) * spec.GetShotInterval(step));
            _attackLockUntil = Time.time + hold + ATTACK_LOCK_MARGIN;
            _combat.SetLocomotionSpeed(0f);
        }

        // 애니메이션 뷰가 이번 프레임 재생을 정한 뒤에 속도만 덧입힌다
        private void LateUpdate()
        {
            _comboTrial?.ApplyAnimationBoost();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void SendInputs(double now)
        {
            bool aimed = _player.TryReadCursorPoint(out Vector3 cursor);
            Vector3 toCursor = aimed ? cursor - transform.position : transform.forward;
            toCursor.y = 0f;
            if (toCursor.sqrMagnitude < 0.0001f)
            {
                toCursor = transform.forward;
            }

            ushort yaw = FacingRules.EncodeYaw(toCursor.normalized);

            // 보정은 기본 공격 방향에만 쓰고 기술은 커서 그대로 쓴다
            Vector3 attackAim = ResolveAssistedAim(toCursor.normalized);
            ushort  attackYaw = FacingRules.EncodeYaw(attackAim);

            if (Input.GetKeyDown(Constants.Player.KEY_SKILL_Q))
            {
                SendSkill(Constants.Combat.SKILL_SLOT_Q, cursor, yaw, now);
            }

            if (Input.GetKeyDown(Constants.Player.KEY_SKILL_W))
            {
                SendSkill(Constants.Combat.SKILL_SLOT_W, cursor, yaw, now);
            }

            if (Input.GetKeyDown(Constants.Player.KEY_SKILL_E))
            {
                SendSkill(Constants.Combat.SKILL_SLOT_E, cursor, yaw, now);
            }

            if (Input.GetKeyDown(Constants.Player.KEY_SKILL_R))
            {
                SendSkill(Constants.Combat.SKILL_SLOT_R, cursor, yaw, now);
            }

            bool overPanel  = DevHitFeedbackTuningPanel.IsPointerOver;
            bool attackDown = (Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_ATTACK)) && (!overPanel);
            bool attackHeld = (Input.GetMouseButton(Constants.Player.MOUSE_BUTTON_ATTACK)) && (!overPanel) && (Time.time >= _nextAttackSend);
            if ((attackDown) || (attackHeld))
            {
                _nextAttackSend = Time.time + ATTACK_REPEAT_SECONDS;
                var attack = new PlayerAttackInput(NextHeader(now), attackYaw);
                _server.HandleAttack(in attack);
            }
        }

        // 원거리 투사체 기본 공격일 때만, 커서 방향에서 반각 안에 든 몬스터 중 각도가 가장 작은 것의 중심으로 방향을 당긴다
        private Vector3 ResolveAssistedAim(Vector3 cursorAim)
        {
            HasAssistTarget = false;
            AttackAim       = cursorAim;

            PlayerConfigSO.BasicAttackSpec attack = GameDB.Player.GetBasicAttack(_playerClass);
            if ((!_aimAssist) || (attack.ArcAngle > 0f) || (attack.ProjectileSpeed <= 0f))
            {
                return cursorAim;
            }

            Vector3 origin    = transform.position;
            float   bestAngle = _aimAssistHalfAngle;
            IReadOnlyList<DevHitFeedbackMonster> monsters = DevHitFeedbackMonster.Alive;
            for (int i = 0; i < monsters.Count; ++i)
            {
                DevHitFeedbackMonster monster = monsters[i];
                if (monster.IsDead)
                {
                    continue;
                }

                Vector3 delta = monster.transform.position - origin;
                delta.y = 0f;
                float distance = delta.magnitude;
                if ((distance < MIN_ASSIST_DISTANCE) || (distance > attack.Range)
                    || (!BasicAttackRules.IsWithinHeight(origin, monster.transform.position, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE)))
                {
                    continue;
                }

                float angle = Vector3.Angle(cursorAim, delta);
                if (angle <= bestAngle)
                {
                    bestAngle       = angle;
                    HasAssistTarget = true;
                    AssistPoint     = monster.transform.position;
                    AttackAim       = delta / distance;
                }
            }

            return AttackAim;
        }

        private void SendSkill(int slot, Vector3 point, ushort yaw, double now)
        {
            var input = new PlayerSkillInput(NextHeader(now), ESkillRequestKind.Regular, (byte)slot, yaw, point);
            _server.HandleSkill(in input);
        }

        private PlayerInputHeader NextHeader(double now)
        {
            ++_sequence;
            return new PlayerInputHeader(Constants.Network.PROTOCOL_VERSION, SESSION_EPOCH, PARTICIPANT_ID, _sequence, now);
        }

        // 기술 쿨다운을 모두 지우고 기력을 가득 채운다. 서버 기술 상태의 비공개 필드를 직접 쓴다
        private void ClearCooldowns()
        {
            SkillServer skills = _server.Skills;
            if (_readyAtField == null)
            {
                _readyAtField = typeof(SkillServer).GetField("_readyAt", BindingFlags.NonPublic | BindingFlags.Instance);
                _energyField  = typeof(SkillServer).GetField("_energy", BindingFlags.NonPublic | BindingFlags.Instance);
            }

            System.Array.Clear((double[])_readyAtField.GetValue(skills), 0, Constants.Player.SKILL_SLOT_COUNT);
            _energyField.SetValue(skills, skills.MaxEnergy);
        }

        // 새 시전이 시작되면 그 기술의 준비와 발동 시간 동안 화면을 당긴다
        private void ApplySkillCamera()
        {
            SkillServer skills = _server.Skills;
            if (skills.CastInstanceId == _lastCastId)
            {
                return;
            }

            _lastCastId = skills.CastInstanceId;
            if ((_playerClass != EPlayerClass.Mageknight) || (skills.Slot != Constants.Combat.SKILL_SLOT_R)
                || (_skillRZoomAmount <= 0f) || (_player.FeedbackCamera == null)
                || (!GameDB.Skills.TryGetSkillBySlot(_playerClass, skills.Slot, out SkillCatalogSO.SkillDefinition skill)))
            {
                return;
            }

            float holdSeconds = skill.Timeline.Windup + skill.Timeline.Active;
            _player.FeedbackCamera.ZoomHold(_skillRZoomAmount, holdSeconds, _skillRZoomInSmooth, _skillRZoomOutSmooth);
        }

        // 서버가 정한 정면을 캐릭터 방향에 반영한다. 바뀐 순간에만 적용해 걷는 동안의 자체 회전과 다투지 않는다
        private void ApplyFacing()
        {
            ushort yaw = _combat.FacingYaw;
            if ((_hasYaw) && (yaw == _lastYaw))
            {
                return;
            }

            _hasYaw  = true;
            _lastYaw = yaw;
            Vector3 facing = FacingRules.DecodeYaw(yaw);
            if (facing.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            }
        }

        // 몬스터는 실행 중에 나오고 사라지므로 주기적으로 살아 있는 목록을 훑어 서버 판정 대상에 넣고 뺀다
        private void ScanMonsters()
        {
            if (Time.time < _nextScanTime)
            {
                return;
            }

            _nextScanTime = Time.time + MONSTER_SCAN_SECONDS;

            IReadOnlyList<DevHitFeedbackMonster> monsters = DevHitFeedbackMonster.Alive;
            for (int i = 0; i < monsters.Count; ++i)
            {
                DevHitFeedbackMonster monster = monsters[i];
                if ((monster.IsDead) || (_byMonster.ContainsKey(monster)))
                {
                    continue;
                }

                var target = new DevMonsterCombatTarget(_registry.IssueCombatId(), monster, monster.GetComponent<Collider>());
                if (_registry.Register(target))
                {
                    _targets[target.CombatId] = target;
                    _byMonster[monster]       = target;
                }
            }

            _staleMonsters.Clear();
            foreach (KeyValuePair<DevHitFeedbackMonster, DevMonsterCombatTarget> pair in _byMonster)
            {
                if ((pair.Key == null) || (pair.Key.IsDead))
                {
                    _staleMonsters.Add(pair.Key);
                }
            }

            for (int i = 0; i < _staleMonsters.Count; ++i)
            {
                DevMonsterCombatTarget target = _byMonster[_staleMonsters[i]];
                _registry.Unregister(target.CombatId);
                _targets.Remove(target.CombatId);
                _byMonster.Remove(_staleMonsters[i]);
            }
        }

        // 서버가 확정한 피해를 기존 타격감 경로(Hit 이벤트)로 넘긴다
        private void OnDamageApplied(AppliedDamageNotice notice)
        {
            if ((notice.OwnerCombatId != _server.CombatId) || (!notice.Result.CountsAsHit)
                || (!_targets.TryGetValue(notice.TargetCombatId, out DevMonsterCombatTarget target)) || (target.Collider == null))
            {
                return;
            }

            bool isTick = _tracker.IsDamageOverTime
                || ((_tracker.CurrentSource != null) && (_tickSources.Contains(_tracker.CurrentSource)));
            if ((!_feedbackOnTickDamage) && (isTick))
            {
                return;
            }

            bool    isWave   = IsSkillSource(_tracker.CurrentSource, Constants.Combat.SKILL_SLOT_W);
            bool    isHeavyShot = (Time.time < _heavyShotUntil) && (_tracker.CurrentSource == Constants.Combat.BASIC_ATTACK_SOURCE_ID);
            bool    isHeavy  = (notice.Result.AppliedAmount >= (_stats.Attack * HEAVY_DAMAGE_RATIO)) || ((isWave) && (_skillWHeavyFeedback))
                || (isHeavyShot);
            Vector3 hitPoint = target.Collider.ClosestPoint(transform.position);
            // 지속 피해는 몬스터 반응만 보이고 플레이어는 굳지 않으며 화면도 흔들지 않는다(틱마다 멈추거나 흔들면 뚝뚝 끊긴다)
            float shakeScale = isTick ? 0f : (IsSkillSource(_tracker.CurrentSource, Constants.Combat.SKILL_SLOT_R) ? _skillRShakeScale : 1f);

            // 검파는 플레이어 대신 날아가는 검파 본체가 맞는 순간 멈춘다
            _player.ReportHit(target.Collider, hitPoint, isHeavy, (!isTick) && (!isWave), shakeScale);
            if (isWave)
            {
                FreezeWaveBody();
            }
        }

        // 공용 기술 데이터는 메모리에서만 바꾸고 종료할 때 원래 값으로 되돌린다. 파일은 저장하지 않는다
        // 궁수 콤보 시험은 서버 판정기를 그대로 두고 호스트 틱 앞뒤에서만 끼어든다
        private void SetUpComboTrial(ArcherVfxView archerView)
        {
            // 정식 데이터가 연발을 가지면 서버와 뷰가 직접 재생한다. 시험용 추가 발사를 겹치지 않는다
            if ((_playerClass == EPlayerClass.Archer) && (GameDB.Player.GetBasicAttack(_playerClass).GetShotCount(1) > 1))
            {
                return;
            }

            if ((!_archerComboTrial) || (_playerClass != EPlayerClass.Archer))
            {
                return;
            }

            var settings = new DevArcherComboTrial.Settings
            {
                BurstShots          = _burstShots,
                BurstGap            = _burstGap,
                BurstAnimSpeedScale = _burstAnimSpeedScale
            };

            _comboTrial = new DevArcherComboTrial(in settings, _server, _combat, GetComponentInChildren<Animator>(true));
            System.Action restore = _comboTrial.DivideBurstCoefficient(GameDB.Player.GetBasicAttack(_playerClass));
            if (restore != null)
            {
                _restoreOverrides.Add(restore);
            }

            if (archerView != null)
            {
                archerView.HeavyBasicStep = HEAVY_SHOT_STEP;
            }
        }

        private void ApplyDataOverrides()
        {
            if ((_mageknightComboTrial) && (_playerClass == EPlayerClass.Mageknight))
            {
                System.Action restoreCombo = DevMageknightComboTrial.ApplyData(GameDB.Player.GetBasicAttack(_playerClass));
                if (restoreCombo != null)
                {
                    _restoreOverrides.Add(restoreCombo);
                }
            }

            if ((_playerClass != EPlayerClass.Mageknight)
                || (!GameDB.Skills.TryGetSkillBySlot(_playerClass, Constants.Combat.SKILL_SLOT_W, out SkillCatalogSO.SkillDefinition skill)))
            {
                return;
            }

            OverrideAreaField(skill.Area, "_castRange", _skillWRange);
        }

        private void OverrideAreaField(SkillCatalogSO.SkillArea area, string fieldName, float value)
        {
            FieldInfo field = typeof(SkillCatalogSO.SkillArea).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if ((field == null) || (value <= 0f))
            {
                return;
            }

            float original = (float)field.GetValue(area);
            field.SetValue(area, value);
            _restoreOverrides.Add(() => field.SetValue(area, original));
        }

        private bool IsSkillSource(string sourceId, int slot)
        {
            return (sourceId != null) && (_playerClass == EPlayerClass.Mageknight)
                && (GameDB.Skills.TryGetSkillBySlot(_playerClass, slot, out SkillCatalogSO.SkillDefinition skill))
                && (skill.DefinitionId == sourceId);
        }

        private void FreezeWaveBody()
        {
            if ((_waveView == null) || (_skillWBodyFreezeFrames <= 0))
            {
                return;
            }

            int castId = _server.Skills.CastInstanceId;
            if (castId != _waveFreezeCastId)
            {
                _waveFreezeCastId      = castId;
                _waveFreezeSpentFrames = 0;
            }

            if ((_waveFreezeSpentFrames + _skillWBodyFreezeFrames) > _skillWFreezeBudgetFrames)
            {
                return;
            }

            _waveFreezeSpentFrames += _skillWBodyFreezeFrames;
            _waveView.FreezeWave(_skillWBodyFreezeFrames / FREEZE_FRAMES_PER_SECOND);
        }

        // 첫 타격 계수가 없고 틱 계수만 있는 기술이 지속 피해원이다(장판, 불 화살 화상)
        private void CollectTickSources()
        {
            for (int slot = 0; slot < Constants.Player.SKILL_SLOT_COUNT; ++slot)
            {
                if ((GameDB.Skills.TryGetSkillBySlot(_playerClass, slot, out SkillCatalogSO.SkillDefinition skill))
                    && (skill.Effect.TickCoefficient > 0f) && (skill.Effect.DamageCoefficient <= 0f))
                {
                    _tickSources.Add(skill.DefinitionId);
                }
            }
        }

        // 판정기가 피해를 알리는 동안 어떤 기술의 요청인지 알 수 있게 요청의 출처를 잠깐 들고 있는다
        private sealed class SourceTrackingResolver : IDamageResolver
        {
            private readonly IDamageResolver _inner;

            public string CurrentSource { get; private set; }
            public bool IsDamageOverTime { get; private set; }

            public SourceTrackingResolver(IDamageResolver inner) { _inner = inner; }

            public DamageResult Apply(in DamageRequest request)
            {
                string previousSource = CurrentSource;
                bool previousIsDamageOverTime = IsDamageOverTime;
                CurrentSource = request.SourceDefinitionId;
                IsDamageOverTime = request.IsDamageOverTime;
                try
                {
                    return _inner.Apply(in request);
                }
                finally
                {
                    CurrentSource = previousSource;
                    IsDamageOverTime = previousIsDamageOverTime;
                }
            }

            public int IssueInstanceId() { return _inner.IssueInstanceId(); }
        }

        // 몬스터에게 맞으면 서버 판정의 체력도 같은 비율로 깎아 피격 모션과 사망 모션이 실제 경로로 재생되게 한다
        private void OnPlayerDamaged(int hitsLeft)
        {
            float amount = (_stats.MaxHealth / _player.MaxHits) * PLAYER_DAMAGE_RATIO;
            _server.ApplyDamage(amount, hitsLeft <= 0, NetworkTime.time);
        }
    }
}
