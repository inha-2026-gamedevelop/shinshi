// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 색 결계 후보 위치는 실제 맵에서 이동 가능 거리와 장애물을 검증한 뒤에만 운영 값이 된다. 이 창구는 그 검증된 후보를 기믹에 준다.
    // 기믹은 후보 중에서 이동 거리와 두 색 사이 거리 조건을 만족하는 배치를 고르며, 후보가 모자라면 임의 좌표를 만들지 않고 정지한다.
    // 운영 구현은 아직 없다. 구현이 없으면 이 기믹은 연결하지 않는다
    /// <summary> 색 결계 후보 위치 조회 </summary>
    public interface IWolbaekWardSiteSource
    {
        /// <summary> 검증된 결계 중심 후보(월드)를 담고 개수를 돌려준다, 전장 안쪽이어야 하며 비어 있으면 기믹이 정지한다 </summary>
        int CollectCandidates(List<Vector3> centers);
    }
}
