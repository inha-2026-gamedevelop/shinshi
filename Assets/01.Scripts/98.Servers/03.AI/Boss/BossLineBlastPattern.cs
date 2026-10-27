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
    // 시간과 길이와 폭과 회전 속도와 피해 배수는 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    // 기본은 추적 시간 동안 회전 속도로 제한해 돈다. naturalAim을 켜면(월백) MucheolAimTracker로 최단 경로를 부드럽게 돌아 고정 시각에 대상 방향에 닿는다
    /// <summary> 대형 전방 직사각형 요력 방출, 추적 회전 뒤 방향을 고정하고 예고가 끝나면 한 번 타격한다 </summary>
    public sealed class BossLineBlastPattern : BossPatternBase
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

        private readonly float                      _referenceHealth;
        private readonly IBossLineTelegraphListener _listener;
        private readonly List<ICombatTarget>        _buffer = new List<ICombatTarget>();
        private readonly MucheolAimTracker          _tracker;

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
        // naturalAim은 기본값 꺼짐이 기존 동작(회전 속도 x 추적 시간으로 제한)이다. 월백 팩토리가 켠다
        public BossLineBlastPattern(float referenceHealth, IBossLineTelegraphListener listener = null, bool naturalAim = false)
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
                definition.Shape.Range, definition.Shape.Width, definition.TelegraphSeconds);

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

            // 거의 같은 방향에서 acos 기반 SignedAngle은 0.02도쯤 흔들려 긴 직사각형 끝에서 수 mm 어긋난다. atan2로 구한다
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

        // 한 번만 쓴다. 대상마다 요청을 내지만 한 동작 번호를 공유하므로 대상당 한 번이다
        private void Strike()
        {
            if (_hasHit)
            {
                return;
            }

            _hasHit = true;

            PatternDefinition definition = Context.Definition;
            float   length = definition.Shape.Range;
            float   width  = definition.Shape.Width;
            Vector3 origin = Context.Self.Position;

            // 조회는 원이라 직사각형의 먼 모서리까지 닿는 반경이 필요하다. 안팎은 아래에서 직사각형으로 다시 가른다
            Context.Targets.GetLivingInRadius(origin, QueryRadius(length, width), ECombatFaction.Player, true, _buffer);

            int   actionInstanceId = Context.Damage.IssueInstanceId();
            float rawDamage        = definition.DamageRef * _referenceHealth;
            EDamageFlags flags     = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;
            int   hitCount         = 0;

            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget target = _buffer[i];
                if (!IsInsideRectangle(origin, _aim, target.Position, length, width))
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

        /// <summary> 시작점에서 직사각형의 가장 먼 모서리까지의 거리에 부동소수 여유를 더한 조회 반경 </summary>
        public static float QueryRadius(float length, float width)
        {
            float halfWidth = width * HALF;
            return Mathf.Sqrt((length * length) + (halfWidth * halfWidth)) + EDGE_EPSILON;
        }

        // 수평면에서 전방 0~길이, 좌우 각각 폭의 절반까지이며 네 경계를 모두 포함한다. 시작점 뒤쪽은 제외한다
        private static bool IsInsideRectangle(Vector3 origin, Vector3 aim, Vector3 targetPosition, float length, float width)
        {
            Vector3 offset = targetPosition - origin;
            offset.y = 0f;

            float forward = (offset.x * aim.x) + (offset.z * aim.z);
            float lateral = (offset.x * aim.z) - (offset.z * aim.x);

            return (forward >= -EDGE_EPSILON) && (forward <= (length + EDGE_EPSILON))
                && (Mathf.Abs(lateral) <= ((width * HALF) + EDGE_EPSILON));
        }

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? fallback : direction.normalized;
        }
    }
}
