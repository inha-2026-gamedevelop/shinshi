// System
using System.Collections.Generic;

namespace Shinshi.Network
{
    /// <summary> 파티 채팅의 수신 대상을 찾는 창구, 마을은 파티 방이 구현한다 </summary>
    public interface IChatPartyLookup
    {
        // 참가자가 든 파티의 참가자 번호(본인 포함)를 into에 채운다. 파티에 없으면 거짓
        bool TryGetPartyMembers(int participantId, List<int> into);
    }
}
