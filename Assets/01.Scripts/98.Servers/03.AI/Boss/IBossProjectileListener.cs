// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 투사체 한 발의 발사와 소멸 알림이다. 복제 계층이 클라이언트로 전달하는 구성을 가정하며 구현은 아직 없다
    // 시전 번호가 투사체 한 발을 가리키는 키다. 클라이언트는 발사 값으로 비행을 표시하고 충돌과 벽은 다시 계산하지 않는다
    /// <summary> 투사체의 발사와 소멸 시점 알림 </summary>
    public interface IBossProjectileListener
    {
        /// <summary> 발사, 속도와 경로 길이로 비행을 표시한다. pathLength는 벽 앞까지의 길이(최대 사거리 이하)이고 launchServerTime부터 센다 </summary>
        void OnProjectileLaunched(int castInstanceId, Vector3 origin, Vector3 direction, float speed, float width, float pathLength,
            double launchServerTime);

        /// <summary> 소멸, position은 사라진 위치이고 hitTargetId는 Hit일 때만 판정한 대상 번호다(아니면 CombatIds.NONE) </summary>
        void OnProjectileEnded(int castInstanceId, EBossProjectileEnd end, Vector3 position, int hitTargetId);
    }
}
