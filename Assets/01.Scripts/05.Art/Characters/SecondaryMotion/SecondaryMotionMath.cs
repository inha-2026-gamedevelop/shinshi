// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 보조본의 길이, 회전 한계, 캡슐 충돌과 순간이동을 계산한다 </summary>
    public static class SecondaryMotionMath
    {
        /****************************************
        *                Fields
        ****************************************/

        public const float LENGTH_EPSILON = 0.00001f;
        public const float MAX_ANGLE      = 180f;

        private const float LENGTH_EPSILON_SQUARED = LENGTH_EPSILON * LENGTH_EPSILON;
        private const float OPPOSITE_DOT_THRESHOLD = -0.999999f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 물리 계산에 들어갈 스칼라가 NaN이나 무한대가 아닌지 확인한다 </summary>
        public static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }

        /// <summary> 벡터와 길이 계산까지 유한한지 확인한다 </summary>
        public static bool IsFinite(Vector3 value)
        {
            return (IsFinite(value.x)) && (IsFinite(value.y)) && (IsFinite(value.z)) && (IsFinite(value.sqrMagnitude));
        }

        /// <summary> 유효한 회전으로 정규화할 수 있는지 확인한다 </summary>
        public static bool IsFinite(Quaternion value)
        {
            float norm = (value.x * value.x) + (value.y * value.y) + (value.z * value.z) + (value.w * value.w);
            return (IsFinite(value.x)) && (IsFinite(value.y)) && (IsFinite(value.z)) && (IsFinite(value.w))
                && (IsFinite(norm)) && (norm > LENGTH_EPSILON_SQUARED);
        }

        /// <summary> 기준 방향의 길이를 유지하고 최대 회전각 밖으로 나간 방향을 제한한다 </summary>
        public static Vector3 ConstrainDirection(Vector3 candidate, Vector3 rest, float maxAngle)
        {
            if ((!IsFinite(rest)) || (rest.sqrMagnitude <= LENGTH_EPSILON_SQUARED))
            {
                return Vector3.zero;
            }

            if ((!IsFinite(candidate)) || (candidate.sqrMagnitude <= LENGTH_EPSILON_SQUARED)
                || (!IsFinite(maxAngle)))
            {
                return rest;
            }

            float length = rest.magnitude;
            float radians = Mathf.Clamp(maxAngle, 0f, MAX_ANGLE) * Mathf.Deg2Rad;
            Vector3 restDirection = rest / length;
            Vector3 candidateDirection = candidate.normalized;
            float dot = Mathf.Clamp(Vector3.Dot(restDirection, candidateDirection), -1f, 1f);
            float cosine = Mathf.Cos(radians);
            if (dot >= cosine)
            {
                // 짧은 본은 한 substep의 회전량이 작으므로 범위 안의 후보를 그대로 누적한다
                return candidateDirection * length;
            }

            Vector3 tangent = candidateDirection - (restDirection * dot);
            if (tangent.sqrMagnitude <= LENGTH_EPSILON_SQUARED)
            {
                tangent = GetPerpendicular(restDirection);
            }

            return ((restDirection * cosine) + (tangent.normalized * Mathf.Sin(radians))) * length;
        }

        /// <summary> 작은 방향 변화도 누적할 수 있는 회전을 만든다, 입력 벡터의 길이에 영향을 받지 않는다 </summary>
        public static Quaternion GetSwingRotation(Vector3 from, Vector3 to)
        {
            if ((!IsFinite(from)) || (!IsFinite(to)) || (from.sqrMagnitude <= LENGTH_EPSILON_SQUARED)
                || (to.sqrMagnitude <= LENGTH_EPSILON_SQUARED))
            {
                return Quaternion.identity;
            }

            Vector3 a = from.normalized;
            Vector3 b = to.normalized;
            float dot = Mathf.Clamp(Vector3.Dot(a, b), -1f, 1f);
            if (dot <= OPPOSITE_DOT_THRESHOLD)
            {
                return Quaternion.AngleAxis(MAX_ANGLE, GetPerpendicular(a));
            }

            Vector3 cross = Vector3.Cross(a, b);
            return new Quaternion(cross.x, cross.y, cross.z, 1f + dot).normalized;
        }

        /// <summary> 캡슐 안의 끝점을 표면으로 밀어낸다, 축 위의 끝점에도 유한한 탈출 방향을 만든다 </summary>
        public static Vector3 ResolveCapsule(Vector3 point, Vector3 start, Vector3 end,
            float combinedRadius, Vector3 fallbackDirection)
        {
            if ((!IsFinite(point)) || (!IsFinite(start)) || (!IsFinite(end))
                || (!IsFinite(combinedRadius)) || (combinedRadius <= 0f))
            {
                return point;
            }

            Vector3 axis = end - start;
            float axisLengthSquared = axis.sqrMagnitude;
            if (!IsFinite(axisLengthSquared))
            {
                return point;
            }

            float along = (axisLengthSquared > LENGTH_EPSILON_SQUARED)
                ? Mathf.Clamp01(Vector3.Dot(point - start, axis) / axisLengthSquared) : 0f;
            Vector3 closest = start + (axis * along);
            Vector3 separation = point - closest;
            float distance = separation.magnitude;
            if ((!IsFinite(distance)) || (distance >= combinedRadius))
            {
                return point;
            }

            if (distance > LENGTH_EPSILON)
            {
                return closest + (separation * (combinedRadius / distance));
            }

            Vector3 escape = IsFinite(fallbackDirection) ? fallbackDirection : Vector3.right;
            if (axisLengthSquared > LENGTH_EPSILON_SQUARED)
            {
                escape -= axis * (Vector3.Dot(escape, axis) / axisLengthSquared);
                if (escape.sqrMagnitude <= LENGTH_EPSILON_SQUARED)
                {
                    escape = Vector3.Cross(axis, Vector3.right);
                    if (escape.sqrMagnitude <= LENGTH_EPSILON_SQUARED)
                    {
                        escape = Vector3.Cross(axis, Vector3.up);
                    }
                }
            }

            if ((!IsFinite(escape)) || (escape.sqrMagnitude <= LENGTH_EPSILON_SQUARED))
            {
                escape = Vector3.right;
            }

            return closest + (escape.normalized * combinedRadius);
        }

        /// <summary> 길이와 각도 제한 후에도 캡슐 내부에 남았는지 확인한다 </summary>
        public static bool IsInsideCapsule(Vector3 point, Vector3 start, Vector3 end, float radius, float tolerance)
        {
            if ((!IsFinite(point)) || (!IsFinite(start)) || (!IsFinite(end)) || (!IsFinite(radius))
                || (!IsFinite(tolerance)) || (radius <= Mathf.Max(0f, tolerance)))
            {
                return false;
            }

            Vector3 axis = end - start;
            float axisLengthSquared = axis.sqrMagnitude;
            if (!IsFinite(axisLengthSquared))
            {
                return false;
            }

            float along = (axisLengthSquared > LENGTH_EPSILON_SQUARED)
                ? Mathf.Clamp01(Vector3.Dot(point - start, axis) / axisLengthSquared) : 0f;
            Vector3 closest = start + (axis * along);
            return (point - closest).magnitude < (radius - Mathf.Max(0f, tolerance));
        }

        /// <summary> 캐릭터 루트의 큰 이동이나 회전, 잘못된 포즈에서는 이전 관성을 버린다 </summary>
        public static bool HasTeleported(Vector3 previousPosition, Quaternion previousRotation,
            Vector3 position, Quaternion rotation, float distanceThreshold, float angleThreshold)
        {
            if ((!IsFinite(previousPosition)) || (!IsFinite(previousRotation))
                || (!IsFinite(position)) || (!IsFinite(rotation))
                || (!IsFinite(distanceThreshold)) || (!IsFinite(angleThreshold)))
            {
                return true;
            }

            float distance = (position - previousPosition).magnitude;
            return (!IsFinite(distance)) || (distance > Mathf.Max(0f, distanceThreshold))
                || (Quaternion.Angle(previousRotation, rotation) > Mathf.Clamp(angleThreshold, 0f, MAX_ANGLE));
        }

        private static Vector3 GetPerpendicular(Vector3 direction)
        {
            Vector3 perpendicular = Vector3.Cross(direction, Vector3.up);
            if (perpendicular.sqrMagnitude <= LENGTH_EPSILON_SQUARED)
            {
                perpendicular = Vector3.Cross(direction, Vector3.right);
            }

            return perpendicular.normalized;
        }
    }
}
