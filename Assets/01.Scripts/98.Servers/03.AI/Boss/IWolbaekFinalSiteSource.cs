// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 노란 여우불 위치는 중앙 바닥에서 플레이어가 둘러싸 공격할 공간과 광역 공격 범위를 실제 맵으로 검증한 뒤에만 운영 값이 된다.
    // 기믹은 목록 앞에서부터 필요한 수만큼 그대로 쓰며 섞거나 임의 좌표를 만들지 않는다. 모자라면 정지한다. 운영 구현은 아직 없다
    /// <summary> 노란 여우불 고정 위치 조회 </summary>
    public interface IWolbaekFinalSiteSource
    {
        /// <summary> 검증된 노란 여우불 중심(월드)을 우선순위 순서로 담고 개수를 돌려준다, 중앙 바닥 안이어야 한다 </summary>
        int CollectYellowSites(List<Vector3> centers);
    }
}
