// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary> SO 기술을 서버 시각으로 실행한다, 모든 버퍼는 소유자 생성 때 확보한다 </summary>
    public sealed class SkillServer : ICompanionContext, ICompanionSlotSource
    {
        /****************************************
        *                Fields
        ****************************************/

        // 투사체, 장판(지속 피해와 결계), 부적 변형의 지연 타격(후속 타격, 충격파, 폭발)
        private enum EEffectKind : byte
        {
            Projectile = 0,
            Field      = 1,
            Strike     = 2
        }

        // 부적이 바꾼 판정 수치는 생성 때 값으로 고정한다. 틱마다 SO와 수정값에서 다시 계산하지 않는다
        private struct Effect
        {
            public bool                           Active;
            public EEffectKind                    Kind;
            public SkillCatalogSO.SkillDefinition Skill;
            public SkillModifiers                 Modifiers;
            public int                            CastId;
            public int                            ActionId;
            public Vector3                        Origin;
            public Vector3                        Aim;
            public double                         NextTick;
            public double                         EndsAt;
            public float                          Travelled;
            public double                         LastTime;
            public ESkillShape                    Shape;            // 지연 타격 판정 형태
            public float                          Radius;           // m, 장판과 지연 타격 반경, 부채꼴이면 길이
            public float                          ArcAngle;         // 도, 부채꼴 지연 타격
            public float                          Range;            // m, 투사체 사거리
            public float                          Coefficient;      // A, 투사체 첫 적중과 지연 타격 계수, 피해 보조 전
            public float                          ExtraCoefficient; // A, 관통 뒤 타격과 장판 종료 폭발 계수, 피해 보조 전
            public int                            PierceLeft;       // 관통 투사체가 더 맞힐 수 있는 적 수
            public int                            HitCount;         // 관통 투사체가 지금까지 맞힌 대상 수, 목록은 _hitIds의 이 효과 칸이다
            public float                          Root;             // 초, 지연 타격 속박
            public bool                           Pending;          // 투사체가 아직 발사 전, 시작 시각에 시전자 위치에서 출발한다
            public bool                           DiscardOnCancel;  // 취소, 회피, 사망 때 아직 나가지 않았으면 버린다
            public double                         StartAt;          // 서버 시각, 대기 투사체 발사 시각
            public float                          Speed;            // m/초, 투사체 속도
            public float                          HalfWidth;        // m, 투사체 반폭
        }

        // 궁수 불 화살이 건 화상 하나, 대상마다 하나만 두고 다시 맞으면 끝 시각만 늘린다. 틱 주기는 처음 건 시각에서 이어 간다
        private struct Burn
        {
            public bool   Active;
            public int    TargetId;
            public int    CastId;
            public double NextTick;
            public double EndsAt;
        }

        // 갈래 수의 가운데 번호를 구하는 배율, 세 갈래면 -1, 0, +1 칸으로 퍼진다
        private const float CENTER_INDEX_SCALE = 0.5f;

        // 한 궁수가 동시에 화상을 거는 대상 수 상한, 넘으면 새 대상에는 화상을 걸지 않는다
        private const int MAX_BURNS = 16;

        private readonly SkillCasterAnchor                _owner;
        private readonly EPlayerClass                     _playerClass;
        private readonly int                              _participantId;
        private readonly SkillCatalogSO                   _catalog;
        private readonly PlayerConfigSO                   _player;
        private readonly CombatTargetRegistry             _registry;
        private readonly IDamageResolver                  _resolver;
        private readonly CombatLineOfSight                _lineOfSight;
        private readonly ISkillNavigation                 _navigation;
        private readonly ITalismanModifierResolver        _modifiers;
        private readonly SkillCatalogSO.SkillDefinition[] _skills;
        private readonly double[]                         _readyAt;
        private readonly Effect[]                         _effects;
        private readonly int[]                            _hitIds;
        private readonly List<ICombatTarget>              _targets;
        private readonly CompanionServer[]                _companions;
        private readonly CompanionCombatState[]           _companionStates;
        private readonly ResonanceLedger                  _resonance;
        private readonly bool                             _discardPending;
        private readonly Burn[]                           _burns;
        private readonly string                           _burnSourceId;

        private SkillCatalogSO.SkillDefinition     _skill;
        private SkillCatalogSO.AscensionDefinition _ascension;
        private SkillModifiers                     _castModifiers;
        private SkillExecutionPlan                 _plan;
        private ESkillPhase                        _phase            = ESkillPhase.Idle;
        private ESkillRequestKind                  _kind;
        private int                                _slot             = Constants.Combat.SKILL_SLOT_Q;
        private int                                _castId           = CombatIds.NONE;
        private int                                _actionId         = CombatIds.NONE;
        private Vector3                            _origin;
        private Vector3                            _aim;
        private Vector3                            _point;
        private double                             _resolveAt        = 0.0;
        private double                             _activeEndsAt     = 0.0;
        private double                             _recoveryEndsAt   = 0.0;
        private double                             _ascensionReadyAt = 0.0;
        private double                             _lastTick         = 0.0;
        private float                              _energy           = 0f;
        private bool                               _guardSucceeded   = false;
        private bool                               _dashStopped      = false;
        private float                              _maxEnergy        = 0f;
        private EPlayerControlKind                 _control          = EPlayerControlKind.None;
        private int                                _stacks           = 0;
        private int                                _lastStackCastId  = CombatIds.NONE;
        private float                              _castStackBonus   = 0f;
        private Vector3                            _blinkEnd;
        private EArcherElement                     _element          = EArcherElement.None;
        private EArcherElement                     _lastHitElement   = EArcherElement.None;

        /****************************************
        *              Properties
        ****************************************/

        public float             Energy             => _energy;
        public EPlayerClass      PlayerClass        => _playerClass;
        public int               ParticipantId      => _participantId;
        public IDamageable       DamageCaster       => _owner;
        public int               CompanionSlotCount => _companions.Length;
        public float             MaxEnergy          => _maxEnergy;
        public float             Resonance          => _resonance.Value;
        public ESkillPhase       Phase              => _phase;
        public int               CastInstanceId     => _castId;
        public int               ActionInstanceId   => _actionId;
        public int               Slot               => _slot;
        public ESkillRequestKind Kind               => _kind;
        public Vector3           Origin             => _origin;
        public Vector3           Aim                => _aim;
        public Vector3           Point              => _point;
        public double            ResolveAt          => _resolveAt;
        public double            AscensionReadyAt   => _ascensionReadyAt;
        public double            NextActionAt       => _recoveryEndsAt;
        public EPlayerControlKind Control           => _control;
        public int               ClassStacks        => _stacks;
        public EArcherElement    ArcherElement      => _element;

        // 궁수 기본 공격이 지금 속성으로 받는 보정, 다른 직업과 바람이 아닌 속성은 1이다
        public float             BasicAttackIntervalScale  => ArcherElementRules.GetAttackIntervalScale(_catalog, _element);
        public float             BasicProjectileSpeedScale => ArcherElementRules.GetProjectileSpeedScale(_catalog, _element);
        public bool              BlocksMovement     => _phase != ESkillPhase.Idle;
        public bool              IsGuardWindow      => (_phase == ESkillPhase.Active) && (IsCounter);

        // 타격 구간은 회피로 끊지 못한다. 단 검사, 마검사, 궁수는 아직 나가지 않은 타격이 남아 있으면 회피가 그 타격을 버린다
        public bool              LocksDodge         => (_phase == ESkillPhase.Active) && (!IsGuardWindow) && (!HasDiscardablePending(_castId));
        private bool IsCounter => (_kind == ESkillRequestKind.Regular) && (_playerClass == EPlayerClass.Warrior)
            && (_slot == Constants.Combat.WARRIOR_COUNTER_SLOT);

        /****************************************
        *                Methods
        ****************************************/

        // maxEnergy는 CharacterStats의 최대 기력이다. 0 이하면 PlayerConfigSO의 기본 최대 기력을 쓴다
        public SkillServer(ISkillCaster owner, EPlayerClass playerClass, int participantId, SkillCatalogSO catalog,
            PlayerConfigSO player, CombatTargetRegistry registry, IDamageResolver resolver, CombatLineOfSight lineOfSight,
            double now, ITalismanModifierResolver modifiers = null, ISkillNavigation navigation = null, float maxEnergy = 0f)
        {
            if ((owner == null) || (catalog == null) || (player == null) || (registry == null) || (resolver == null) || (lineOfSight == null))
            {
                throw new ArgumentNullException(nameof(owner));
            }
            _owner           = new SkillCasterAnchor(owner);
            _playerClass     = playerClass;
            _discardPending  = IsWeaponClass(playerClass);
            _participantId   = participantId;
            _catalog         = catalog;
            _player          = player;
            _registry        = registry;
            _resolver        = resolver;
            _lineOfSight     = lineOfSight;
            _navigation      = navigation ?? new SkillNavigation();
            _modifiers       = modifiers;
            _skills          = new SkillCatalogSO.SkillDefinition[Constants.Player.SKILL_SLOT_COUNT];
            _readyAt         = new double[Constants.Player.SKILL_SLOT_COUNT];
            _effects         = new Effect[Constants.Combat.MAX_SKILL_EFFECTS_PER_PLAYER];
            _hitIds          = new int[Constants.Combat.MAX_SKILL_EFFECTS_PER_PLAYER * Constants.Combat.MAX_PIERCE_HITS_PER_PROJECTILE];
            _targets         = new List<ICombatTarget>(Constants.Combat.MAX_COMBAT_TARGETS);
            _resonance       = new ResonanceLedger(catalog, Constants.Combat.RESONANCE_LEDGER_CAPACITY);
            _companions      = new CompanionServer[Constants.Combat.COMPANIONS_PER_PLAYER];
            _companionStates = new CompanionCombatState[Constants.Combat.COMPANIONS_PER_PLAYER];
            _burns           = new Burn[MAX_BURNS];
            for (int i = 0; i < _skills.Length; ++i)
            {
                if (!catalog.TryGetSkillBySlot(playerClass, i, out _skills[i]))
                {
                    throw new InvalidOperationException("직업 기술 슬롯이 비어 있다");
                }

                // 소환 슬롯을 동료 칸 번호로 쓰므로 칸 수를 넘는 슬롯이면 범위 밖 접근으로 틱이 멈춘다
                if ((_skills[i].Area.Shape == ESkillShape.Summon) && (i >= _companions.Length))
                {
                    throw new InvalidOperationException("소환 기술이 동료 칸 수를 넘는 슬롯에 있다");
                }
            }
            for (int i = 0; i < _companions.Length; ++i)
            {
                _companions[i] = new CompanionServer(_owner, catalog, registry, resolver, lineOfSight, this);
            }
            _maxEnergy = (maxEnergy > 0f) ? maxEnergy : player.BaseMaxEnergy;
            _energy    = _maxEnergy;
            _lastTick  = now;

            // 화상 피해는 불 부여 기술의 피해로 집계한다. 궁수가 아니면 쓰지 않는다
            _burnSourceId = Constants.Combat.BASIC_ATTACK_SOURCE_ID;
            for (int i = 0; i < _skills.Length; ++i)
            {
                if (_skills[i].Effect.ArcherElement == EArcherElement.Fire)
                {
                    _burnSourceId = _skills[i].DefinitionId;
                }
            }
        }

        public double GetReadyAt(int slot) => _readyAt[slot];
        public CompanionServer GetCompanion(int slot) => _companions[slot];

        /// <summary> 소유자가 받은 제어를 알린다, 시전과 틱마다 소유자가 그 시각의 값을 넣는다. 기절이 새로 걸리면 진행 중 기술을 취소하고 취소했으면 참 </summary>
        public bool SetControl(EPlayerControlKind control)
        {
            EPlayerControlKind previous = _control;
            _control = control;
            if (control == previous)
            {
                return false;
            }

            // 속박이면 돌진과 후퇴의 이동만 끝낸다. 이미 만든 사격 효과와 남은 발사는 이어 간다
            if (control == EPlayerControlKind.Root)
            {
                _dashStopped = true;
                return false;
            }

            // 비용과 쿨다운은 돌려주지 않고, 아직 나가지 않은 타격과 발사는 Cancel이 버린다
            if ((control == EPlayerControlKind.Stun) && (_phase != ESkillPhase.Idle))
            {
                Cancel();
                return true;
            }

            return false;
        }

        // 시전 중 몸이 움직이는 기술, 돌진(직선형)과 후퇴 사격
        private static bool IsMovementSkill(SkillCatalogSO.SkillDefinition skill, in SkillModifiers modifiers)
        {
            return (skill.Area.Shape == ESkillShape.Line) || (TalismanModifierRules.GetRetreatDistance(skill, in modifiers) > 0f);
        }

        /// <summary> 공통 입력 게이트 승인 뒤 호출, 거절은 자원과 쿨다운을 바꾸지 않는다 </summary>
        public bool TryCast(ESkillRequestKind kind, int slot, Vector3 point, ushort aimYaw, double now,
            bool phaseAllowsCombat, out ERejectReason reason)
        {
            reason = ERejectReason.None;
            if ((!phaseAllowsCombat) || (_phase != ESkillPhase.Idle))
            {
                reason = ERejectReason.WrongPhase;
                return false;
            }
            if (_owner.LifeState != EParticipantLifeState.Alive)
            {
                reason = ERejectReason.Dead;
                return false;
            }
            if (PlayerControlRules.BlocksAction(_control))
            {
                reason = ERejectReason.Disabled;
                return false;
            }
            SkillCatalogSO.SkillDefinition skill = null;
            SkillCatalogSO.AscensionDefinition ascension = null;
            if (kind == ESkillRequestKind.Regular)
            {
                if ((slot < 0) || (slot >= _skills.Length))
                {
                    reason = ERejectReason.InvalidTarget;
                    return false;
                }
                skill = _skills[slot];
            }
            else if ((kind == ESkillRequestKind.Ascension) && (_playerClass == EPlayerClass.Summoner))
            {
                for (int i = 0; i < _catalog.Ascensions.Count; ++i)
                {
                    if (_catalog.Ascensions[i].Slot == slot)
                    {
                        ascension = _catalog.Ascensions[i];
                        break;
                    }
                }
            }
            if ((skill == null) && (ascension == null))
            {
                reason = ERejectReason.InvalidTarget;
                return false;
            }
            if (!IsFinite(point))
            {
                reason = ERejectReason.InvalidTarget;
                return false;
            }
            SkillModifiers modifier = (skill != null) ? (_modifiers?.Resolve(_participantId, skill) ?? SkillModifiers.Identity) : SkillModifiers.Identity;
            if (!IsValidModifiers(in modifier))
            {
                reason = ERejectReason.InvalidTarget;
                return false;
            }
            if ((skill != null) && (IgnoresTalismanShape(skill)))
            {
                modifier = new SkillModifiers(modifier.EnergyScale, modifier.CooldownScale, modifier.DamageScale, 0f);
            }
            if ((skill != null) && (PlayerControlRules.BlocksMovementSkill(_control)) && (IsMovementSkill(skill, in modifier)))
            {
                reason = ERejectReason.Disabled;
                return false;
            }
            bool isPoint = (ascension != null) || (skill.Area.AimMode == ESkillAimMode.Point);
            float range = (ascension != null) ? ascension.CastRange : TalismanModifierRules.GetCastRange(skill, in modifier);
            Vector3 origin = _owner.Position;
            if (isPoint)
            {
                if (HorizontalDistanceSqr(origin, point) > range * range)
                {
                    reason = ERejectReason.OutOfRange;
                    return false;
                }
                if ((!BasicAttackRules.IsWithinHeight(origin, point, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE))
                    || (_lineOfSight.IsBlocked(origin, point)) || (!_navigation.TrySample(point, out point)))
                {
                    reason = ERejectReason.InvalidTarget;
                    return false;
                }
                // SamplePosition으로 벽 반대편이나 사거리 밖으로 보정된 좌표도 다시 검증한다
                if ((!IsFinite(point)) || (!BasicAttackRules.IsWithinHeight(origin, point, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE)))
                {
                    reason = ERejectReason.InvalidTarget;
                    return false;
                }
                if ((HorizontalDistanceSqr(origin, point) > range * range) || (_lineOfSight.IsBlocked(origin, point)))
                {
                    reason = ERejectReason.OutOfRange;
                    return false;
                }
            }
            float cost = (skill != null) ? TalismanModifierRules.GetEnergyCost(skill, in modifier) : ascension.ResonanceCost;
            float cooldown = (skill != null) ? TalismanModifierRules.GetCooldown(skill, in modifier) : _catalog.AscensionSharedCooldown;
            if ((!IsNonNegativeFinite(cost)) || (!IsNonNegativeFinite(cooldown))
                || ((skill != null) && ((!IsNonNegativeFinite(skill.Effect.DamageCoefficient * modifier.DamageScale))
                    || (!IsNonNegativeFinite(skill.Effect.TickCoefficient * modifier.DamageScale)))))
            {
                reason = ERejectReason.InvalidTarget;
                return false;
            }
            float available = (skill != null) ? _energy : Resonance;
            if (available < cost)
            {
                reason = ERejectReason.InsufficientEnergy;
                return false;
            }
            if (now < ((skill != null) ? _readyAt[slot] : _ascensionReadyAt))
            {
                reason = ERejectReason.Cooldown;
                return false;
            }
            // 스택도 기술 자원이라 기력 부족과 같은 사유로 알린다
            if ((skill != null) && (skill.Effect.RequiredStacks > 0) && (_stacks < skill.Effect.RequiredStacks))
            {
                reason = ERejectReason.InsufficientEnergy;
                return false;
            }
            SkillExecutionPlan plan = (skill != null) ? new SkillExecutionPlan(skill, in modifier) : default;
            if ((skill != null) && (plan.SlotCount > CountFreeEffects()))
            {
                reason = ERejectReason.NoCapacity;
                return false;
            }

            bool summon = (skill != null) && (skill.Area.Shape == ESkillShape.Summon);
            if ((summon) && (!_companions[slot].TryReserve()))
            {
                reason = ERejectReason.NoCapacity;
                return false;
            }

            int castId = _resolver.IssueInstanceId();
            if (!_resonance.OpenCast(castId))
            {
                if (summon)
                {
                    _companions[slot].CancelReservation();
                }
                reason = ERejectReason.NoCapacity;
                return false;
            }
            // 자원과 행동을 함께 확정하며 쿨다운은 확정 서버 시각을 쓴다
            if (skill != null)
            {
                _energy -= cost;
                _readyAt[slot] = now + cooldown;
            }
            else
            {
                _resonance.TrySpend(cost);
                _ascensionReadyAt = now + _catalog.AscensionSharedCooldown;
            }
            // 스택 소모 기술은 확정 때 쓴 스택으로 계수를 고정한다. 준비 중 취소돼도 비용처럼 돌려주지 않는다
            _castStackBonus = 0f;
            if ((skill != null) && (skill.Effect.StackCoefficient > 0f))
            {
                _castStackBonus = _stacks * skill.Effect.StackCoefficient;
                _stacks         = 0;
            }
            if ((skill != null) && (skill.Effect.RequiredStacks > 0))
            {
                _stacks -= skill.Effect.RequiredStacks;
            }
            _skill         = skill;
            _ascension     = ascension;
            _castModifiers = modifier;
            _plan          = plan;
            _kind          = kind;
            _slot          = slot;
            _origin        = origin;
            _aim           = FacingRules.DecodeYaw(aimYaw);
            _point         = isPoint ? point : origin + (_aim * ((skill != null) ? skill.Area.CenterOffset : 0f));
            _castId        = castId;
            _actionId      = _resolver.IssueInstanceId();
            float windup = (skill != null) ? skill.Timeline.Windup : ascension.Windup;
            float active = (skill != null) ? TalismanModifierRules.GetActiveSeconds(skill, in modifier) : 0f;
            float recovery = (skill != null) ? skill.Timeline.Recovery : ascension.Recovery;
            _resolveAt      = now + windup;
            _activeEndsAt   = _resolveAt + active;
            _recoveryEndsAt = _activeEndsAt + recovery;
            if ((skill != null) && (_discardPending))
            {
                // 후속 타격과 순차 발사가 끝나기 전에는 같은 동작이다. 그 사이 회피, 사망이 아직 나가지 않은 타격을 버린다
                double lastFireAt = _resolveAt + plan.ScheduledTail;
                _recoveryEndsAt   = Math.Max(_activeEndsAt, lastFireAt) + recovery;
            }
            _phase          = ESkillPhase.Windup;
            _guardSucceeded = false;
            _dashStopped    = false;
            _owner.SetSkillFacing(_aim);
            return true;
        }

        /// <summary> 회피나 반격 이동 취소에서 호출, 이미 생성된 효과와 확정 비용은 보존한다 </summary>
        public void Cancel()
        {
            if ((_phase == ESkillPhase.Windup) && (_skill != null) && (_skill.Area.Shape == ESkillShape.Summon))
            {
                _companions[_slot].CancelReservation();
            }
            DiscardPendingEffects(_castId);
            if (!HasEffectCast(_castId))
            {
                _resonance.ReleaseCast(_castId);
            }
            _phase          = ESkillPhase.Idle;
            _guardSucceeded = false;
        }

        // 아직 나가지 않은 후속 타격, 순차 발사, 후속 투사체만 버린다. 이미 나간 투사체와 장판은 원래 수명을 유지한다
        private void DiscardPendingEffects(int castId)
        {
            for (int i = 0; i < _effects.Length; ++i)
            {
                ref Effect effect = ref _effects[i];
                if ((effect.Active) && (effect.DiscardOnCancel) && (effect.CastId == castId)
                    && ((effect.Kind == EEffectKind.Strike) || (effect.Pending)))
                {
                    effect.Active = false;
                }
            }
        }

        private bool HasDiscardablePending(int castId)
        {
            for (int i = 0; i < _effects.Length; ++i)
            {
                ref Effect effect = ref _effects[i];
                if ((effect.Active) && (effect.DiscardOnCancel) && (effect.CastId == castId)
                    && ((effect.Kind == EEffectKind.Strike) || (effect.Pending)))
                {
                    return true;
                }
            }
            return false;
        }

        // 궁수 속성 부여와 속성 폭발은 화살 부적이 바꾸던 형태(사거리, 폭, 관통, 후속 화살, 후퇴)를 쓰지 않는다.
        // 기력, 쿨다운, 피해 비율만 남기고 나머지 변형은 버린다. 남기면 관통 변형이 폭발 계수를 3.6A로 바꿔 버린다
        private static bool IgnoresTalismanShape(SkillCatalogSO.SkillDefinition skill)
        {
            return (skill.Effect.ArcherElement != EArcherElement.None) || (skill.Effect.RequiredStacks > 0);
        }

        private static bool IsWeaponClass(EPlayerClass playerClass)
        {
            return (playerClass == EPlayerClass.Swordsman) || (playerClass == EPlayerClass.Mageknight)
                || (playerClass == EPlayerClass.Archer);
        }

        public void Tick(double now, float deltaTime, PathRequestBudget pathBudget)
        {
            if (_owner.LifeState == EParticipantLifeState.Alive)
            {
                _energy = Mathf.Min(MaxEnergy, _energy + (_player.EnergyRegenPerSecond * (float)Math.Max(0.0, now - _lastTick)));
                TickAction(now);
            }
            else
            {
                Cancel();
            }
            _lastTick = now;
            TickEffects(now);
            TickBurns(now);
            for (int i = 0; i < _companions.Length; ++i)
            {
                _companions[i].Tick(deltaTime, now, pathBudget);
            }
            ReplicateCompanions();
        }

        private void TickAction(double now)
        {
            if ((_phase == ESkillPhase.Windup) && (now >= _resolveAt))
            {
                _phase = ESkillPhase.Active;
                Activate(now);
            }
            if ((_phase == ESkillPhase.Active) && (_skill != null) && (_skill.Area.Shape == ESkillShape.Line)
                && (!_skill.Effect.TeleportToEnd))
            {
                TickDash(now);
            }
            if ((_phase == ESkillPhase.Active) && (_skill != null) && (_skill.Effect.RetreatDistance > 0f))
            {
                TickRetreat(now);
            }
            if ((_phase == ESkillPhase.Active) && (now >= _activeEndsAt))
            {
                if ((IsCounter) && (_guardSucceeded))
                {
                    ApplyCounter(now);
                }
                if ((_skill != null) && (_skill.Effect.TeleportToEnd))
                {
                    ApplyBlink(now);
                }
                _phase = ESkillPhase.Recovery;
            }
            if ((_phase == ESkillPhase.Recovery) && (now >= _recoveryEndsAt))
            {
                if (!HasEffectCast(_castId))
                {
                    _resonance.ReleaseCast(_castId);
                }
                _phase = ESkillPhase.Idle;
            }
        }

        private void Activate(double now)
        {
            if (_ascension != null)
            {
                ApplyAscension(now);
                return;
            }
            if (_skill.Area.Shape == ESkillShape.Buff)
            {
                ApplyBuff();
                return;
            }
            if ((_skill.Area.Shape == ESkillShape.Line) && (_skill.Effect.TeleportToEnd))
            {
                _blinkEnd = GetBlinkEnd();
                return;
            }
            if ((IsCounter) || (_skill.Area.Shape == ESkillShape.Line))
            {
                return;
            }
            if (_skill.Area.Shape == ESkillShape.Summon)
            {
                if (!_companions[_slot].Start(_skill, _castId, _resolveAt, in _castModifiers))
                {
                    Debug.LogError("[Combat] 예약된 동료의 소환 확정에 실패했다");
                }
                return;
            }

            AddStacks(_skill.Effect.StackGain);

            // 핵심 변형이 정한 형태(원형 교체, 근접의 갈래 투사체 교체)와 보조가 바꾼 반경, 각도로 첫 타격과 뒤따르는 타격을 모두 판정한다
            SkillCoreVariant core   = _castModifiers.Core;
            ESkillShape      shape  = TalismanModifierRules.GetShape(_skill, in _castModifiers);
            if (_plan.IsProjectile)
            {
                AddProjectiles(_resolveAt);
                if (_plan.HasFollowUpProjectile)
                {
                    AddFollowUpProjectile(_resolveAt + _plan.FollowUpProjectileBase + _plan.CoreDelay);
                }
                return;
            }
            float            radius = TalismanModifierRules.GetRadius(_skill, in _castModifiers);
            float            arc    = TalismanModifierRules.GetArcAngle(_skill, in _castModifiers);
            Vector3          center = (_skill.Area.Shape == ESkillShape.Cone) ? _owner.Position : _point;
            if (_skill.Effect.DamageCoefficient > 0f)
            {
                // 보스는 끌어오지 않고, 속박은 ApplyHit가 보스 허용 구간과 최대 시간으로 자른다
                if (core.Is(ETalismanEffectKind.PullRoot))
                {
                    PullTargets(shape, center, _aim, radius, arc, core, now);
                }

                ApplyArea(_skill, in _castModifiers, shape, center, _aim, radius, arc, _castId, _actionId,
                    TalismanModifierRules.GetPrimaryCoefficient(_skill, in _castModifiers),
                    _skill.Effect.RootDuration + core.RootBonus, now);
                if (_plan.EmitsSecondHit)
                {
                    AddStrike(shape, center, radius, arc, _skill.Effect.SecondHitCoefficient, _resolveAt + _plan.SecondHitDelay, _discardPending);
                }
                if (_plan.HasFollowUpStrike)
                {
                    AddStrike(shape, center, radius, arc, core.AddedCoefficient, _resolveAt + _plan.CoreDelay, _discardPending);
                }
                else if (_plan.HasShockwave)
                {
                    AddStrike(ESkillShape.Circle, center, core.Radius, 0f, core.AddedCoefficient, _resolveAt + _plan.CoreDelay);
                }
                else if (_plan.HasFollowUpProjectile)
                {
                    AddFollowUpProjectile(_resolveAt + _plan.FollowUpProjectileBase + _plan.CoreDelay);
                }
            }
            if (_plan.HasDurationEffect)
            {
                // 화염 분출은 지속 피해를 지연 뒤 단일 폭발로 바꾼다. 반경과 피해 보조는 폭발에도 그대로 붙는다
                if (core.Is(ETalismanEffectKind.Detonate))
                {
                    AddStrike(ESkillShape.Circle, center, radius, 0f,
                        TalismanModifierRules.GetPrimaryCoefficient(_skill, in _castModifiers), _resolveAt + core.Delay);
                }
                else
                {
                    AddField(_resolveAt, radius);
                }
            }
        }

        // 지속 장판과 결계. 잔불 폭발은 종료 때 같은 반경으로 한 번 더 친다
        private void AddField(double now, float radius)
        {
            int index = GetFreeEffect();
            if (index < 0)
            {
                return;
            }
            SkillCoreVariant core = _castModifiers.Core;
            _effects[index] = new Effect
            {
                Active = true, Kind = EEffectKind.Field, Skill = _skill, Modifiers = _castModifiers, CastId = _castId,
                ActionId = _actionId, Origin = _point, Aim = _aim, LastTime = now, Shape = ESkillShape.Circle, Radius = radius,
                EndsAt = now + TalismanModifierRules.GetDuration(_skill, in _castModifiers),
                NextTick = Constants.Combat.FIELD_TICKS_IMMEDIATELY ? now : now + _skill.Effect.TickInterval,
                ExtraCoefficient = core.Is(ETalismanEffectKind.EndExplosion) ? core.AddedCoefficient : 0f
            };
        }

        // 삼갈래 술식은 같은 동작 번호로 갈래를 나눠 쏴서 한 대상이 두 갈래에 맞아도 적중 원장이 한 번만 받는다
        private void AddProjectiles(double now)
        {
            SkillCoreVariant core         = _castModifiers.Core;
            bool             pierceShot   = core.Is(ETalismanEffectKind.PierceShot);
            int              count        = _plan.ProjectileCount;
            float            step         = TalismanModifierRules.GetProjectileStepAngle(_skill, in _castModifiers);
            int              repeat       = _plan.VolleyCount;
            float            range        = TalismanModifierRules.GetCastRange(_skill, in _castModifiers);
            float            speed        = TalismanModifierRules.GetProjectileSpeed(_skill, in _castModifiers);
            float            halfWidth    = TalismanModifierRules.GetProjectileWidth(_skill, in _castModifiers) * Constants.Combat.SKILL_HALF_WIDTH_SCALE;
            float            coefficient  = TalismanModifierRules.GetPrimaryCoefficient(_skill, in _castModifiers) + _castStackBonus;
            if (speed <= 0f)
            {
                return;
            }

            // 첫 적중과 이후 적중 계수를 가른다. 구 관통 술식만 뒤 적중이 추가 계수이고, 기본 관통과 관통 확장은 모든 적중이 같은 계수다
            float extraCoefficient = pierceShot ? core.AddedCoefficient : coefficient;
            int   pierceLeft       = pierceShot ? core.ExtraTargets : Math.Max(0, TalismanModifierRules.GetMaxTargets(_skill, in _castModifiers) - 1);
            pierceLeft = Math.Min(pierceLeft, Constants.Combat.MAX_PIERCE_HITS_PER_PROJECTILE - 1);
            for (int shot = 0; shot < repeat; ++shot)
            {
                // 순차 발사는 발사마다 새 동작 번호를 받아 같은 적도 발사마다 맞는다. 한 발사의 갈래는 같은 번호를 나눠 쓴다
                double launchAt = now + _plan.GetShotOffset(shot);
                int    action   = (shot == 0) ? _actionId : _resolver.IssueInstanceId();
                for (int i = 0; i < count; ++i)
                {
                    int index = GetFreeEffect();
                    if (index < 0)
                    {
                        return;
                    }
                    float   angle = (count > 1) ? ((i - ((count - 1) * CENTER_INDEX_SCALE)) * step) : 0f;
                    Vector3 aim   = (angle != 0f) ? (Quaternion.AngleAxis(angle, Vector3.up) * _aim) : _aim;
                    _effects[index] = new Effect
                    {
                        Active = true, Kind = EEffectKind.Projectile, Skill = _skill, Modifiers = _castModifiers, CastId = _castId,
                        ActionId = action, Origin = _owner.Position, Aim = aim, LastTime = launchAt, Range = range,
                        EndsAt = launchAt + (range / speed), Coefficient = coefficient, ExtraCoefficient = extraCoefficient,
                        PierceLeft = pierceLeft, Speed = speed, HalfWidth = halfWidth,
                        Pending = shot > 0, StartAt = launchAt, DiscardOnCancel = _discardPending
                    };
                }
            }
        }

        // 같은 동작 끝에서 같은 방향으로 한 발 더 나간다. 발사 시각의 시전자 위치에서 출발하고 그전에 취소되면 버려진다
        private void AddFollowUpProjectile(double at)
        {
            SkillCoreVariant core  = _castModifiers.Core;
            float speed = (core.Speed > 0f) ? core.Speed : TalismanModifierRules.GetProjectileSpeed(_skill, in _castModifiers);
            float range = (core.Length > 0f) ? core.Length : TalismanModifierRules.GetCastRange(_skill, in _castModifiers);
            float width = (core.Width > 0f) ? Math.Max(0f, core.Width + _castModifiers.WidthDelta)
                : TalismanModifierRules.GetProjectileWidth(_skill, in _castModifiers);
            int index = GetFreeEffect();
            if ((index < 0) || (speed <= 0f))
            {
                return;
            }
            int maxTargets = (core.ExtraTargets > 0) ? core.ExtraTargets : TalismanModifierRules.GetMaxTargets(_skill, in _castModifiers);
            _effects[index] = new Effect
            {
                Active = true, Kind = EEffectKind.Projectile, Skill = _skill, Modifiers = _castModifiers, CastId = _castId,
                ActionId = _resolver.IssueInstanceId(), Origin = _owner.Position, Aim = _aim, LastTime = at, Range = range,
                EndsAt = at + (range / speed), Coefficient = core.AddedCoefficient, ExtraCoefficient = core.AddedCoefficient,
                PierceLeft = Math.Min(Math.Max(0, maxTargets - 1), Constants.Combat.MAX_PIERCE_HITS_PER_PROJECTILE - 1), Speed = speed,
                HalfWidth = width * Constants.Combat.SKILL_HALF_WIDTH_SCALE, Pending = true, StartAt = at, DiscardOnCancel = true
            };
        }

        // 후속 타격, 충격파, 폭발처럼 첫 판정 뒤 정해진 시각에 한 번 치는 판정. 발동 때 새 동작 번호를 받아 같은 적도 다시 맞는다
        private void AddStrike(ESkillShape shape, Vector3 center, float radius, float arc, float coefficient, double at,
            bool discardOnCancel = false)
        {
            int index = GetFreeEffect();
            if (index < 0)
            {
                return;
            }
            _effects[index] = new Effect
            {
                Active = true, Kind = EEffectKind.Strike, Skill = _skill, Modifiers = _castModifiers, CastId = _castId,
                ActionId = CombatIds.NONE, Origin = center, Aim = _aim, LastTime = at, NextTick = at, EndsAt = at,
                Shape = shape, Radius = radius, ArcAngle = arc, Coefficient = coefficient,
                DiscardOnCancel = discardOnCancel
            };
        }

        private void TickEffects(double now)
        {
            for (int i = 0; i < _effects.Length; ++i)
            {
                ref Effect effect = ref _effects[i];
                if (!effect.Active)
                {
                    continue;
                }
                switch (effect.Kind)
                {
                    case EEffectKind.Projectile:
                        TickProjectile(ref effect, i, now);
                        if (now >= effect.EndsAt)
                        {
                            effect.Active = false;
                        }
                        break;
                    case EEffectKind.Strike:
                        if (now >= effect.NextTick)
                        {
                            ApplyArea(effect.Skill, in effect.Modifiers, effect.Shape, effect.Origin, effect.Aim, effect.Radius,
                                effect.ArcAngle, effect.CastId, _resolver.IssueInstanceId(), effect.Coefficient, 0f, now);
                            effect.Active = false;
                        }
                        break;
                    default:
                        TickField(ref effect, now);
                        break;
                }
                if ((!effect.Active) && (!HasEffectCast(effect.CastId)))
                {
                    _resonance.ReleaseCast(effect.CastId);
                }
            }
        }

        private void TickField(ref Effect effect, double now)
        {
            if (effect.Skill.Effect.TickCoefficient > 0f)
            {
                // 경계 마지막 틱까지 포함하고 한 시전 번호 아래 각 주기에 새 동작 번호를 발급한다
                while ((effect.NextTick <= now) && (effect.NextTick <= effect.EndsAt))
                {
                    int action = _resolver.IssueInstanceId();
                    ApplyArea(effect.Skill, in effect.Modifiers, ESkillShape.Circle, effect.Origin, effect.Aim, effect.Radius, 0f,
                        effect.CastId, action, effect.Skill.Effect.TickCoefficient, 0f, now, EDamageFlags.DamageOverTime);
                    effect.NextTick += effect.Skill.Effect.TickInterval;
                }
            }
            if (now < effect.EndsAt)
            {
                return;
            }
            if (effect.ExtraCoefficient > 0f)
            {
                ApplyArea(effect.Skill, in effect.Modifiers, ESkillShape.Circle, effect.Origin, effect.Aim, effect.Radius, 0f,
                    effect.CastId, _resolver.IssueInstanceId(), effect.ExtraCoefficient, 0f, now);
            }
            effect.Active = false;
        }

        // 관통 술식은 맞힌 대상보다 먼 거리에서 다음 적을 찾는다. 첫 적중은 원래 계수, 뒤는 추가 계수다
        private void TickProjectile(ref Effect effect, int slot, double now)
        {
            if (effect.Pending)
            {
                if (now < effect.StartAt)
                {
                    return;
                }
                // 대기 투사체는 발사 시각의 시전자 위치에서 출발한다. 발사 시각과 현재 틱 사이의 이동도 같은 틱에서 이어 간다
                effect.Pending  = false;
                effect.Origin   = _owner.Position;
                effect.LastTime = effect.StartAt;
            }
            int   hitBase  = slot * Constants.Combat.MAX_PIERCE_HITS_PER_PROJECTILE;
            float elapsed  = (float)Math.Max(0.0, now - effect.LastTime);
            float distance = Mathf.Min(effect.Range - effect.Travelled, effect.Speed * elapsed);
            float clear     = _lineOfSight.GetClearDistance(effect.Origin, effect.Aim, distance);
            float halfWidth = effect.HalfWidth;
            bool  wallCut   = (effect.DiscardOnCancel) && (clear < distance);

            // 기본 공격 화살처럼 투사체 쪽으로 달려온 적이 틱 사이에 지나쳤을 수 있어 지난 위치 뒤를 되짚는다. 쏜 자리보다 뒤는 보지 않는다.
            // 한 틱 안에서는 출발점을 고정하고 start만 앞으로 옮긴다. 다음 적은 앞서 맞힌 적과 같거나 먼 거리에서 처음 닿는다
            float start = -Mathf.Min(effect.Travelled, Constants.Combat.ARROW_TARGET_MAX_APPROACH_SPEED * elapsed);
            while (effect.Active)
            {
                int index = FirstBodyOnSegment(effect.Origin, effect.Aim, start, clear, halfWidth, hitBase, effect.HitCount,
                    out float along);
                if (index < 0)
                {
                    break;
                }
                // 벽과 대상이 같은 거리면 벽이 먼저다
                if ((wallCut) && (along >= clear))
                {
                    break;
                }
                ICombatTarget target      = _targets[index];
                float         coefficient = (effect.HitCount == 0) ? effect.Coefficient : effect.ExtraCoefficient;
                ApplyHit(target, effect.Skill.DefinitionId, effect.CastId, effect.ActionId, effect.Origin + (effect.Aim * along),
                    coefficient * effect.Modifiers.DamageScale, effect.Modifiers.ResonanceBonus, 0f, false, now,
                    travelDirection: effect.Aim);
                if (effect.PierceLeft <= 0)
                {
                    effect.Active = false;
                    break;
                }
                --effect.PierceLeft;
                _hitIds[hitBase + effect.HitCount] = target.CombatId;
                ++effect.HitCount;
                start = along;
            }
            effect.Origin    += effect.Aim * clear;
            effect.Travelled += clear;
            effect.LastTime   = now;
            if ((clear < distance) || (effect.Travelled >= effect.Range))
            {
                effect.Active = false;
            }
            _targets.Clear();
        }

        // 관통 돌진은 적 몸체에서 멈추지 않고 지나간 경로의 적을 모두 친다. 같은 동작 번호라 한 적은 한 번만 맞는다
        private void TickDash(double now)
        {
            if (_dashStopped)
            {
                return;
            }
            bool  pierce = _castModifiers.Core.Is(ETalismanEffectKind.PierceDash);
            float range  = TalismanModifierRules.GetCastRange(_skill, in _castModifiers);
            float elapsed = (float)Math.Max(0.0, Math.Min(now, _activeEndsAt) - _resolveAt);
            Vector3 desired = _origin + (_aim * (range * elapsed / _skill.Timeline.Active));
            Vector3 from = _owner.Position;
            Vector3 delta = desired - from;
            float distance = delta.magnitude;
            float clear = _lineOfSight.GetClearDistance(from, _aim, distance);
            Vector3 navigationEnd = _navigation.Move(from, from + (_aim * clear));
            float horizontalDistance = Mathf.Sqrt(HorizontalDistanceSqr(from, navigationEnd));
            clear = Mathf.Min(clear, horizontalDistance);
            float radius      = Constants.Combat.SKILL_TARGET_BODY_RADIUS;
            float halfWidth   = _skill.Area.Width * Constants.Combat.SKILL_HALF_WIDTH_SCALE;
            float coefficient = TalismanModifierRules.GetPrimaryCoefficient(_skill, in _castModifiers);
            int index = -1;
            float along = 0f;
            if (pierce)
            {
                ApplyLine(_skill, in _castModifiers, from, _aim, clear + radius, halfWidth, _castId, _actionId, coefficient, now);
            }
            else
            {
                index = FirstOnRay(from, _aim, clear + radius, halfWidth, 0, 0, out along);
            }
            if (index >= 0)
            {
                ApplyHit(_targets[index], _skill.DefinitionId, _castId, _actionId, from,
                    coefficient * _castModifiers.DamageScale, _castModifiers.ResonanceBonus, 0f, false, now);
                clear = Mathf.Min(clear, Mathf.Max(0f, along - radius));
                _dashStopped = true;
            }
            if (clear < distance)
            {
                _dashStopped = true;
            }
            Vector3 stopped = from + (_aim * clear);
            if ((index < 0) && (horizontalDistance <= distance))
            {
                stopped = navigationEnd;
            }
            else if (_navigation.TrySample(stopped, out Vector3 sampled))
            {
                stopped.y = sampled.y;
            }
            _owner.SetSkillPosition(stopped);
            _targets.Clear();
        }

        // 자기 강화, 발동 순간부터 정한 시간 동안 피격이상 면역. 궁수 속성 부여는 발동 순간부터 다음 기본 공격의 속성을 바꾼다
        private void ApplyBuff()
        {
            if (_skill.Effect.HitImmunityDuration > 0f)
            {
                _owner.GrantHitImmunity(_resolveAt + _skill.Effect.HitImmunityDuration);
            }
            if (_skill.Effect.ArcherElement != EArcherElement.None)
            {
                _element = _skill.Effect.ArcherElement;
            }
            AddStacks(_skill.Effect.StackGain);
        }

        /// <summary> 궁수 기본 공격 화살 한 발이 적을 맞힌 뒤 부른다, element는 그 화살이 나갈 때의 속성이다 </summary>
        public void OnArrowHit(EArcherElement element, ICombatTarget target, in DamageResult result, double now)
        {
            if ((_playerClass != EPlayerClass.Archer) || (!result.CountsAsHit) || (target == null))
            {
                return;
            }

            _stacks = ArcherElementRules.AddStacks(_stacks, ArcherElementRules.GetStackGain(_catalog, element, _lastHitElement),
                _catalog.ArcherStackMax);
            if (element != EArcherElement.None)
            {
                _lastHitElement = element;
            }

            if (target.LifeState != EParticipantLifeState.Alive)
            {
                return;
            }

            switch (element)
            {
                case EArcherElement.Ice:
                    if (target is ISkillSlowTarget slow)
                    {
                        slow.ApplySlow(_catalog.ChillSlowRate, _catalog.ChillDuration, now);
                    }
                    break;
                case EArcherElement.Fire:
                    ApplyBurn(target.CombatId, now);
                    break;
                default:
                    break;
            }
        }

        private void ApplyBurn(int targetId, double now)
        {
            int free = -1;
            for (int i = 0; i < _burns.Length; ++i)
            {
                if ((_burns[i].Active) && (_burns[i].TargetId == targetId))
                {
                    _burns[i].EndsAt = now + _catalog.BurnDuration;
                    return;
                }
                if ((!_burns[i].Active) && (free < 0))
                {
                    free = i;
                }
            }
            if (free < 0)
            {
                return;
            }
            _burns[free] = new Burn
            {
                Active = true, TargetId = targetId, CastId = _resolver.IssueInstanceId(),
                NextTick = now + _catalog.BurnTickInterval, EndsAt = now + _catalog.BurnDuration
            };
        }

        // 끝 시각의 마지막 틱까지 포함한다. 대상이 죽거나 사라지면 남은 틱을 버린다. 시전자가 죽어도 이미 건 화상은 끝까지 탄다
        private void TickBurns(double now)
        {
            for (int i = 0; i < _burns.Length; ++i)
            {
                ref Burn burn = ref _burns[i];
                if (!burn.Active)
                {
                    continue;
                }
                while ((burn.NextTick <= now) && (burn.NextTick <= burn.EndsAt))
                {
                    if ((!_registry.TryGet(burn.TargetId, out ICombatTarget target)) || (target.LifeState != EParticipantLifeState.Alive))
                    {
                        burn.Active = false;
                        break;
                    }
                    ApplyHit(target, _burnSourceId, burn.CastId, _resolver.IssueInstanceId(), target.Position,
                        _catalog.BurnTickCoefficient, 0f, 0f, false, now, EDamageFlags.DamageOverTime);
                    burn.NextTick += _catalog.BurnTickInterval;
                }
                if ((now >= burn.EndsAt) || (burn.NextTick > burn.EndsAt))
                {
                    burn.Active = false;
                }
            }
        }

        // 순간 이동 도착점은 발동 순간 정한다. 벽 앞에서는 몸 반경만큼 떨어져 멈추고, 이동 불가 지형은 NavMesh가 막은 자리까지만 간다
        private Vector3 GetBlinkEnd()
        {
            float   range = TalismanModifierRules.GetCastRange(_skill, in _castModifiers);
            Vector3 from  = _owner.Position;
            float   clear = _lineOfSight.GetClearDistance(from, _aim, range);
            if (clear < range)
            {
                clear = Mathf.Max(0f, clear - Constants.Combat.PLAYER_BODY_RADIUS);
            }
            Vector3 desired       = from + (_aim * clear);
            Vector3 navigationEnd = _navigation.Move(from, desired);
            if (HorizontalDistanceSqr(from, navigationEnd) < (clear * clear))
            {
                return navigationEnd;
            }
            if (_navigation.TrySample(desired, out Vector3 sampled))
            {
                desired.y = sampled.y;
            }
            return desired;
        }

        // 지나온 경로의 적을 한 번씩 베고 경로 끝으로 옮긴다. 발동 중 속박되면 베기만 하고 자리에 남는다
        private void ApplyBlink(double now)
        {
            Vector3 from   = _origin;
            Vector3 delta  = _blinkEnd - from;
            delta.y = 0f;
            float   length = delta.magnitude;
            float   width  = TalismanModifierRules.GetProjectileWidth(_skill, in _castModifiers);
            ApplyLine(_skill, in _castModifiers, from, _aim, length + Constants.Combat.SKILL_TARGET_BODY_RADIUS,
                width * Constants.Combat.SKILL_HALF_WIDTH_SCALE, _castId, _actionId,
                TalismanModifierRules.GetPrimaryCoefficient(_skill, in _castModifiers), now);
            if ((_dashStopped) || (_owner.LifeState != EParticipantLifeState.Alive))
            {
                return;
            }
            _owner.SetSkillPosition(_blinkEnd);
            AddStacks(_skill.Effect.StackGain);
        }

        private void AddStacks(int amount)
        {
            if ((amount <= 0) || (_playerClass != EPlayerClass.Mageknight))
            {
                return;
            }
            _stacks = Math.Min(_catalog.MageknightStackMax, _stacks + amount);
        }

        // 후퇴 사격, 조준 반대 방향으로 시전 때 고정한 거리만큼 물러난다. 벽, 이동 불가 지형, 적 몸체 앞에서 멈추고 아군은 지나간다
        private void TickRetreat(double now)
        {
            if (_dashStopped)
            {
                return;
            }
            float total = TalismanModifierRules.GetRetreatDistance(_skill, in _castModifiers);
            if (total <= 0f)
            {
                return;
            }
            float duration = (_skill.Effect.RetreatDuration > 0f) ? _skill.Effect.RetreatDuration : _skill.Timeline.Active;
            float ratio    = (duration > 0f) ? Mathf.Clamp01((float)(Math.Min(now, _activeEndsAt) - _resolveAt) / duration) : 1f;
            Vector3 back    = -_aim;
            Vector3 from    = _owner.Position;
            Vector3 desired = _origin + (back * (total * ratio));
            float   distance = Vector3.Dot(desired - from, back);
            if (distance <= 0f)
            {
                return;
            }
            float clear = _lineOfSight.GetClearDistance(from, back, distance);
            Vector3 navigationEnd = _navigation.Move(from, from + (back * clear));
            float   horizontalDistance = Mathf.Sqrt(HorizontalDistanceSqr(from, navigationEnd));
            clear = Mathf.Min(clear, horizontalDistance);

            float bodyRadius = Constants.Combat.PLAYER_BODY_RADIUS + Constants.Combat.SKILL_TARGET_BODY_RADIUS;
            _registry.GetLivingInRadius(from, clear + bodyRadius, ECombatFaction.Enemy, true, _targets);
            for (int i = 0; i < _targets.Count; ++i)
            {
                Vector3 position = _targets[i].Position;
                if (!BasicAttackRules.IsWithinHeight(from, position, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE))
                {
                    continue;
                }
                clear = DodgeRules.GetClearDistance(from, back, clear, position, bodyRadius);
            }
            _targets.Clear();

            if (clear < distance)
            {
                _dashStopped = true;
            }
            Vector3 stopped = from + (back * clear);
            if ((clear >= horizontalDistance) && (horizontalDistance <= distance))
            {
                stopped = navigationEnd;
            }
            else if (_navigation.TrySample(stopped, out Vector3 sampled))
            {
                stopped.y = sampled.y;
            }
            _owner.SetSkillPosition(stopped);
        }

        private bool IsHit(int hitBase, int hitCount, int combatId)
        {
            for (int i = 0; i < hitCount; ++i)
            {
                if (_hitIds[hitBase + i] == combatId)
                {
                    return true;
                }
            }

            return false;
        }

        // 이미 맞힌 대상(hitIds의 hitBase부터 hitCount개)은 고르지 않는다. 같은 거리에 나란히 선 적 사이를 오가며 관통 수를 쓰지 않게 한다
        private int FirstOnRay(Vector3 origin, Vector3 aim, float range, float halfWidth, int hitBase, int hitCount, out float along)
        {
            _registry.GetLivingInRadius(origin, range + halfWidth, ECombatFaction.Enemy, true, _targets);
            int nearest = -1;
            along = float.MaxValue;
            for (int i = 0; i < _targets.Count; ++i)
            {
                Vector3 position = _targets[i].Position;
                if ((IsHit(hitBase, hitCount, _targets[i].CombatId))
                    || (!BasicAttackRules.IsWithinHeight(origin, position, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE))
                    || (_lineOfSight.IsBlocked(origin, position))
                    || (!BasicAttackRules.TryProjectOnRay(origin, aim, position, range, halfWidth, out float candidate)))
                {
                    continue;
                }
                if ((candidate < along) || ((candidate == along) && ((nearest < 0) || (_targets[i].CombatId < _targets[nearest].CombatId))))
                {
                    along = candidate;
                    nearest = i;
                }
            }
            return nearest;
        }

        // 출발점에서 진행 방향 start ~ end 구간을 지나는 폭 있는 투사체가 처음 닿는 몸체를 고른다. 대상 몸체 반경을 더하고 start는 음수일 수 있다.
        // 이미 맞힌 대상(hitIds의 hitBase부터 hitCount개)은 고르지 않는다. 같은 거리면 전투 번호가 작은 쪽이다
        private int FirstBodyOnSegment(Vector3 origin, Vector3 aim, float start, float end, float halfWidth, int hitBase, int hitCount,
            out float along)
        {
            float search = Mathf.Max(end, -start) + halfWidth + Constants.Combat.MAX_BODY_RADIUS;
            _registry.GetLivingInRadius(origin, search, ECombatFaction.Enemy, true, _targets);
            int nearest = -1;
            along = float.MaxValue;
            for (int i = 0; i < _targets.Count; ++i)
            {
                ICombatTarget target   = _targets[i];
                Vector3       position = target.Position;
                if ((IsHit(hitBase, hitCount, target.CombatId))
                    || (!BasicAttackRules.IsWithinHeight(origin, position, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE))
                    || (_lineOfSight.IsBlocked(origin, position))
                    || (!BasicAttackRules.TryHitBodyOnSegment(origin, aim, position, start, end, halfWidth, target.BodyRadius,
                        out float candidate)))
                {
                    continue;
                }
                if ((candidate < along) || ((candidate == along) && ((nearest < 0) || (target.CombatId < _targets[nearest].CombatId))))
                {
                    along   = candidate;
                    nearest = i;
                }
            }
            return nearest;
        }

        // 반격 성공 뒤 한 번 친다. 반격 파동은 전방 부채꼴 대신 전방 직선 파동이다
        private void ApplyCounter(double now)
        {
            SkillCoreVariant core = _castModifiers.Core;
            if (core.Is(ETalismanEffectKind.CounterWave))
            {
                ApplyLine(_skill, in _castModifiers, _owner.Position, _aim, core.Length,
                    core.Width * Constants.Combat.SKILL_HALF_WIDTH_SCALE, _castId, _actionId,
                    TalismanModifierRules.GetPrimaryCoefficient(_skill, in _castModifiers), now);
                return;
            }
            ApplyArea(_skill, in _castModifiers, ESkillShape.Cone, _owner.Position, _aim,
                TalismanModifierRules.GetRadius(_skill, in _castModifiers), TalismanModifierRules.GetArcAngle(_skill, in _castModifiers),
                _castId, _actionId, _skill.Effect.DamageCoefficient, _skill.Effect.RootDuration, now);
        }

        private void ApplyArea(SkillCatalogSO.SkillDefinition skill, in SkillModifiers modifiers, ESkillShape shape, Vector3 center,
            Vector3 aim, float radius, float arcAngle, int castId, int actionId, float coefficient, float root, double now,
            EDamageFlags flags = EDamageFlags.None)
        {
            _registry.GetLivingInRadius(center, radius, ECombatFaction.Enemy, true, _targets);
            for (int i = 0; i < _targets.Count; ++i)
            {
                ICombatTarget target = _targets[i];
                if ((shape == ESkillShape.Cone)
                    && (!BasicAttackRules.IsInArc(center, aim, target.Position, radius, arcAngle)))
                {
                    continue;
                }
                if ((!BasicAttackRules.IsWithinHeight(center, target.Position, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE))
                    || (_lineOfSight.IsBlocked(center, target.Position)))
                {
                    continue;
                }
                ApplyHit(target, skill.DefinitionId, castId, actionId, center, coefficient * modifiers.DamageScale,
                    modifiers.ResonanceBonus, root, false, now, flags);
            }
            _targets.Clear();
        }

        // 시작점에서 aim 방향 길이 length, 반폭 halfWidth 직사각형 안의 적을 모두 친다
        private void ApplyLine(SkillCatalogSO.SkillDefinition skill, in SkillModifiers modifiers, Vector3 origin, Vector3 aim,
            float length, float halfWidth, int castId, int actionId, float coefficient, double now)
        {
            _registry.GetLivingInRadius(origin, length + halfWidth, ECombatFaction.Enemy, true, _targets);
            for (int i = 0; i < _targets.Count; ++i)
            {
                ICombatTarget target = _targets[i];
                if ((!BasicAttackRules.IsWithinHeight(origin, target.Position, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE))
                    || (_lineOfSight.IsBlocked(origin, target.Position))
                    || (!BasicAttackRules.TryProjectOnRay(origin, aim, target.Position, length, halfWidth, out _)))
                {
                    continue;
                }
                ApplyHit(target, skill.DefinitionId, castId, actionId, origin, coefficient * modifiers.DamageScale,
                    modifiers.ResonanceBonus, 0f, false, now);
            }
            _targets.Clear();
        }

        // 진중 응집, 제어 가능한 일반 적만 진 중심으로 한 번 옮긴다. 보스와 끌기를 구현하지 않은 대상은 그 자리에서 속박만 받는다
        // 끌기 거리가 있는 변형은 시전자 전방 지점 쪽으로 최대 그 거리만큼만, 벽 앞까지 당긴다
        private void PullTargets(ESkillShape shape, Vector3 center, Vector3 aim, float radius, float arcAngle, SkillCoreVariant core,
            double now)
        {
            bool    bounded = core.PullDistance > 0f;
            Vector3 point   = bounded ? (center + (aim * core.PullPointOffset)) : center;
            _registry.GetLivingInRadius(center, radius, ECombatFaction.Enemy, true, _targets);
            for (int i = 0; i < _targets.Count; ++i)
            {
                ICombatTarget target = _targets[i];
                if ((!(target is ISkillControlTarget control)) || (control.IsBoss) || (!control.AllowsRoot)
                    || (!(target is ISkillPullTarget pull)))
                {
                    continue;
                }
                if (((shape == ESkillShape.Cone) && (!BasicAttackRules.IsInArc(center, aim, target.Position, radius, arcAngle)))
                    || (!BasicAttackRules.IsWithinHeight(center, target.Position, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE))
                    || (_lineOfSight.IsBlocked(center, target.Position)))
                {
                    continue;
                }
                if (!bounded)
                {
                    pull.PullTo(point, now);
                    continue;
                }
                Vector3 from  = target.Position;
                Vector3 delta = point - from;
                delta.y = 0f;
                float distance = Mathf.Min(core.PullDistance, delta.magnitude);
                if (distance <= 0f)
                {
                    continue;
                }
                Vector3 direction = delta.normalized;
                float   clear     = _lineOfSight.GetClearDistance(from, direction, distance);
                if (clear <= 0f)
                {
                    continue;
                }
                Vector3 destination = from + (direction * clear);
                if (_navigation.TrySample(destination, out Vector3 sampled))
                {
                    destination.y = sampled.y;
                }
                pull.PullTo(destination, now);
            }
            _targets.Clear();
        }

        private void ApplyAscension(double now)
        {
            _registry.GetLivingInRadius(_point, _ascension.Radius, ECombatFaction.Enemy, true, _targets);
            int nearest = -1;
            float distance = float.MaxValue;
            for (int i = 0; i < _targets.Count; ++i)
            {
                ICombatTarget target = _targets[i];
                if ((!BasicAttackRules.IsWithinHeight(_point, target.Position, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE))
                    || (_lineOfSight.IsBlocked(_point, target.Position)))
                {
                    continue;
                }
                if (!_ascension.SingleTarget)
                {
                    ApplyHit(target, _ascension.DefinitionId, _castId, _actionId, _point,
                        _ascension.DamageCoefficient, 0f, _ascension.RootDuration, true, now);
                    continue;
                }
                if ((target is IDamageable damageable) && (damageable.IsInvulnerable))
                {
                    continue;
                }
                float candidate = HorizontalDistanceSqr(_point, target.Position);
                if ((candidate < distance) || ((candidate == distance) && ((nearest < 0) || (target.CombatId < _targets[nearest].CombatId))))
                {
                    distance = candidate;
                    nearest = i;
                }
            }
            if (nearest >= 0)
            {
                ApplyHit(_targets[nearest], _ascension.DefinitionId, _castId, _actionId, _point,
                    _ascension.DamageCoefficient, 0f, _ascension.RootDuration, true, now);
            }
            _targets.Clear();
        }

        private void ApplyHit(ICombatTarget target, string source, int castId, int actionId, Vector3 origin,
            float coefficient, float resonanceBonus, float root, bool ascension, double now,
            EDamageFlags flags = EDamageFlags.None, Vector3 travelDirection = default)
        {
            var request = new DamageRequest(_owner.CombatId, target.CombatId, castId, actionId,
                _owner.AttackPower * coefficient, flags, origin, source, travelDirection);
            DamageResult result = _resolver.Apply(in request);
            if (_playerClass == EPlayerClass.Summoner)
            {
                _resonance.Record(in request, in result, ascension ? EResonanceSource.Ascension : EResonanceSource.Skill, resonanceBonus);
            }
            if ((result.CountsAsHit) && (target.LifeState == EParticipantLifeState.Alive) && (root > 0f)
                && (target is ISkillControlTarget control) && (control.AllowsRoot))
            {
                float duration = root * control.RootDurationScale;
                control.ApplyRoot(source, control.IsBoss ? Mathf.Min(duration, Constants.Combat.BOSS_ROOT_MAX_SECONDS) : duration, now);
            }
        }

        /// <summary> 방어 가능한 전방 타격마다 검사, 창 전체를 첫 성공 때 닫지 않는다 </summary>
        public bool CanGuard(in DamageRequest request, double now)
        {
            return (IsGuardWindow) && (now >= _resolveAt) && (now < _activeEndsAt) && (!request.IsUnblockable)
                && (BasicAttackRules.IsInArc(_owner.Position, _aim, request.SourcePosition, float.MaxValue, _skill.Area.ArcAngle));
        }

        public void MarkGuardSuccess() { _guardSucceeded = true; }

        public void OnBasicHit(in DamageRequest request, in DamageResult result)
        {
            // 한 번 휘두르기는 한 시전 번호를 쓰므로 여러 적을 맞혀도 스택은 한 번만 오른다
            if ((_playerClass == EPlayerClass.Mageknight) && (result.CountsAsHit) && (request.CastInstanceId != _lastStackCastId))
            {
                _lastStackCastId = request.CastInstanceId;
                AddStacks(_catalog.ClassStackPerBasicSwing);
            }
            if (_playerClass == EPlayerClass.Summoner)
            {
                _resonance.Record(in request, in result, EResonanceSource.Basic);
                _resonance.ReleaseCast(request.CastInstanceId);
            }
        }

        public void OnCompanionHit(in DamageRequest request, in DamageResult result)
        {
            _resonance.Record(in request, in result, EResonanceSource.Companion);
            _resonance.ReleaseCast(request.CastInstanceId);
        }

        public bool TryGetMarkedTarget(Vector3 origin, out ICombatTarget target)
        {
            target = null;
            float nearest = float.MaxValue;
            bool hasBoss = false;
            for (int i = 0; i < _effects.Length; ++i)
            {
                ref Effect effect = ref _effects[i];
                if ((!effect.Active) || (effect.Kind != EEffectKind.Field) || (effect.Skill.Effect.AllyDamageBonus <= 0f))
                {
                    continue;
                }
                _registry.GetLivingInRadius(effect.Origin, effect.Radius, ECombatFaction.Enemy, true, _targets);
                for (int j = 0; j < _targets.Count; ++j)
                {
                    ICombatTarget candidate = _targets[j];
                    bool boss = (candidate is ISkillControlTarget control) && (control.IsBoss);
                    float distance = HorizontalDistanceSqr(origin, candidate.Position);
                    if ((hasBoss) && (!boss))
                    {
                        continue;
                    }
                    if ((target == null) || ((boss) && (!hasBoss)) || (distance < nearest))
                    {
                        if ((_lineOfSight.IsBlocked(origin, candidate.Position))
                            || (!BasicAttackRules.IsWithinHeight(origin, candidate.Position, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE)))
                        {
                            continue;
                        }
                        target = candidate;
                        nearest = distance;
                        hasBoss = boss;
                    }
                }
                _targets.Clear();
            }
            return target != null;
        }

        public float GetCompanionDamageBonus(ICombatTarget target)
        {
            float bonus = 0f;
            for (int i = 0; i < _effects.Length; ++i)
            {
                ref Effect effect = ref _effects[i];
                if ((effect.Active) && (effect.Kind == EEffectKind.Field) && (effect.Skill.Effect.AllyDamageBonus > bonus)
                    && (HorizontalDistanceSqr(effect.Origin, target.Position) <= effect.Radius * effect.Radius))
                {
                    bonus = effect.Skill.Effect.AllyDamageBonus;
                }
            }
            return bonus;
        }

        public SkillCombatSnapshot CaptureSnapshot()
        {
            return new SkillCombatSnapshot(_energy, Resonance, _readyAt[Constants.Combat.SKILL_SLOT_Q],
                _readyAt[Constants.Combat.SKILL_SLOT_W], _readyAt[Constants.Combat.SKILL_SLOT_E],
                _readyAt[Constants.Combat.SKILL_SLOT_R], _ascensionReadyAt);
        }

        public void Restore(in SkillCombatSnapshot snapshot)
        {
            _energy = Mathf.Clamp(snapshot.Energy, 0f, MaxEnergy);
            _resonance.Restore(snapshot.Resonance);
            _ascensionReadyAt = snapshot.AscensionReadyAt;
            for (int i = 0; i < _readyAt.Length; ++i)
            {
                _readyAt[i] = snapshot.GetReadyAt(i);
            }
        }

        public void Shutdown()
        {
            Cancel();
            for (int i = 0; i < _companions.Length; ++i)
            {
                _companions[i].Stop();
            }
            for (int i = 0; i < _effects.Length; ++i)
            {
                if (_effects[i].Active)
                {
                    _resonance.ReleaseCast(_effects[i].CastId);
                    _effects[i].Active = false;
                }
            }
            for (int i = 0; i < _burns.Length; ++i)
            {
                _burns[i].Active = false;
            }
            ReplicateCompanions();
        }

        /// <summary> 본체 사망은 동료만 즉시 복귀시키고 마검사, 궁수 스택을 비운다, 생성된 장판과 화상은 원래 수명을 유지한다. 궁수 속성은 유지한다 </summary>
        public void OnOwnerDeath()
        {
            Cancel();
            _stacks         = 0;
            _lastHitElement = EArcherElement.None;
            for (int i = 0; i < _companions.Length; ++i)
            {
                _companions[i].Stop();
            }
            ReplicateCompanions();
        }

        public int GetActiveCompanionId(int slot)
        {
            if ((slot < 0) || (slot >= _companions.Length) || (!_companions[slot].IsActive))
            {
                return CombatIds.NONE;
            }
            return _companions[slot].CombatId;
        }

        /// <summary> 서버 스포너가 생성한 동료 상태를 연결한다, 플레이어가 생성 요청을 보내지 않는다 </summary>
        public bool BindCompanionState(int slot, CompanionCombatState state)
        {
            if ((_playerClass != EPlayerClass.Summoner) || (slot < 0) || (slot >= _companions.Length) || (state == null))
            {
                return false;
            }
            _companionStates[slot] = state;
            ReplicateCompanions();
            return true;
        }

        public void UnbindCompanionState(int slot)
        {
            if ((slot < 0) || (slot >= _companionStates.Length))
            {
                return;
            }
            _companionStates[slot] = null;
        }

        private void ReplicateCompanions()
        {
            for (int i = 0; i < _companionStates.Length; ++i)
            {
                CompanionCombatState state = _companionStates[i];
                if (state == null)
                {
                    continue;
                }
                CompanionServer companion = _companions[i];
                state.SetCombatSnapshot(companion.CombatId, companion.OwnerCombatId, companion.TargetId, (byte)i,
                    companion.IsActive, companion.ExpiresAt, companion.Health, companion.MaxHealth, companion.Position,
                    FacingRules.EncodeYaw(companion.Forward));
            }
        }

        /// <summary> 재접속 유예 동안 남은 효과와 자원 상태를 같은 실행 객체에서 계속 돌린다 </summary>
        public void Detach()
        {
            Cancel();
            _owner.Detach();
        }

        public void Rebind(ISkillCaster owner) { _owner.Rebind(owner); }

        private bool HasEffectCast(int castId)
        {
            for (int i = 0; i < _effects.Length; ++i)
            {
                if ((_effects[i].Active) && (_effects[i].CastId == castId))
                {
                    return true;
                }
            }
            return false;
        }

        private int CountFreeEffects()
        {
            int count = 0;
            for (int i = 0; i < _effects.Length; ++i)
            {
                if (!_effects[i].Active)
                {
                    ++count;
                }
            }
            return count;
        }

        private static bool IsValidModifiers(in SkillModifiers modifiers)
        {
            return (IsNonNegativeFinite(modifiers.EnergyScale)) && (IsNonNegativeFinite(modifiers.CooldownScale))
                && (IsNonNegativeFinite(modifiers.DamageScale)) && (IsNonNegativeFinite(modifiers.ResonanceBonus))
                && (IsNonNegativeFinite(modifiers.AttackIntervalScale)) && (IsFinite(modifiers.RadiusDelta))
                && (IsFinite(modifiers.ArcAngleDelta)) && (IsFinite(modifiers.RangeDelta)) && (IsFinite(modifiers.MoveDistanceDelta))
                && (IsFinite(modifiers.DurationDelta)) && (IsFinite(modifiers.GuardDurationDelta))
                && (IsNonNegativeFinite(modifiers.FixedRadius)) && (IsNonNegativeFinite(modifiers.FixedArcAngle))
                && (IsNonNegativeFinite(modifiers.MarkedDamageBonus)) && (IsFinite(modifiers.WidthDelta))
                && (IsNonNegativeFinite(modifiers.FixedWidth)) && (IsFinite(modifiers.FanAngleDelta));
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }

        private int GetFreeEffect()
        {
            for (int i = 0; i < _effects.Length; ++i)
            {
                if (!_effects[i].Active)
                {
                    return i;
                }
            }
            return -1;
        }

        private static float HorizontalDistanceSqr(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x;
            float z = a.z - b.z;
            return (x * x) + (z * z);
        }

        private static bool IsFinite(Vector3 value)
        {
            return (IsNonNegativeFinite(Mathf.Abs(value.x))) && (IsNonNegativeFinite(Mathf.Abs(value.y)))
                && (IsNonNegativeFinite(Mathf.Abs(value.z)));
        }

        private static bool IsNonNegativeFinite(float value)
        {
            return (value >= 0f) && (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }
    }
}
