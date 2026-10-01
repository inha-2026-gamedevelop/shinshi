// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 패턴과 직사각형 예고 VFX 사이의 연결 자리다. 복제 계층이 이 알림을 클라이언트로 전달하는 구성을 가정한다. 구현은 아직 없다
    /// <summary> 직사각형 패턴의 예고 시작, 추적 방향 변경, 방향 고정, 타격, 취소 시점 알림 </summary>
    public interface IBossLineTelegraphListener
    {
        /// <summary> 예고 시작, origin은 시작 때 보스 위치이며 타격은 그 시점의 보스 위치에서 판정한다. direction은 추적 전 방향이며 고정 전까지 바뀐다 </summary>
        void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float length, float width, float telegraphSeconds);

        /// <summary> 추적 중 조준 방향이 실제로 바뀌었다, direction은 수평 단위 벡터이며 고정 전에만 온다 </summary>
        void OnAimChanged(int castInstanceId, Vector3 direction);

        /// <summary> 방향이 고정됐다, 이후 같은 시전에서는 바뀌지 않는다 </summary>
        void OnDirectionLocked(int castInstanceId, Vector3 direction);

        /// <summary> 타격이 확정됐다, hitCount는 피해 요청이 실제로 나간 대상 수다 </summary>
        void OnHit(int castInstanceId, int hitCount);

        /// <summary> 타격 전에 중단됐다, 이후 타격은 나오지 않는다 </summary>
        void OnCancelled(int castInstanceId, EBossInterruptReason reason);
    }
}
