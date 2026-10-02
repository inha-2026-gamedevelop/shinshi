// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.AI;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary> 풀에서 재사용하는 계약 동료 한 체의 수명, 서버 이동, 피해와 소유 관계 </summary>
    public sealed class CompanionServer : IDamageable
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MELEE_SLOT  = 0;
        private const int RANGED_SLOT = 1;

        private readonly ISkillCaster         _owner;
        private readonly SkillCatalogSO       _catalog;
        private readonly CombatTargetRegistry _registry;
        private readonly IDamageResolver      _resolver;
        private readonly CombatLineOfSight    _lineOfSight;
        private readonly ICompanionContext    _context;
        private readonly List<ICombatTarget>  _targets;
        private readonly NavMeshPath          _navPath;
        private readonly PathFollower         _path;

        private SkillCatalogSO.SkillDefinition _skill;
        private int                            _combatId            = CombatIds.NONE;
        private int                            _castId              = CombatIds.NONE;
        private int                            _targetId            = CombatIds.NONE;
        private bool                           _active              = false;
        private bool                           _reserved            = false;
        private bool                           _ranged              = false;
        private float                          _health              = 0f;
        private float                          _maxHealth           = 0f;
        private float                          _defense             = 0f;
        private Vector3                        _position            = Vector3.zero;
        private Vector3                        _forward             = Vector3.forward;
        private double                         _expiresAt           = 0.0;
        private double                         _nextAttackAt        = 0.0;
        private double                         _nextRepathAt        = 0.0;
        private double                         _unreachableSince    = double.NaN;
        private Vector3                        _pathDestination     = Vector3.zero;
        private bool                           _hasDestination      = false;
        private bool                           _projectileActive    = false;
        private Vector3                        _projectilePosition  = Vector3.zero;
        private Vector3                        _projectileDirection = Vector3.forward;
        private float                          _projectileRemaining = 0f;
        private double                         _projectileLastTime  = 0.0;
        private int                            _projectileActionId  = CombatIds.NONE;

        /****************************************
        *              Properties
        ****************************************/

        public bool                  IsActive        => _active;
        public int                   TargetId        => _targetId;
        public int                   SummonCastId    => _castId;
        public double                ExpiresAt       => _expiresAt;
        public int                   CombatId        => _combatId;
        public ECombatFaction        Faction         => _owner.Faction;
        public EParticipantLifeState LifeState       => _active ? EParticipantLifeState.Alive : EParticipantLifeState.Dead;
        public bool                  IsSummon        => true;
        public int                   OwnerCombatId   => _owner.CombatId;
        public Vector3               Position        => _position;
        public Vector3               Forward         => _forward;
        public float                 BodyRadius      => Constants.Combat.COMPANION_BODY_RADIUS;
        public float                 Health          => _health;
        public float                 MaxHealth       => _maxHealth;
        public float                 Defense         => _defense;
        public bool                  IsInvulnerable  => false;
        public bool                  IsGuardingFront => false;

        /****************************************
        *                Methods
        ****************************************/

        public CompanionServer(ISkillCaster owner, SkillCatalogSO catalog, CombatTargetRegistry registry,
            IDamageResolver resolver, CombatLineOfSight lineOfSight, ICompanionContext context)
        {
            if ((owner == null) || (catalog == null) || (registry == null) || (resolver == null)
                || (lineOfSight == null) || (context == null))
            {
                throw new ArgumentNullException(nameof(owner), "동료의 소유자와 서버 판정 의존성이 필요하다");
            }

            _owner       = owner;
            _catalog     = catalog;
            _registry    = registry;
            _resolver    = resolver;
            _lineOfSight = lineOfSight;
            _context     = context;
            _targets     = new List<ICombatTarget>(Constants.Combat.MAX_COMBAT_TARGETS);
            _navPath     = new NavMeshPath();
            _path        = new PathFollower(Constants.Player.MAX_PATH_CORNERS);
        }

        /// <summary> 소환 완료 시 호출한다, 거절된 재호출은 기존 동료를 유지한다 </summary>
        public bool Start(SkillCatalogSO.SkillDefinition skill, int castId, double now)
        {
            if ((skill == null) || (skill.Area == null) || (skill.Effect == null)
                || (skill.PlayerClass != EPlayerClass.Summoner) || (skill.Area.Shape != ESkillShape.Summon)
                || ((skill.Slot != MELEE_SLOT) && (skill.Slot != RANGED_SLOT)) || (castId == CombatIds.NONE)
                || (!IsFinite(now)) || (_owner.CombatId == CombatIds.NONE)
                || (_owner.LifeState != EParticipantLifeState.Alive)
                || (!IsPositiveFinite(skill.Effect.Duration)) || (!IsPositiveFinite(skill.Effect.CompanionAttackInterval))
                || (!IsPositiveFinite(skill.Effect.CompanionAttackRange))
                || (!IsPositiveFinite(skill.Effect.CompanionAttackCoefficient))
                || (!IsPositiveFinite(_owner.MaxHealth * _catalog.CompanionHealthRate))
                || (!IsNonNegativeFinite(_owner.Defense * _catalog.CompanionDefenseRate)))
            {
                return false;
            }

            if ((!_active) && (!TryReserve()))
            {
                return false;
            }

            if (_active)
            {
                int previousId = _combatId;
                // 같은 객체의 번호를 바꾼 뒤 제거하면 목록에 옛 등록이 남으므로 옛 번호로 먼저 제거한다
                if (!_registry.Unregister(previousId))
                {
                    return false;
                }

                _combatId = _registry.IssueCombatId();
                if (!_registry.Register(this))
                {
                    _combatId = previousId;
                    _registry.Register(this);
                    return false;
                }
            }

            _skill            = skill;
            _castId           = castId;
            _ranged           = skill.Slot == RANGED_SLOT;
            _maxHealth        = _owner.MaxHealth * _catalog.CompanionHealthRate;
            _health           = _maxHealth;
            _defense          = _owner.Defense * _catalog.CompanionDefenseRate;
            _position         = _owner.Position;
            _forward          = FacingRules.TryGetHorizontalDirection(_owner.Forward, out Vector3 facing) ? facing : Vector3.forward;
            _expiresAt        = now + skill.Effect.Duration;
            _nextAttackAt     = now + skill.Effect.CompanionAttackInterval;
            _nextRepathAt     = now;
            _unreachableSince = double.NaN;
            _hasDestination   = false;
            _targetId         = CombatIds.NONE;
            _projectileActive = false;
            _path.Stop();
            _reserved = false;
            _active   = true;
            return true;
        }

        /// <summary> 비용 확정 전에 자리를 확보한다, 준비 중인 신규 동료는 사망 상태라 판정 대상에서 빠진다 </summary>
        public bool TryReserve()
        {
            if ((_active) || (_reserved))
            {
                return true;
            }

            int previousId = _combatId;
            _combatId = _registry.IssueCombatId();
            if (!_registry.Register(this))
            {
                _combatId = previousId;
                return false;
            }

            _reserved = true;
            return true;
        }

        /// <summary> 발동 전 신규 소환의 예약만 해제한다, 재호출이 취소되어도 기존 활성 동료는 유지한다 </summary>
        public void CancelReservation()
        {
            if ((!_reserved) || (_active))
            {
                return;
            }

            _registry.Unregister(_combatId);
            _reserved = false;
        }

        /// <summary> 계약 공간으로 복귀한다, 소환 기술의 재사용 시간과 본체 부활 횟수는 바꾸지 않는다 </summary>
        public void Stop()
        {
            if ((!_active) && (!_reserved))
            {
                return;
            }

            _registry.Unregister(_combatId);
            _active           = false;
            _reserved         = false;
            _projectileActive = false;
            _health           = 0f;
            _targetId         = CombatIds.NONE;
            _hasDestination   = false;
            _unreachableSince = double.NaN;
            _path.Stop();
        }

        /// <summary> 플레이어 이동과 같은 틱 경로 예산을 쓴다, 동료는 서버 논리 좌표만 가진다 </summary>
        public void Tick(float dt, double now, PathRequestBudget budget)
        {
            if ((!_active) || (budget == null) || (!IsFinite(now)) || (!IsNonNegativeFinite(dt)))
            {
                return;
            }

            if ((_owner.LifeState != EParticipantLifeState.Alive) || (now >= _expiresAt))
            {
                Stop();
                return;
            }

            TickProjectile(now);

            float ownerDistanceSqr = HorizontalDistanceSqr(_position, _owner.Position);
            if ((ownerDistanceSqr >= (_catalog.CompanionWarpDistance * _catalog.CompanionWarpDistance))
                || ((!double.IsNaN(_unreachableSince)) && ((now - _unreachableSince) >= _catalog.CompanionStuckSeconds)))
            {
                TryWarpToOwner(now);
                return;
            }

            if (ownerDistanceSqr >= (_catalog.CompanionRecallDistance * _catalog.CompanionRecallDistance))
            {
                _targetId = CombatIds.NONE;
                MoveTo(_owner.Position, dt, now, budget);
                return;
            }

            ICombatTarget target = SelectTarget();
            _targetId = (target == null) ? CombatIds.NONE : target.CombatId;
            if (target == null)
            {
                MoveTo(_owner.Position, dt, now, budget);
                return;
            }

            Vector3 delta = target.Position - _position;
            if (FacingRules.TryGetHorizontalDirection(delta, out Vector3 direction))
            {
                _forward = direction;
            }

            float range = _skill.Effect.CompanionAttackRange;
            bool inRange = HorizontalDistanceSqr(_position, target.Position) <= (range * range);
            bool blocked = _lineOfSight.IsBlocked(_position, target.Position);
            if ((!inRange) || (blocked))
            {
                // 공격 가능한 거리에 도착하면 멈춰 원거리 동료가 타깃 몸체까지 달려들지 않게 한다
                // 벽에 가려진 타깃은 가시 위치가 나올 때까지 타깃 방향의 내비 경로를 따라간다
                float stoppingRange = Mathf.Max(0f, range - Constants.Combat.SKILL_TARGET_BODY_RADIUS);
                Vector3 destination = blocked ? target.Position : (target.Position - (_forward * stoppingRange));
                MoveTo(destination, dt, now, budget);
                return;
            }

            _path.Stop();
            _hasDestination   = false;
            _unreachableSince = double.NaN;

            if ((now < _nextAttackAt) || ((_ranged) && (_projectileActive))
                || (Mathf.Abs(target.Position.y - _position.y) > Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE))
            {
                return;
            }

            // 밀린 공격을 한 틱에 몰아 실행하지 않고 각 실제 공격 동작에 새 번호를 발급한다
            _nextAttackAt = now + _skill.Effect.CompanionAttackInterval;
            int actionId = _resolver.IssueInstanceId();
            if (_ranged)
            {
                _projectileActive    = true;
                _projectilePosition  = _position;
                _projectileDirection = _forward;
                _projectileRemaining = range;
                _projectileLastTime  = now;
                _projectileActionId  = actionId;
                return;
            }

            ApplyAttack(target, _position, actionId);
        }

        public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
        {
            if ((!_active) || (!IsNonNegativeFinite(appliedAmount)))
            {
                return;
            }

            _health = Mathf.Max(0f, _health - appliedAmount);
            if ((isLethal) || (_health <= Constants.Combat.HEALTH_EPSILON))
            {
                Stop();
            }
        }

        private ICombatTarget SelectTarget()
        {
            if ((_context.TryGetMarkedTarget(_position, out ICombatTarget marked)) && (IsValidTarget(marked)))
            {
                return marked;
            }

            int count = _registry.GetLivingInRadius(_owner.Position, _catalog.CompanionSearchRadius,
                ECombatFaction.Enemy, false, _targets);
            ICombatTarget nearest = null;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; ++i)
            {
                ICombatTarget candidate = _targets[i];
                float distance = HorizontalDistanceSqr(_position, candidate.Position);
                if ((distance < nearestDistance)
                    || ((distance == nearestDistance) && ((nearest == null) || (candidate.CombatId < nearest.CombatId))))
                {
                    nearest         = candidate;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private bool IsValidTarget(ICombatTarget target)
        {
            return (target != null) && (target.Faction == ECombatFaction.Enemy)
                && (target.LifeState == EParticipantLifeState.Alive) && (!target.IsSummon)
                && (_registry.TryGet(target.CombatId, out ICombatTarget registered)) && (ReferenceEquals(target, registered));
        }

        private void TickProjectile(double now)
        {
            if ((!_projectileActive) || (now <= _projectileLastTime))
            {
                return;
            }

            float distance = Mathf.Min(_projectileRemaining,
                Constants.Combat.COMPANION_PROJECTILE_SPEED * (float)(now - _projectileLastTime));
            float clear = _lineOfSight.GetClearDistance(_projectilePosition, _projectileDirection, distance);
            ICombatTarget hit = FirstTargetOnRay(_projectilePosition, _projectileDirection, clear);
            if (hit != null)
            {
                _projectileActive = false;
                ApplyAttack(hit, _projectilePosition, _projectileActionId);
            }

            _projectilePosition  += _projectileDirection * clear;
            _projectileRemaining -= clear;
            _projectileLastTime = now;
            if ((clear < distance) || (_projectileRemaining <= 0f))
            {
                _projectileActive = false;
            }
        }

        private ICombatTarget FirstTargetOnRay(Vector3 origin, Vector3 direction, float range)
        {
            float radius = Constants.Combat.SKILL_TARGET_BODY_RADIUS;
            int count = _registry.GetLivingInRadius(origin, range + radius, ECombatFaction.Enemy, false, _targets);
            ICombatTarget first = null;
            float firstDistance = float.PositiveInfinity;
            for (int i = 0; i < count; ++i)
            {
                ICombatTarget candidate = _targets[i];
                Vector3 offset = candidate.Position - origin;
                if (Mathf.Abs(offset.y) > Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE)
                {
                    continue;
                }

                offset.y = 0f;
                float along = Vector3.Dot(offset, direction);
                float sideSqr = Mathf.Max(0f, offset.sqrMagnitude - (along * along));
                if ((along < 0f) || (sideSqr > (radius * radius)))
                {
                    continue;
                }

                float distance = Mathf.Max(0f, along - Mathf.Sqrt((radius * radius) - sideSqr));
                if ((distance > range) || (_lineOfSight.IsBlocked(origin, candidate.Position)))
                {
                    continue;
                }

                if ((distance < firstDistance)
                    || ((distance == firstDistance) && ((first == null) || (candidate.CombatId < first.CombatId))))
                {
                    first         = candidate;
                    firstDistance = distance;
                }
            }

            return first;
        }

        private void ApplyAttack(ICombatTarget target, Vector3 origin, int actionId)
        {
            float bonus = _context.GetCompanionDamageBonus(target);
            if ((!IsNonNegativeFinite(bonus)) || (!IsNonNegativeFinite(_owner.AttackPower)))
            {
                return;
            }

            float rawDamage = _owner.AttackPower * _skill.Effect.CompanionAttackCoefficient * (1f + bonus);
            if (!IsPositiveFinite(rawDamage))
            {
                return;
            }

            // 첫 적에 닿을 때 시전 번호를 발급한다. 비행 중 다른 공격이 끝나도 공명 종료 고수위에 걸리지 않는다.
            // 동작 번호는 발사 때 정한 값을 유지하고 소환 생성 번호와 분리한다.
            var request = new DamageRequest(_combatId, target.CombatId, _resolver.IssueInstanceId(), actionId, rawDamage,
                EDamageFlags.None, origin, _skill.DefinitionId);
            DamageResult result = _resolver.Apply(in request);
            if (result.CountsAsHit)
            {
                _context.OnCompanionHit(in request, in result);
            }
        }

        private void MoveTo(Vector3 destination, float dt, double now, PathRequestBudget budget)
        {
            if (HorizontalDistanceSqr(_position, destination) <= Constants.Player.MOVE_SAME_DESTINATION_EPSILON_SQR)
            {
                _path.Stop();
                _hasDestination   = false;
                _unreachableSince = double.NaN;
                return;
            }

            bool needsPath = (!_hasDestination) || (!_path.IsMoving)
                || (HorizontalDistanceSqr(_pathDestination, destination) > Constants.Player.MOVE_SAME_DESTINATION_EPSILON_SQR);
            if ((needsPath) && (now >= _nextRepathAt) && (budget.TryConsume()))
            {
                _nextRepathAt = now + Constants.Player.MOVE_REPATH_MIN_INTERVAL_SECONDS;
                if (TryBeginPath(destination))
                {
                    _unreachableSince = double.NaN;
                }
                else
                {
                    _path.Stop();
                    _hasDestination = false;
                    if (double.IsNaN(_unreachableSince))
                    {
                        _unreachableSince = now;
                    }
                }
            }

            _position = _path.Step(_position, _catalog.CompanionMoveSpeed * dt, out Vector3 direction, out bool moved);
            if (moved)
            {
                _forward = direction;
            }
        }

        private bool TryBeginPath(Vector3 destination)
        {
            if ((!NavMesh.SamplePosition(destination, out NavMeshHit hit, Constants.Player.MOVE_DESTINATION_SAMPLE_RADIUS, NavMesh.AllAreas))
                || (!NavMesh.CalculatePath(_position, hit.position, NavMesh.AllAreas, _navPath))
                || (_navPath.status != NavMeshPathStatus.PathComplete))
            {
                return false;
            }

            int count = _navPath.GetCornersNonAlloc(_path.CornerBuffer);
            if ((count <= 1) || (count >= _path.CornerBuffer.Length))
            {
                return false;
            }

            _path.Begin(count);
            _pathDestination = destination;
            _hasDestination  = true;
            return true;
        }

        private void TryWarpToOwner(double now)
        {
            _targetId = CombatIds.NONE;
            _path.Stop();
            _hasDestination = false;
            Vector3 destination = _owner.Position - (_owner.Forward * _catalog.CompanionWarpOffset);
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, Constants.Player.MOVE_DESTINATION_SAMPLE_RADIUS, NavMesh.AllAreas))
            {
                _position         = hit.position;
                _unreachableSince = double.NaN;
                _nextRepathAt     = now;
            }

            // 복귀 지점이 없으면 벽 안으로 순간 이동하지 않고 다음 틱에 유효 지점을 다시 찾는다
        }

        private static float HorizontalDistanceSqr(Vector3 first, Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return (x * x) + (z * z);
        }

        private static bool IsFinite(double value)
        {
            return (!double.IsNaN(value)) && (!double.IsInfinity(value));
        }

        private static bool IsPositiveFinite(float value)
        {
            return (value > 0f) && (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }

        private static bool IsNonNegativeFinite(float value)
        {
            return (value >= 0f) && (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }
    }
}
