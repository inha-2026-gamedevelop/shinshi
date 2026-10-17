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
    // 서버 시각은 바깥이 Tick에 넘긴다. Mirror와 Time.time에 묶이지 않아 EditMode에서 그대로 돌릴 수 있다.
    // 타격 시각은 이 클래스가 센 서버 시각이 정한다. 애니메이션 이벤트나 클라이언트 신호는 받지 않는다.
    // 플레이어 기술과 공격이 끝난 뒤 틱마다 한 번 부른다. 같은 틱에 사망과 타격 시각이 겹치면 사망이 이긴다.
    // 누가 Tick을 부르는지와 레지스트리 등록은 서버 부트스트랩의 몫이며 아직 연결하지 않았다
    /// <summary> 근접 산귀의 단순 FSM, 대기 - 접근 - 예고 - 실행 - 후딜 순으로 전방 부채꼴 공격 하나를 반복한다 </summary>
    public sealed class MeleeEnemyBrain
    {
        /****************************************
        *              Constants
        ****************************************/

        // 아래 세 값은 이번 프로토타입 제안값이다. 공유 기획의 확정값이 아니므로 EnemyCatalogSO에 넣지 않는다
        public const float PROTOTYPE_RECOVER_SECONDS  = 0.35f; // 타격 뒤 새 공격을 막는 시간
        public const float PROTOTYPE_ENGAGE_RATIO     = 0.8f;  // 공격 반경 대비 접근을 멈추는 거리 비율
        public const float PROTOTYPE_HIT_STUN_SECONDS = 1f;    // 예고 중 맞아 공격이 취소된 뒤 굳는 시간, 피격 클립 길이에 맞춘 값

        private const int   INITIAL_BUFFER_CAPACITY = 16;
        private const float RANGE_EPSILON           = 0.001f; // m, 접근 정지 거리의 부동소수 오차 흡수
        private const float SEPARATION_EPSILON      = 0.0001f; // m, 겹침 방지가 위치를 바꿨는지 가르는 오차

        /****************************************
        *                Fields
        ****************************************/

        private readonly EnemyServer          _self;
        private readonly EnemyDefinition      _definition;
        private readonly float                _rawDamage;
        private readonly float                _recoverSeconds;
        private readonly ICombatTargetQuery   _targets;
        private readonly EnemyTargetSelector  _selector;
        private readonly IDamageResolver      _damage;
        private readonly EnemyCrowd           _crowd;
        private EnemyCrowd.SidestepState      _sidestep;
        private readonly List<ICombatTarget>  _buffer = new List<ICombatTarget>(INITIAL_BUFFER_CAPACITY);

        private double  _lastTickTime;
        private bool    _hasTicked;
        private int     _castId   = CombatIds.NONE;
        private double  _telegraphStartTime;
        private double  _telegraphEndTime;
        private double  _recoverEndTime;
        private double  _hitEndTime;
        private float   _lastHealth;
        private double  _nextTelegraphTime = double.NegativeInfinity;
        private Vector3 _aim;

        /****************************************
        *              Properties
        ****************************************/

        public EEnemyState State { get; private set; } = EEnemyState.Idle;

        /// <summary> 현재 추적 중인 타깃 번호, 없으면 CombatIds.NONE </summary>
        public int TargetId => _selector.TargetId;

        /// <summary> 예고 중 고정된 공격 방향, 예고가 아니면 마지막 값 </summary>
        public Vector3 AimDirection => _aim;

        /// <summary> 서버가 확정한 타격 횟수, 표현이 타격 순간을 알아채는 데 쓴다 </summary>
        public int ExecutedCount { get; private set; }

        /// <summary> 마지막 타격에서 피해가 적용된 대상 수 </summary>
        public int LastHitCount { get; private set; }

        public float AttackRange => _definition.AttackShape.Range;

        public float AttackArcAngle => _definition.AttackShape.ArcAngle;

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

        /// <summary> 타격을 확정한 틱에 불린다, 인자는 피해가 적용된 대상 수 </summary>
        public event Action<int> AttackExecuted;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 전장 지역의 Href다. 방어 전 피해는 AttackDamageRef x Href이고
        // 플레이어의 현재 체력으로 대신하지 않는다. crowd가 null이면 적끼리 겹침 방지를 하지 않는다
        public MeleeEnemyBrain(EnemyServer self, EnemyDefinition definition, float referenceHealth,
            ICombatTargetQuery targets, IDamageResolver damage, float recoverSeconds = PROTOTYPE_RECOVER_SECONDS,
            EnemyCrowd crowd = null)
        {
            if (self == null)
            {
                throw new ArgumentNullException(nameof(self));
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if ((definition.AttackShape == null) || (definition.AttackShape.Shape != EEnemyAttackShape.Fan))
            {
                throw new ArgumentException("근접 산귀는 부채꼴 공격만 쓴다", nameof(definition));
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

            _self           = self;
            _definition     = definition;
            _rawDamage      = definition.AttackDamageRef * referenceHealth;
            _recoverSeconds = Mathf.Max(0f, recoverSeconds);
            _targets        = targets;
            _selector       = new EnemyTargetSelector(targets);
            _damage         = damage;
            _crowd          = crowd;
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

            // 사망은 예고와 후딜과 이동보다 먼저 본다. 예고 중이던 타격은 여기서 함께 사라진다
            if (_self.LifeState != EParticipantLifeState.Alive)
            {
                State = EEnemyState.Dead;
                _selector.Clear();
                return;
            }

            float deltaSeconds = _hasTicked ? Mathf.Max(0f, (float)(serverTime - _lastTickTime)) : 0f;
            _lastTickTime = serverTime;
            _hasTicked    = true;

            // 체력이 줄었는지는 틱마다 갱신한다. 예고가 끝나는 틱에 함께 맞았으면 타격보다 피격이 먼저다
            bool damaged = _self.Health < _lastHealth;
            _lastHealth  = _self.Health;

            if (State == EEnemyState.Telegraph)
            {
                if (damaged)
                {
                    InterruptByHit(serverTime);
                    return;
                }

                if (serverTime >= _telegraphEndTime)
                {
                    Execute(serverTime);
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
            ICombatTarget target = _selector.Resolve(_self.Position);
            if (target == null)
            {
                State = EEnemyState.Idle;
                MoveSelf(_self.Position, deltaSeconds, false);
                return;
            }

            State = EEnemyState.Chase;

            Vector3 position   = _self.Position;
            Vector3 toTarget   = target.Position - position;
            toTarget.y = 0f;

            float   distance   = toTarget.magnitude;
            float   engageDist = _definition.AttackShape.Range * PROTOTYPE_ENGAGE_RATIO;
            Vector3 desired    = position;
            float   step       = 0f;

            if (distance > engageDist)
            {
                step    = Mathf.Min(_definition.MoveSpeed * _self.GetMoveSpeedScale(serverTime) * deltaSeconds, distance - engageDist);
                desired = position + ((toTarget / distance) * step);
            }

            // 공격 반경 안까지 들어온 개체는 막혀도 옆으로 더 파고들지 않고 그 자리에서 친다
            bool separated = MoveSelf(desired, deltaSeconds, distance > _definition.AttackShape.Range);
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

            // 이웃에 막혀 접근 정지 거리까지 못 들어간 개체도 공격 반경 안이면 친다. 안 그러면 앞줄 뒤의 개체가 영영 못 때린다
            float attackDist = separated ? _definition.AttackShape.Range : (engageDist + RANGE_EPSILON);
            if ((distance <= attackDist) && (serverTime >= _nextTelegraphTime))
            {
                BeginTelegraph(serverTime, toTarget);
            }
        }

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

        // 예고 중 피해를 받으면 타격 없이 예고를 버리고 굳는다. 굳는 동안 맞아도 시간은 늘지 않고, 끝나면 바로 다시 예고할 수 있다
        private void InterruptByHit(double serverTime)
        {
            _castId            = CombatIds.NONE;
            _hitEndTime        = serverTime + PROTOTYPE_HIT_STUN_SECONDS;
            _nextTelegraphTime = _hitEndTime;
            State              = EEnemyState.Hit;
        }

        // 방향은 이 시점에 한 번 정하고 예고가 끝날 때까지 바꾸지 않는다. 주기는 예고 시작부터 센다
        private void BeginTelegraph(double serverTime, Vector3 toTarget)
        {
            _aim = BossTelegraphRules.Flatten(toTarget, _self.Forward);
            _self.SetForward(_aim);

            _telegraphStartTime = serverTime;
            _telegraphEndTime   = serverTime + _definition.TelegraphSeconds;
            _nextTelegraphTime  = serverTime + _definition.AttackInterval;
            _castId             = _damage.IssueInstanceId();
            State               = EEnemyState.Telegraph;
        }

        // 타깃을 따로 맞히지 않는다. 고정한 부채꼴 안에 지금 있는 유효 대상이 각각 한 번씩 맞는다.
        // 대상마다 요청을 내지만 한 동작 번호를 공유하므로 적중 기록이 같은 대상의 중복 적중을 막는다
        private void Execute(double serverTime)
        {
            Vector3 origin = _self.Position;
            float   radius = _definition.AttackShape.Range;
            float   arc    = _definition.AttackShape.ArcAngle;

            // 소환물도 범위 피해를 받는다
            _targets.GetLivingInRadius(origin, radius, ECombatFaction.Player, true, _buffer);

            int          actionId = _damage.IssueInstanceId();
            EDamageFlags flags    = _definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;
            int          hitCount = 0;

            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget target = _buffer[i];
                if (!BossTelegraphRules.IsInside(EBossTelegraphShape.Fan, origin, _aim, radius, 0f, arc, target.Position))
                {
                    continue;
                }

                DamageRequest request = new DamageRequest(_self.CombatId, target.CombatId, _castId, actionId,
                    _rawDamage, flags, origin, _definition.DefinitionId);

                DamageResult result = _damage.Apply(in request);
                if (result.IsApplied)
                {
                    ++hitCount;
                }
            }

            _buffer.Clear();

            _recoverEndTime = serverTime + _recoverSeconds;
            State           = EEnemyState.Recover;
            LastHitCount    = hitCount;
            ++ExecutedCount;
            AttackExecuted?.Invoke(hitCount);
        }
    }
}
