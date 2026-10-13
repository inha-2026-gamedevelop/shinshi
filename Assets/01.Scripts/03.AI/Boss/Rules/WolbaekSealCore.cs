// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 핵은 개인에게 고정 배정되지 않는다. 한 차례 동안 위치가 바뀌지 않는다
    /// <summary> 봉인 핵 하나의 입력, 한 차례 안의 번호와 중심 </summary>
    public readonly struct WolbaekSealCore
    {
        /****************************************
        *              Properties
        ****************************************/

        public int Index { get; }

        public Vector3 Center { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekSealCore(int index, Vector3 center)
        {
            Index  = index;
            Center = center;
        }
    }
}
