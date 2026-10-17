// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 현재 이동 창구(IBossMovement)는 정적 지형 벽까지만 알고 구조물을 식별하지 못한다. 구조물의 실제 탐지와 파괴는 맵과 서버 쪽이 맡고,
    // AI는 이 조회로 "어떤 구조물에 막혔는가"만 묻는다. 서버 구현은 BossChargeStructureField다.
    // 조회는 상태가 없다. 같은 경로를 여러 번 물으면 같은 구조물을 반복해 돌려줄 수 있고, 한 시전에서 충돌을 한 번만 처리하는 일은
    // 호출하는 패턴이 맡는다. 구현은 파괴된 구조물을 돌려주지 않아야 한다. 패턴은 이동을 제한한 순간에 구조물 식별자를 기록하므로
    // 그 뒤 구조물이 사라져도 이미 제한한 구조물로 보고된다. 구조물이 돌진 도중 사라지면 이후 조회는 그 구조물을 돌려주지 않는다
    /// <summary> 돌진 경로에서 처음 만나는 구조물을 알려 주는 조회, 구현은 서버 쪽이 맵 배치로 한다 </summary>
    public interface IBossChargeStructureQuery
    {
        /// <summary> origin에서 수평 단위 방향으로 maxDistance까지 반경 bodyRadius 몸체로 쓸 때 처음 닿는 구조물, 없으면 거짓이다. hit의 거리는 maxDistance 이하다 </summary>
        bool TryGetFirstStructure(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius,
            out BossChargeStructureHit hit);
    }
}
