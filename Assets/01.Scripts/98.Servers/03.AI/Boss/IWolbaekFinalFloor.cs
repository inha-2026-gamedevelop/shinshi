// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 현재 개발 씬의 바닥은 한 덩어리이고 서버 이동에는 중력과 높이 판정이 없다. 그래서 낙하는 이 창구 뒤에서 서버 위치로 판정한다.
    // 기믹은 바닥을 직접 만지지 않고 제거를 요청하며, 제거 순간 외곽에 서 있다는 이유만으로는 누구도 죽이지 않는다.
    // 제거 뒤 누가 실제로 떨어졌는지는 이 창구가 정해 CollectFallen으로 내주고, 기믹은 받은 참가자만 추락 사망으로 처리한다.
    // 구현이 없으면 최후의 영역은 연결하지 않는다
    /// <summary> 최후의 영역의 외곽 바닥 상태와 낙하 판정 </summary>
    public interface IWolbaekFinalFloor
    {
        /// <summary> 중앙 반경만 남기고 외곽 바닥을 제거한다, 거절하면 거짓이고 기믹은 아무도 죽이지 않고 정지한다. 같은 시전으로 다시 부르면 이미 제거됐으므로 참이다 </summary>
        bool TryRemoveOuterFloor(int castInstanceId, double serverTime);

        // 기믹이 외곽 제거 뒤 진행 중인 틱마다 한 번 부른다. 구현은 이 호출을 위치 표본 한 번으로 삼아 낙하를 확정한다.
        // 한 참가자는 한 생명에 한 번만 내주며, 부활하면 이력이 지워져 다시 떨어질 수 있다. 쓰러진 사람과 이탈이 확정된 사람은 내주지 않고
        // 전장에 남은 재접속 유예 참가자는 내준다. 부활 직후 보호는 낙하를 막지 않는다
        /// <summary> results를 먼저 비우고 낙하가 확정됐고 아직 내주지 않은 참가자의 전투 번호를 채운 뒤 개수를 돌려준다, 제거 전에는 항상 0이다 </summary>
        int CollectFallen(double serverTime, List<int> results);

        /// <summary> 남은 중앙 바닥에서 검증된 부활 지점, 공용 부활이 이 지점을 써야 한다. 얻지 못하면 거짓이다 </summary>
        bool TryGetSafeRevivePoint(int combatId, out Vector3 point);
    }
}
