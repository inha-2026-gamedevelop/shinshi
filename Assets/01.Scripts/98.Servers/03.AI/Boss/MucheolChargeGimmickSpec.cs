// System
using System;

namespace Shinshi.AI
{
    // 발동 체력 비율, 필요한 구조물 수, 그로기 시간은 밸런스 값이라 실행 코드에 두지 않고 호출하는 쪽(데이터)이 정한다
    /// <summary> 무철 돌진 유도 기믹의 설정, 발동 체력 비율과 파괴해야 하는 서로 다른 구조물 수와 그로기 시간 </summary>
    public sealed class MucheolChargeGimmickSpec
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int MIN_STRUCTURE_COUNT = 1;

        /****************************************
        *                Fields
        ****************************************/

        private readonly float _triggerHealthRate;
        private readonly int   _requiredStructureCount;
        private readonly float _groggySeconds;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 체력 비율이 이 값 이하(경계 포함)가 되면 발동한다 </summary>
        public float TriggerHealthRate => _triggerHealthRate;

        /// <summary> 반복을 끝내려면 부서져야 하는 서로 다른 지정 구조물 수 </summary>
        public int RequiredStructureCount => _requiredStructureCount;

        /// <summary> 초, 마지막 시전의 회복이 끝난 경계부터의 그로기 시간 </summary>
        public float GroggySeconds => _groggySeconds;

        /****************************************
        *                Methods
        ****************************************/

        public MucheolChargeGimmickSpec(float triggerHealthRate, int requiredStructureCount, float groggySeconds)
        {
            if ((float.IsNaN(triggerHealthRate)) || (triggerHealthRate <= 0f) || (triggerHealthRate > 1f))
            {
                throw new ArgumentOutOfRangeException(nameof(triggerHealthRate), "발동 체력 비율은 0 초과 1 이하다");
            }

            if (requiredStructureCount < MIN_STRUCTURE_COUNT)
            {
                throw new ArgumentOutOfRangeException(nameof(requiredStructureCount), "필요한 구조물 수는 1 이상이다");
            }

            if ((float.IsNaN(groggySeconds)) || (float.IsInfinity(groggySeconds)) || (groggySeconds <= 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(groggySeconds), "그로기 시간은 0보다 큰 유한값이다");
            }

            _triggerHealthRate      = triggerHealthRate;
            _requiredStructureCount = requiredStructureCount;
            _groggySeconds          = groggySeconds;
        }
    }
}
