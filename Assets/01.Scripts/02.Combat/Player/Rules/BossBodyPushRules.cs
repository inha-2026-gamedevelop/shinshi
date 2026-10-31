// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    // 보스 중심에서 (밀침 반경 + 플레이어 반경)인 원 안에 플레이어 중심이 들어오면 중심에서 플레이어 쪽으로 원 경계까지 민다.
    // 미는 길은 회피와 같은 내비 메시 벽 검사로 잘라 벽 너머로 넘기지 않는다. 벽에 막히면 원 안에 남을 수 있다.
    // 서버 틱 끝 밀어내기와 소유 클라이언트 예측이 이 규칙 하나만 쓴다. 중심이 정확히 겹치면 양쪽이 같은 방향(+z)으로 민다
    /// <summary> 보스 몸통 밀어내기 계산, 서버 판정과 소유 클라이언트 예측이 같이 쓴다 </summary>
    public static class BossBodyPushRules
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DISTANCE = 0.0001f; // m, 이보다 가까우면 방향을 정할 수 없어 고정 방향으로 민다
        private const float EDGE_EPSILON = 0.001f;  // m, 경계에 딱 닿은 위치를 다음 틱에 다시 겹친 것으로 보지 않게 하는 여유

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 플레이어 중심이 이 거리 안이면 밀린다 </summary>
        public static float GetBlockDistance(float pushRadius, float playerRadius)
        {
            return Mathf.Max(0f, pushRadius) + Mathf.Max(0f, playerRadius);
        }

        /// <summary> 겹쳤으면 밀 방향(수평 단위 벡터)과 거리를 낸다, 닿기만 했거나 거리가 0이면 거짓 </summary>
        public static bool TryGetPushOut(Vector3 position, Vector3 center, float blockDistance, out Vector3 direction,
            out float distance)
        {
            direction = Vector3.zero;
            distance  = 0f;
            if (!(blockDistance > 0f))
            {
                return false;
            }

            float offsetX = position.x - center.x;
            float offsetZ = position.z - center.z;
            float sqr     = (offsetX * offsetX) + (offsetZ * offsetZ);
            if (sqr >= (blockDistance * blockDistance))
            {
                return false;
            }

            float current = Mathf.Sqrt(sqr);
            direction = (current > MIN_DISTANCE) ? new Vector3(offsetX / current, 0f, offsetZ / current) : Vector3.forward;
            distance  = (blockDistance - current) + EDGE_EPSILON;
            return true;
        }

        /// <summary> 보스 하나에 대해 밀어낸 위치, 겹치지 않았으면 그대로다 </summary>
        public static Vector3 Resolve(Vector3 position, IBossBodyBlocker blocker, float playerRadius)
        {
            if ((blocker == null) || (!blocker.BlocksBodies))
            {
                return position;
            }

            float block = GetBlockDistance(blocker.BodyPushRadius, playerRadius);
            if (!TryGetPushOut(position, blocker.Position, block, out Vector3 direction, out float distance))
            {
                return position;
            }

            // 출발점이 메시 밖이면(서버 연출 이동 직후 등) 벽 검사 없이 수평으로만 민다
            if (DodgeRules.TryGetWallClampedEnd(position, direction, distance, out Vector3 _, out Vector3 end))
            {
                return end;
            }

            return position + (direction * distance);
        }

        /// <summary> 클라이언트에 등록된 모든 보스에 대해 밀어낸 위치 </summary>
        public static Vector3 ResolveRegistered(Vector3 position)
        {
            int count = BossBodyBlockerRegistry.Count;
            for (int i = 0; i < count; ++i)
            {
                position = Resolve(position, BossBodyBlockerRegistry.Get(i), Constants.Combat.PLAYER_BODY_RADIUS);
            }

            return position;
        }
    }
}
