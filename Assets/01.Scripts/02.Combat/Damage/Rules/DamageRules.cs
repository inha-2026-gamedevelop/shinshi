// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 피해 계산 공식, 서버 판정과 성장 화면의 방어 미리보기가 같이 쓴다 </summary>
    public static class DamageRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 방어로 줄어드는 비율, 방어 / (방어 + 상수)를 상한으로 자른다 </summary>
        public static float DefenseReductionRate(float defense, float reductionConstant, float reductionCap)
        {
            if ((defense <= 0f) || (reductionConstant <= 0f))
            {
                return 0f;
            }

            float rate = defense / (defense + reductionConstant);
            return Mathf.Clamp(rate, 0f, reductionCap);
        }

        /// <summary> 방어 감소를 적용한 피해, 음수나 NaN 원피해는 0으로 본다 </summary>
        public static float ApplyDefense(float rawDamage, float defense, float reductionConstant, float reductionCap)
        {
            // NaN은 비교가 모두 거짓이라 이 형태로 같이 걸러진다
            if (!(rawDamage > 0f))
            {
                return 0f;
            }

            return rawDamage * (1f - DefenseReductionRate(defense, reductionConstant, reductionCap));
        }

        /// <summary> 피해 출발 위치가 대상 정면 기준 후방인지, 수평면에서만 본다 </summary>
        public static bool IsRearHit(Vector3 targetPosition, Vector3 targetForward, Vector3 sourcePosition, float rearMinAngleDegrees)
        {
            float toSourceX = sourcePosition.x - targetPosition.x;
            float toSourceZ = sourcePosition.z - targetPosition.z;
            float toSourceSqr = (toSourceX * toSourceX) + (toSourceZ * toSourceZ);
            float forwardSqr  = (targetForward.x * targetForward.x) + (targetForward.z * targetForward.z);

            // 같은 위치에서 터진 피해는 방향이 없으므로 정면으로 본다
            if ((toSourceSqr <= Constants.Player.DIRECTION_EPSILON_SQR) || (forwardSqr <= Constants.Player.DIRECTION_EPSILON_SQR))
            {
                return false;
            }

            float cos = ((toSourceX * targetForward.x) + (toSourceZ * targetForward.z)) / Mathf.Sqrt(toSourceSqr * forwardSqr);
            return cos < Mathf.Cos(rearMinAngleDegrees * Mathf.Deg2Rad);
        }
    }
}
