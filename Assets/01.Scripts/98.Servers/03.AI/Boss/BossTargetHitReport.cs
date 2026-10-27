// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.AI
{
    /// <summary> 패턴 기반 클래스를 쓰지 않는 관리자와 기믹도 맞은 대상을 알릴 수 있게 하는 공통 창구 </summary>
    public static class BossTargetHitReport
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DIRECTION_SQR = 0.0001f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 리스너가 IBossTargetHitListener도 구현했을 때만 알린다. 피해 요청이 나간 뒤에 부른다 </summary>
        public static void Report(object listener, int castInstanceId, ICombatTarget target, Vector3 origin)
        {
            if (!(listener is IBossTargetHitListener hitListener))
            {
                return;
            }

            Vector3 position  = target.Position;
            Vector3 direction = position - origin;
            direction.y = 0f;
            direction = (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? Vector3.zero : direction.normalized;
            hitListener.OnTargetHit(castInstanceId, target.CombatId, position, direction);
        }
    }
}
