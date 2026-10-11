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
    // 기본은 추적 시간 동안 회전 속도로 제한해 돈다. naturalAim을 켜면(월백) MucheolAimTracker로 최단 경로를 부드럽게 돌아 고정 시각에 대상 방향에 닿는다
    //
    // 발 수와 간격은 패턴 정의에서 읽는다. 발 수가 미지정(0)이거나 1이면 예고가 끝나는 순간 한 발이고 1페이즈 P04가 이 경우다
    // 여러 발이면 k번째 발의 예약 시각은 실행 시작 경계 + k x 간격이고 모든 발이 같은 고정 방향을 쓴다. 발사는 예약 시각 기준으로 센다
    //   첫 발은 실행 단계에 들어가는 순간, 나머지는 실행 중 틱에서, 실행이 끝나는 경계에 놓인 마지막 발은 회복 단계에 들어가는 순간 놓치지 않고 낸다
    //   한 틱에 도래한 발이 둘 이상이면 건너뛰지 않고 모두 낸다. 발마다 발사 위치는 그 발을 처리하는 시점의 보스 서버 위치이고 경로 길이도 그때 잰다
    //   과거 위치를 기록하지 않으므로 틱이 거칠면 늦게 처리된 발은 예약 시각의 위치가 아니라 처리 시점의 보스 위치와 벽 조회를 쓴다
    // 발마다 새 동작 번호(ActionInstanceId)를 받는다. 시전 번호는 한 시전의 발들이 함께 쓰고, 투사체 한 발은 동작 번호로 구분한다
    /// <summary> 월백 여우불 투사체, 추적 회전 뒤 방향을 고정하고 예고가 끝나면 정의된 발 수만큼 같은 방향으로 순서대로 발사한다 </summary>
    public sealed class BossProjectilePattern : BossPatternBase
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DIRECTION_SQR = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const float  ANGLE_EPSILON    = 0.001f;    // 도, 경계 각도의 부동소수 오차 흡수
        private const double FIRE_EPSILON     = 0.000001;  // 초, 예약 시각 비교의 부동소수 오차 흡수

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                          _referenceHealth;
        private readonly IBossProjectileLauncher        _launcher;
        private readonly IBossProjectileTelegraphListener _listener;
        private readonly MucheolAimTracker              _tracker;

        private Vector3 _aim;
        private double  _trackStart;
        private double  _trackedUntil;
        private bool    _isLocked;
        private int     _firedCount;
        private double  _executeStart;
        private float   _pathLength;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 현재 조준 방향, 수평 단위 벡터 </summary>
        public Vector3 AimDirection => _aim;

        public bool IsDirectionLocked => _isLocked;

        /// <summary> 예고가 보여 주는 경로 길이, 벽이 가까우면 사거리보다 짧다 </summary>
        public float PathLength => _pathLength;

        /// <summary> 한 발이라도 발사했다 </summary>
        public bool HasFired => _firedCount > 0;

        /// <summary> 지금까지 발사한 발 수 </summary>
        public int FiredCount => _firedCount;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        // naturalAim은 기본값 꺼짐이 기존 동작(회전 속도 x 추적 시간으로 제한)이다. 월백 팩토리가 켠다
        public BossProjectilePattern(float referenceHealth, IBossProjectileLauncher launcher, IBossProjectileTelegraphListener listener = null,
            bool naturalAim = false)
        {
            if (launcher == null)
            {
                throw new ArgumentNullException(nameof(launcher));
            }

            _referenceHealth = referenceHealth;
            _launcher        = launcher;
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
            _firedCount   = 0;
            _pathLength   = MeasurePath(_aim);

            _tracker?.Begin(_aim, serverTime, definition.TrackingSeconds, definition.TurnRate);

            _listener?.OnTelegraphStarted(Context.CastInstanceId, Context.Self.Position, _aim, _pathLength,
                definition.Shape.Width, definition.TelegraphSeconds);

            AdvanceTracking(serverTime);
        }

        protected override void OnTick(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Telegraph)
            {
                AdvanceTracking(serverTime);
                return;
            }

            FireDue(serverTime);
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Recover)
            {
                // 실행이 끝나는 경계에 놓인 마지막 발이 틱 순서 때문에 빠지지 않게 여기서 한 번 더 확인한다. serverTime은 실행 끝 경계다
                FireDue(serverTime);
                return;
            }

            if (state != EBossAttackState.Execute)
            {
                return;
            }

            // 큰 시간 간격으로 틱이 건너뛰어도 고정은 반드시 발사보다 먼저 끝난다. serverTime은 FSM이 넘기는 예고 끝 경계다
            AdvanceTracking(serverTime);
            LockDirection();
            _executeStart = serverTime;
            FireDue(serverTime);
        }

        // 발사 전에 끊긴 경우만 알린다. 발사한 뒤의 투사체 소멸은 지속물 관리자가 알린다
        protected override void OnCancelled(EBossInterruptReason reason)
        {
            if (!HasFired)
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

            _aim        = _tracker.Aim;
            _pathLength = MeasurePath(_aim);
            _listener?.OnAimChanged(Context.CastInstanceId, _aim, _pathLength);
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

        // 예약 시각이 지금까지인 발을 모두 낸다. 발사 시각은 틱 시각이 아니라 예약 시각이라 틱이 거칠어도 발 사이 간격이 유지된다
        private void FireDue(double serverTime)
        {
            PatternDefinition definition = Context.Definition;
            int    count    = definition.ProjectileCount;
            double interval = definition.ProjectileIntervalSeconds;

            while (_firedCount < count)
            {
                double scheduled = _executeStart + (_firedCount * interval);
                if (scheduled > (serverTime + FIRE_EPSILON))
                {
                    return;
                }

                Fire(scheduled);
            }
        }

        // 발마다 보스의 지금 서버 위치에서 고정 방향으로 쏜다. 순서 번호를 먼저 올려 발사 중 재진입해도 같은 발을 두 번 내지 않는다
        private void Fire(double launchTime)
        {
            ++_firedCount;

            PatternDefinition definition = Context.Definition;
            EDamageFlags flags = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;

            BossProjectileSpec spec = new BossProjectileSpec(Context.CastInstanceId, Context.Damage.IssueInstanceId(),
                Context.Self.CombatId, definition.DefinitionId, Context.Self.Position, _aim, definition.Shape.ProjectileSpeed,
                definition.Shape.Range, definition.Shape.Width, definition.DamageRef * _referenceHealth, flags, launchTime, Context.Damage);

            _launcher.TryLaunch(in spec);
        }

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? fallback : direction.normalized;
        }
    }
}
