// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 공격 허가와 실행이 같은 벽 검사를 쓰도록 모았다. 돌진 이동의 벽 보정(IBossMovement)은 별개이며 이 검사가 막지 않는다
    /// <summary> 일반 적과 대상 사이에 정적 지형(벽)이 끼었는지 보는 수평 시선 검사 </summary>
    public static class EnemySight
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float SIGHT_TOLERANCE = 0.1f;  // m, 스윕이 목표 끝에서 남기는 틈(벽 여유)을 막힘으로 보지 않는 오차
        private const float MIN_DISTANCE    = 0.01f; // m, 이보다 가까우면 가릴 벽이 없다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> from에서 to까지 벽에 막히지 않았으면 참, walls가 null이면 검사하지 않고 참 </summary>
        public static bool IsClear(IBossWallQuery walls, Vector3 from, Vector3 to)
        {
            if (walls == null)
            {
                return true;
            }

            Vector3 offset = to - from;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance <= MIN_DISTANCE)
            {
                return true;
            }

            float clear = walls.GetClearDistance(from, offset / distance, distance, 0f);
            return clear >= (distance - SIGHT_TOLERANCE);
        }
    }
}
