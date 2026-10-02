// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 패턴과 장판 예고 VFX 사이의 연결 자리다. 복제 계층이 이 알림을 클라이언트로 전달하는 구성을 가정한다. 구현은 아직 없다
    // 장판이 활성된 뒤는 IBossAreaListener가 알린다. 패턴이 끝나도 장판은 남기 때문에 두 알림을 나눴다
    /// <summary> 장판 패턴의 예고 시작과 활성 전 취소 알림 </summary>
    public interface IBossAreaTelegraphListener
    {
        /// <summary> 예고 시작, center는 이 시전이 끝날 때까지 바뀌지 않는다(타깃이 움직이거나 죽어도 그대로) </summary>
        void OnTelegraphStarted(int castInstanceId, Vector3 center, float radius, float telegraphSeconds);

        /// <summary> 활성 전에 중단됐다, 예약한 슬롯은 돌려줬다. 활성한 뒤에는 오지 않고 남은 장판은 IBossAreaListener가 알린다 </summary>
        void OnCancelled(int castInstanceId, EBossInterruptReason reason);
    }
}
