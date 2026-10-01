// System
using System;

namespace Shinshi.Common
{
    /// <summary> 피해 요청의 성질, 공격 명세에서 정해지고 판정 중에 바뀌지 않는다 </summary>
    [Flags]
    public enum EDamageFlags
    {
        None        = 0,

        // 정면이어도 반격 방어로 막지 못한다. 지면 지속 피해와 방어 불가 패턴
        Unblockable = 1,

        // 적중해도 공격자 쪽에 공명을 주지 않는다. 소환 자체와 강림 피해
        NoResonance = 2
    }
}
