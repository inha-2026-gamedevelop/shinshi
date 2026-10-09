// System
using System;

namespace Shinshi.AI
{
    // PatternDefinition에는 맞은 대상의 밀림 거리가 없어 전장 공격의 실행 데이터를 따로 받는다.
    // 공격 반경, 각도, 피해, 예고와 회복 시간은 패턴 정의에서 읽는다. 수치는 호출하는 쪽(데이터)이 정하며 이 클래스에는 값이 없다
    /// <summary> 무철 전장 공격의 실행 데이터, 만든 뒤에는 바뀌지 않고 값이 어긋나면 만들 때 예외로 막는다 </summary>
    public sealed class MucheolBattlefieldSpec
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly float _pushDistance;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> m, 피해가 적용된 대상을 보스에게서 바깥으로 밀려고 시도하는 최대 거리 </summary>
        public float PushDistance => _pushDistance;

        /****************************************
        *                Methods
        ****************************************/

        // 0은 밀지 않는다는 뜻이다
        public MucheolBattlefieldSpec(float pushDistance)
        {
            if ((float.IsNaN(pushDistance)) || (float.IsInfinity(pushDistance)) || (pushDistance < 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(pushDistance), "밀림 거리는 0 이상의 유한값이다");
            }

            _pushDistance = pushDistance;
        }
    }
}
