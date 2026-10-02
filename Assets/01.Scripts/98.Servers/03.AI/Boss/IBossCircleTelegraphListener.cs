// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 패턴과 원형 지면 VFX 사이의 연결 자리다. 부채꼴 알림과 달리 방향이 없고 중심이 시전 내내 고정이라 별도로 둔다.
    // VFX는 클라이언트 어셈블리에 있어 서버가 직접 부를 수 없으므로 복제 계층이 이 알림을 받아 전달하는 구성을 가정한다. 구현은 아직 없다
    /// <summary> 원형 패턴의 예고 시작, 타격, 취소 시점 알림 </summary>
    public interface IBossCircleTelegraphListener
    {
        /// <summary> 예고 시작, center는 이 시전이 끝날 때까지 바뀌지 않는다 </summary>
        void OnTelegraphStarted(int castInstanceId, Vector3 center, float radius, float telegraphSeconds);

        /// <summary> 타격이 확정됐다, hitCount는 피해 요청이 실제로 나간 대상 수다 </summary>
        void OnHit(int castInstanceId, int hitCount);

        /// <summary> 타격 전에 중단됐다, 이후 타격은 나오지 않는다 </summary>
        void OnCancelled(int castInstanceId, EBossInterruptReason reason);
    }
}
