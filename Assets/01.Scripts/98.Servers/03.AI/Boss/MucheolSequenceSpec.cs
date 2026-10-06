// System
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Shinshi.AI
{
    // 타 수와 형태 조합은 정하지 않는다. 검증은 타마다 형태에 맞는 크기와 지연 순서만 본다
    /// <summary> 무철 순차 타격의 타 목록, 만든 뒤에는 바뀌지 않고 값이 어긋나면 만들 때 예외로 막는다 </summary>
    public sealed class MucheolSequenceSpec
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MAX_ARC_ANGLE   = 360f;
        private const float MAX_AIM_OFFSET  = 180f;

        /****************************************
        *                Fields
        ****************************************/

        // 공개 목록은 읽기 전용 래퍼다. 배열을 그대로 내보내면 호출자가 배열로 캐스팅해 타 값을 바꿀 수 있다
        private readonly ReadOnlyCollection<MucheolSequenceStep> _steps;

        /****************************************
        *              Properties
        ****************************************/

        public IReadOnlyList<MucheolSequenceStep> Steps => _steps;

        public int StepCount => _steps.Count;

        /****************************************
        *                Methods
        ****************************************/

        // 지연은 앞 타보다 같거나 늦어야 한다. 순서가 뒤집히면 타 번호 포인터가 뒤 타를 먼저 기다려 앞 타가 늦게 나간다
        public MucheolSequenceSpec(params MucheolSequenceStep[] steps)
        {
            if ((steps == null) || (steps.Length == 0))
            {
                throw new ArgumentException("순차 타격 타가 비어 있다", nameof(steps));
            }

            float previousDelay = 0f;
            for (int i = 0; i < steps.Length; ++i)
            {
                MucheolSequenceStep step = steps[i];

                if (!IsPositiveFinite(step.Range))
                {
                    throw new ArgumentOutOfRangeException(nameof(steps), $"타 {i}의 크기는 0보다 큰 유한값이다");
                }

                if ((step.Shape == EMucheolSequenceShape.Fan) && ((!IsPositiveFinite(step.ArcAngle)) || (step.ArcAngle > MAX_ARC_ANGLE)))
                {
                    throw new ArgumentOutOfRangeException(nameof(steps), $"타 {i}의 각도는 0 초과 360 이하다");
                }

                if ((step.Shape == EMucheolSequenceShape.Line) && (!IsPositiveFinite(step.Width)))
                {
                    throw new ArgumentOutOfRangeException(nameof(steps), $"타 {i}의 폭은 0보다 큰 유한값이다");
                }

                if ((float.IsNaN(step.AimOffsetDegrees)) || (float.IsInfinity(step.AimOffsetDegrees))
                    || (Math.Abs(step.AimOffsetDegrees) > MAX_AIM_OFFSET))
                {
                    throw new ArgumentOutOfRangeException(nameof(steps), $"타 {i}의 방향 오프셋은 -180 이상 180 이하다");
                }

                if (!IsPositiveFinite(step.DamageRef))
                {
                    throw new ArgumentOutOfRangeException(nameof(steps), $"타 {i}의 피해 배수는 0보다 큰 유한값이다");
                }

                if ((float.IsNaN(step.DelaySeconds)) || (float.IsInfinity(step.DelaySeconds)) || (step.DelaySeconds < previousDelay))
                {
                    throw new ArgumentOutOfRangeException(nameof(steps), $"타 {i}의 지연은 0 이상이고 앞 타보다 같거나 늦어야 한다");
                }

                previousDelay = step.DelaySeconds;
            }

            // 입력 배열을 복사해 감싼다. 호출자가 원본 배열을 바꿔도 이 객체는 달라지지 않는다
            _steps = new ReadOnlyCollection<MucheolSequenceStep>((MucheolSequenceStep[])steps.Clone());
        }

        private static bool IsPositiveFinite(float value)
        {
            return (value > 0f) && (!float.IsInfinity(value));
        }
    }
}
