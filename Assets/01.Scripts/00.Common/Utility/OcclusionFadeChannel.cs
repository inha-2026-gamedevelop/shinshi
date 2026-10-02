// Unity
using UnityEngine;

namespace Shinshi.Common
{
    /// <summary> 지금 화면에서 비워진 가림 범위를 나누는 값 채널, 카메라가 쓰고 커서 판정이 읽는다 </summary>
    public static class OcclusionFadeChannel
    {
        // 카메라 파트와 전투 입력이 서로를 참조하지 않게 값만 둔다. 쓰는 쪽이 없는 씬에서는 꺼진 채로 남아
        // 커서 판정이 예전처럼 가장 가까운 충돌을 고른다

        /****************************************
        *              Constants
        ****************************************/

        // 디더가 이 비율 위로 남기면 보이는 면이라 클릭이 통과하지 않는다
        private const float VISIBLE_THRESHOLD = 0.999f;

        /****************************************
        *                Fields
        ****************************************/

        private static bool               _isActive = false;
        private static OcclusionFadeShape _shape;

        public static bool               IsActive => _isActive;
        public static OcclusionFadeShape Shape    => _shape;

        /****************************************
        *              Unity Event
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 범위가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            Clear();
        }

        /****************************************
        *                Methods
        ****************************************/

        public static void Publish(in OcclusionFadeShape shape)
        {
            _shape    = shape;
            _isActive = true;
        }

        public static void Clear()
        {
            _shape    = default;
            _isActive = false;
        }

        /// <summary> 이 지점이 비워진 가림 안이면 참, 커서 레이캐스트가 이 충돌을 건너뛰고 뒤를 고른다 </summary>
        public static bool IsFaded(Vector3 point)
        {
            if (!_isActive)
            {
                return false;
            }

            return _shape.GetVisibility(point) < VISIBLE_THRESHOLD;
        }
    }
}
