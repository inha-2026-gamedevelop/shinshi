// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    // 서버 보스 몸체(BossServer)와 클라이언트 복제 상태(BossState)가 함께 구현한다. 서버 이동과 소유 클라이언트 예측이
    // 같은 규칙(ArenaEdgeRules)으로 플레이어를 전장 안에 가둬야 예측이 서버 위치로 끌려가지 않는다
    /// <summary> 바깥 바닥이 무너져 플레이어를 원형 전장 안에 가두는 보스 </summary>
    public interface IBossArenaEdge
    {
        /// <summary> 지금 전장 밖으로 걸어 나갈 수 없는가, 참이면 보스 밀침으로 밖에 나간 사람만 떨어진다 </summary>
        bool ConfinesToArena { get; }

        /// <summary> 전장 중심, 수평 위치만 쓴다 </summary>
        Vector3 ArenaCenter { get; }

        /// <summary> 남은 바닥의 반경(m) </summary>
        float ArenaRadius { get; }
    }
}
