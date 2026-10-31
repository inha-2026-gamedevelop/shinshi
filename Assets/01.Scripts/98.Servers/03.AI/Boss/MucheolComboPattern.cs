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
    // 예고 시간, 추적, 회전 속도, 실행 시간, 회복은 패턴 정의에서, 타별 형태와 피해와 지연은 콤보 데이터에서 읽는다. 이 클래스에는 밸런스 값이 없다.
    // 월백 부채꼴 패턴을 두 번 시작하지 않고 한 시전에서 두 타를 관리하는 이유는, 한 시전이 방향 하나와 시전 번호 하나를 공유해야
    // 예고 표현과 정리(취소)가 시전 단위로 맞기 때문이다. 타마다 피해 동작 번호를 새로 받아 같은 대상이 타마다 한 번씩 맞는다.
    // 타는 실행 시작 시각 기준으로 예약하고, 틱이 거칠어 여러 타가 한꺼번에 도래해도 순서대로 하나씩 처리한다.
    // 타를 처리하는 시점의 보스와 대상 위치로 판정한다. 과거 위치를 기록하지 않아 거친 틱에서는 도래 시각의 위치와 다를 수 있다
    /// <summary> 무철 콤보, 추적 회전 뒤 방향을 고정하고 실행 단계에서 정해진 시각마다 전방 부채꼴을 한 번씩 타격한다 </summary>
    public sealed class MucheolComboPattern : BossPatternBase
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DIRECTION_SQR = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const float ANGLE_EPSILON     = 0.001f;    // 도, 경계 각도의 부동소수 오차 흡수

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                      _referenceHealth;
        private readonly MucheolComboSpec           _spec;
        private readonly IBossComboTelegraphListener _listener;
        private readonly MucheolAimTracker          _tracker;
        private readonly MucheolBodyShift           _bodyShift;
        private readonly List<ICombatTarget>        _buffer = new List<ICombatTarget>();

        private Vector3 _aim;
        private double  _trackStart;
        private double  _trackedUntil;
        private bool    _isLocked;
        private bool    _isExecuting;
        private double  _executeStart;
        private double  _recoverEnd;
        private int     _nextHit;
        private int     _scheduledHitCount;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 현재 조준 방향, 수평 단위 벡터 </summary>
        public Vector3 AimDirection => _aim;

        public bool IsDirectionLocked => _isLocked;

        /// <summary> 지금까지 처리한 타 수, 다음에 칠 타의 번호와 같다 </summary>
        public int ResolvedHitCount => _nextHit;

        /// <summary> 실행이 시작된 뒤 이 시전에서 칠 수 있는 타 수, 회복 종료보다 늦은 타는 세지 않는다 </summary>
        public int ScheduledHitCount => _scheduledHitCount;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        // naturalAim을 켜면 추적 회전이 MucheolAimTracker의 최단 경로 부드러운 회전이 된다. 기본값 꺼짐은 회전 속도로 제한하는 기존 동작이다
        // bodyShift는 무철 몸 이동이다. 있으면 타마다 그 타 확정 순간의 보스 위치에서 판정하고, 예고는 첫 타의 예상 위치에 그린다.
        // 예고 사건은 모든 타가 기준점 하나를 공유해 둘째 타부터는 예고와 판정 위치가 경로 차이만큼 다를 수 있다
        public MucheolComboPattern(float referenceHealth, MucheolComboSpec spec, IBossComboTelegraphListener listener = null,
            bool naturalAim = false, MucheolBodyShift bodyShift = null)
        {
            _tracker = naturalAim ? new MucheolAimTracker() : null;

            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            _referenceHealth = referenceHealth;
            _spec            = spec;
            _listener        = listener;
            _bodyShift       = bodyShift;
        }

        protected override void OnBegin(double serverTime)
        {
            _aim          = Flatten(Context.Self.Forward, Vector3.forward);
            _trackStart   = serverTime;
            _trackedUntil = serverTime;
            _isLocked     = false;
            _isExecuting  = false;
            _executeStart = 0.0;
            _recoverEnd   = 0.0;
            _nextHit      = 0;
            _scheduledHitCount = 0;

            _tracker?.Begin(_aim, serverTime, Context.Definition.TrackingSeconds, Context.Definition.TurnRate);

            Vector3 origin = Context.Self.Position;
            if (_bodyShift != null)
            {
                _bodyShift.Begin(Context.Self, MucheolBodyShift.ResolveForward(Context, _tracker != null), serverTime,
                    serverTime + Context.Definition.TelegraphSeconds);
                origin = _bodyShift.PredictStrikePosition(0);
            }

            _listener?.OnTelegraphStarted(Context.CastInstanceId, origin, _aim,
                Context.Definition.TelegraphSeconds, _spec.Hits);

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
                ResolveDueHits(serverTime);
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
                ScheduleHits();
                ResolveDueHits(serverTime);
                AdvanceBody(serverTime);
            }
            else if (state == EBossAttackState.Recover)
            {
                // 실행 끝 경계와 같은 시각의 타(지연이 실행 시간과 같은 타)는 틱이 경계에서 끊겨도 여기서 마무리한다
                ResolveDueHits(serverTime);
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

            double limit = (_nextHit < _spec.HitCount) ? Math.Min(serverTime, _bodyShift.GetStrikeTime(_nextHit)) : serverTime;
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

        // 타깃이 사라졌거나 죽었으면 마지막으로 읽은 방향을 유지한다
        private void TrackNaturally(double serverTime)
        {
            bool hasTarget = MucheolAimTracker.TryReadTarget(Context, Context.Self.Position, out Vector3 toTarget);
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

            Vector3 toTarget = target.Position - Context.Self.Position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < MIN_DIRECTION_SQR)
            {
                return;
            }

            float angle = Vector3.SignedAngle(_aim, toTarget, Vector3.up);
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
        private void ScheduleHits()
        {
            PatternDefinition definition = Context.Definition;
            _recoverEnd = (_executeStart + definition.ActiveSeconds) + definition.RecoverySeconds;

            _scheduledHitCount = 0;
            for (int i = 0; i < _spec.HitCount; ++i)
            {
                if ((_executeStart + _spec.Hits[i].DelaySeconds) > _recoverEnd)
                {
                    break;
                }

                ++_scheduledHitCount;
            }
        }

        // 도래한 타를 앞에서부터 하나씩 처리한다. 타 번호 포인터가 한 번 처리한 타를 다시 치지 않게 한다
        private void ResolveDueHits(double serverTime)
        {
            if (!_isExecuting)
            {
                return;
            }

            while ((_nextHit < _scheduledHitCount) && (!IsCancelled) && (!IsFinished))
            {
                MucheolComboHit hit = _spec.Hits[_nextHit];
                if (serverTime < (_executeStart + hit.DelaySeconds))
                {
                    return;
                }

                // 몸 이동은 이 타의 확정 순간까지만 옮겨 그 순간의 위치에서 판정한다
                _bodyShift?.Advance(_executeStart + hit.DelaySeconds);

                int index = _nextHit;
                ++_nextHit;
                Strike(index, hit);
            }
        }

        // 타마다 피해 동작 번호를 새로 받는다. 한 타 안에서는 한 번호를 공유해 대상당 한 번이다
        private void Strike(int index, MucheolComboHit hit)
        {
            Vector3 origin = Context.Self.Position;

            // 소환물도 범위 피해를 받는다
            Context.Targets.GetLivingInRadius(origin, hit.Range, ECombatFaction.Player, true, _buffer);

            int          actionInstanceId = Context.Damage.IssueInstanceId();
            float        rawDamage        = hit.DamageRef * _referenceHealth;
            EDamageFlags flags            = Context.Definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;
            int          hitCount         = 0;

            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget target = _buffer[i];
                if (!BossFanShape.IsInsideSector(origin, _aim, target.Position, hit.Range, hit.ArcAngle))
                {
                    continue;
                }

                DamageRequest request = new DamageRequest(Context.Self.CombatId, target.CombatId,
                    Context.CastInstanceId, actionInstanceId, rawDamage, flags, origin, Context.Definition.DefinitionId);

                if (TryApplyHit(in request, out DamageResult _))
                {
                    ++hitCount;
                }
            }

            _buffer.Clear();
            _listener?.OnHit(Context.CastInstanceId, index, hitCount);
        }

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? fallback : direction.normalized;
        }
    }
}
