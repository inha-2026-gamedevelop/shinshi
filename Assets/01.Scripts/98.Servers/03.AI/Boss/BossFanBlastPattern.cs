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
    // 시간과 거리와 각도와 피해 배수는 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    // 기본은 추적 시간 동안 회전 속도로 제한해 돈다. naturalAim을 켜면(무철, 월백) MucheolAimTracker로 최단 경로를 부드럽게 돌아 고정 시각에 대상 방향에 닿는다
    /// <summary> 전방 부채꼴 요력 폭발, 추적 회전 뒤 방향을 고정하고 예고가 끝나면 한 번 타격한다 </summary>
    public sealed class BossFanBlastPattern : BossPatternBase
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
        private readonly IBossFanTelegraphListener  _listener;
        private readonly MucheolAimTracker          _tracker;
        private readonly List<ICombatTarget>        _buffer = new List<ICombatTarget>();

        private Vector3 _aim;
        private double  _trackStart;
        private double  _trackedUntil;
        private bool    _isLocked;
        private bool    _hasHit;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 현재 조준 방향, 수평 단위 벡터 </summary>
        public Vector3 AimDirection => _aim;

        public bool IsDirectionLocked => _isLocked;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        // naturalAim은 부드러운 조준이다. 기본값 꺼짐은 회전 속도로 제한하는 기존 동작이고 무철과 월백 팩토리가 켠다
        public BossFanBlastPattern(float referenceHealth, IBossFanTelegraphListener listener = null, bool naturalAim = false)
        {
            _referenceHealth = referenceHealth;
            _listener        = listener;
            _tracker         = naturalAim ? new MucheolAimTracker() : null;
        }

        protected override void OnBegin(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            _aim          = Flatten(Context.Self.Forward, Vector3.forward);
            _trackStart   = serverTime;
            _trackedUntil = serverTime;
            _isLocked     = false;
            _hasHit       = false;

            _tracker?.Begin(_aim, serverTime, definition.TrackingSeconds, definition.TurnRate);

            _listener?.OnTelegraphStarted(Context.CastInstanceId, Context.Self.Position, _aim,
                definition.Shape.Range, definition.Shape.ArcAngle, definition.TelegraphSeconds);

            AdvanceTracking(serverTime);
        }

        protected override void OnTick(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Telegraph)
            {
                AdvanceTracking(serverTime);
            }
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            if (state != EBossAttackState.Execute)
            {
                return;
            }

            // 큰 시간 간격으로 틱이 건너뛰어도 고정은 반드시 타격보다 먼저 끝난다
            AdvanceTracking(serverTime);
            LockDirection();
            Strike();
        }

        protected override void OnCancelled(EBossInterruptReason reason)
        {
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

        // 한 번만 쓴다. 대상마다 요청을 내지만 한 동작 번호를 공유하므로 대상당 한 번이다
        private void Strike()
        {
            if (_hasHit)
            {
                return;
            }

            _hasHit = true;

            PatternDefinition definition = Context.Definition;
            float   radius = definition.Shape.Range;
            float   arc    = definition.Shape.ArcAngle;
            Vector3 origin = Context.Self.Position;

            // 소환물도 범위 피해를 받는다
            Context.Targets.GetLivingInRadius(origin, radius, ECombatFaction.Player, true, _buffer);

            int   actionInstanceId = Context.Damage.IssueInstanceId();
            float rawDamage        = definition.DamageRef * _referenceHealth;
            EDamageFlags flags     = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;
            int   hitCount         = 0;

            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget target = _buffer[i];
                if (!BossFanShape.IsInsideSector(origin, _aim, target.Position, radius, arc))
                {
                    continue;
                }

                DamageRequest request = new DamageRequest(Context.Self.CombatId, target.CombatId,
                    Context.CastInstanceId, actionInstanceId, rawDamage, flags, origin, definition.DefinitionId);

                if (TryApplyHit(in request, out DamageResult _))
                {
                    ++hitCount;
                    ReportTargetHit(_listener, target, origin);
                }
            }

            _buffer.Clear();
            _listener?.OnHit(Context.CastInstanceId, hitCount);
        }

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? fallback : direction.normalized;
        }
    }
}
