// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 부채꼴 알림은 한 시전에 형태 하나, 타격 한 번이라 타마다 형태가 다른 콤보를 담지 못해 따로 둔다.
    // 복제 계층이 이 알림을 클라이언트로 전달하는 구성을 가정한다. 구현은 아직 없다
    /// <summary> 콤보 패턴의 예고 시작, 추적 방향 변경, 방향 고정, 타별 타격, 취소 시점 알림 </summary>
    public interface IBossComboTelegraphListener
    {
        /// <summary> 예고 시작, hits는 이 시전의 타 목록이고 시전이 끝날 때까지 바뀌지 않는다. 방향은 모든 타가 공유한다 </summary>
        void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float telegraphSeconds,
            IReadOnlyList<MucheolComboHit> hits);

        /// <summary> 추적 중 조준 방향이 실제로 바뀌었다 </summary>
        void OnAimChanged(int castInstanceId, Vector3 direction);

        /// <summary> 방향이 고정됐다, 이후 모든 타가 이 방향을 쓴다 </summary>
        void OnDirectionLocked(int castInstanceId, Vector3 direction);

        /// <summary> hitIndex번째 타가 확정됐다, hitCount는 그 타에서 피해 요청이 실제로 나간 대상 수다 </summary>
        void OnHit(int castInstanceId, int hitIndex, int hitCount);

        /// <summary> 도중에 중단됐다, 이후 남은 타는 나오지 않으며 이미 적용된 피해는 되돌리지 않는다 </summary>
        void OnCancelled(int castInstanceId, EBossInterruptReason reason);
    }
}
