using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 하드 P04의 이동 예고, 중심 고정, 활성 전 취소를 클라이언트 표현에 전달하는 계약 </summary>
    public interface IBossHardAreaListener
    {
        void OnMovingStarted(int castInstanceId, int areaId, int targetCombatId, Vector3 center, float radius,
            double startServerTime, float followSeconds, float fixedSeconds);

        void OnMovingUpdated(int castInstanceId, int areaId, Vector3 center, double serverTime);

        void OnCenterFixed(int castInstanceId, int areaId, Vector3 center, double fixedServerTime);

        /// <summary> 활성 전 취소, None은 활성 실패를 뜻한다 </summary>
        void OnCancelled(int castInstanceId, int areaId, EBossInterruptReason reason);
    }
}
