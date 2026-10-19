// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

using EnemyDefinition = Shinshi.Data.EnemyCatalogSO.EnemyDefinition;

namespace Shinshi.AI
{
    // 서버 시각은 바깥이 Tick에 넘긴다. 발사 위치와 방향은 예고 시작에 고정하고 투사체 비행과 적중은 BossProjectileManager가 판정한다.
    // 접근 - 예고 - 발사 - 후딜을 반복하고 플레이어가 3m 안으로 붙으면 2초에 한 번 최대 2m 물러난다
    /// <summary> 원거리 산귀의 단순 FSM, 6~8m 거리에서 직선 투사체 하나를 쏜다 </summary>
    public sealed class RangedEnemyBrain
    {
        /****************************************
        *              Constants
        ****************************************/

        // 거리와 후퇴 규칙은 전투 설계 문서(원거리 6~8m, 3m 안이면 2초에 한 번 최대 2m 후퇴)의 값이다
        public const float PREFERRED_MIN_DISTANCE     = 6f;
        public const float PREFERRED_MAX_DISTANCE     = 8f;
        public const float RETREAT_TRIGGER_DISTANCE   = 3f;
        public const float RETREAT_MAX_DISTANCE       = 2f;
        public const float RETREAT_COOLDOWN_SECONDS   = 2f;

        // 이번 프로토타입 제안값이다. 근접형과 같은 기준을 쓴다
        public const float PROTOTYPE_RECOVER_SECONDS  = 0.35f;
        public const float PROTOTYPE_HIT_STUN_SECONDS = 1f;

        private const float RANGE_EPSILON      = 0.001f;
        private const float SEPARATION_EPSILON = 0.0001f;
        private const float MOVED_EPSILON      = 0.01f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly EnemyServer           _self;
        private readonly EnemyDefinition       _definition;
        private readonly float                 _rawDamage;
        private readonly float                 _recoverSeconds;
        private readonly EnemyTargetSelector   _selector;
        private readonly BossProjectileManager _projectiles;
        private readonly BossDamageGate        _gate;
        private readonly IBossMovement         _movement;
        private readonly EnemyCrowd            _crowd;
        private readonly IEnemyNavigation      _navigation;
        private readonly EnemyTelegraphLimiter _limiter;
        private readonly IBossWallQuery        _walls;
        private readonly EnemyPathSteer        _steer = new EnemyPathSteer();
        private EnemyCrowd.SidestepState       _sidestep;

        private double  _lastTickTime;
        private bool    _hasTicked;
        private int     _castId = CombatIds.NONE;
        private double  _telegraphStartTime;
        private double  _telegraphEndTime;
        private double  _recoverEndTime;
        private double  _hitEndTime;
        private float   _lastHealth;
        private double  _nextTelegraphTime = double.NegativeInfinity;
        private double  _nextRetreatTime   = double.NegativeInfinity;
        private Vector3 _aim;

        /****************************************
        *              Properties
        ****************************************/

        public EEnemyState State { get; private set; } = EEnemyState.Idle;

        /// <summary> 현재 추적 중인 타깃 번호, 없으면 CombatIds.NONE </summary>
        public int TargetId => _selector.TargetId;

        /// <summary> 예고 시작에 고정한 발사 방향, 수평 단위 벡터 </summary>
        public Vector3 AimDirection => _aim;

        /// <summary> 서버가 확정한 발사 횟수 </summary>
        public int ExecutedCount { get; private set; }

        /// <summary> 후퇴한 횟수 </summary>
        public int RetreatCount { get; private set; }

        public double TelegraphStartTime => _telegraphStartTime;

        public double TelegraphEndTime => _telegraphEndTime;

        /// <summary> 예고 진행도 0에서 1, 예고 중이 아니면 0 </summary>
        public float TelegraphProgress(double serverTime)
        {
            if (State != EEnemyState.Telegraph)
            {
                return 0f;
            }

            double length = _telegraphEndTime - _telegraphStartTime;
            return (length > 0.0) ? Mathf.Clamp01((float)((serverTime - _telegraphStartTime) / length)) : 1f;
        }

        /****************************************
        *                Methods
        ****************************************/

