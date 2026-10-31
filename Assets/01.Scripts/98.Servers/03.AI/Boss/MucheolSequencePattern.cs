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
    // 예고 시간, 추적, 회전 속도, 실행 시간, 회복은 패턴 정의에서, 타별 형태와 크기와 방향 오프셋과 피해와 지연은 순차 타격 데이터에서 읽는다.
    // 이 클래스에는 밸런스 값이 없다. 무철 1페이즈 콤보(MucheolComboPattern)는 타가 부채꼴뿐이고 방향 오프셋이 없어
    // 좌우 구역이나 충격파(직사각형), 이중 원형을 담지 못해 같은 구조로 형태를 넓힌 별도 실행으로 둔다. 1페이즈 콤보는 고치지 않는다.
    // 한 시전이 방향 하나와 시전 번호 하나를 공유해야 예고 표현과 정리(취소)가 시전 단위로 맞는다. 타마다 피해 동작 번호를 새로 받아
    // 같은 대상이 타마다 한 번씩 맞고, 한 타 안에서는 한 번호를 공유해 대상당 한 번이다.
    // 타는 실행 시작 시각 기준으로 예약하고, 틱이 거칠어 여러 타가 한꺼번에 도래해도 순서대로 하나씩 처리한다.
    // 기준점은 예고 시작 때의 보스 위치로 고정한다. 예고가 그 위치에 그려지므로 판정도 같은 위치를 쓴다.
    // 무철 몸 이동(bodyShift)이 있으면 보스가 움직이므로 예고는 첫 타의 예상 위치에 그리고, 판정은 타마다 그 타 확정 순간의 보스 위치다.
    // 예고 사건은 모든 타가 기준점 하나를 공유해 둘째 타부터는 예고와 판정 위치가 경로 차이만큼 다를 수 있다.
    // 대상 위치는 타를 처리하는 시점의 값이라 거친 틱에서는 도래 시각의 위치와 다를 수 있다
    /// <summary> 무철 2페이즈 순차 타격, 추적 회전 뒤 방향을 고정하고 실행 단계에서 정해진 시각마다 타별 형태를 한 번씩 타격한다 </summary>
    public sealed class MucheolSequencePattern : BossPatternBase
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DIRECTION_SQR = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const float ANGLE_EPSILON     = 0.001f;    // 도, 경계 각도의 부동소수 오차 흡수
        private const float EDGE_EPSILON      = 0.001f;    // m, 직사각형 경계와 조회 반경의 부동소수 오차 흡수
        private const float HALF              = 0.5f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                             _referenceHealth;
        private readonly MucheolSequenceSpec               _spec;
        private readonly IMucheolSequenceTelegraphListener _listener;
        private readonly MucheolAimTracker                 _tracker;
        private readonly MucheolBodyShift                  _bodyShift;
        private readonly List<ICombatTarget>               _buffer = new List<ICombatTarget>();

        private Vector3 _origin;
        private Vector3 _aim;
        private double  _trackStart;
        private double  _trackedUntil;
        private bool    _isLocked;
        private bool    _isExecuting;
        private double  _executeStart;
        private double  _recoverEnd;
        private int     _nextStep;
        private int     _scheduledStepCount;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 현재 조준 방향, 수평 단위 벡터 </summary>
        public Vector3 AimDirection => _aim;

        public bool IsDirectionLocked => _isLocked;

        /// <summary> 지금까지 처리한 타 수, 다음에 칠 타의 번호와 같다 </summary>
        public int ResolvedStepCount => _nextStep;

        /// <summary> 실행이 시작된 뒤 이 시전에서 칠 수 있는 타 수, 회복 종료보다 늦은 타는 세지 않는다 </summary>
        public int ScheduledStepCount => _scheduledStepCount;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        // naturalAim을 켜면 첫 조준(기준 방향)의 추적 회전이 MucheolAimTracker의 최단 경로 부드러운 회전이 된다. 타별 상대 각도는 그대로다.
        // 기본값 꺼짐은 회전 속도로 제한하는 기존 동작이다
        public MucheolSequencePattern(float referenceHealth, MucheolSequenceSpec spec, IMucheolSequenceTelegraphListener listener = null,
            bool naturalAim = false, MucheolBodyShift bodyShift = null)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            _referenceHealth = referenceHealth;
            _spec            = spec;
            _listener        = listener;
            _tracker         = naturalAim ? new MucheolAimTracker() : null;
            _bodyShift       = bodyShift;
        }

        protected override void OnBegin(double serverTime)
        {
            _origin             = Context.Self.Position;
            _aim                = Flatten(Context.Self.Forward, Vector3.forward);
            _trackStart         = serverTime;
            _trackedUntil       = serverTime;
            _isLocked           = false;
            _isExecuting        = false;
            _executeStart       = 0.0;
            _recoverEnd         = 0.0;
            _nextStep           = 0;
            _scheduledStepCount = 0;

            _tracker?.Begin(_aim, serverTime, Context.Definition.TrackingSeconds, Context.Definition.TurnRate);

            // 조준도 예상 타격 위치에서 잰다. 예고가 그 자리에서 대상 쪽을 향해야 하기 때문이다
            if (_bodyShift != null)
            {
                _bodyShift.Begin(Context.Self, MucheolBodyShift.ResolveForward(Context, _tracker != null), serverTime,
                    serverTime + Context.Definition.TelegraphSeconds);
                _origin = _bodyShift.PredictStrikePosition(0);
            }

            _listener?.OnTelegraphStarted(Context.CastInstanceId, _origin, _aim, Context.Definition.TelegraphSeconds, _spec.Steps);

            AdvanceTracking(serverTime);
        }

        // 예고 중에는 추적을 진행하고, 실행과 회복 단계에서는 도래한 타를 처리한다.
        // FSM은 단계 경계를 넘기 전에 현재 단계로 Tick을 한 번 부르므로 거친 틱의 뒤 타는 여기서 처리된다
        protected override void OnTick(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Telegraph)
            {
                AdvanceBody(serverTime);
                AdvanceTracking(serverTime);
            }
            else
            {
                ResolveDueSteps(serverTime);
                AdvanceBody(serverTime);
            }
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Execute)
            {
                AdvanceBody(serverTime);

                // 큰 시간 간격으로 틱이 건너뛰어도 고정은 반드시 첫 타보다 먼저 끝난다. serverTime은 FSM이 넘기는 예고 끝 경계다
                AdvanceTracking(serverTime);
                LockDirection();

                _isExecuting  = true;
                _executeStart = serverTime;
                ScheduleSteps();
                ResolveDueSteps(serverTime);
                AdvanceBody(serverTime);
            }
            else if (state == EBossAttackState.Recover)
            {
                // 실행 끝 경계와 같은 시각의 타(지연이 실행 시간과 같은 타)는 틱이 경계에서 끊겨도 여기서 마무리한다
                ResolveDueSteps(serverTime);
                AdvanceBody(serverTime);
            }
        }

        protected override void OnCancelled(EBossInterruptReason reason)
        {
            _listener?.OnCancelled(Context.CastInstanceId, reason);
        }

        protected override void OnFinished()
        {
            _bodyShift?.Complete();
        }

        // 아직 치지 않은 타의 확정 시각을 넘지 않는다. 거친 틱에서 몸이 타격 위치를 지나친 뒤에 판정하지 않게 한다
        private void AdvanceBody(double serverTime)
        {
            if (_bodyShift == null)
            {
                return;
            }

            double limit = (_nextStep < _spec.StepCount) ? Math.Min(serverTime, _bodyShift.GetStrikeTime(_nextStep)) : serverTime;
            _bodyShift.Advance(limit);
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

        // 타깃이 사라졌거나 죽었으면 마지막으로 읽은 방향을 유지한다. 기준점은 예고 시작 때의 보스 위치다
        private void TrackNaturally(double serverTime)
        {
            bool hasTarget = MucheolAimTracker.TryReadTarget(Context, _origin, out Vector3 toTarget);
            if (!_tracker.Advance(serverTime, hasTarget, toTarget))
            {
                return;
            }

            _aim = _tracker.Aim;
            _listener?.OnAimChanged(Context.CastInstanceId, _aim);
        }

        // 타깃이 사라졌거나 죽었으면 현재 방향을 유지한다
        private void RotateTowardTarget(float maxDegrees)
        {
            if (maxDegrees <= 0f)
            {
                return;
            }

            if (!Context.Targets.TryGet(Context.TargetCombatId, out ICombatTarget target))
            {
                return;
            }

            if (target.LifeState != EParticipantLifeState.Alive)
            {
                return;
            }

            Vector3 toTarget = target.Position - _origin;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < MIN_DIRECTION_SQR)
            {
                return;
            }

            // 거의 같은 방향에서 acos 기반 SignedAngle은 흔들려 긴 판정 끝에서 어긋난다. atan2로 구한다
            float cross = (_aim.z * toTarget.x) - (_aim.x * toTarget.z);
            float dot   = (_aim.x * toTarget.x) + (_aim.z * toTarget.z);
            float angle = Mathf.Atan2(cross, dot) * Mathf.Rad2Deg;
            float step  = Mathf.Clamp(angle, -maxDegrees, maxDegrees);
            if (Mathf.Abs(step) <= ANGLE_EPSILON)
            {
                return;
            }

            _aim = (Quaternion.AngleAxis(step, Vector3.up) * _aim).normalized;
            _listener?.OnAimChanged(Context.CastInstanceId, _aim);
        }

        private void LockDirection()
        {
            if (_isLocked)
            {
                return;
            }

            _isLocked = true;
            _listener?.OnDirectionLocked(Context.CastInstanceId, _aim);
        }

        // 실행 시작 시각이 정해지는 순간에 이 시전이 칠 수 있는 타를 가른다. 회복 종료는 FSM이 경계를 만드는 식(앞 경계에 더함)과
        // 같게 구하고, 그보다 늦은 타는 틱이 아무리 커도 치지 않는다. 지연은 앞 타보다 늦게만 설정되므로 앞에서부터 센다
        private void ScheduleSteps()
        {
            PatternDefinition definition = Context.Definition;
            _recoverEnd = (_executeStart + definition.ActiveSeconds) + definition.RecoverySeconds;

            _scheduledStepCount = 0;
            for (int i = 0; i < _spec.StepCount; ++i)
            {
                if ((_executeStart + _spec.Steps[i].DelaySeconds) > _recoverEnd)
                {
                    break;
                }

                ++_scheduledStepCount;
            }
        }

        // 도래한 타를 앞에서부터 하나씩 처리한다. 타 번호 포인터가 한 번 처리한 타를 다시 치지 않게 한다
        private void ResolveDueSteps(double serverTime)
        {
            if (!_isExecuting)
            {
                return;
            }

            while ((_nextStep < _scheduledStepCount) && (!IsCancelled) && (!IsFinished))
            {
                MucheolSequenceStep step = _spec.Steps[_nextStep];
                if (serverTime < (_executeStart + step.DelaySeconds))
                {
                    return;
                }

                // 몸 이동은 이 타의 확정 순간까지만 옮기고 그 순간의 보스 위치를 기준점으로 판정한다
                if (_bodyShift != null)
                {
                    _bodyShift.Advance(_executeStart + step.DelaySeconds);
                    _origin = Context.Self.Position;
                }

                int index = _nextStep;
                ++_nextStep;
                Strike(index, step);
            }
        }

        // 타마다 피해 동작 번호를 새로 받는다. 한 타 안에서는 한 번호를 공유해 대상당 한 번이다
        private void Strike(int index, MucheolSequenceStep step)
        {
            Vector3 direction = (Quaternion.AngleAxis(step.AimOffsetDegrees, Vector3.up) * _aim).normalized;

            // 조회는 원이라 직사각형의 먼 모서리까지 닿는 반경이 필요하다. 안팎은 아래에서 형태별로 다시 가른다
            Context.Targets.GetLivingInRadius(_origin, QueryRadius(step), ECombatFaction.Player, true, _buffer);

            int          actionInstanceId = Context.Damage.IssueInstanceId();
            float        rawDamage        = step.DamageRef * _referenceHealth;
            EDamageFlags flags            = Context.Definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;
            int          hitCount         = 0;

            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget target = _buffer[i];
                if (!IsInside(step, direction, target.Position))
                {
                    continue;
                }

                DamageRequest request = new DamageRequest(Context.Self.CombatId, target.CombatId,
                    Context.CastInstanceId, actionInstanceId, rawDamage, flags, _origin, Context.Definition.DefinitionId);

                if (TryApplyHit(in request, out DamageResult _))
                {
                    ++hitCount;
                }
            }

            _buffer.Clear();
            _listener?.OnStepHit(Context.CastInstanceId, index, hitCount);
        }

        private static float QueryRadius(MucheolSequenceStep step)
        {
            if (step.Shape != EMucheolSequenceShape.Line)
            {
                return step.Range;
            }

            float halfWidth = step.Width * HALF;
            return Mathf.Sqrt((step.Range * step.Range) + (halfWidth * halfWidth)) + EDGE_EPSILON;
        }

        private bool IsInside(MucheolSequenceStep step, Vector3 direction, Vector3 targetPosition)
        {
            switch (step.Shape)
            {
                case EMucheolSequenceShape.Fan:
                    return BossFanShape.IsInsideSector(_origin, direction, targetPosition, step.Range, step.ArcAngle);

                case EMucheolSequenceShape.Line:
                    return IsInsideLine(step, direction, targetPosition);

                default:
                    return IsInsideCircle(step, targetPosition);
            }
        }

        // 수평면에서 기준점 기준 전방 0~길이, 좌우 각각 폭의 절반까지이며 네 경계를 모두 포함한다
        private bool IsInsideLine(MucheolSequenceStep step, Vector3 direction, Vector3 targetPosition)
        {
            Vector3 offset = targetPosition - _origin;
            offset.y = 0f;

            float forward = (offset.x * direction.x) + (offset.z * direction.z);
            float lateral = (offset.x * direction.z) - (offset.z * direction.x);

            return (forward >= -EDGE_EPSILON) && (forward <= (step.Range + EDGE_EPSILON))
                && (Mathf.Abs(lateral) <= ((step.Width * HALF) + EDGE_EPSILON));
        }

        // 거리 경계를 포함한다. 수평 거리로 잰다
        private bool IsInsideCircle(MucheolSequenceStep step, Vector3 targetPosition)
        {
            Vector3 offset = targetPosition - _origin;
            offset.y = 0f;

            return offset.sqrMagnitude <= (step.Range * step.Range);
        }

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? fallback : direction.normalized;
        }
    }
}
