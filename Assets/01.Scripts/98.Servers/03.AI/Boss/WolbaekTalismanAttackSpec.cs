// System
using System;

namespace Shinshi.AI
{
    // 하드 부적진 기믹의 보스 공격 설정이다. 이지는 세부 공격 구성이 정해지지 않아 이 설정을 만들지 않는다.
    // 고리 두 개는 안쪽 시작, 경계, 바깥쪽 끝 세 반경으로 나눈다. 구조물의 중심 거리가 경계 반경과 같아 구조물의 안쪽 절반과 바깥쪽 절반에
    // 번갈아 안전한 자리가 남는다. 수치는 모두 시험 전 초기값이며 밸런스 데이터로 옮겨 갈 자리다
    /// <summary> 부적진 기믹 중 고리와 직선 공격의 설정, 반경과 폭과 예고와 간격과 피해와 밀려남 </summary>
    public sealed class WolbaekTalismanAttackSpec
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly float _innerRingMinRadius;
        private readonly float _ringSplitRadius;
        private readonly float _outerRingMaxRadius;
        private readonly float _lineWidth;
        private readonly float _telegraphSeconds;
        private readonly float _intervalSeconds;
        private readonly float _damageRef;
        private readonly float _pushDistance;
        private readonly bool  _resumesAmbientHazard;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> m, 안쪽 고리의 안쪽 반경 </summary>
        public float InnerRingMinRadius => _innerRingMinRadius;

        /// <summary> m, 안쪽 고리의 바깥 반경이자 바깥쪽 고리의 안쪽 반경 </summary>
        public float RingSplitRadius => _ringSplitRadius;

        /// <summary> m, 바깥쪽 고리의 바깥 반경 </summary>
        public float OuterRingMaxRadius => _outerRingMaxRadius;

        /// <summary> m, 직선 공격의 폭 </summary>
        public float LineWidth => _lineWidth;

        /// <summary> 초, 예고를 시작한 뒤 판정이 날 때까지 </summary>
        public float TelegraphSeconds => _telegraphSeconds;

        /// <summary> 초, 한 공격의 예고 시작에서 다음 공격의 예고 시작까지 </summary>
        public float IntervalSeconds => _intervalSeconds;

        /// <summary> 지역 기준 체력(Href) 대비 방어 전 피해 계수 </summary>
        public float DamageRef => _damageRef;

        /// <summary> m, 맞은 대상을 보스 바깥쪽으로 미는 거리 </summary>
        public float PushDistance => _pushDistance;

        /// <summary> 참이면 전환 시간이 끝난 뒤 상시 환경 위험물을 다시 켠다 </summary>
        public bool ResumesAmbientHazard => _resumesAmbientHazard;

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekTalismanAttackSpec(float innerRingMinRadius, float ringSplitRadius, float outerRingMaxRadius,
            float lineWidth, float telegraphSeconds, float intervalSeconds, float damageRef, float pushDistance,
            bool resumesAmbientHazard)
        {
            if ((!IsFinite(innerRingMinRadius)) || (innerRingMinRadius < 0f) || (!(ringSplitRadius > innerRingMinRadius))
                || (!(outerRingMaxRadius > ringSplitRadius)) || (float.IsInfinity(outerRingMaxRadius)))
            {
                throw new ArgumentOutOfRangeException(nameof(ringSplitRadius), "고리 반경은 안쪽 시작 < 경계 < 바깥쪽 끝의 유한값이다");
            }

            if ((!IsFinite(lineWidth)) || (lineWidth <= 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(lineWidth), "직선 폭은 0보다 큰 유한값이다");
            }

            if ((!IsFinite(telegraphSeconds)) || (telegraphSeconds <= 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(telegraphSeconds), "예고 시간은 0보다 큰 유한값이다");
            }

            if ((!IsFinite(intervalSeconds)) || (intervalSeconds <= telegraphSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "공격 간격은 예고 시간보다 긴 유한값이다");
            }

            if ((!IsFinite(damageRef)) || (damageRef <= 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(damageRef), "피해 계수는 0보다 큰 유한값이다");
            }

            if ((!IsFinite(pushDistance)) || (pushDistance < 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(pushDistance), "밀려남 거리는 0 이상의 유한값이다");
            }

            _innerRingMinRadius   = innerRingMinRadius;
            _ringSplitRadius      = ringSplitRadius;
            _outerRingMaxRadius   = outerRingMaxRadius;
            _lineWidth            = lineWidth;
            _telegraphSeconds     = telegraphSeconds;
            _intervalSeconds      = intervalSeconds;
            _damageRef            = damageRef;
            _pushDistance         = pushDistance;
            _resumesAmbientHazard = resumesAmbientHazard;
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }
    }
}
