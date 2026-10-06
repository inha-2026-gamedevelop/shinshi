// Unity
using UnityEngine;

namespace Shinshi.Network
{
    // 서버와 클라이언트가 같은 씬 배치를 쓰므로 양쪽에서 올린다. 서버만 판정에 쓰고 클라이언트는 표시 연결용으로 읽을 수 있다.
    // 움직이는 노드는 없다고 보고 켜질 때의 위치만 올린다
    /// <summary> 마을 씬의 약초 노드 하나, 구역과 노드 번호는 채집 요청과 노드 상태 메시지의 번호와 같다 </summary>
    public sealed class TownGatherNodeMarker : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private int _zone = 0; // 생활 구역 번호
        [SerializeField] private int _node = 0; // 구역 안 노드 번호

        /****************************************
        *              Properties
        ****************************************/

        public int Zone => _zone;
        public int Node => _node;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            TownGatherNodeRegistry.Register(_zone, _node, transform.position);
        }

        private void OnDisable()
        {
            TownGatherNodeRegistry.Unregister(_zone, _node);
        }
    }
}
