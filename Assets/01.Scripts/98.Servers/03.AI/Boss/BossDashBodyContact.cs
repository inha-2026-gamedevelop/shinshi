// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 돌진 패턴 둘(BossDashPattern, MucheolMultiDashPattern)이 같은 몸통 충돌 규칙을 쓰도록 한곳에 둔다.
    // 보스 몸체가 IBossBodyBlocker이면 경로 직사각형 밖이어도 지나가는 몸통(밀침 반경 + 대상 반경)과 겹친 대상을 맞히고,
    // 맞은 대상을 돌진 방향의 옆으로 짧게 민다. 민 뒤에도 몸통 안이면 서버 틱 끝 밀어내기가 바깥에 놓는다
    /// <summary> 보스 돌진의 몸통 겹침 판정과 옆 밀침 </summary>
    public static class BossDashBodyContact
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 보스 몸체의 밀침 반경, 몸통 계약이 없거나 막지 않으면 0이다 </summary>
        public static float GetPushRadius(ICombatTarget self)
        {
            IBossBodyBlocker body = self as IBossBodyBlocker;
            return ((body != null) && (body.BlocksBodies)) ? body.BodyPushRadius : 0f;
        }

        /// <summary> 대상 조회 범위에 더할 몸통 거리, 가장 큰 대상 몸체까지 넉넉히 잡는다 </summary>
        public static float GetQueryReach(float pushRadius)
        {
            return (pushRadius > 0f) ? (pushRadius + Constants.Combat.MAX_BODY_RADIUS) : 0f;
        }

        // 몸통 중심이 지나간 구간(from~to) 옆은 띠, 그 앞은 이번 틱 끝 몸통 원이다. 밀침 반경이 없으면 거짓이다
        /// <summary> 대상 중심이 이번 틱에 지나간 몸통과 겹쳤는가, origin과 aim은 그 돌진의 시작점과 수평 단위 방향이다 </summary>
        public static bool IsInsideBodyBand(Vector3 origin, Vector3 aim, ICombatTarget target, float from, float to,
            float pushRadius)
        {
            if (!(pushRadius > 0f))
            {
                return false;
            }

            float   overlap = BossBodyPushRules.GetBlockDistance(pushRadius, target.BodyRadius);
            Vector3 offset  = target.Position - origin;
            offset.y = 0f;

            float forward = (offset.x * aim.x) + (offset.z * aim.z);
            float lateral = (offset.x * aim.z) - (offset.z * aim.x);
            if (forward < from)
            {
                return false;
            }

            if (forward <= to)
            {
                return Mathf.Abs(lateral) < overlap;
            }

            float ahead = forward - to;
            return ((ahead * ahead) + (lateral * lateral)) < (overlap * overlap);
        }

        // 대상이 있는 쪽으로 민다. 정확히 경로 위면 오른쪽이다. 막힘은 밀림 창구가 벽과 전장 경계로 자른다.
        // 회피 중이거나 피격이상 면역이라 밀 수 없는 대상은 창구가 거절하고 틱 끝 밀어내기만 받는다
        /// <summary> 맞은 대상을 돌진 방향의 옆으로 민다, 밀림 창구가 없으면 아무것도 하지 않는다 </summary>
        public static void PushAside(IBossHitDisplacer displacer, Vector3 origin, Vector3 aim, ICombatTarget target)
        {
            if ((displacer == null) || (target == null))
            {
                return;
            }

            Vector3 offset  = target.Position - origin;
            float   lateral = (offset.x * aim.z) - (offset.z * aim.x);
            float   side    = (lateral < 0f) ? -1f : 1f;
            Vector3 aside   = new Vector3(aim.z * side, 0f, -aim.x * side);
            displacer.TryPush(target.CombatId, aside, Constants.Combat.BOSS_DASH_SIDE_PUSH_DISTANCE);
        }
    }
}
