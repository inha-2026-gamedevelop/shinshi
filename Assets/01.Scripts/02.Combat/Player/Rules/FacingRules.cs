// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 수평 방향과 ushort 각도의 변환, 조준 입력과 바라보는 방향 복제가 같이 쓴다 </summary>
    public static class FacingRules
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float FULL_TURN_DEGREES = 360f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 수평 성분만 남겨 정규화한다, 길이가 거의 0이면 실패 </summary>
        public static bool TryGetHorizontalDirection(Vector3 vector, out Vector3 direction)
        {
            float sqr = (vector.x * vector.x) + (vector.z * vector.z);

            // NaN도 이 비교에서 거짓이 되어 걸러진다
            if ((!(sqr > Constants.Player.DIRECTION_EPSILON_SQR)) || (float.IsInfinity(sqr)))
            {
                direction = Vector3.zero;
                return false;
            }

            float inverse = 1f / Mathf.Sqrt(sqr);
            direction = new Vector3(vector.x * inverse, 0f, vector.z * inverse);
            return true;
        }

        /// <summary> +Z를 0으로 시계 방향 각도를 ushort로 줄인다, 수평 단위 벡터를 넘긴다 </summary>
        public static ushort EncodeYaw(Vector3 direction)
        {
            float degrees = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            if (degrees < 0f)
            {
                degrees += FULL_TURN_DEGREES;
            }

            int steps = Mathf.RoundToInt(degrees / FULL_TURN_DEGREES * Constants.Player.YAW_QUANTIZATION_STEPS);
            return (ushort)(steps % Constants.Player.YAW_QUANTIZATION_STEPS);
        }

        public static Vector3 DecodeYaw(ushort yaw)
        {
            float radians = yaw * (FULL_TURN_DEGREES / Constants.Player.YAW_QUANTIZATION_STEPS) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }
    }
}
