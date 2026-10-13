// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 현재 개발 씬의 바닥은 한 덩어리이고 서버 이동은 자동 낙하 판정이 아니다. 외곽 바닥을 따로 준비한 운영 구조와 그 위에 선 사람의 판정은
    // 이 창구 뒤에서 구현해야 하며 구현이 없으면 최후의 영역은 연결하지 않는다.
    // 기믹은 바닥을 직접 만지지 않고 제거를 요청하며, 제거된 바닥 위에 있던 사람이 누구인지는 이 창구가 서버 위치로 판정한다
    /// <summary> 최후의 영역의 외곽 바닥 상태와 추락 판정, 운영 구현은 아직 없다 </summary>
    public interface IWolbaekFinalFloor
    {
        /// <summary> 중앙 반경만 남기고 외곽 바닥을 제거한다, 거절하면 거짓이고 기믹은 아무도 죽이지 않고 정지한다. 같은 시전으로 다시 부르면 이미 제거됐으므로 참이다 </summary>
        bool TryRemoveOuterFloor(int castInstanceId, double serverTime);

        /// <summary> 제거 요청이 받아들여진 뒤 이 참가자의 서버 위치가 사라진 바닥 위인지, 제거 전에는 항상 거짓이다 </summary>
        bool IsOnRemovedFloor(int combatId);

        /// <summary> 남은 중앙 바닥에서 검증된 부활 지점, 공용 부활이 이 지점을 써야 한다. 얻지 못하면 거짓이다 </summary>
        bool TryGetSafeRevivePoint(int combatId, out Vector3 point);
    }
}
