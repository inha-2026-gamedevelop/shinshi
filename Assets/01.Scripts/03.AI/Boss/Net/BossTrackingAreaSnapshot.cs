// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 활성 전인 추적 장판 하나의 현재 상태 </summary>
    public struct BossTrackingAreaSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public BossTrackingAreaStart Start { get; set; }

        /// <summary> 마지막으로 알린 중심 </summary>
        public Vector3 Center { get; set; }

        public bool IsFixed { get; set; }

        public double FixedServerTime { get; set; }
    }
}
