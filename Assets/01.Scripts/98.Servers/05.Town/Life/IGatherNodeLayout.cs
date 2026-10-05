// Unity
using UnityEngine;

namespace Shinshi.Network
{
    /// <summary> 채집 노드의 서버 기준 위치를 찾는 창구, 실제 구현은 마을 씬의 노드 표시 목록을 읽는다 </summary>
    public interface IGatherNodeLayout
    {
        bool TryGetNode(int zone, int node, out Vector3 position);
    }
}