        // movement는 후퇴가 벽을 넘지 않게 하는 정적 벽 검사다. navigation이 있으면 후퇴 지점이 걸을 수 있는 지면일 때만 물러난다
        public RangedEnemyBrain(EnemyServer self, EnemyDefinition definition, float referenceHealth, ICombatTargetQuery targets,
            BossProjectileManager projectiles, BossDamageGate gate, IBossMovement movement, EnemyCrowd crowd = null,
            IEnemyNavigation navigation = null, EnemyTelegraphLimiter limiter = null, float recoverSeconds = PROTOTYPE_RECOVER_SECONDS,
            IBossWallQuery walls = null)
        {
            if (self == null)
            {
                throw new ArgumentNullException(nameof(self));
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if ((definition.AttackShape == null) || (definition.AttackShape.Shape != EEnemyAttackShape.Projectile)
                || (!(definition.AttackShape.ProjectileSpeed > 0f)))
            {
                throw new ArgumentException("원거리 산귀는 속도가 있는 투사체 공격만 쓴다", nameof(definition));
            }

            if ((!(referenceHealth > 0f)) || (float.IsInfinity(referenceHealth)))
            {
                throw new ArgumentOutOfRangeException(nameof(referenceHealth), "지역 기준 체력은 0보다 큰 유한값이다");
            }

            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            _self           = self;
            _definition     = definition;
            _rawDamage      = definition.AttackDamageRef * referenceHealth;
            _recoverSeconds = Mathf.Max(0f, recoverSeconds);
            _projectiles    = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
            _gate           = gate ?? throw new ArgumentNullException(nameof(gate));
            _movement       = movement ?? throw new ArgumentNullException(nameof(movement));
            _crowd          = crowd;
            _navigation     = navigation;
            _limiter        = limiter;
            _walls          = walls;
            _selector      = new EnemyTargetSelector(targets, navigation);
            _sidestep       = new EnemyCrowd.SidestepState { Side = ((self.CombatId & 1) == 0) ? 1 : -1 };
            _aim            = self.Forward;
            _lastHealth     = self.Health;
        }

        /// <summary> 서버 시각으로 한 틱 진행한다 </summary>
        public void Tick(double serverTime)
        {
            if (State == EEnemyState.Dead)
            {
                return;
            }

            if (_self.LifeState != EParticipantLifeState.Alive)
            {
                State = EEnemyState.Dead;
                _limiter?.Release(_self.CombatId);
                _selector.Clear();
                return;
            }

            float deltaSeconds = _hasTicked ? Mathf.Max(0f, (float)(serverTime - _lastTickTime)) : 0f;
            _lastTickTime = serverTime;
            _hasTicked    = true;

            bool damaged = _self.Health < _lastHealth;
            _lastHealth  = _self.Health;
            if (damaged)
            {
                _selector.RequestReevaluate();
            }

            if (State == EEnemyState.Telegraph)
            {
                if (damaged)
                {
                    InterruptByHit(serverTime);
                    return;
                }

                if (serverTime >= _telegraphEndTime)
                {
                    Fire(serverTime);
                }

                return;
            }

            if (State == EEnemyState.Hit)
            {
                if (serverTime < _hitEndTime)
                {
                    return;
                }

                State = EEnemyState.Idle;
            }

            if (State == EEnemyState.Recover)
            {
                if (serverTime < _recoverEndTime)
                {
                    return;
                }

                State = EEnemyState.Idle;
            }

            TickChase(serverTime, deltaSeconds);
        }

        private void TickChase(double serverTime, float deltaSeconds)
        {
            ICombatTarget target = (_navigation != null) ? _selector.Resolve(_self.Position, serverTime) : _selector.Resolve(_self.Position);
            if (target == null)
            {
                State = EEnemyState.Idle;
                return;
            }

            State = EEnemyState.Chase;

            Vector3 toTarget = target.Position - _self.Position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            // 벽이 가로막으면 사거리 안이어도 쏘지 않고 경로를 따라 돌아간다. 벽 너머 대상에게서는 물러나지 않는다
            bool sighted = EnemySight.IsClear(_walls, _self.Position, target.Position);

            if ((distance < RETREAT_TRIGGER_DISTANCE) && sighted && (serverTime >= _nextRetreatTime))
            {
                TryRetreat(serverTime, toTarget, distance);
                toTarget   = target.Position - _self.Position;
                toTarget.y = 0f;
                distance   = toTarget.magnitude;
            }
            else if ((distance > PREFERRED_MAX_DISTANCE) || (!sighted))
            {
                distance   = Approach(serverTime, deltaSeconds, target, toTarget, distance, sighted);
                toTarget   = target.Position - _self.Position;
                toTarget.y = 0f;
            }

            _self.SetForward(toTarget);

            if ((State == EEnemyState.Chase) && (distance <= (PREFERRED_MAX_DISTANCE + RANGE_EPSILON)) && (serverTime >= _nextTelegraphTime)
                && EnemySight.IsClear(_walls, _self.Position, target.Position))
            {
                if ((_limiter != null) && (!_limiter.TryAcquire(_self.CombatId, target.CombatId, true)))
                {
                    return;
                }

                BeginTelegraph(serverTime, toTarget);
            }
        }

        // 길찾기가 있으면 경로의 다음 꺾임 지점으로, 없으면 직선으로 가까워진다. 경로가 끊기면 제자리에 선다
        private float Approach(double serverTime, float deltaSeconds, ICombatTarget target, Vector3 toTarget, float distance,
            bool sighted)
        {
            Vector3 position      = _self.Position;
            Vector3 heading       = toTarget;
            float   headingLength = distance;

            if (_navigation != null)
            {
                if (!_steer.TryGetPoint(_navigation, position, target.Position, serverTime, out Vector3 steerPoint))
                {
                    State = EEnemyState.Idle;
                    return distance;
                }

                Vector3 toSteer = steerPoint - position;
                toSteer.y = 0f;
                float steerLength = toSteer.magnitude;
                if (steerLength > SEPARATION_EPSILON)
                {
                    heading       = toSteer;
                    headingLength = steerLength;
                }
            }

            float step = _definition.MoveSpeed * _self.GetMoveSpeedScale(serverTime) * deltaSeconds;
            step = sighted ? Mathf.Min(step, distance - PREFERRED_MAX_DISTANCE) : step;
            step = Mathf.Min(step, headingLength);
            if (step <= 0f)
            {
                return distance;
            }

            MoveSelf(position + ((heading / headingLength) * step), deltaSeconds);

            Vector3 after = target.Position - _self.Position;
            after.y = 0f;
            return after.magnitude;
        }

        // 벽과 걸을 수 없는 지면을 넘지 않는 만큼만 물러난다. 한 걸음도 못 물러나면 쿨다운을 쓰지 않는다
        private void TryRetreat(double serverTime, Vector3 toTarget, float distance)
        {
            Vector3 position = _self.Position;
            Vector3 away     = (distance > SEPARATION_EPSILON) ? (-toTarget / distance) : -_self.Forward;
            away.y = 0f;
            if (away.sqrMagnitude < SEPARATION_EPSILON)
            {
                return;
            }

            away.Normalize();
            Vector3 reached = _movement.Move(position, position + (away * RETREAT_MAX_DISTANCE), _self.BodyRadius);

            if (((reached - position).sqrMagnitude < (MOVED_EPSILON * MOVED_EPSILON))
                || ((_navigation != null) && (!_navigation.IsWalkable(reached))))
            {
                return;
            }

            _self.SetPosition(reached);
            _nextRetreatTime = serverTime + RETREAT_COOLDOWN_SECONDS;
            ++RetreatCount;
        }

        private void MoveSelf(Vector3 desired, float deltaSeconds)
        {
            if (_crowd == null)
            {
                _self.SetPosition(desired);
                return;
            }

            float   maxStep  = (_definition.MoveSpeed + EnemyCrowd.PROTOTYPE_DEPENETRATION_SPEED) * deltaSeconds;
            Vector3 resolved = _crowd.ResolveWithSidestep(_self, desired, maxStep, deltaSeconds, true, ref _sidestep);
            _self.SetPosition(resolved);
        }

        private void InterruptByHit(double serverTime)
        {
            _limiter?.Release(_self.CombatId);

            _castId            = CombatIds.NONE;
            _hitEndTime        = serverTime + PROTOTYPE_HIT_STUN_SECONDS;
            _nextTelegraphTime = _hitEndTime;
            State              = EEnemyState.Hit;
        }

        private void BeginTelegraph(double serverTime, Vector3 toTarget)
        {
            _aim = BossTelegraphRules.Flatten(toTarget, _self.Forward);
            _self.SetForward(_aim);

            _telegraphStartTime = serverTime;
            _telegraphEndTime   = serverTime + _definition.TelegraphSeconds;
            _nextTelegraphTime  = serverTime + _definition.AttackInterval;
            _castId             = _gate.IssueInstanceId();
            State               = EEnemyState.Telegraph;
        }

        // 발사 시각은 틱 시각이 아니라 예고 끝 시각이다. 슬롯이 가득 차 발사하지 못해도 후딜은 똑같이 센다
        private void Fire(double serverTime)
        {
            _limiter?.Release(_self.CombatId);

            var spec = new BossProjectileSpec(_castId, _gate.IssueInstanceId(), _self.CombatId, _definition.DefinitionId, _self.Position,
                _aim, _definition.AttackShape.ProjectileSpeed, _definition.AttackShape.Range, _definition.AttackShape.Width,
                _rawDamage, _definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable, _telegraphEndTime, _gate);

            _projectiles.TryLaunch(in spec);

            _recoverEndTime = serverTime + _recoverSeconds;
            State           = EEnemyState.Recover;
            ++ExecutedCount;
        }
    }
}
