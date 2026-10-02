// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 패턴과 투사체 예고 VFX 사이의 연결 자리다. 복제 계층이 이 알림을 클라이언트로 전달하는 구성을 가정한다. 구현은 아직 없다
    // 발사와 그 뒤는 IBossProjectileListener가 알린다. 패턴이 끝나도 투사체는 남기 때문에 두 알림을 나눴다
    /// <summary> 투사체 패턴의 예고 시작, 추적 방향 변경, 방향 고정, 발사 전 취소 알림 </summary>
    public interface IBossProjectileTelegraphListener
    {
        /// <summary> 예고 시작, length는 발사 위치에서 벽 앞까지(최대 사거리 이하)의 경로 길이이고 width는 투사체 폭이다 </summary>
        void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float length, float width, float telegraphSeconds);

        /// <summary> 추적 중 조준 방향이 실제로 바뀌었다, 방향이 바뀌면 벽까지의 거리도 달라지므로 length를 함께 보낸다 </summary>
        void OnAimChanged(int castInstanceId, Vector3 direction, float length);

        /// <summary> 방향이 고정됐다, 발사 뒤에도 유도하지 않으므로 이후 방향은 바뀌지 않는다 </summary>
        void OnDirectionLocked(int castInstanceId, Vector3 direction, float length);

        /// <summary> 발사 전에 중단됐다, 발사한 뒤에는 오지 않고 남은 투사체는 IBossProjectileListener가 알린다 </summary>
        void OnCancelled(int castInstanceId, EBossInterruptReason reason);
    }
}
