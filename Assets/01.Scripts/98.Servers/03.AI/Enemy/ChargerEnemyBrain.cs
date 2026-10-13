// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

using EnemyDefinition = Shinshi.Data.EnemyCatalogSO.EnemyDefinition;

namespace Shinshi.AI
{
    // 서버 시각은 바깥이 Tick에 넘긴다. 돌진 시작은 예고 끝 시각이고 속도는 정의의 거리 / 실행 시간으로 일정하다.
    // 그래서 틱이 늦어져도 한 틱에 가는 거리는 정의의 거리를 넘지 않고, 후딜 끝도 틱 시각이 아니라 이 공식 시각으로 센다.
    // 벽 검사와 이동은 보스 돌진과 같은 IBossMovement 창구를 쓴다. 플레이어와 소환물은 막지 않고 정적 지형만 막는다.
    // 판정은 몸체 중심이 지나간 직사각형 구간이고 대상은 중심점으로 본다. 돌진 외의 평타와 접촉 피해는 없다.
    // 누가 Tick을 부르는지와 레지스트리 등록은 서버 부트스트랩의 몫이며 아직 연결하지 않았다
    /// <summary> 돌진 산귀의 단순 FSM, 대기 - 접근 - 예고 - 돌진 - 후딜 순으로 고정 방향 돌진 하나를 반복한다 </summary>
    public sealed class ChargerEnemyBrain
    {
        /****************************************
        *              Constants
        ****************************************/

        // 이번 프로토타입 제안값이다. 공유 기획의 확정값이 아니므로 EnemyCatalogSO에 넣지 않는다
        public const float PROTOTYPE_RECOVER_SECONDS  = 0.6f; // 돌진이 끝난 뒤 새 공격을 막는 시간
        public const float PROTOTYPE_HIT_STUN_SECONDS = 1f;   // 예고나 돌진 중 맞아 공격이 취소된 뒤 굳는 시간, 피격 클립 길이에 맞춘 값

        private const float MIN_DIRECTION_SQR       = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const float EDGE_EPSILON            = 0.001f;    // m, 직사각형 경계와 조회 반경의 부동소수 오차 흡수
        private const float STOP_EPSILON            = 0.001f;    // m, 도착과 벽 정지를 가르는 오차
        private const float SEPARATION_EPSILON      = 0.0001f;   // m, 겹침 방지가 위치를 바꿨는지 가르는 오차
        private const float HALF                    = 0.5f;
        private const int   INITIAL_BUFFER_CAPACITY = 16;

        /****************************************
        *                Fields
        ****************************************/

        private readonly EnemyServer          _self;
        private readonly EnemyDefinition      _definition;
        private readonly float                _rawDamage;
        private readonly float                _recoverSeconds;
        private readonly ICombatTargetQuery   _targets;
        private readonly IDamageResolver      _damage;
        private readonly IBossMovement        _movement;
        private readonly EnemyCrowd           _crowd;
        private EnemyCrowd.SidestepState      _sidestep;
        private readonly EnemyTargetSelector  _selector;
        private readonly List<ICombatTarget>  _buffer  = new List<ICombatTarget>(INITIAL_BUFFER_CAPACITY);
        private readonly HashSet<int>         _handled = new HashSet<int>();

        private double  _lastTickTime;
        private bool    _hasTicked;
        private int     _castId   = CombatIds.NONE;
        private int     _actionId = CombatIds.NONE;
        private double  _telegraphStartTime;
        private double  _telegraphEndTime;
        private double  _dashStartTime;
        private double  _recoverEndTime;
        private double  _hitEndTime;
        private float   _lastHealth;
        private double  _nextTelegraphTime = double.NegativeInfinity;
        private Vector3 _aim;
        private Vector3 _origin;
        private float   _clearLength;
        private float   _maxDistance;
        private float   _traveled;
        private int     _dashHits;

        /****************************************
        *              Properties
        ****************************************/

        public EEnemyState State { get; private set; } = EEnemyState.Idle;

        /// <summary> 현재 추적 중인 타깃 번호, 없으면 CombatIds.NONE </summary>
        public int TargetId => _selector.TargetId;

        /// <summary> 예고 시작에 고정한 돌진 방향, 수평 단위 벡터 </summary>
        public Vector3 AimDirection => _aim;

        /// <summary> 예고 시작에 고정한 돌진 시작 위치 </summary>
        public Vector3 DashOrigin => _origin;

