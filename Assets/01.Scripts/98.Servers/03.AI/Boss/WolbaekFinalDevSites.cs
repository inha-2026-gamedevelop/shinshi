// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 시험용 노란 여우불 좌표다. 중앙 바닥 안쪽 둘레에 네 곳을 임시로 놓았다.
    // 실제 맵에서 중앙 바닥과 플레이어 공격 공간을 검증한 값이 아니라 운영 좌표가 아니다
    /// <summary> 시험용 임시 노란 여우불 위치 </summary>
    public sealed class WolbaekFinalDevSites : IWolbaekFinalSiteSource
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   SITE_COUNT  = 4;
        private const float SITE_RADIUS = 4f;
        private const float FULL_TURN   = 360f;
        private const float OFFSET      = 45f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly Vector3 _center;

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekFinalDevSites(Vector3 center)
        {
            _center = center;
        }

        public int CollectYellowSites(List<Vector3> centers)
        {
            for (int i = 0; i < SITE_COUNT; ++i)
            {
                float angle = (OFFSET + ((FULL_TURN / SITE_COUNT) * i)) * Mathf.Deg2Rad;
                centers.Add(_center + new Vector3(Mathf.Cos(angle) * SITE_RADIUS, 0f, Mathf.Sin(angle) * SITE_RADIUS));
            }

            return centers.Count;
        }
    }
}
