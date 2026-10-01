// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 서버 판정용 전투 대상 조회, 결과 버퍼를 비운 뒤 채우고 개수를 돌려준다. 세션 참가자와 스폰 목록을 아는 구현은 서버 전용이라 서버 어셈블리에 둔다 </summary>
    public interface ICombatTargetQuery
    {
        // 반환된 참조는 이번 틱 판정에만 쓰고 보관하지 않는다. 보관하려면 CombatId를 남기고 TryGet으로 다시 찾는다

        /// <summary> 살아 있는 원래 참가 플레이어, 미접속 생존자는 넣고 소환물과 이탈 정산된 참가자는 뺀다 </summary>
        int GetLivingPlayers(List<ICombatTarget> results);

        /// <summary> 중심에서 수평 거리가 반경 이하인 살아 있는 대상, 경계를 포함하고 소환물 포함 여부를 고른다 </summary>
        int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
            List<ICombatTarget> results);

        // 보스 주변 인원은 includeSummons를 false로 센다
        // TODO(민성): 전투 봇이 도입되면 주변 인원에 넣는 조회를 추가한다. 체험판은 봇이 없다

        /// <summary> GetLivingInRadius와 같은 조건의 개수만 센다 </summary>
        int CountLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons);

        /// <summary> 번호로 대상을 찾는다, 전장에서 제거된 대상은 찾지 못한다 </summary>
        bool TryGet(int combatId, out ICombatTarget target);
    }
}
