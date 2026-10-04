// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 원본 발바닥과 리그별 목표 좌표로 침투한 발만 위로 보정한다 </summary>
    public static class PlayerFemaleCharacterFootSupportMath
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MIN_ROTATION_LENGTH_SQR = 0.000001f;
        private const float UNIT_ROTATION_TOLERANCE = 0.001f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> FK 방향을 보존하는 Humanoid 목표 위치와 회전을 구한다 </summary>
        public static bool TryMapGoal(Vector3 footPosition, Quaternion footRotation,
            in PlayerFemaleCharacterFootSupportProfile.GoalMapping mapping,
            out Vector3 goalPosition, out Quaternion goalRotation)
        {
            goalPosition = footPosition;
            goalRotation = footRotation;
            if ((!SecondaryMotionMath.IsFinite(footPosition)) || (!IsValidRotation(footRotation))
                || (!SecondaryMotionMath.IsFinite(mapping.GoalLocalBoneOffset))
                || (!IsValidRotation(mapping.GoalLocalBoneRotation)))
            {
                return false;
            }

            goalRotation = footRotation * Quaternion.Inverse(mapping.GoalLocalBoneRotation);
            goalPosition = footPosition - goalRotation * mapping.GoalLocalBoneOffset;
            return SecondaryMotionMath.IsFinite(goalPosition) && IsValidRotation(goalRotation);
        }

        /// <summary> 실제 침투량과 여유가 허용 범위 안에 있을 때만 상승량을 반환한다 </summary>
        public static bool TryGetRaise(Vector3 supportPosition, Vector3 groundPoint,
            Vector3 groundNormal, float extraHeight, float maximumRaise, float minimumGroundDot,
            out float raise)
        {
            raise = 0f;
            if ((!SecondaryMotionMath.IsFinite(supportPosition)) || (!SecondaryMotionMath.IsFinite(groundPoint))
                || (!SecondaryMotionMath.IsFinite(groundNormal)) || (!SecondaryMotionMath.IsFinite(extraHeight))
                || (!SecondaryMotionMath.IsFinite(maximumRaise)) || (!SecondaryMotionMath.IsFinite(minimumGroundDot))
                || (extraHeight < 0f) || (maximumRaise <= 0f) || (minimumGroundDot < 0f)
                || (minimumGroundDot > 1f) || (groundNormal.sqrMagnitude <= MIN_ROTATION_LENGTH_SQR))
            {
                return false;
            }

            float penetration = groundPoint.y - supportPosition.y;
            if ((penetration <= 0f) || (penetration + extraHeight > maximumRaise)
                || (Vector3.Dot(groundNormal.normalized, Vector3.up) < minimumGroundDot))
            {
                return false;
            }

            raise = penetration + extraHeight;
            return true;
        }

        /// <summary> 비유한 값과 단위 길이가 아닌 Quaternion을 거절한다 </summary>
        public static bool IsValidRotation(Quaternion rotation)
        {
            if (!SecondaryMotionMath.IsFinite(rotation))
            {
                return false;
            }

            float lengthSqr = rotation.x * rotation.x + rotation.y * rotation.y
                + rotation.z * rotation.z + rotation.w * rotation.w;
            return (lengthSqr > MIN_ROTATION_LENGTH_SQR) && (Mathf.Abs(lengthSqr - 1f) <= UNIT_ROTATION_TOLERANCE);
        }
    }
}
