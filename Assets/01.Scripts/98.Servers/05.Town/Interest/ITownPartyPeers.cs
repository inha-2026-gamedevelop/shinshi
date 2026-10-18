// System
using System.Collections.Generic;

namespace Shinshi.Network
{
    /// <summary> 한 접속자의 파티원 번호를 준다. 번호는 풀이에 넣은 접속자 번호(id)와 같은 공간이다 </summary>
    public interface ITownPartyPeers
    {
        // 파티원 번호(본인 포함 여부는 상관없다)를 into에 채운다. 파티에 없으면 거짓
        bool TryGetPeers(int id, List<int> into);
    }
}
