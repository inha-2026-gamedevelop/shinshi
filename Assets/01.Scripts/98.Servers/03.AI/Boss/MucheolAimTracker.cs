// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 무철 방향성 공격이 선택된 대상을 향해 돌 때 쓰는 조준 규칙이다. 월백의 돌진, 부채꼴, 투사체, 직선도 팩토리가 옵트인으로 같은 규칙을 쓴다.
    // 회전은 MucheolYawMotion이 맡아 최단 경로로 돌고(맞은편은 오른쪽, 돌던 방향은 한도 안에서 유지), 남은 각도와 남은 시간으로 속도를 정해
    // 방향 고정 시각(시작 + 추적 시간)까지 반드시 목표에 닿는다. 정의의 회전 속도는 도착 시간을 넉넉히 잡을 때의 최고 속도 기준으로만 쓴다.
    // 목표 갱신 중단: 고정 시각 직전의 짧은 구간(settle)에는 플레이어가 움직여도 목표를 갱신하지 않는다. 계속 움직이는 대상을 유한한 속도로
    // 마지막 순간까지 정확히 쫓으면서 회전이 부드러울 수는 없기 때문이다. 그 구간에서는 마지막으로 읽은 방향으로 부드럽게 수렴해
    // 고정 시각에 그 방향에 정확히 닿는다. 고정 시각 자체는 정의의 값 그대로이며, 고정 뒤에는 대상을 따라 바뀌지 않는다.
    // 구간 길이는 추적 시간의 비율이되 상한이 있다. 추적 시간이 0이면 돌 시간이 없어 방향이 바뀌지 않는다.
    // 대상이 사라졌거나 죽으면 마지막으로 읽은 방향을 유지하며, 다른 플레이어로 바꾸지 않는다
    /// <summary> 무철 방향성 공격의 조준, 최단 경로로 부드럽게 돌아 고정 시각에 대상 방향에 닿는다 </summary>
    public sealed class MucheolAimTracker
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double SETTLE_MAX_SECONDS  = 0.15;   // 초, 목표 갱신을 멈추는 고정 직전 구간의 상한
        private const double SETTLE_RATIO        = 0.3;    // 추적 시간 중 목표 갱신을 멈추는 구간의 비율
        private const float  MIN_HORIZON_SECONDS = 0.1f;   // 초, 아주 작은 각도도 이보다 빠르게 옮기지 않는다
        private const float  MIN_DIRECTION_SQR   = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const float  CHANGE_EPSILON      = 0.001f; // 도, 방향이 달라졌다고 알리는 최소 변화

        /****************************************
        *                Fields
        ****************************************/

        private readonly MucheolYawMotion _motion = new MucheolYawMotion();

        private Vector3 _aim;
        private double  _time;
        private double  _lockTime;
        private double  _freezeTime;
        private float   _nominalRate;
        private bool    _hasGoal;
        private bool    _isGoalFrozen;
        private float   _goalYaw;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 조준 방향, 수평 단위 벡터 </summary>
        public Vector3 Aim => _aim;

        /// <summary> 고정 시각 직전 구간에 들어 목표를 더 읽지 않는다 </summary>
        public bool IsGoalFrozen => _isGoalFrozen;

        /// <summary> 목표를 더 읽지 않는 시각 </summary>
        public double FreezeTime => _freezeTime;

        /// <summary> 이 시각까지 목표 방향에 닿는다, 방향을 고정하는 시각과 같다 </summary>
        public double LockTime => _lockTime;

        /****************************************
        *                Methods
        ****************************************/

        // aim은 시작 방향, nominalRate는 정의의 회전 속도(도/초)로 최고 속도의 기준이다
        public void Begin(Vector3 aim, double startTime, float trackingSeconds, float nominalRate)
        {
            _aim          = aim;
            _time         = startTime;
            _lockTime     = startTime + trackingSeconds;
            _freezeTime   = _lockTime - Math.Min(SETTLE_MAX_SECONDS, trackingSeconds * SETTLE_RATIO);
            _nominalRate  = nominalRate;
            _hasGoal      = false;
            _isGoalFrozen = false;
            _goalYaw      = 0f;

            _motion.Reset(ToYaw(aim), startTime);
        }

        /// <summary>
        /// 선택된 대상을 읽는다. origin에서 대상까지의 수평 벡터를 돌려주고, 대상이 없거나 죽었거나 너무 가까우면 거짓이다.
        /// 거짓이면 마지막으로 읽은 방향을 유지한다
        /// </summary>
        public static bool TryReadTarget(BossPatternContext context, Vector3 origin, out Vector3 toTarget)
        {
            toTarget = default;

            if (!context.Targets.TryGet(context.TargetCombatId, out ICombatTarget target))
            {
                return false;
            }

            if (target.LifeState != EParticipantLifeState.Alive)
            {
                return false;
            }

            toTarget   = target.Position - origin;
            toTarget.y = 0f;
            return toTarget.sqrMagnitude >= MIN_DIRECTION_SQR;
        }

        /// <summary> serverTime까지 진행한다, 방향이 달라졌으면 참이다. 고정 시각 이후의 시각은 고정 시각으로 본다 </summary>
        public bool Advance(double serverTime, bool hasTarget, Vector3 toTarget)
        {
            if (!_isGoalFrozen)
            {
                if (hasTarget)
                {
                    _goalYaw = ToYaw(toTarget);
                    _hasGoal = true;
                }

                if (serverTime >= _freezeTime)
                {
                    _isGoalFrozen = true;
                }
            }

            double now = Math.Min(serverTime, _lockTime);
            if ((!_hasGoal) || (now <= _time))
            {
                return false;
            }

            float before = _motion.Yaw;
            _motion.Advance(now, _goalYaw, _lockTime, _nominalRate, MIN_HORIZON_SECONDS);
            _time = now;

            float after = _motion.Yaw;
            if (Mathf.Abs(Mathf.DeltaAngle(before, after)) <= CHANGE_EPSILON)
            {
                return false;
            }

            _aim = FromYaw(after);
            return true;
        }

        private static float ToYaw(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        private static Vector3 FromYaw(float yaw)
        {
            float radians = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }
    }
}
