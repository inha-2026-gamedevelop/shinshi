// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Network
{
    // 마을 씬은 서버가 열린 뒤 Mirror가 불러오므로 부트 시점에는 노드가 없다. 씬의 표시가 켜질 때 여기에 올리고
    // 서버의 채집 판정은 요청이 올 때마다 여기서 위치를 찾는다. 위치는 서버 판정의 기준이라 씬 배치값 그대로 쓴다
    /// <summary> 마을 씬에 놓인 약초 노드 위치 목록 </summary>
    public static class TownGatherNodeRegistry
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly Dictionary<long, Vector3> _positions = new Dictionary<long, Vector3>();

        /****************************************
        *              Properties
        ****************************************/

        public static int Count => _positions.Count;

        /****************************************
        *                Methods
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 노드가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _positions.Clear();
        }

        /// <summary> 구역과 노드 번호를 한 키로 묶는다 </summary>
        public static long ToKey(int zone, int node)
        {
            return ((long)zone << 32) | (uint)node;
        }

        public static void Register(int zone, int node, Vector3 position)
        {
            _positions[ToKey(zone, node)] = position;
        }

        public static void Unregister(int zone, int node)
        {
            _positions.Remove(ToKey(zone, node));
        }

        public static bool TryGetPosition(int zone, int node, out Vector3 position)
        {
            return _positions.TryGetValue(ToKey(zone, node), out position);
        }
    }
}
