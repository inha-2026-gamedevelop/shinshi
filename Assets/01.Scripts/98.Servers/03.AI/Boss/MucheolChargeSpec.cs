// System
using System;

namespace Shinshi.AI
{
    // 돌진 거리, 폭, 시간, 피해, 일반 회복은 패턴 정의에 있고, 정의에 없는 충돌 회복만 이 설정이 가진다. 수치는 호출하는 쪽(데이터)이 정한다
    /// <summary> 무철 돌진의 충돌 설정, 충돌로 끝났을 때의 회복 시간 </summary>
    public sealed class MucheolChargeSpec
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly float _collisionRecoverySeconds;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 초, 벽이나 구조물에 막혀 끝났을 때 실행 종료 경계부터 재는 회복 시간 </summary>
        public float CollisionRecoverySeconds => _collisionRecoverySeconds;

        /****************************************
        *                Methods
        ****************************************/

        public MucheolChargeSpec(float collisionRecoverySeconds)
        {
            if ((float.IsNaN(collisionRecoverySeconds)) || (float.IsInfinity(collisionRecoverySeconds))
                || (collisionRecoverySeconds <= 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(collisionRecoverySeconds), "충돌 회복 시간은 0보다 큰 유한값이다");
            }

            _collisionRecoverySeconds = collisionRecoverySeconds;
        }
    }
}
