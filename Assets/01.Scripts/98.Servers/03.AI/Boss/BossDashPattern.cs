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
    // 시간과 거리와 폭과 회전 속도와 피해 배수는 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    // 판정 경로는 예고와 같은 직사각형(시작점에서 방향으로 길이, 좌우 폭의 절반)이고 끝단은 평평하다. 길이는 몸체가 벽 앞에서 실제로 갈 수 있는 거리다
    // 대상은 중심점으로 판정한다. 부채꼴, 원형, 직사각형 패턴이 모두 같은 방식이라 BodyRadius는 이동 검사에만 쓴다
    // 기본은 추적 시간 동안 회전 속도로 제한해 돌며 월백 P01과 무철 P03이 이 방식이다. snapAimToTarget을 켜면(무철 P06) 예고를 시작하는 순간 선택된 대상
    // 쪽으로 각도 제한 없이 방향을 잡고, 방향을 고정하는 시각까지 같은 대상을 따라간다. 켜도 고정한 뒤에는 돌진 중에 꺾이지 않는다
    // naturalAim을 켜면(무철 P03, 월백 P01) MucheolAimTracker로 최단 경로를 부드럽게 돌아 고정 시각에 대상 방향에 닿는다. snapAimToTarget과 같이 켤 수 없다
    /// <summary> 월백 P01 돌진, 추적 회전 뒤 방향을 고정하고 일정한 속도로 직진하며 지나간 구간의 대상을 한 번씩 타격한다 </summary>
    public sealed class BossDashPattern : BossPatternBase
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DIRECTION_SQR = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const float ANGLE_EPSILON     = 0.001f;    // 도, 경계 각도의 부동소수 오차 흡수
        private const float EDGE_EPSILON      = 0.001f;    // m, 직사각형 경계와 조회 반경의 부동소수 오차 흡수
        private const float STOP_EPSILON      = 0.001f;    // m, 도착과 벽 정지를 가르는 오차
        private const float HALF              = 0.5f;
        private const float SNAP_MAX_DEGREES  = 180f;      // 도, atan2 각도의 최댓값이라 어느 방향이든 제한 없이 돈다

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                     _referenceHealth;
        private readonly IBossMovement             _movement;
        private readonly IBossDashTelegraphListener _listener;
        private readonly bool                      _snapAimToTarget;
        private readonly MucheolAimTracker         _tracker;
        private readonly List<ICombatTarget>       _buffer  = new List<ICombatTarget>();
        private readonly HashSet<int>              _handled = new HashSet<int>();

        private Vector3 _aim;
        private double  _trackStart;
        private double  _trackedUntil;
        private bool    _isLocked;
        private float   _clearLength;

        private Vector3 _origin;
        private Vector3 _position;
        private double  _executeStart;
        private double  _executeEnd;
        private float   _maxDistance;
        private float   _traveled;
        private int     _actionInstanceId;
        private bool    _isDashing;
        private bool    _isStopped;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 현재 조준 방향, 수평 단위 벡터 </summary>
        public Vector3 AimDirection => _aim;

        public bool IsDirectionLocked => _isLocked;

        /// <summary> 예고가 보여 주는 길이이자 돌진이 갈 수 있는 최대 거리, 벽이 가까우면 정의의 거리보다 짧다 </summary>
        public float ClearLength => _clearLength;

        /// <summary> 돌진 시작점에서 지금까지 실제로 간 거리 </summary>
        public float TraveledDistance => _traveled;

        /// <summary> 더 이상 가지 않는다, 정해진 거리에 닿았거나 벽 앞에서 멈췄다 </summary>
        public bool IsStopped => _isStopped;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        // snapAimToTarget은 예고를 시작할 때 대상 쪽으로 바로 조준하고 회전 속도 제한을 두지 않는다. 기본값 꺼짐은 기존 동작이다
        // naturalAim은 부드러운 조준이다. 기본값 꺼짐은 회전 속도로 제한하는 기존 동작이고 무철과 월백 팩토리가 켠다
        public BossDashPattern(float referenceHealth, IBossMovement movement, IBossDashTelegraphListener listener = null,
            bool snapAimToTarget = false, bool naturalAim = false)
        {
            if (movement == null)
            {
                throw new ArgumentNullException(nameof(movement));
            }

            if (snapAimToTarget && naturalAim)
            {
                throw new ArgumentException("바로 조준과 부드러운 조준은 같이 쓸 수 없다", nameof(naturalAim));
            }

            _tracker = naturalAim ? new MucheolAimTracker() : null;

            _referenceHealth = referenceHealth;
            _movement        = movement;
            _listener        = listener;
            _snapAimToTarget = snapAimToTarget;
        }

        protected override void OnBegin(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            _aim              = Flatten(Context.Self.Forward, Vector3.forward);
            _trackStart       = serverTime;
            _trackedUntil     = serverTime;
            _isLocked         = false;
            _origin           = Context.Self.Position;
            _position         = _origin;
            _traveled         = 0f;
            _maxDistance      = 0f;
            _actionInstanceId = CombatIds.NONE;
            _isDashing        = false;
            _isStopped        = false;
            _handled.Clear();

            _tracker?.Begin(_aim, serverTime, definition.TrackingSeconds, definition.TurnRate);

            // 예고 시작 알림이 처음부터 대상 쪽 방향을 싣도록 알리기 전에 조준한다
            if (_snapAimToTarget)
            {
                TryTurnToward(SNAP_MAX_DEGREES);
            }

            _clearLength = MeasureClearLength(_aim);

            _listener?.OnTelegraphStarted(Context.CastInstanceId, _origin, _aim,
                _clearLength, definition.Shape.Width, definition.TelegraphSeconds);

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
                AdvanceDash(serverTime);
            }
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Execute)
            {
                BeginDash(serverTime);
            }
            else if (state == EBossAttackState.Recover)
            {
                // 실행 마지막 틱이 거칠었어도 실행 끝 시각까지의 이동과 타격은 여기서 마무리한다
                AdvanceDash(serverTime);
                _isDashing = false;
            }
        }

        protected override void OnCancelled(EBossInterruptReason reason)
        {
            _isDashing = false;
            _listener?.OnCancelled(Context.CastInstanceId, reason);
        }

        // 추적 구간 밖의 시간은 회전량에 넣지 않는다. 틱이 거칠어도 총 회전은 회전 속도 x 추적 시간을 넘지 않는다
        private void AdvanceTracking(double serverTime)
        {
            if (_isLocked)
            {
                return;
            }

            double trackEnd = _trackStart + Context.Definition.TrackingSeconds;
            double until    = Math.Min(serverTime, trackEnd);
            double elapsed  = until - _trackedUntil;

            if (_tracker != null)
            {
                TrackNaturally(serverTime);
            }
            else if (_snapAimToTarget)
            {
                // 고정 시각까지는 틱마다 대상 쪽으로 다시 잡는다. 틱이 거칠어도 고정 때의 방향은 그 틱의 대상 위치로 정해진다
                RotateTowardTarget(SNAP_MAX_DEGREES);
                _trackedUntil = until;
            }
            else if (elapsed > 0.0)
            {
                RotateTowardTarget((float)elapsed * Context.Definition.TurnRate);
                _trackedUntil = until;
            }

            if (serverTime >= trackEnd)
            {
                LockDirection();
            }
        }

        // 타깃이 사라졌거나 죽었으면 마지막으로 읽은 방향을 유지한다
        private void TrackNaturally(double serverTime)
        {
            bool hasTarget = MucheolAimTracker.TryReadTarget(Context, Context.Self.Position, out Vector3 toTarget);
            if (!_tracker.Advance(serverTime, hasTarget, toTarget))
            {
                return;
            }

            _aim         = _tracker.Aim;
            _clearLength = MeasureClearLength(_aim);
            _listener?.OnAimChanged(Context.CastInstanceId, _aim, _clearLength);
        }

        // 타깃이 사라졌거나 죽었으면 마지막 유효 방향을 유지한다
        private void RotateTowardTarget(float maxDegrees)
        {
            if (!TryTurnToward(maxDegrees))
            {
                return;
            }

            _clearLength = MeasureClearLength(_aim);
            _listener?.OnAimChanged(Context.CastInstanceId, _aim, _clearLength);
        }

        // 방향이 달라졌는지만 돌려주고 알리지 않는다. 알리는 일은 부르는 쪽이 한다
        private bool TryTurnToward(float maxDegrees)
        {
            if (maxDegrees <= 0f)
            {
                return false;
            }

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

            // 거의 같은 방향에서 acos 기반 SignedAngle은 흔들려 긴 경로 끝에서 어긋난다. atan2로 구한다
            float cross = (_aim.z * toTarget.x) - (_aim.x * toTarget.z);
            float dot   = (_aim.x * toTarget.x) + (_aim.z * toTarget.z);
            float angle = Mathf.Atan2(cross, dot) * Mathf.Rad2Deg;
            float step  = Mathf.Clamp(angle, -maxDegrees, maxDegrees);
            if (Mathf.Abs(step) <= ANGLE_EPSILON)
            {
                return false;
            }

            _aim = (Quaternion.AngleAxis(step, Vector3.up) * _aim).normalized;
            return true;
        }

        private void LockDirection()
        {
            if (_isLocked)
            {
                return;
            }

            _isLocked = true;
            _listener?.OnDirectionLocked(Context.CastInstanceId, _aim, _clearLength);
        }

        // 몸체 반경으로 잰 벽 앞 거리, 정의의 최대 거리를 넘지 않는다
        private float MeasureClearLength(Vector3 direction)
        {
            float range = Context.Definition.Shape.Range;
            float clear = _movement.GetClearDistance(Context.Self.Position, direction, range, Context.Self.BodyRadius);
            return Mathf.Clamp(clear, 0f, range);
        }

        // 큰 시간 간격으로 틱이 건너뛰어도 고정은 반드시 이동보다 먼저 끝난다. serverTime은 FSM이 넘기는 예고 끝 경계다
        private void BeginDash(double serverTime)
        {
            AdvanceTracking(serverTime);
            LockDirection();

            PatternDefinition definition = Context.Definition;

            _origin           = Context.Self.Position;
            _position         = _origin;
            _executeStart     = serverTime;
            _executeEnd       = serverTime + definition.ActiveSeconds;
            _maxDistance      = MeasureClearLength(_aim);
            _traveled         = 0f;
            _actionInstanceId = Context.Damage.IssueInstanceId();
            _isDashing        = true;
            _isStopped        = false;

            // 속도는 벽과 상관없이 정의의 거리 / 실행 시간이다. 실행 시간이 0이면 순간 이동이라 속도를 알릴 수 없어 0이다
            float speed = (definition.ActiveSeconds > 0f) ? (definition.Shape.Range / definition.ActiveSeconds) : 0f;
            _listener?.OnDashStarted(Context.CastInstanceId, _origin, _aim, _maxDistance, definition.ActiveSeconds, speed,
                serverTime);
        }

        // 속도는 정의의 거리 / 실행 시간으로 일정하다. 실행 끝 시각 이후에는 정의의 거리를 넘지 않는다
        private float DistanceAt(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            if (serverTime >= _executeEnd)
            {
                return definition.Shape.Range;
            }

            double elapsed = serverTime - _executeStart;
            if (elapsed <= 0.0)
            {
                return 0f;
            }

            return (float)(definition.Shape.Range * (elapsed / definition.ActiveSeconds));
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
                _listener?.OnDashStopped(Context.CastInstanceId, _position,
                    _maxDistance < (Context.Definition.Shape.Range - STOP_EPSILON), serverTime);
            }
        }

        // 한 틱에 지나간 구간만 본다. 한 번 처리한 대상은 방어, 반격, 무적 결과와 상관없이 다시 판정하지 않는다
        private void SweepHits(float from, float to)
        {
            PatternDefinition definition = Context.Definition;
            float halfWidth = definition.Shape.Width * HALF;
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

                // 후방 판정 기준은 돌진 시작 위치다. 틱 크기와 상관없이 같은 방향에서 온 공격으로 본다
                DamageRequest request = new DamageRequest(Context.Self.CombatId, target.CombatId,
                    Context.CastInstanceId, _actionInstanceId, rawDamage, flags, _origin, definition.DefinitionId);

                if (TryApplyHit(in request, out DamageResult _))
                {
                    ++hitCount;
                    ReportTargetHit(_listener, target, _origin);
                }
            }

            _buffer.Clear();

            if (hitCount > 0)
            {
                _listener?.OnHit(Context.CastInstanceId, hitCount);
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
