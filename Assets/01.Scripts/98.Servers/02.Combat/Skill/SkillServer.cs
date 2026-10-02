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
    public sealed class SkillServer : ICompanionContext
    {
        /****************************************
        *                Fields
        ****************************************/

        private struct Effect
        {
            public bool                           Active;
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
        }

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
        private readonly List<ICombatTarget>              _targets;
        private readonly CompanionServer[]                _companions;
        private readonly CompanionCombatState[]           _companionStates;
        private readonly ResonanceLedger                  _resonance;

        private SkillCatalogSO.SkillDefinition     _skill;
        private SkillCatalogSO.AscensionDefinition _ascension;
        private SkillModifiers                     _castModifiers;
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

        /****************************************
        *              Properties
        ****************************************/

        public float             Energy           => _energy;
        public EPlayerClass      PlayerClass      => _playerClass;
        public float             MaxEnergy        => _player.BaseMaxEnergy;
        public float             Resonance        => _resonance.Value;
        public ESkillPhase       Phase            => _phase;
        public int               CastInstanceId   => _castId;
        public int               ActionInstanceId => _actionId;
        public int               Slot             => _slot;
        public ESkillRequestKind Kind             => _kind;
        public Vector3           Origin           => _origin;
        public Vector3           Aim              => _aim;
        public Vector3           Point            => _point;
        public double            ResolveAt        => _resolveAt;
        public double            AscensionReadyAt => _ascensionReadyAt;
        public double            NextActionAt     => _recoveryEndsAt;
        public bool              BlocksMovement   => _phase != ESkillPhase.Idle;
        public bool              IsGuardWindow    => (_phase == ESkillPhase.Active) && (IsCounter);
        private bool IsCounter => (_kind == ESkillRequestKind.Regular) && (_playerClass == EPlayerClass.Warrior)
            && (_slot == Constants.Combat.WARRIOR_COUNTER_SLOT);

        /****************************************
        *                Methods
        ****************************************/

        public SkillServer(ISkillCaster owner, EPlayerClass playerClass, int participantId, SkillCatalogSO catalog,
            PlayerConfigSO player, CombatTargetRegistry registry, IDamageResolver resolver, CombatLineOfSight lineOfSight,
            double now, ITalismanModifierResolver modifiers = null, ISkillNavigation navigation = null)
        {
            if ((owner == null) || (catalog == null) || (player == null) || (registry == null) || (resolver == null) || (lineOfSight == null))
            {
                throw new ArgumentNullException(nameof(owner));
            }
            _owner           = new SkillCasterAnchor(owner);
            _playerClass     = playerClass;
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
            _targets         = new List<ICombatTarget>(Constants.Combat.MAX_COMBAT_TARGETS);
            _resonance       = new ResonanceLedger(catalog, Constants.Combat.RESONANCE_LEDGER_CAPACITY);
            _companions      = new CompanionServer[Constants.Combat.COMPANIONS_PER_PLAYER];
            _companionStates = new CompanionCombatState[Constants.Combat.COMPANIONS_PER_PLAYER];
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
            _energy   = player.BaseMaxEnergy;
            _lastTick = now;
        }

        public double GetReadyAt(int slot) => _readyAt[slot];
        public CompanionServer GetCompanion(int slot) => _companions[slot];

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
            bool isPoint = (ascension != null) || (skill.Area.AimMode == ESkillAimMode.Point);
            float range = (ascension != null) ? ascension.CastRange : skill.Area.CastRange;
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
            SkillModifiers modifier = (skill != null) ? (_modifiers?.Resolve(_participantId, skill) ?? SkillModifiers.Identity) : SkillModifiers.Identity;
            if ((!IsNonNegativeFinite(modifier.EnergyScale)) || (!IsNonNegativeFinite(modifier.CooldownScale))
                || (!IsNonNegativeFinite(modifier.DamageScale)) || (!IsNonNegativeFinite(modifier.ResonanceBonus)))
            {
                reason = ERejectReason.InvalidTarget;
                return false;
            }
            float cost = (skill != null) ? skill.EnergyCost * modifier.EnergyScale : ascension.ResonanceCost;
            float cooldown = (skill != null) ? skill.Cooldown * modifier.CooldownScale : _catalog.AscensionSharedCooldown;
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
            if ((skill != null) && ((skill.Area.Shape == ESkillShape.Projectile)
                || ((skill.Area.Shape != ESkillShape.Summon) && (skill.Effect.Duration > 0f))) && (GetFreeEffect() < 0))
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
            _skill         = skill;
            _ascension     = ascension;
            _castModifiers = modifier;
            _kind          = kind;
            _slot          = slot;
            _origin        = origin;
            _aim           = FacingRules.DecodeYaw(aimYaw);
            _point         = isPoint ? point : origin + (_aim * ((skill != null) ? skill.Area.CenterOffset : 0f));
            _castId        = castId;
            _actionId      = _resolver.IssueInstanceId();
            float windup = (skill != null) ? skill.Timeline.Windup : ascension.Windup;
            float active = (skill != null) ? skill.Timeline.Active : 0f;
            float recovery = (skill != null) ? skill.Timeline.Recovery : ascension.Recovery;
            _resolveAt      = now + windup;
            _activeEndsAt   = _resolveAt + active;
            _recoveryEndsAt = _activeEndsAt + recovery;
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
            if (!HasEffectCast(_castId))
            {
                _resonance.ReleaseCast(_castId);
            }
            _phase          = ESkillPhase.Idle;
            _guardSucceeded = false;
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
            if ((_phase == ESkillPhase.Active) && (_skill != null) && (_skill.Area.Shape == ESkillShape.Line))
            {
                TickDash(now);
            }
            if ((_phase == ESkillPhase.Active) && (now >= _activeEndsAt))
            {
                if ((IsCounter) && (_guardSucceeded))
                {
                    ApplyArea(_skill, _castModifiers, _owner.Position, _aim, _castId, _actionId,
                        _skill.Effect.DamageCoefficient, _skill.Effect.RootDuration, false, now);
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
            if ((IsCounter) || (_skill.Area.Shape == ESkillShape.Line))
            {
                return;
            }
            if (_skill.Area.Shape == ESkillShape.Summon)
            {
                if (!_companions[_slot].Start(_skill, _castId, _resolveAt))
                {
                    Debug.LogError("[Combat] 예약된 동료의 소환 확정에 실패했다");
                }
                return;
            }
            if (_skill.Area.Shape == ESkillShape.Projectile)
            {
                AddEffect(_resolveAt);
                return;
            }
            if (_skill.Effect.DamageCoefficient > 0f)
            {
                Vector3 center = (_skill.Area.Shape == ESkillShape.Cone) ? _owner.Position : _point;
                ApplyArea(_skill, _castModifiers, center, _aim, _castId, _actionId,
                    _skill.Effect.DamageCoefficient, _skill.Effect.RootDuration, false, now);
            }
            if (_skill.Effect.Duration > 0f)
            {
                AddEffect(_resolveAt);
            }
        }

        private void AddEffect(double now)
        {
            int index = GetFreeEffect();
            if (index < 0)
            {
                return;
            }
            bool projectile = _skill.Area.Shape == ESkillShape.Projectile;
            _effects[index] = new Effect
            {
                Active = true, Skill = _skill, Modifiers = _castModifiers, CastId = _castId, ActionId = _actionId,
                Origin = projectile ? _owner.Position : _point, Aim = _aim, LastTime = now,
                EndsAt = now + (projectile ? (_skill.Area.CastRange / _skill.Area.ProjectileSpeed) : _skill.Effect.Duration),
                NextTick = Constants.Combat.FIELD_TICKS_IMMEDIATELY ? now : now + _skill.Effect.TickInterval
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
                if (effect.Skill.Area.Shape == ESkillShape.Projectile)
                {
                    TickProjectile(ref effect, now);
                }
                else if (effect.Skill.Effect.TickCoefficient > 0f)
                {
                    // 경계 마지막 틱까지 포함하고 한 시전 번호 아래 각 주기에 새 동작 번호를 발급한다
                    while ((effect.NextTick <= now) && (effect.NextTick <= effect.EndsAt))
                    {
                        int action = _resolver.IssueInstanceId();
                        ApplyArea(effect.Skill, effect.Modifiers, effect.Origin, effect.Aim, effect.CastId, action,
                            effect.Skill.Effect.TickCoefficient, 0f, false, now);
                        effect.NextTick += effect.Skill.Effect.TickInterval;
                    }
                }
                if (now >= effect.EndsAt)
                {
                    effect.Active = false;
                }
                if (!effect.Active)
                {
                    _resonance.ReleaseCast(effect.CastId);
                }
            }
        }

        private void TickProjectile(ref Effect effect, double now)
        {
            float distance = Mathf.Min(effect.Skill.Area.CastRange - effect.Travelled,
                effect.Skill.Area.ProjectileSpeed * (float)Math.Max(0.0, now - effect.LastTime));
            float clear = _lineOfSight.GetClearDistance(effect.Origin, effect.Aim, distance);
            int index = FirstOnRay(effect.Origin, effect.Aim, clear, effect.Skill.Area.Width * Constants.Combat.SKILL_HALF_WIDTH_SCALE,
                out _);
            if (index >= 0)
            {
                ApplyHit(_targets[index], effect.Skill.DefinitionId, effect.CastId, effect.ActionId, effect.Origin,
                    effect.Skill.Effect.DamageCoefficient * effect.Modifiers.DamageScale, effect.Modifiers.ResonanceBonus, 0f, false, now);
                effect.Active = false;
            }
            effect.Origin += effect.Aim * clear;
            effect.Travelled += clear;
            effect.LastTime = now;
            if ((clear < distance) || (effect.Travelled >= effect.Skill.Area.CastRange))
            {
                effect.Active = false;
            }
            _targets.Clear();
        }

        private void TickDash(double now)
        {
            if (_dashStopped)
            {
                return;
            }
            float elapsed = (float)Math.Max(0.0, Math.Min(now, _activeEndsAt) - _resolveAt);
            Vector3 desired = _origin + (_aim * (_skill.Area.CastRange * elapsed / _skill.Timeline.Active));
            Vector3 from = _owner.Position;
            Vector3 delta = desired - from;
            float distance = delta.magnitude;
            float clear = _lineOfSight.GetClearDistance(from, _aim, distance);
            Vector3 navigationEnd = _navigation.Move(from, from + (_aim * clear));
            float horizontalDistance = Mathf.Sqrt(HorizontalDistanceSqr(from, navigationEnd));
            clear = Mathf.Min(clear, horizontalDistance);
            float radius = Constants.Combat.SKILL_TARGET_BODY_RADIUS;
            int index = FirstOnRay(from, _aim, clear + radius, _skill.Area.Width * Constants.Combat.SKILL_HALF_WIDTH_SCALE, out float along);
            if (index >= 0)
            {
                ApplyHit(_targets[index], _skill.DefinitionId, _castId, _actionId, from,
                    _skill.Effect.DamageCoefficient * _castModifiers.DamageScale, _castModifiers.ResonanceBonus, 0f, false, now);
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

        private int FirstOnRay(Vector3 origin, Vector3 aim, float range, float halfWidth, out float along)
        {
            _registry.GetLivingInRadius(origin, range + halfWidth, ECombatFaction.Enemy, true, _targets);
            int nearest = -1;
            along = float.MaxValue;
            for (int i = 0; i < _targets.Count; ++i)
            {
                Vector3 position = _targets[i].Position;
                if ((!BasicAttackRules.IsWithinHeight(origin, position, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE))
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

        private void ApplyArea(SkillCatalogSO.SkillDefinition skill, SkillModifiers modifiers, Vector3 center, Vector3 aim,
            int castId, int actionId, float coefficient, float root, bool ascension, double now)
        {
            _registry.GetLivingInRadius(center, skill.Area.Radius, ECombatFaction.Enemy, true, _targets);
            for (int i = 0; i < _targets.Count; ++i)
            {
                ICombatTarget target = _targets[i];
                if ((skill.Area.Shape == ESkillShape.Cone)
                    && (!BasicAttackRules.IsInArc(center, aim, target.Position, skill.Area.Radius, skill.Area.ArcAngle)))
                {
                    continue;
                }
                if ((!BasicAttackRules.IsWithinHeight(center, target.Position, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE))
                    || (_lineOfSight.IsBlocked(center, target.Position)))
                {
                    continue;
                }
                ApplyHit(target, skill.DefinitionId, castId, actionId, center, coefficient * modifiers.DamageScale,
                    modifiers.ResonanceBonus, root, ascension, now);
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
            float coefficient, float resonanceBonus, float root, bool ascension, double now)
        {
            var request = new DamageRequest(_owner.CombatId, target.CombatId, castId, actionId,
                _owner.AttackPower * coefficient, EDamageFlags.None, origin, source);
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
                if ((!effect.Active) || (effect.Skill.Effect.AllyDamageBonus <= 0f))
                {
                    continue;
                }
                _registry.GetLivingInRadius(effect.Origin, effect.Skill.Area.Radius, ECombatFaction.Enemy, true, _targets);
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
                if ((effect.Active) && (effect.Skill.Effect.AllyDamageBonus > bonus)
                    && (HorizontalDistanceSqr(effect.Origin, target.Position) <= effect.Skill.Area.Radius * effect.Skill.Area.Radius))
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
            ReplicateCompanions();
        }

        /// <summary> 본체 사망은 동료만 즉시 복귀시키고 생성된 장판은 원래 수명을 유지한다 </summary>
        public void OnOwnerDeath()
        {
            Cancel();
            for (int i = 0; i < _companions.Length; ++i)
            {
                _companions[i].Stop();
            }
            ReplicateCompanions();
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
