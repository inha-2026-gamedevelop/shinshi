// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 값은 실제 맵 검증을 거친 운영 좌표가 아니라 시험 전장 중심 둘레의 임시 배치다
    /// <summary> 시험용 임시 결계 후보, 전장 중심 둘레에 같은 간격으로 놓는다. 맵 검증을 거친 운영 좌표가 아니다 </summary>
    public sealed class WolbaekWardDevSites : IWolbaekWardSiteSource
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   CANDIDATE_COUNT = 8;
        private const float RING_RADIUS     = 11f;  // m, 인접 후보 사이가 약 8.4m라 두 색 최소 거리를 채운다
        private const float FULL_TURN       = Mathf.PI * 2f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly Vector3 _center;

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekWardDevSites(Vector3 center)
        {
            _center = center;
        }

        public int CollectCandidates(List<Vector3> centers)
        {
            for (int i = 0; i < CANDIDATE_COUNT; ++i)
            {
                float angle = (i / (float)CANDIDATE_COUNT) * FULL_TURN;
                centers.Add(_center + new Vector3(Mathf.Cos(angle) * RING_RADIUS, 0f, Mathf.Sin(angle) * RING_RADIUS));
            }

            return centers.Count;
        }
    }
}
