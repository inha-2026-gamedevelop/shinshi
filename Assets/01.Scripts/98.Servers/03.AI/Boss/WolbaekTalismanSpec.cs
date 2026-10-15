// System
using System;

namespace Shinshi.AI
{
    // 발동 체력 비율과 각 단계 시간과 구조물 배치는 밸런스 값이라 실행 코드에 두지 않고 호출하는 쪽(데이터)이 정한다.
    // 보스 공격 설정(Attack)은 하드에서만 있고 이지는 비워 둔다. 이지의 세부 공격 구성은 정해지지 않았다
    /// <summary> 월백 사방 부적진 기믹의 설정, 발동 체력과 단계 시간과 구조물 배치와 하드 공격 </summary>
    public sealed class WolbaekTalismanSpec
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly float                    _triggerHealthRate;
        private readonly float                    _nextBoundaryHealthRate;
        private readonly float                    _transitionSeconds;
        private readonly float                    _holdSeconds;
        private readonly float                    _counterSeconds;
        private readonly float                    _structureDistance;
        private readonly float                    _structureRadius;
        private readonly WolbaekTalismanAttackSpec _attack;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 체력 비율이 이 값 이하(경계 포함)가 되면 발동한다, 그 전에는 이 값에서 멈춘다 </summary>
        public float TriggerHealthRate => _triggerHealthRate;

        /// <summary> 반격 시간 중 보스가 더 내려가지 못하는 다음 경계의 체력 비율 </summary>
        public float NextBoundaryHealthRate => _nextBoundaryHealthRate;

        /// <summary> 초, 공격 없는 전환 시간, 월백이 이 시간 동안 전장 중앙으로 이동한다 </summary>
        public float TransitionSeconds => _transitionSeconds;

        /// <summary> 초, 필수 참가자 전원이 자기 자리에 연속으로 머물러야 하는 시간 </summary>
        public float HoldSeconds => _holdSeconds;

        /// <summary> 초, 성공 뒤 보스가 피해를 받는 시간 </summary>
        public float CounterSeconds => _counterSeconds;

        /// <summary> m, 기준점에서 동서남북 구조물 중심까지 </summary>
        public float StructureDistance => _structureDistance;

        /// <summary> m, 구조물 반경, 이 안에 있어야 자리를 지킨 것이다 </summary>
        public float StructureRadius => _structureRadius;

        /// <summary> 하드 보스 공격 설정, 이지는 null이다 </summary>
        public WolbaekTalismanAttackSpec Attack => _attack;

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekTalismanSpec(float triggerHealthRate, float nextBoundaryHealthRate, float transitionSeconds,
            float holdSeconds, float counterSeconds, float structureDistance, float structureRadius,
            WolbaekTalismanAttackSpec attack)
        {
            if ((!IsFinite(triggerHealthRate)) || (triggerHealthRate <= 0f) || (triggerHealthRate > 1f))
            {
                throw new ArgumentOutOfRangeException(nameof(triggerHealthRate), "발동 체력 비율은 0 초과 1 이하다");
            }

            if ((!IsFinite(nextBoundaryHealthRate)) || (nextBoundaryHealthRate < 0f) || (nextBoundaryHealthRate >= triggerHealthRate))
            {
                throw new ArgumentOutOfRangeException(nameof(nextBoundaryHealthRate), "다음 경계 비율은 0 이상이고 발동 비율보다 낮다");
            }

            // 전환 시간 동안 월백이 중앙으로 이동하므로 0이면 이동할 시간이 없다
            if ((!IsFinite(transitionSeconds)) || (transitionSeconds <= 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(transitionSeconds), "전환 시간은 0보다 큰 유한값이다");
            }

            if ((!IsFinite(holdSeconds)) || (holdSeconds <= 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(holdSeconds), "자리 유지 시간은 0보다 큰 유한값이다");
            }

            if ((!IsFinite(counterSeconds)) || (counterSeconds < 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(counterSeconds), "반격 시간은 0 이상의 유한값이다");
            }

            if ((!IsFinite(structureRadius)) || (structureRadius <= 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(structureRadius), "구조물 반경은 0보다 큰 유한값이다");
            }

            if ((!IsFinite(structureDistance)) || (structureDistance <= structureRadius))
            {
                throw new ArgumentOutOfRangeException(nameof(structureDistance), "구조물 거리는 구조물 반경보다 큰 유한값이다");
            }

            _triggerHealthRate      = triggerHealthRate;
            _nextBoundaryHealthRate = nextBoundaryHealthRate;
            _transitionSeconds      = transitionSeconds;
            _holdSeconds            = holdSeconds;
            _counterSeconds         = counterSeconds;
            _structureDistance      = structureDistance;
            _structureRadius        = structureRadius;
            _attack                 = attack;
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }
    }
}
