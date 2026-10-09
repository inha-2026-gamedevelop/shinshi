// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 기존 부채꼴 알림(IBossFanTelegraphListener)은 조준 갱신과 방향 고정 알림이 있어 예고 중 방향이 바뀔 수 있는 계약이다.
    // 전장 공격은 예고 시작 순간의 위치와 방향으로 끝까지 고정이라 그 알림을 아예 두지 않고 따로 둔다.
    // 로컬 연결부가 부채꼴 알림을 월백 P02 예고로 그리는 경로도 피한다
    /// <summary> 무철 전장 공격(mucheol_ph2_p07)의 예고 시작, 타격, 밀림, 취소 시점 알림 </summary>
    public interface IMucheolBattlefieldListener
    {
        /// <summary> 예고 시작, origin과 direction은 시전이 끝날 때까지 바뀌지 않는다. 공격 영역은 direction 기준 arcAngle 부채꼴이고 나머지 뒤쪽이 안전지대다 </summary>
        void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float radius, float arcAngle,
            float telegraphSeconds);

        /// <summary> 한 대상이 맞아 밀림을 시도했다, distance는 벽과 경계에 막힌 뒤 실제로 밀린 거리다. 쓰러뜨린 피해에도 불린다 </summary>
        void OnPushed(int castInstanceId, int combatId, float distance);

        /// <summary> 타격이 확정됐다, hitCount는 피해가 실제로 적용된 대상 수다 </summary>
        void OnHit(int castInstanceId, int hitCount);

        /// <summary> 도중에 중단됐다, 타격 전이면 피해가 나오지 않으며 이미 적용된 피해는 되돌리지 않는다 </summary>
        void OnCancelled(int castInstanceId, EBossInterruptReason reason);
    }
}
