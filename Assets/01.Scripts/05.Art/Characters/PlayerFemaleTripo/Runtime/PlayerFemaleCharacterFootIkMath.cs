// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 접지 높이와 발바닥 여유를 적용하고 공중의 발에는 IK를 적용하지 않는 시각 계산 </summary>
    public static class PlayerFemaleCharacterFootIkMath
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MIN_NORMAL_LENGTH_SQR = 0.000001f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> IK 목표와 접지 가중치를 구하며 비유한 입력, 가파른 면, 높이 범위 밖은 거절한다 </summary>
        public static bool TryGetTarget(Vector3 animatedPosition, Quaternion animatedRotation, Vector3 groundPoint,
            Vector3 groundNormal, Vector3 up, float soleHeight, float contactRange, float maxRaise,
            float maxDrop, float minimumGroundDot, out Vector3 targetPosition, out Quaternion targetRotation,
            out float contactWeight)
        {
            targetPosition = animatedPosition;
            targetRotation = animatedRotation;
            contactWeight = 0f;
            if ((!SecondaryMotionMath.IsFinite(animatedPosition)) || (!SecondaryMotionMath.IsFinite(animatedRotation))
                || (!SecondaryMotionMath.IsFinite(groundPoint)) || (!SecondaryMotionMath.IsFinite(groundNormal))
                || (!SecondaryMotionMath.IsFinite(up)) || (!SecondaryMotionMath.IsFinite(soleHeight))
                || (!SecondaryMotionMath.IsFinite(contactRange)) || (!SecondaryMotionMath.IsFinite(maxRaise))
                || (!SecondaryMotionMath.IsFinite(maxDrop)) || (!SecondaryMotionMath.IsFinite(minimumGroundDot))
                || (groundNormal.sqrMagnitude <= MIN_NORMAL_LENGTH_SQR) || (up.sqrMagnitude <= MIN_NORMAL_LENGTH_SQR)
                || (soleHeight < 0f) || (contactRange <= 0f) || (maxRaise < 0f) || (maxDrop < 0f)
                || (minimumGroundDot < 0f) || (minimumGroundDot > 1f))
            {
                return false;
            }

            up = up.normalized;
            groundNormal = groundNormal.normalized;
            if (Vector3.Dot(groundNormal, up) < minimumGroundDot)
            {
                return false;
            }

            Vector3 solePosition = animatedPosition - up * soleHeight;
            float correction = Vector3.Dot(groundPoint - solePosition, up);
            if ((correction > maxRaise) || (correction < -maxDrop) || (-correction >= contactRange))
            {
                return false;
            }

            contactWeight = (correction >= 0f) ? 1f : 1f + correction / contactRange;
            targetPosition = animatedPosition + up * correction;
            targetRotation = Quaternion.FromToRotation(animatedRotation * Vector3.up, groundNormal) * animatedRotation;
            return (SecondaryMotionMath.IsFinite(targetPosition)) && (SecondaryMotionMath.IsFinite(targetRotation));
        }
    }
}
