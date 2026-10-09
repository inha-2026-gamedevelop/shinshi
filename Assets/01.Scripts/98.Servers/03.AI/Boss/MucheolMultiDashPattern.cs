// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 돌진 한 번의 거리, 폭, 피해, 방어 가능 여부는 패턴 정의에서, 예고와 추적 시간도 패턴 정의에서(첫 돌진),
    // 돌진 시간과 재조준 시간은 연속 돌진 데이터에서 읽는다. 이 클래스에는 밸런스 값이 없다.
    // 이지 조준 규칙: 돌진마다 예고를 시작하는 순간(첫 돌진은 시전 시작, 이후는 앞 돌진이 끝난 자리) 대상 쪽으로 방향을 바로 잡고,
    // 방향을 고정하는 시각까지 대상을 따라가다 고정한다. 회전 각도 제한은 없고, 고정한 뒤에는 돌진 중에도 꺾이지 않는다.
    // 대상은 선택 때 고른 하나뿐이다. 대상이 사라졌거나 죽었으면 마지막 유효 방향을 유지하고 다른 플레이어로 바꾸지 않는다.
    // 월백 돌진(BossDashPattern)은 속도와 시간을 시전의 정의 하나에서 읽고 한 번 달리면 끝나, 같은 시전 안에서 방향을 다시 잡고
    // 시간을 다르게 줄 수 없다. 정의를 복제해 돌진을 여러 번 시작하면 시전 번호와 예고가 갈라지므로, 같은 판정 구조(지나간 구간의
    // 직사각형을 한 번씩 타격, 벽 앞 정지)를 시전 하나 안에서 돌진마다 되풀이하는 별도 실행으로 둔다. 월백 돌진은 고치지 않는다.
    // 돌진 시작 시각은 틱 시각이 아니라 계산한 경계(첫 돌진의 실행 시작 + 돌진 번호 x 주기)라 틱이 거칠어도 밀리지 않는다.
    // 돌진마다 피해 동작 번호를 새로 받고 피격 기록을 비워, 같은 시전 안에서 돌진마다 같은 대상이 한 번씩 맞는다.
    // 실행 구간의 길이는 공격 FSM이 정한다. 지금 FSM은 정의의 ActiveSeconds로 고정하므로 계획한 돌진을 다 달릴 시간이 없으면
    // 실행이 끝나는 경계에서 남은 돌진을 버리고 알린다. FSM이 IBossExecuteDuration을 읽으면 계획한 시간만큼 실행이 이어진다
    /// <summary> 무철 연속 돌진, 돌진마다 대상 쪽으로 새로 조준하고 고정해 같은 시전 안에서 여러 번 달린다 </summary>
    public sealed class MucheolMultiDashPattern : BossPatternBase, IBossExecuteDuration
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  MIN_DIRECTION_SQR = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const float  ANGLE_EPSILON     = 0.001f;    // 도, 경계 각도의 부동소수 오차 흡수
        private const float  EDGE_EPSILON      = 0.001f;    // m, 직사각형 경계와 조회 반경의 부동소수 오차 흡수
        private const float  STOP_EPSILON      = 0.001f;    // m, 도착과 벽 정지를 가르는 오차
        private const double TIME_EPSILON      = 0.0005;    // 초, 단위가 다른 시간을 더해 만든 경계의 부동소수 오차 흡수
        private const float  HALF              = 0.5f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                     _referenceHealth;
        private readonly IBossMovement             _movement;
        private readonly MucheolMultiDashSpec      _spec;
        private readonly int                       _dashCount;
        private readonly IMucheolMultiDashListener _listener;
        private readonly List<ICombatTarget>       _buffer  = new List<ICombatTarget>();
        private readonly HashSet<int>              _handled = new HashSet<int>();

        private Vector3 _aim;
        private double  _trackStart;
        private bool    _isLocked;
        private float   _clearLength;

        private bool    _isExecuting;
        private bool    _isEnded;
        private double  _executeStart;
        private double  _executeBoundary;
        private int     _dashIndex;
        private int     _completedDashes;
        private double  _nextStart;

        private bool    _isDashing;
        private double  _dashStart;
        private double  _dashEnd;
        private Vector3 _origin;
        private Vector3 _position;
        private float   _maxDistance;
        private float   _traveled;
        private int     _actionInstanceId;
        private bool    _isStopped;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 현재 조준 방향, 수평 단위 벡터 </summary>
        public Vector3 AimDirection => _aim;

        public bool IsDirectionLocked => _isLocked;

        /// <summary> 이 시전이 달리기로 계획한 돌진 횟수 </summary>
        public int PlannedDashCount => _dashCount;

        /// <summary> 끝까지 달린 돌진 수, 벽에 막혀 멈춘 돌진도 끝난 것으로 센다 </summary>
        public int CompletedDashCount => _completedDashes;

        /// <summary> 지금 달리는 돌진의 번호, 재조준 중이면 다음에 달릴 돌진의 번호다 </summary>
        public int DashIndex => _dashIndex;

        /// <summary> 지금 돌진이 달리고 있다 </summary>
        public bool IsDashing => _isDashing;

        /// <summary> 지금 돌진의 시작점에서 실제로 간 거리 </summary>
        public float TraveledDistance => _traveled;

        /// <summary> 남은 돌진을 버렸거나 모두 달렸다 </summary>
        public bool IsEnded => _isEnded;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        // dashCount는 이 시전이 달릴 횟수다. 강화 구간 여부에 따라 정하는 일은 만드는 쪽(팩토리)이 한다
        public MucheolMultiDashPattern(float referenceHealth, IBossMovement movement, MucheolMultiDashSpec spec, int dashCount,
            IMucheolMultiDashListener listener = null)
        {
            if (movement == null)
            {
                throw new ArgumentNullException(nameof(movement));
            }

            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            if (dashCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(dashCount));
            }

            _referenceHealth = referenceHealth;
            _movement        = movement;
            _spec            = spec;
            _dashCount       = dashCount;
            _listener        = listener;
        }

        /// <summary> 계획한 돌진을 모두 달리는 데 필요한 실행 시간, 공격 FSM이 읽는다면 실행 단계에 들어간 직후 한 번 읽는다 </summary>
        public float GetExecuteSeconds(float definitionSeconds)
        {
            float planned = _spec.GetExecuteSeconds(_dashCount);

            // 읽혔다는 것은 FSM이 이 값으로 실행 구간을 정한다는 뜻이다. 읽지 않는 FSM에서는 정의의 값이 구간이다
            _executeBoundary = _executeStart + planned;
            return planned;
        }

        protected override void OnBegin(double serverTime)
        {
            _aim              = Flatten(Context.Self.Forward, Vector3.forward);
            _trackStart       = serverTime;
            _isLocked         = false;
            _isExecuting      = false;
            _isEnded          = false;
            _executeStart     = 0.0;
            _executeBoundary  = 0.0;
            _dashIndex        = 0;
            _completedDashes  = 0;
            _nextStart        = 0.0;
            _isDashing        = false;
            _isStopped        = false;
            _traveled         = 0f;
            _maxDistance      = 0f;
            _actionInstanceId = CombatIds.NONE;
            _origin           = Context.Self.Position;
            _position         = _origin;
            _handled.Clear();

            // 예고가 처음부터 대상 쪽을 보이도록 알리기 전에 방향을 잡는다. 이 방향은 예고 시작 알림이 싣는다
            TryAimAtTarget();
            _clearLength = MeasureClearLength(_aim);

            _listener?.OnTelegraphStarted(Context.CastInstanceId, _origin, _aim, _clearLength, Context.Definition.Shape.Width,
                Context.Definition.TelegraphSeconds, _dashCount);

            AdvanceTracking(serverTime);
        }

        protected override void OnTick(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Telegraph)
            {
                AdvanceTracking(serverTime);
            }
            else if (state == EBossAttackState.Execute)
            {
                AdvanceExecution(Math.Min(serverTime, _executeBoundary));
            }
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Execute)
            {
                // 큰 시간 간격으로 틱이 건너뛰어도 고정은 반드시 이동보다 먼저 끝난다. serverTime은 FSM이 넘기는 예고 끝 경계다
                AdvanceTracking(serverTime);
                LockDirection();

                _isExecuting     = true;
                _executeStart    = serverTime;
                _executeBoundary = serverTime + Context.Definition.ActiveSeconds;

                BeginDash(serverTime);
                AdvanceExecution(serverTime);
            }
            else if (state == EBossAttackState.Recover)
            {
                // 실행 마지막 틱이 거칠었어도 실행 끝 경계까지의 이동과 타격은 여기서 마무리하고, 그 뒤에 남은 돌진은 버린다
                AdvanceExecution(Math.Min(serverTime, _executeBoundary));
                CutRemainingDashes();
            }
        }

        protected override void OnCancelled(EBossInterruptReason reason)
        {
            _isDashing = false;
            _isEnded   = true;
            _listener?.OnCancelled(Context.CastInstanceId, reason);
        }

        // 고정 시각까지는 틱마다 대상 쪽으로 다시 잡는다. 틱이 거칠어도 고정 때의 방향은 그 틱의 대상 위치로 정해진다
        private void AdvanceTracking(double serverTime)
        {
            if (_isLocked)
            {
                return;
            }

            FollowTarget();

            if (serverTime >= (_trackStart + Context.Definition.TrackingSeconds))
            {
                LockDirection();
            }
        }

        // 방향이 달라졌으면 예고에 알린다
        private void FollowTarget()
        {
            if (!TryAimAtTarget())
            {
                return;
            }

            _clearLength = MeasureClearLength(_aim);
            _listener?.OnAimChanged(Context.CastInstanceId, _dashIndex, _aim, _clearLength);
        }

        // 선택 때 고른 대상 한 명의 현재 위치를 향해 각도 제한 없이 방향을 바꾼다. 알림은 부르는 쪽이 한다.
        // 타깃이 사라졌거나 죽었으면 마지막 유효 방향을 유지한다
        private bool TryAimAtTarget()
        {
            if (!Context.Targets.TryGet(Context.TargetCombatId, out ICombatTarget target))
            {
                return false;
            }

            if (target.LifeState != EParticipantLifeState.Alive)
            {
                return false;
            }

            Vector3 toTarget = target.Position - Context.Self.Position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < MIN_DIRECTION_SQR)
            {
                return false;
            }

            Vector3 direction = toTarget.normalized;

            // 거의 같은 방향에서 acos 기반 SignedAngle은 흔들려 긴 경로 끝에서 어긋난다. atan2로 구한다
            float cross = (_aim.z * direction.x) - (_aim.x * direction.z);
            float dot   = (_aim.x * direction.x) + (_aim.z * direction.z);
            float angle = Mathf.Atan2(cross, dot) * Mathf.Rad2Deg;
            if (Mathf.Abs(angle) <= ANGLE_EPSILON)
            {
                return false;
            }

            _aim = direction;
            return true;
        }

        private void LockDirection()
        {
            if (_isLocked)
            {
                return;
            }

            _isLocked = true;
            _listener?.OnDirectionLocked(Context.CastInstanceId, _dashIndex, _aim, _clearLength);
        }

        // 몸체 반경으로 잰 벽 앞 거리, 정의의 최대 거리를 넘지 않는다
        private float MeasureClearLength(Vector3 direction)
        {
            float range = Context.Definition.Shape.Range;
            float clear = _movement.GetClearDistance(Context.Self.Position, direction, range, Context.Self.BodyRadius);
            return Mathf.Clamp(clear, 0f, range);
        }

        // 두 시각이 같은 경계인지 가른다. 초 단위 값을 더해 만든 경계는 부동소수 오차만큼 어긋날 수 있다
        private static bool IsReached(double serverTime, double target)
        {
            return (serverTime + TIME_EPSILON) >= target;
        }

        // 돌진 시작 시각부터 serverTime까지 진행한다. 돌진이 끝나면 재조준을 거쳐 다음 돌진을 이어서 시작하고,
        // 한 번에 여러 돌진이 도래해도 앞에서부터 하나씩 처리한다
        private void AdvanceExecution(double serverTime)
        {
            if (!_isExecuting)
            {
                return;
            }

            while ((!_isEnded) && (!IsCancelled) && (!IsFinished))
            {
                if (_isDashing)
                {
                    if (!IsReached(serverTime, _dashEnd))
                    {
                        AdvanceDash(serverTime);
                        return;
                    }

                    AdvanceDash(_dashEnd);
                    EndDash();
                    continue;
                }

                // 재조준 구간에서도 방향을 고정하는 시각까지 대상을 따라간다
                FollowTarget();
                if (!IsReached(serverTime, _nextStart))
                {
                    return;
                }

                LockDirectionForNextDash();
                BeginDash(_nextStart);
            }
        }

        private void LockDirectionForNextDash()
        {
            _listener?.OnDirectionLocked(Context.CastInstanceId, _dashIndex, _aim, _clearLength);
        }

        // 돌진 하나를 시작한다. 속도는 벽과 상관없이 정의의 거리 / 돌진 시간이다
        private void BeginDash(double startTime)
        {
            PatternDefinition definition = Context.Definition;

            _origin           = Context.Self.Position;
            _position         = _origin;
            _dashStart        = startTime;
            _dashEnd          = startTime + _spec.DashSeconds;
            _clearLength      = MeasureClearLength(_aim);
            _maxDistance      = _clearLength;
            _traveled         = 0f;
            _actionInstanceId = Context.Damage.IssueInstanceId();
            _isDashing        = true;
            _isStopped        = false;
            _handled.Clear();

            float speed = definition.Shape.Range / _spec.DashSeconds;
            _listener?.OnDashStarted(Context.CastInstanceId, _dashIndex, _origin, _aim, _maxDistance, _spec.DashSeconds, speed,
                startTime);
        }

        // 돌진이 끝났다. 마지막이면 실행을 마치고, 아니면 재조준을 시작한다
        private void EndDash()
        {
            _isDashing = false;
            ++_completedDashes;
            ++_dashIndex;

            if (_dashIndex >= _dashCount)
            {
                _isEnded = true;
                return;
            }

            _nextStart = _dashEnd + _spec.ReaimSeconds;

            // 다음 돌진이 실행 구간 안에서 시작할 수 없으면 재조준을 보이지 않고 여기서 남은 돌진을 버린다
            if (!IsReached(_executeBoundary, _nextStart))
            {
                CutRemainingDashes();
                return;
            }

            // 앞 돌진이 벽에 막혀 멈췄어도 멈춘 자리에서 대상 쪽으로 새로 조준한다
            TryAimAtTarget();
            _clearLength = MeasureClearLength(_aim);

            _listener?.OnReaimStarted(Context.CastInstanceId, _dashIndex, Context.Self.Position, _aim, _clearLength,
                Context.Definition.Shape.Width, _spec.ReaimSeconds);
        }

        // 실행이 끝나는 경계에서 아직 달리는 돌진은 그 자리에서 멈추고 남은 돌진은 버린다
        private void CutRemainingDashes()
        {
            if ((!_isExecuting) || _isEnded || IsCancelled)
            {
                return;
            }

            if (_isDashing)
            {
                _isDashing = false;
                _listener?.OnDashStopped(Context.CastInstanceId, _dashIndex, _position, false);
            }

            _isEnded = true;
            _listener?.OnTruncated(Context.CastInstanceId, _dashCount, _completedDashes);
        }

        // 지금 돌진의 속도는 정의의 거리 / 돌진 시간으로 일정하고, 돌진 끝 시각 이후에는 정의의 거리를 넘지 않는다
        private float DistanceAt(double serverTime)
        {
            float range = Context.Definition.Shape.Range;

            if (serverTime >= _dashEnd)
            {
                return range;
            }

            double elapsed = serverTime - _dashStart;
            if (elapsed <= 0.0)
            {
                return 0f;
            }

            return (float)(range * (elapsed / _spec.DashSeconds));
        }

        private void AdvanceDash(double serverTime)
        {
            if ((!_isDashing) || _isStopped)
            {
                return;
            }

            float desired = Mathf.Min(DistanceAt(serverTime), _maxDistance);
            if (desired > _traveled)
            {
                Vector3 to      = _origin + (_aim * desired);
                Vector3 reached = _movement.Move(_position, to, Context.Self.BodyRadius);

                Vector3 offset = reached - _origin;
                offset.y = 0f;

                // 앞으로만 가고 이번 틱의 목표를 넘지 않는다. 벽이 더 일찍 막았으면 그 지점이 새 최대 거리다
                float along = Mathf.Clamp((offset.x * _aim.x) + (offset.z * _aim.z), _traveled, desired);
                if (along < (desired - STOP_EPSILON))
                {
                    _maxDistance = along;
                }

                _position = reached;

                if (along > _traveled)
                {
                    SweepHits(_traveled, along);
                    _traveled = along;
                }
            }

            if (_traveled >= (_maxDistance - STOP_EPSILON))
            {
                _isStopped = true;
                _listener?.OnDashStopped(Context.CastInstanceId, _dashIndex, _position,
                    _maxDistance < (Context.Definition.Shape.Range - STOP_EPSILON));
            }
        }

        // 한 틱에 지나간 구간만 본다. 한 돌진 안에서 한 번 처리한 대상은 방어, 반격, 무적 결과와 상관없이 다시 판정하지 않는다
        private void SweepHits(float from, float to)
        {
            PatternDefinition definition = Context.Definition;
            float halfWidth  = definition.Shape.Width * HALF;
            float halfLength = (to - from) * HALF;

            // 조회는 원이라 구간 직사각형의 먼 모서리까지 닿는 반경이 필요하다. 안팎은 아래에서 직사각형으로 다시 가른다
            Vector3 center = _origin + (_aim * (from + halfLength));
            float   radius = Mathf.Sqrt((halfLength * halfLength) + (halfWidth * halfWidth)) + EDGE_EPSILON;
            Context.Targets.GetLivingInRadius(center, radius, ECombatFaction.Player, true, _buffer);

            float        rawDamage = definition.DamageRef * _referenceHealth;
            EDamageFlags flags     = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;
            int          hitCount  = 0;

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

                // 후방 판정 기준은 그 돌진의 시작 위치다. 틱 크기와 상관없이 같은 방향에서 온 공격으로 본다
                DamageRequest request = new DamageRequest(Context.Self.CombatId, target.CombatId,
                    Context.CastInstanceId, _actionInstanceId, rawDamage, flags, _origin, definition.DefinitionId);

                if (TryApplyHit(in request, out DamageResult _))
                {
                    ++hitCount;
                }
            }

            _buffer.Clear();

            if (hitCount > 0)
            {
                _listener?.OnHit(Context.CastInstanceId, _dashIndex, hitCount);
            }
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

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? fallback : direction.normalized;
        }
    }
}
