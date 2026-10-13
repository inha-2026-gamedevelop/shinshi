// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 시험용 핵 후보 좌표다. 전장 중심 둘레의 바깥 여덟 곳과 안쪽 네 곳을 임시로 놓았다.
    // 실제 맵에서 핵 주변 약 3m 회피 공간과 이동 경로를 검증한 값이 아니라 운영 좌표가 아니다
    /// <summary> 시험용 임시 봉인 핵 후보 </summary>
    public sealed class WolbaekSealDevSites : IWolbaekSealSiteSource
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   OUTER_COUNT  = 8;
        private const int   INNER_COUNT  = 4;
        private const float OUTER_RADIUS = 12f;
        private const float INNER_RADIUS = 5f;
        private const float FULL_TURN    = 360f;

        // 바깥 고리 점 사이의 한가운데 방위
        private const float INNER_OFFSET_DEGREES = 22.5f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly Vector3 _center;

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekSealDevSites(Vector3 center)
        {
            _center = center;
        }

        public int CollectCandidates(List<Vector3> centers)
        {
            AddRing(centers, OUTER_COUNT, OUTER_RADIUS, 0f);

            // 안쪽 고리는 바깥 고리 점 사이 방위에 놓아 두 고리의 점이 가까이 붙지 않게 한다
            AddRing(centers, INNER_COUNT, INNER_RADIUS, INNER_OFFSET_DEGREES);
            return centers.Count;
        }

        private void AddRing(List<Vector3> centers, int count, float radius, float offsetDegrees)
        {
            for (int i = 0; i < count; ++i)
            {
                float angle = (offsetDegrees + ((FULL_TURN / count) * i)) * Mathf.Deg2Rad;
                centers.Add(_center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
        }
    }
}
