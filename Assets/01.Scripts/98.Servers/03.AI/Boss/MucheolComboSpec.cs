// System
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Shinshi.AI
{
    // 타 수는 정하지 않는다. 지금 쓰는 값은 두 타지만 검증은 타 수와 상관없이 같다
    /// <summary> 무철 콤보의 타 목록, 만든 뒤에는 바뀌지 않고 값이 어긋나면 만들 때 예외로 막는다 </summary>
    public sealed class MucheolComboSpec
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MAX_ARC_ANGLE = 360f;

        /****************************************
        *                Fields
        ****************************************/

        // 공개 목록은 읽기 전용 래퍼다. 배열을 그대로 내보내면 호출자가 배열로 캐스팅해 타 값을 바꿀 수 있다
        private readonly ReadOnlyCollection<MucheolComboHit> _hits;

        /****************************************
        *              Properties
        ****************************************/

        public IReadOnlyList<MucheolComboHit> Hits => _hits;

        public int HitCount => _hits.Count;

        /****************************************
        *                Methods
        ****************************************/

        // 지연은 앞 타보다 같거나 늦어야 한다. 순서가 뒤집히면 타 번호 포인터가 뒤 타를 먼저 기다려 앞 타가 늦게 나간다
        public MucheolComboSpec(params MucheolComboHit[] hits)
        {
            if ((hits == null) || (hits.Length == 0))
            {
                throw new ArgumentException("콤보 타가 비어 있다", nameof(hits));
            }

            float previousDelay = 0f;
            for (int i = 0; i < hits.Length; ++i)
            {
                MucheolComboHit hit = hits[i];

                if (!IsPositiveFinite(hit.Range))
                {
                    throw new ArgumentOutOfRangeException(nameof(hits), $"타 {i}의 사거리는 0보다 큰 유한값이다");
                }

                if ((!IsPositiveFinite(hit.ArcAngle)) || (hit.ArcAngle > MAX_ARC_ANGLE))
                {
                    throw new ArgumentOutOfRangeException(nameof(hits), $"타 {i}의 각도는 0 초과 360 이하다");
                }

                if (!IsPositiveFinite(hit.DamageRef))
                {
                    throw new ArgumentOutOfRangeException(nameof(hits), $"타 {i}의 피해 배수는 0보다 큰 유한값이다");
                }

                if ((float.IsNaN(hit.DelaySeconds)) || (float.IsInfinity(hit.DelaySeconds)) || (hit.DelaySeconds < previousDelay))
                {
                    throw new ArgumentOutOfRangeException(nameof(hits), $"타 {i}의 지연은 0 이상이고 앞 타보다 같거나 늦어야 한다");
                }

                previousDelay = hit.DelaySeconds;
            }

            // 입력 배열을 복사해 감싼다. 호출자가 원본 배열을 바꿔도 이 객체는 달라지지 않는다
            _hits = new ReadOnlyCollection<MucheolComboHit>((MucheolComboHit[])hits.Clone());
        }

        private static bool IsPositiveFinite(float value)
        {
            return (value > 0f) && (!float.IsInfinity(value));
        }
    }
}
