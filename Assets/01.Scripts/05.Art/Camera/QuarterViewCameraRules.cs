// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 쿼터뷰 카메라의 각도, 따라가기 오프셋, 휠 줌 단계 계산 </summary>
    public static class QuarterViewCameraRules
    {
        /****************************************
        *                Methods
        ****************************************/

        public static Quaternion GetRotation(float pitchAngle, float yawAngle)
        {
            return Quaternion.Euler(pitchAngle, yawAngle, 0f);
        }

        // 카메라가 대상 발밑이 아니라 초점 높이를 바라보도록 초점에서 시선 반대쪽으로 거리만큼 물린다
        /// <summary> 따라갈 대상의 위치에서 카메라 위치까지의 월드 오프셋 </summary>
        public static Vector3 GetFollowOffset(float pitchAngle, float yawAngle, float distance, float focusHeight)
        {
            Vector3 forward = GetRotation(pitchAngle, yawAngle) * Vector3.forward;
            return (Vector3.up * focusHeight) - (forward * distance);
        }

        // 휠 한 칸이 한 단계다. 빠르게 여러 칸을 굴려도 한 프레임에 한 단계만 옮겨 전환 애니메이션이 건너뛰지 않는다
        /// <summary> 휠 입력을 반영한 다음 줌 단계, 위로 굴리면 번호가 커지며 가까워진다 </summary>
        public static int StepZoomLevel(int level, float scroll, int levelCount)
        {
            if ((levelCount <= 0) || (scroll == 0f))
            {
                return Mathf.Clamp(level, 0, Mathf.Max(0, levelCount - 1));
            }

            int next = (scroll > 0f) ? (level + 1) : (level - 1);
            return Mathf.Clamp(next, 0, levelCount - 1);
        }
    }
}