        /// <summary> 예고가 보여 주는 길이이자 돌진이 갈 수 있는 최대 거리, 벽이 가까우면 정의의 거리보다 짧다 </summary>
        public float DashLength => _clearLength;

        /// <summary> 판정 전체 폭 </summary>
        public float DashWidth => _definition.AttackShape.Width;

        /// <summary> 돌진 시작점에서 지금까지 실제로 간 거리 </summary>
        public float TraveledDistance => _traveled;

        /// <summary> 서버가 확정한 돌진 횟수, 돌진이 끝난 틱에 늘어난다 </summary>
        public int ExecutedCount { get; private set; }

        /// <summary> 마지막 돌진에서 피해가 적용된 대상 수 </summary>
        public int LastHitCount { get; private set; }

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

        /// <summary> 돌진이 끝난 틱에 불린다, 인자는 이번 돌진에서 피해가 적용된 대상 수 </summary>
        public event Action<int> AttackExecuted;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 전장 지역의 Href다. 방어 전 피해는 AttackDamageRef x Href이고
        // 플레이어의 현재 체력으로 대신하지 않는다. crowd가 null이면 적끼리 겹침 방지를 하지 않는다
        public ChargerEnemyBrain(EnemyServer self, EnemyDefinition definition, float referenceHealth,
            ICombatTargetQuery targets, IDamageResolver damage, IBossMovement movement,
            float recoverSeconds = PROTOTYPE_RECOVER_SECONDS, EnemyCrowd crowd = null)
        {
            if (self == null)
            {
                throw new ArgumentNullException(nameof(self));
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if ((definition.AttackShape == null) || (definition.AttackShape.Shape != EEnemyAttackShape.Dash))
            {
                throw new ArgumentException("돌진 산귀는 돌진 공격만 쓴다", nameof(definition));
            }

            if (!(definition.AttackActiveSeconds > 0f))
            {
                throw new ArgumentException("돌진 실행 시간은 0보다 커야 한다", nameof(definition));
            }

            if ((!(referenceHealth > 0f)) || (float.IsInfinity(referenceHealth)))
            {
                throw new ArgumentOutOfRangeException(nameof(referenceHealth), "지역 기준 체력은 0보다 큰 유한값이다");
            }

            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            if (damage == null)
            {
                throw new ArgumentNullException(nameof(damage));
            }

            if (movement == null)
            {
                throw new ArgumentNullException(nameof(movement));
            }

            _self           = self;
            _definition     = definition;
            _rawDamage      = definition.AttackDamageRef * referenceHealth;
            _recoverSeconds = Mathf.Max(0f, recoverSeconds);
            _targets        = targets;
            _damage         = damage;
            _movement       = movement;
            _crowd          = crowd;
            _sidestep       = new EnemyCrowd.SidestepState { Side = ((self.CombatId & 1) == 0) ? 1 : -1 };
            _selector       = new EnemyTargetSelector(targets);
            _aim            = self.Forward;
            _origin         = self.Position;
            _lastHealth     = self.Health;
        }

        /// <summary> 서버 시각으로 한 틱 진행한다 </summary>
        public void Tick(double serverTime)
        {
            if (State == EEnemyState.Dead)
            {
                return;
            }

            // 사망은 예고와 돌진과 후딜보다 먼저 본다. 아직 적용하지 않은 돌진 피해는 여기서 함께 사라진다
            if (_self.LifeState != EParticipantLifeState.Alive)
            {
                State = EEnemyState.Dead;
                _selector.Clear();
                _handled.Clear();
                return;
            }

            float deltaSeconds = _hasTicked ? Mathf.Max(0f, (float)(serverTime - _lastTickTime)) : 0f;
            _lastTickTime = serverTime;
            _hasTicked    = true;

            // 체력이 줄었는지는 틱마다 갱신한다. 예고가 끝나는 틱이나 돌진 중에 맞았으면 공격보다 피격이 먼저다
            bool damaged = _self.Health < _lastHealth;
            _lastHealth  = _self.Health;

            if (damaged && ((State == EEnemyState.Telegraph) || (State == EEnemyState.Dash)))
            {
                InterruptByHit(serverTime);
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

            if (State == EEnemyState.Telegraph)
            {
                if (serverTime < _telegraphEndTime)
                {
                    return;
                }

                BeginDash();
            }

            if (State == EEnemyState.Dash)
            {
                AdvanceDash(serverTime);
                return;
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
            ICombatTarget target = _selector.Resolve(_self.Position);
            if (target == null)
            {
                State = EEnemyState.Idle;
                MoveSelf(_self.Position, deltaSeconds, false);
                return;
            }

            State = EEnemyState.Chase;

            Vector3 position = _self.Position;
            Vector3 toTarget = target.Position - position;
            toTarget.y = 0f;

            float   distance   = toTarget.magnitude;
            float   engageDist = _definition.AttackShape.Range;
            Vector3 desired    = position;
            float   step       = 0f;

            if (distance > engageDist)
            {
                step    = Mathf.Min(_definition.MoveSpeed * deltaSeconds, distance - engageDist);
                desired = position + ((toTarget / distance) * step);
            }

            bool separated = MoveSelf(desired, deltaSeconds, distance > engageDist);
            if (separated)
            {
                toTarget   = target.Position - _self.Position;
                toTarget.y = 0f;
                distance   = toTarget.magnitude;
            }
            else
            {
                distance -= step;
            }

            _self.SetForward(toTarget);

            if ((distance <= (engageDist + STOP_EPSILON)) && (serverTime >= _nextTelegraphTime))
            {
                BeginTelegraph(serverTime, toTarget);
            }
        }

        // 접근 이동만 이웃 몸체를 피한다. 돌진 구간은 벽만 막는 확정 직선이라 여기를 거치지 않는다
        // 이동 결과를 서버 몸체에 넣는다. 이웃 몸체를 피하느라 위치가 바뀌었으면 true
        private bool MoveSelf(Vector3 desired, float deltaSeconds, bool allowSidestep)
        {
            if (_crowd == null)
            {
                _self.SetPosition(desired);
                return false;
            }

            float   maxStep  = (_definition.MoveSpeed + EnemyCrowd.PROTOTYPE_DEPENETRATION_SPEED) * deltaSeconds;
            Vector3 resolved = _crowd.ResolveWithSidestep(_self, desired, maxStep, deltaSeconds, allowSidestep, ref _sidestep);
            _self.SetPosition(resolved);

            float x = resolved.x - desired.x;
            float z = resolved.z - desired.z;
            return ((x * x) + (z * z)) > (SEPARATION_EPSILON * SEPARATION_EPSILON);
        }

        // 예고나 돌진 중 피해를 받으면 그 자리에서 공격을 버리고 굳는다. 이미 맞힌 대상의 피해는 남고 완료 횟수는 늘지 않는다.
        // 굳는 동안 맞아도 시간은 늘지 않고, 끝나면 바로 다시 예고할 수 있다
        private void InterruptByHit(double serverTime)
        {
            _castId            = CombatIds.NONE;
            _actionId          = CombatIds.NONE;
            _handled.Clear();
            _hitEndTime        = serverTime + PROTOTYPE_HIT_STUN_SECONDS;
            _nextTelegraphTime = _hitEndTime;
            State              = EEnemyState.Hit;
        }

        // 위치와 방향은 이 시점에 한 번 정하고 돌진이 끝날 때까지 바꾸지 않는다. 주기는 예고 시작부터 센다
        private void BeginTelegraph(double serverTime, Vector3 toTarget)
        {
            _aim    = Flatten(toTarget, _self.Forward);
            _origin = _self.Position;
            _self.SetForward(_aim);

            _clearLength = MeasureClearLength();

            _telegraphStartTime = serverTime;
            _telegraphEndTime   = serverTime + _definition.TelegraphSeconds;
            _nextTelegraphTime  = serverTime + _definition.AttackInterval;
            _castId             = _damage.IssueInstanceId();
            State               = EEnemyState.Telegraph;
        }

        // 돌진 시작 시각은 틱 시각이 아니라 예고 끝 시각이다. 틱이 늦어도 이동은 이 시각부터 센다
        private void BeginDash()
        {
            _dashStartTime = _telegraphEndTime;
            _maxDistance   = MeasureClearLength();
            _traveled      = 0f;
            _dashHits      = 0;
            _actionId      = _damage.IssueInstanceId();
            _handled.Clear();
            State          = EEnemyState.Dash;
        }

        // 몸체 반경으로 잰 벽 앞 거리, 정의의 최대 거리를 넘지 않는다
        private float MeasureClearLength()
        {
            float range = _definition.AttackShape.Range;
            float clear = _movement.GetClearDistance(_origin, _aim, range, _self.BodyRadius);
            return Mathf.Clamp(clear, 0f, range);
        }

        // 속도는 정의의 거리 / 실행 시간으로 일정하고, 실행 끝 시각 이후에는 정의의 거리를 넘지 않는다
        private float DistanceAt(double serverTime)
        {
            float  range   = _definition.AttackShape.Range;
            double elapsed = serverTime - _dashStartTime;

            if (elapsed <= 0.0)
            {
                return 0f;
            }

            if (elapsed >= _definition.AttackActiveSeconds)
            {
                return range;
            }

            return (float)(range * (elapsed / _definition.AttackActiveSeconds));
        }

        private void AdvanceDash(double serverTime)
        {
            float desired = Mathf.Min(DistanceAt(serverTime), _maxDistance);

            if (desired > _traveled)
            {
                Vector3 to      = _origin + (_aim * desired);
                Vector3 reached = _movement.Move(_self.Position, to, _self.BodyRadius);

                Vector3 offset = reached - _origin;
                offset.y = 0f;

                // 앞으로만 가고 이번 틱의 목표를 넘지 않는다. 벽이 더 일찍 막았으면 그 지점이 새 최대 거리다
                float along = Mathf.Clamp((offset.x * _aim.x) + (offset.z * _aim.z), _traveled, desired);
                if (along < (desired - STOP_EPSILON))
                {
                    _maxDistance = along;
                }

                _self.SetPosition(reached);

                if (along > _traveled)
                {
                    SweepHits(_traveled, along);
                    _traveled = along;
                }
            }

            if (_traveled >= (_maxDistance - STOP_EPSILON))
            {
                FinishDash();
            }
        }

        // 한 틱에 지나간 구간만 본다. 한 번 처리한 대상은 방어나 무적 결과와 상관없이 다시 판정하지 않는다.
        // 구간은 틱마다 이어 붙으므로 빠른 이동으로 대상을 건너뛰어 누락되지 않는다
        private void SweepHits(float from, float to)
        {
            float halfWidth  = _definition.AttackShape.Width * HALF;
            float halfLength = (to - from) * HALF;

            // 조회는 원이라 구간 직사각형의 먼 모서리까지 닿는 반경이 필요하다. 안팎은 아래에서 직사각형으로 다시 가른다
            Vector3 center = _origin + (_aim * (from + halfLength));
            float   radius = Mathf.Sqrt((halfLength * halfLength) + (halfWidth * halfWidth)) + EDGE_EPSILON;

            // 소환물도 범위 피해를 받는다
            _targets.GetLivingInRadius(center, radius, ECombatFaction.Player, true, _buffer);

            EDamageFlags flags = _definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;

            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget target = _buffer[i];
                if (!IsInsideSegment(target.Position, from, to, halfWidth))
                {
                    continue;
                }

                if (!_handled.Add(target.CombatId))
                {
                    continue;
                }

                // 후방 판정 기준은 돌진 시작 위치다. 틱 크기와 상관없이 같은 방향에서 온 공격으로 본다
                DamageRequest request = new DamageRequest(_self.CombatId, target.CombatId, _castId, _actionId,
                    _rawDamage, flags, _origin, _definition.DefinitionId);

                DamageResult result = _damage.Apply(in request);
                if (result.IsApplied)
                {
                    ++_dashHits;
                }
            }

            _buffer.Clear();
        }

        // 수평면에서 돌진 시작점 기준 전방 from~to, 좌우 각각 폭의 절반까지이며 네 경계를 모두 포함한다
        private bool IsInsideSegment(Vector3 targetPosition, float from, float to, float halfWidth)
        {
            Vector3 offset = targetPosition - _origin;
            offset.y = 0f;

            float forward = (offset.x * _aim.x) + (offset.z * _aim.z);
            float lateral = (offset.x * _aim.z) - (offset.z * _aim.x);

            return (forward >= (from - EDGE_EPSILON)) && (forward <= (to + EDGE_EPSILON))
                && (Mathf.Abs(lateral) <= (halfWidth + EDGE_EPSILON));
        }

        // 후딜 끝은 도착한 거리에 대응하는 공식 시각에서 센다. 벽에서 일찍 멈췄으면 그 시각, 끝까지 갔으면 실행 끝 시각이다
        private void FinishDash()
        {
            float  range    = _definition.AttackShape.Range;
            double stopTime = _dashStartTime + (_definition.AttackActiveSeconds * (_traveled / range));

            _recoverEndTime = stopTime + _recoverSeconds;
            State           = EEnemyState.Recover;
            LastHitCount    = _dashHits;
            ++ExecutedCount;
            AttackExecuted?.Invoke(_dashHits);
        }

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? fallback : direction.normalized;
        }
    }
}
