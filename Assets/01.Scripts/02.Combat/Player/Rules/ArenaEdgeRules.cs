// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    // 몸체 중심이 설 수 있는 원은 전장 반경에서 몸체 반경만큼 안쪽이다(BossArenaRules와 같은 기준).
    // 걷기, 회피, 기술 이동은 이 원 경계로 되돌리고, 보스 밀침으로 원 밖에 놓인 사람만 서버가 낙사로 처리한다.
    // 서버 틱 끝 가두기와 소유 클라이언트 예측이 이 규칙 하나만 쓴다
    /// <summary> 무너진 전장 바깥으로 나가지 못하게 하는 계산, 서버 판정과 소유 클라이언트 예측이 같이 쓴다 </summary>
    public static class ArenaEdgeRules
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float EDGE_EPSILON   = 0.001f; // m, 경계로 되돌린 위치를 다음 틱에 다시 밖으로 보지 않게 하는 여유
        private const float BEYOND_EPSILON = 0.01f;  // m, 경계에 딱 맞춰 멈춘 위치를 부동소수 오차로 밖이라 보지 않게 하는 여유
        private const float MIN_DISTANCE   = 0.0001f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 몸체 중심이 갈 수 있는 반경 </summary>
        public static float GetLimit(float arenaRadius, float bodyRadius)
        {
            return Mathf.Max(0f, arenaRadius - Mathf.Max(0f, bodyRadius));
        }

        /// <summary> 몸체 중심이 설 수 있는 원 밖인가, 비유한 좌표는 밖으로 본다 </summary>
        public static bool IsBeyond(Vector3 position, Vector3 center, float arenaRadius, float bodyRadius)
        {
            float limit = GetLimit(arenaRadius, bodyRadius) + BEYOND_EPSILON;
            float dx    = position.x - center.x;
            float dz    = position.z - center.z;
            return !(((dx * dx) + (dz * dz)) <= (limit * limit));
        }

        /// <summary> 원 밖이면 중심 쪽으로 경계까지 되돌린 위치, 안이면 그대로다. 높이는 바꾸지 않는다 </summary>
        public static Vector3 Clamp(Vector3 position, Vector3 center, float arenaRadius, float bodyRadius)
        {
            float limit = GetLimit(arenaRadius, bodyRadius);
            float dx    = position.x - center.x;
            float dz    = position.z - center.z;
            float sqr   = (dx * dx) + (dz * dz);
            if ((float.IsNaN(sqr)) || (float.IsInfinity(sqr)) || (sqr <= (limit * limit)))
            {
                return position;
            }

            float current = Mathf.Sqrt(sqr);
            if (current <= MIN_DISTANCE)
            {
                return position;
            }

            float scale = Mathf.Max(0f, limit - EDGE_EPSILON) / current;
            return new Vector3(center.x + (dx * scale), position.y, center.z + (dz * scale));
        }

        /// <summary> 클라이언트에 등록된 보스 중 전장을 가두는 보스에 대해 되돌린 위치 </summary>
        public static Vector3 ClampRegistered(Vector3 position)
        {
            int count = BossBodyBlockerRegistry.Count;
            for (int i = 0; i < count; ++i)
            {
                if ((BossBodyBlockerRegistry.Get(i) is IBossArenaEdge edge) && (edge.ConfinesToArena))
                {
                    position = Clamp(position, edge.ArenaCenter, edge.ArenaRadius, Constants.Combat.PLAYER_BODY_RADIUS);
                }
            }

            return position;
        }
    }
}
