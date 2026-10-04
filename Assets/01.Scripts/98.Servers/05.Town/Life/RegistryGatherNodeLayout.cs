// Unity
using UnityEngine;

namespace Shinshi.Network
{
    // TODO(진욱): 마을 씬에 약초 노드 표시(TownGatherNodeMarker)를 배치해야 채집이 열린다. 없으면 모든 채집 시작이 InvalidTarget이다
    /// <summary> 마을 씬의 TownGatherNodeMarker가 올린 위치를 읽는다 </summary>
    public sealed class RegistryGatherNodeLayout : IGatherNodeLayout
    {
        /****************************************
        *                Methods
        ****************************************/

        public bool TryGetNode(int zone, int node, out Vector3 position)
        {
            return TownGatherNodeRegistry.TryGetPosition(zone, node, out position);
        }
    }
}
