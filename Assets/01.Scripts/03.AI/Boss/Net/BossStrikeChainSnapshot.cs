// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 늦게 접속한 클라이언트가 원래 시작 시각 기준으로 이어 그리는 데 필요한 값이다
    /// <summary> 진행 중인 콤보나 순차 타격의 현재 상태 </summary>
    public struct BossStrikeChainSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public BossStrikeChainStart Start { get; set; }

        /// <summary> 마지막으로 알린 조준 방향 </summary>
        public Vector3 Direction { get; set; }

        public bool IsDirectionLocked { get; set; }

        /// <summary> 이미 확정된 타 수, 타 번호가 이 값보다 작으면 끝난 타다 </summary>
        public int StruckCount { get; set; }
    }
}
