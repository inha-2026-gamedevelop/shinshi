// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 투사체 한 발의 발사와 소멸 알림이다. 복제 계층이 클라이언트로 전달하는 구성을 가정하며 구현은 아직 없다
    // 투사체 한 발은 투사체 번호(동작 번호)가 가리키는 키다. 시전 번호는 같은 시전의 발들이 함께 쓰는 묶음이라 한 발의 소멸이 다른 발을 지우지 않는다
    // 클라이언트는 발사 값으로 비행을 표시하고 충돌과 벽은 다시 계산하지 않는다
    /// <summary> 투사체의 발사와 소멸 시점 알림 </summary>
    public interface IBossProjectileListener
    {
        /// <summary> 발사, 속도와 경로 길이로 비행을 표시한다. pathLength는 벽 앞까지의 길이(최대 사거리 이하)이고 launchServerTime부터 센다 </summary>
        void OnProjectileLaunched(int castInstanceId, int projectileId, Vector3 origin, Vector3 direction, float speed, float width, float pathLength,
            double launchServerTime);

        /// <summary> 소멸, position은 사라진 위치이고 hitTargetId는 Hit일 때만 판정한 대상 번호다(아니면 CombatIds.NONE) </summary>
        void OnProjectileEnded(int castInstanceId, int projectileId, EBossProjectileEnd end, Vector3 position, int hitTargetId);
    }
}
