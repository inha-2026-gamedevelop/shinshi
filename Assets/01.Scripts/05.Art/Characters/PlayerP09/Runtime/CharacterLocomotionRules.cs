// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 캐릭터 이동 표현에 쓰는 순수 계산, 재생 배율과 방향 회전과 발 IK 오프셋을 다룬다 </summary>
    public static class CharacterLocomotionRules
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DIRECTION_SQR = 0.0001f;
        private const float MIN_PLAYBACK      = 1f;

        /****************************************
        *                Methods
        ****************************************/

        // 블렌드 트리 기준값을 클립의 실제 이동 속도로 두면 기준 속도 이하에서는 섞임만으로 발 속도가 맞는다.
        // 가장 빠른 클립보다 빨리 움직일 때만 재생 속도를 올려 발이 미끄러지지 않게 한다
        /// <summary> 실제 이동 속도를 가장 빠른 이동 클립의 원래 속도로 나눈 재생 배율, 1 이상 maximum 이하 </summary>
        public static float ComputePlaybackScale(float speed, float referenceSpeed, float maximumScale)
        {
            if ((!IsFinite(speed)) || (!(speed > 0f)) || (!IsFinite(referenceSpeed)) || (!(referenceSpeed > 0f)))
            {
                return MIN_PLAYBACK;
            }

            float upper = (IsFinite(maximumScale) && (maximumScale > MIN_PLAYBACK)) ? maximumScale : MIN_PLAYBACK;
            return Mathf.Clamp(speed / referenceSpeed, MIN_PLAYBACK, upper);
        }

        /// <summary> 수평 방향이 유효하면 정규화해 돌려준다 </summary>
        public static bool TryGetHorizontal(Vector3 direction, out Vector3 horizontal)
        {
            horizontal = new Vector3(direction.x, 0f, direction.z);
            float sqrMagnitude = horizontal.sqrMagnitude;
            if ((!IsFinite(sqrMagnitude)) || (!(sqrMagnitude >= MIN_DIRECTION_SQR)))
            {
                horizontal = Vector3.zero;
                return false;
            }

            horizontal.Normalize();
            return true;
        }

        /// <summary> 현재 회전에서 목표 방향으로 초당 각도만큼만 돈다, 각도가 0 이하면 바로 맞추고 방향이 없으면 그대로 둔다 </summary>
        public static Quaternion TurnTowards(Quaternion current, Vector3 targetForward, float degreesPerSecond, float deltaSeconds)
        {
            if (!TryGetHorizontal(targetForward, out Vector3 horizontal))
            {
                return current;
            }

            Quaternion target = Quaternion.LookRotation(horizontal, Vector3.up);
            if ((!IsFinite(degreesPerSecond)) || (!(degreesPerSecond > 0f)) || (!IsFinite(deltaSeconds)) || (!(deltaSeconds > 0f)))
            {
                return target;
            }

            return Quaternion.RotateTowards(current, target, degreesPerSecond * deltaSeconds);
        }

        // 계단 옆 난간이나 벽 윗면은 한 칸보다 높으므로 한도로 자르지 않고 지면 후보에서 뺀다
        /// <summary> 맞은 면이 발이 올라설 수 있는 높이인지, 루트보다 maxRaise 넘게 높으면 아니다 </summary>
        public static bool IsReachableGround(float groundHeight, float rootHeight, float maxRaise)
        {
            float rise = groundHeight - rootHeight;
            return (IsFinite(rise)) && (rise <= Mathf.Abs(maxRaise));
        }

        /// <summary> 발 아래 지면과 루트 평면의 높이 차, 올림과 내림 한도로 자른다 </summary>
        public static float ComputeFootOffset(float groundHeight, float rootHeight, float maxRaise, float maxDrop)
        {
            float difference = groundHeight - rootHeight;
            if ((!IsFinite(difference)) || (!IsFinite(maxRaise)) || (!IsFinite(maxDrop)))
            {
                return 0f;
            }

            return Mathf.Clamp(difference, -Mathf.Abs(maxDrop), Mathf.Abs(maxRaise));
        }

        /// <summary> 두 발 중 더 낮은 쪽까지 골반을 내린다, 올리지는 않는다 </summary>
        public static float ComputePelvisOffset(float leftOffset, float rightOffset)
        {
            float left  = IsFinite(leftOffset) ? leftOffset : 0f;
            float right = IsFinite(rightOffset) ? rightOffset : 0f;
            return Mathf.Min(0f, Mathf.Min(left, right));
        }

        /// <summary> 프레임 간격과 무관한 지수 감쇠로 current를 target 쪽으로 옮긴다 </summary>
        public static float Smooth(float current, float target, float sharpness, float deltaSeconds)
        {
            if (!IsFinite(target))
            {
                return IsFinite(current) ? current : 0f;
            }

            if ((!IsFinite(current)) || (!(sharpness > 0f)) || (!(deltaSeconds > 0f)) || (float.IsInfinity(sharpness * deltaSeconds)))
            {
                return target;
            }

            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-sharpness * deltaSeconds));
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }
    }
}
