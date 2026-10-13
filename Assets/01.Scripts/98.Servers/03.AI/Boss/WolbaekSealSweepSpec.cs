// System
using System;

namespace Shinshi.AI
{
    // 하드 전용 넓은 휩쓸기 설정이다. 시각과 피해 비율과 밀려남은 기획이 정했고(추적 시작 후 1.5초와 3초, 0.8초 전 예고, 최대 체력 25%, 1m),
    // 범위(Range)와 각도(HalfAngleDegrees)와 첫 방향은 기획이 정하지 않았다. 범위와 각도는 입력 계약으로만 받고 값은 호출하는 쪽이 정한다.
    // 첫 방향을 서버 난수로 고르는 것은 임시 가정이다
    /// <summary> 하드의 넓은 휩쓸기 설정 </summary>
    public sealed class WolbaekSealSweepSpec
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MAX_HALF_ANGLE = 180f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly float[] _hitOffsetsSeconds;
        private readonly float   _telegraphSeconds;
        private readonly float   _damageRate;
        private readonly float   _pushDistance;
        private readonly float   _range;
        private readonly float   _halfAngleDegrees;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 추적 시작을 0으로 한 각 휩쓸기 판정 시각, 오름차순이다 </summary>
        public float[] HitOffsetsSeconds => (float[])_hitOffsetsSeconds.Clone();

        /// <summary> 휩쓸기 횟수 </summary>
        public int Count => _hitOffsetsSeconds.Length;

        /// <summary> 초, 판정 앞의 예고 시간 </summary>
        public float TelegraphSeconds => _telegraphSeconds;

        /// <summary> 플레이어 최대 체력 대비 방어 전 피해 비율, 방어와 반격으로 막지 못한다 </summary>
        public float DamageRate => _damageRate;

        /// <summary> m, 맞은 대상이 월백 바깥쪽으로 밀리는 거리 </summary>
        public float PushDistance => _pushDistance;

        /// <summary> m, 월백 중심에서 닿는 거리 </summary>
        public float Range => _range;

        /// <summary> 도, 방향 기준 좌우 각도 </summary>
        public float HalfAngleDegrees => _halfAngleDegrees;

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekSealSweepSpec(float[] hitOffsetsSeconds, float telegraphSeconds, float damageRate, float pushDistance,
            float range, float halfAngleDegrees)
        {
            if ((hitOffsetsSeconds == null) || (hitOffsetsSeconds.Length == 0))
            {
                throw new ArgumentException("휩쓸기 판정 시각이 비었다", nameof(hitOffsetsSeconds));
            }

            RequirePositive(telegraphSeconds, nameof(telegraphSeconds), "휩쓸기 예고 시간은 0보다 큰 유한값이다");
            RequirePositive(damageRate, nameof(damageRate), "휩쓸기 피해 비율은 0보다 큰 유한값이다");
            RequirePositive(range, nameof(range), "휩쓸기 범위는 0보다 큰 유한값이다");

            if ((!IsFinite(pushDistance)) || (pushDistance < 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(pushDistance), "밀려남 거리는 0 이상의 유한값이다");
            }

            if ((!IsFinite(halfAngleDegrees)) || (halfAngleDegrees <= 0f) || (halfAngleDegrees > MAX_HALF_ANGLE))
            {
                throw new ArgumentOutOfRangeException(nameof(halfAngleDegrees), "휩쓸기 좌우 각도는 0 초과 180 이하다");
            }

            float previous = 0f;
            for (int i = 0; i < hitOffsetsSeconds.Length; ++i)
            {
                if ((!IsFinite(hitOffsetsSeconds[i])) || (!(hitOffsetsSeconds[i] > previous)))
                {
                    throw new ArgumentOutOfRangeException(nameof(hitOffsetsSeconds), "휩쓸기 판정 시각은 0보다 크고 오름차순이다");
                }

                previous = hitOffsetsSeconds[i];
            }

            if (telegraphSeconds > hitOffsetsSeconds[0])
            {
                throw new ArgumentOutOfRangeException(nameof(telegraphSeconds), "첫 예고는 추적 시작 전에 시작할 수 없다");
            }

            _hitOffsetsSeconds = (float[])hitOffsetsSeconds.Clone();
            _telegraphSeconds  = telegraphSeconds;
            _damageRate        = damageRate;
            _pushDistance      = pushDistance;
            _range             = range;
            _halfAngleDegrees  = halfAngleDegrees;
        }

        private static void RequirePositive(float value, string name, string message)
        {
            if ((!IsFinite(value)) || (value <= 0f))
            {
                throw new ArgumentOutOfRangeException(name, message);
            }
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }
    }
}
