// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 판정 시야 검사, 정적 지형 레이어만 보고 할당 없는 물리 쿼리 하나로 막힘을 판단한다 </summary>
    public sealed class CombatLineOfSight
    {
        // 캐릭터 콜라이더를 마스크에 넣으면 앞선 적이 뒤의 적을 가려 근접 부채꼴 판정이 깨지므로 지형 레이어만 쓴다.
        // 트리거(영역 감지용)는 시야를 막지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private readonly int   _layerMask;
        private readonly float _height;

        /****************************************
        *              Properties
        ****************************************/

        // 거짓이면 장애물 레이어가 없어 시야 검사를 하지 않는다
        public bool IsEnabled => _layerMask != 0;

        /****************************************
        *                Methods
        ****************************************/

        public CombatLineOfSight(int layerMask, float height)
        {
            _layerMask = layerMask;
            _height    = height;
        }

        /// <summary> 레이어 이름으로 마스크를 만든다, 프로젝트에 없는 이름이면 0이다. 초기화 때 한 번 부른다 </summary>
        public static int ResolveLayerMask(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            return (layer < 0) ? 0 : (1 << layer);
        }

        /// <summary> 두 발 위치를 시야 높이에서 이은 선이 지형에 막히는지 </summary>
        public bool IsBlocked(Vector3 from, Vector3 to)
        {
            if (_layerMask == 0)
            {
                return false;
            }

            Vector3 lift = Vector3.up * _height;
            return Physics.Linecast(from + lift, to + lift, _layerMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary> 발 위치에서 수평 방향으로 지형에 막히기 전까지의 거리, 막히지 않으면 maxDistance </summary>
        public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance)
        {
            if (_layerMask == 0)
            {
                return maxDistance;
            }

            if (Physics.Raycast(origin + (Vector3.up * _height), direction, out RaycastHit hit, maxDistance, _layerMask,
                QueryTriggerInteraction.Ignore))
            {
                return hit.distance;
            }

            return maxDistance;
        }
    }
}
