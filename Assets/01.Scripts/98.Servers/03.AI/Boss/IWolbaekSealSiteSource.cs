// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 핵 후보 위치는 실제 맵에서 핵 주변 약 3m 회피 공간과 이동 경로를 검증한 뒤에만 운영 값이 된다. 이 창구는 그 검증된 후보를 기믹에 준다.
    // 기믹은 후보 중에서 전장 안쪽이고 서로 최소 간격을 지키는 조합만 고르며, 후보가 모자라면 임의 좌표를 만들지 않고 정지한다.
    // 운영 구현은 아직 없다. 구현이 없으면 이 기믹은 연결하지 않는다
    /// <summary> 봉인 핵 후보 위치 조회 </summary>
    public interface IWolbaekSealSiteSource
    {
        /// <summary> 검증된 핵 중심 후보(월드)를 담고 개수를 돌려준다, 비어 있거나 모자라면 기믹이 정지한다 </summary>
        int CollectCandidates(List<Vector3> centers);
    }
}
