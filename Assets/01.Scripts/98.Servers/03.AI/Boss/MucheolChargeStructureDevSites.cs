// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 시험용 돌진 구조물 좌표다. 맵에 구조물 표식(BossChargeStructureSet)이 없을 때만 쓴다. 전장 중심 둘레 대각선 네 곳에 놓고
    // 모두 기믹 지정으로 둬 필요 수(3)보다 하나 여유가 있다. 무철 전장 반경 22m, 몸체 반경 2m 기준으로 중심에서 돌진해도
    // 닿고 벽과는 떨어지게 잡은 값일 뿐이다. 실제 맵에서 구조물 크기, 모델, 이동 경로, 플레이어 유도 동선을 검증한 값이 아니라 운영 좌표가 아니다
    /// <summary> 시험용 임시 무철 돌진 구조물, 식별자 1에서 4 </summary>
    public sealed class MucheolChargeStructureDevSites
    {
        /****************************************
        *              Constants
        ****************************************/

        /// <summary> 임시 구조물 수 </summary>
        public const int COUNT = 4;

        private const int   FIRST_ID       = 1;
        private const float RING_RADIUS    = 12f;   // m, 전장 중심에서의 거리
        private const float STRUCTURE_SIZE = 1.5f;  // m, 구조물 반경
        private const float FULL_TURN      = 360f;
        private const float OFFSET_DEGREES = 45f;   // 보스 첫 정면(+z)에 바로 놓이지 않게 대각선으로 돌린다

        /****************************************
        *                Fields
        ****************************************/

        private readonly Vector3 _center;

        /****************************************
        *                Methods
        ****************************************/

        public MucheolChargeStructureDevSites(Vector3 arenaCenter)
        {
            _center = arenaCenter;
        }

        /// <summary> 임시 구조물을 output 뒤에 더하고 더한 수를 돌려준다 </summary>
        public int Collect(List<BossChargeStructureInfo> output)
        {
            for (int i = 0; i < COUNT; ++i)
            {
                float   angle    = (OFFSET_DEGREES + ((FULL_TURN / COUNT) * i)) * Mathf.Deg2Rad;
                Vector3 position = _center + new Vector3(Mathf.Cos(angle) * RING_RADIUS, 0f, Mathf.Sin(angle) * RING_RADIUS);
                output.Add(new BossChargeStructureInfo(FIRST_ID + i, position, STRUCTURE_SIZE, true));
            }

            return COUNT;
        }
    }
}
