// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 전장의 중심과 반경을 패턴에 주는 입력 창구다. 보스 위치는 전장 중심이 아니므로 패턴이 보스 위치로 대신하지 않는다
    // 실제 값의 출처(보스 세션이 가진 전장 정보)를 이 창구에 연결하는 일은 서버 연결 담당이 한다. 연결되지 않으면 전장 기준 패턴은 시작을 거절한다
    /// <summary> 원형 전장의 중심과 반경 조회 </summary>
    public interface IBossArenaSource
    {
        /// <summary> 전장 중심(월드)과 반경(m)을 돌려준다. 정보를 얻지 못하거나 반경이 0 이하이면 거짓이다 </summary>
        bool TryGetArena(out Vector3 center, out float radius);
    }
}
