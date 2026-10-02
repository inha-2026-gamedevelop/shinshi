// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 시간과 사거리와 폭과 속도와 회전 속도와 피해 배수는 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    // 이 객체는 예고와 조준과 방향 고정과 발사까지만 맡는다. 발사된 투사체는 패턴이 끝나도 지속물 관리자가 이어서 진행하므로
    // 취소와 정상 종료(Finish)는 투사체를 건드리지 않는다. 투사체 정리는 IBossEffectLifetime.Cancel이 맡는다
    /// <summary> 월백 P04 여우불 투사체, 추적 회전 뒤 방향을 고정하고 예고가 끝나면 한 발을 발사한다 </summary>
    public sealed class BossProjectilePattern : BossPatternBase
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DIRECTION_SQR = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const float ANGLE_EPSILON     = 0.001f;    // 도, 경계 각도의 부동소수 오차 흡수

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                          _referenceHealth;
        private readonly IBossProjectileLauncher        _launcher;
        private readonly IBossProjectileTelegraphListener _listener;

        private Vector3 _aim;
        private double  _trackStart;
        private double  _trackedUntil;
        private bool    _isLocked;
        private bool    _hasFired;
        private float   _pathLength;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 현재 조준 방향, 수평 단위 벡터 </summary>
        public Vector3 AimDirection => _aim;

        public bool IsDirectionLocked => _isLocked;

        /// <summary> 예고가 보여 주는 경로 길이, 벽이 가까우면 사거리보다 짧다 </summary>
        public float PathLength => _pathLength;

        /// <summary> 한 발을 발사했다, 한 시전에 한 번만 참이 된다 </summary>
        public bool HasFired => _hasFired;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        public BossProjectilePattern(float referenceHealth, IBossProjectileLauncher launcher, IBossProjectileTelegraphListener listener = null)
        {
            if (launcher == null)
            {
                throw new ArgumentNullException(nameof(launcher));
            }

            _referenceHealth = referenceHealth;
            _launcher        = launcher;
            _listener        = listener;
        }

        protected override void OnBegin(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            _aim          = Flatten(Context.Self.Forward, Vector3.forward);
            _trackStart   = serverTime;
            _trackedUntil = serverTime;
            _isLocked     = false;
            _hasFired     = false;
            _pathLength   = MeasurePath(_aim);

            _listener?.OnTelegraphStarted(Context.CastInstanceId, Context.Self.Position, _aim, _pathLength,
                definition.Shape.Width, definition.TelegraphSeconds);

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

            // 큰 시간 간격으로 틱이 건너뛰어도 고정은 반드시 발사보다 먼저 끝난다. serverTime은 FSM이 넘기는 예고 끝 경계다
            AdvanceTracking(serverTime);
            LockDirection();
            Fire(serverTime);
        }

        // 발사 전에 끊긴 경우만 알린다. 발사한 뒤의 투사체 소멸은 지속물 관리자가 알린다
        protected override void OnCancelled(EBossInterruptReason reason)
        {
            if (!_hasFired)
            {
                _listener?.OnCancelled(Context.CastInstanceId, reason);
            }
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

            if (elapsed > 0.0)
            {
                RotateTowardTarget((float)elapsed * Context.Definition.TurnRate);
                _trackedUntil = until;
            }

            if (serverTime >= trackEnd)
            {
                LockDirection();
            }
        }

        // 타깃이 사라졌거나 죽었으면 마지막 유효 방향을 유지한다
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

            // 거의 같은 방향에서 acos 기반 SignedAngle은 흔들려 먼 끝에서 어긋난다. atan2로 구한다
            float cross = (_aim.z * toTarget.x) - (_aim.x * toTarget.z);
            float dot   = (_aim.x * toTarget.x) + (_aim.z * toTarget.z);
            float angle = Mathf.Atan2(cross, dot) * Mathf.Rad2Deg;
            float step  = Mathf.Clamp(angle, -maxDegrees, maxDegrees);
            if (Mathf.Abs(step) <= ANGLE_EPSILON)
            {
                return;
            }

            _aim        = (Quaternion.AngleAxis(step, Vector3.up) * _aim).normalized;
            _pathLength = MeasurePath(_aim);
            _listener?.OnAimChanged(Context.CastInstanceId, _aim, _pathLength);
        }

        private void LockDirection()
        {
            if (_isLocked)
            {
                return;
            }

            _isLocked = true;
            _listener?.OnDirectionLocked(Context.CastInstanceId, _aim, _pathLength);
        }

        // 예고가 보여 주는 길이와 실제 비행 길이가 같은 계산이다
        private float MeasurePath(Vector3 direction)
        {
            PatternDefinition definition = Context.Definition;
            return _launcher.MeasurePath(Context.Self.Position, direction, definition.Shape.Range, definition.Shape.Width);
        }

        // 한 시전에 한 발만 쏜다. 발사 위치는 보스의 서버 위치이고 시각은 예고가 끝난 경계다
        private void Fire(double serverTime)
        {
            if (_hasFired)
            {
                return;
            }

            _hasFired = true;

            PatternDefinition definition = Context.Definition;
            EDamageFlags flags = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;

            BossProjectileSpec spec = new BossProjectileSpec(Context.CastInstanceId, Context.Damage.IssueInstanceId(),
                Context.Self.CombatId, definition.DefinitionId, Context.Self.Position, _aim, definition.Shape.ProjectileSpeed,
                definition.Shape.Range, definition.Shape.Width, definition.DamageRef * _referenceHealth, flags, serverTime, Context.Damage);

            _launcher.TryLaunch(in spec);
        }

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? fallback : direction.normalized;
        }
    }
}
