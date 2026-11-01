// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 마검사 Q 수류 방벽이 거는 보호막의 양, 유지 시간, 피해 흡수 계산 </summary>
    public static class PlayerShieldRules
    {
        // 클라이언트는 흡수 계산을 알 필요가 없어 서버 폴더에 둔다. 남은 보호막은 PlayerCombatState 복제 값으로만 전달된다

        /****************************************
        *                Fields
        ****************************************/

        // 최대 체력 비율, 마검사 Q 한 번이 거는 보호막
        public const float MAGEKNIGHT_Q_SHIELD_RATE = 0.05f;

        // 초, 기술 데이터에 방벽 유지 시간이 없을 때만 쓰는 임시값
        public const float DEFAULT_SHIELD_DURATION = 5f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이 직업의 이 칸 기술이 시전자에게 보호막을 거는지 </summary>
        public static bool GrantsShield(EPlayerClass playerClass, int slot)
        {
            return (playerClass == EPlayerClass.Mageknight) && (slot == Constants.Combat.SKILL_SLOT_Q);
        }

        /// <summary> 최대 체력에서 보호막 양을 구한다, 최대 체력이 양의 유한값이 아니면 0이다 </summary>
        public static float GetGrantAmount(float maxHealth)
        {
            if ((!(maxHealth > 0f)) || (float.IsInfinity(maxHealth)))
            {
                return 0f;
            }

            return maxHealth * MAGEKNIGHT_Q_SHIELD_RATE;
        }

        // 방벽 효과가 피격이상 면역 시간 동안 몸을 따라가므로 보호막도 같은 시간만 유지한다. 그래야 보이는 방벽과 흡수 구간이 어긋나지 않는다
        /// <summary> 보호막 유지 시간, 방벽 유지 시간이 양의 유한값이 아니면 임시값을 쓴다 </summary>
        public static float GetDuration(float barrierDuration)
        {
            if ((!(barrierDuration > 0f)) || (float.IsInfinity(barrierDuration)))
            {
                return DEFAULT_SHIELD_DURATION;
            }

            return barrierDuration;
        }

        /// <summary> 만료 시각 전이고 남은 양이 있으면 참 </summary>
        public static bool IsActive(float shield, double untilServerTime, double serverTime)
        {
            return (shield > 0f) && (serverTime < untilServerTime);
        }

        /// <summary> 보호막이 먼저 흡수하고 체력으로 넘길 피해를 돌려준다, remainingShield는 흡수 뒤 남은 보호막이다 </summary>
        public static float Absorb(float shield, float damage, out float remainingShield)
        {
            // NaN은 비교가 모두 거짓이라 이 형태로 같이 걸러진다. 잘못된 보호막은 없는 것으로 보고 피해는 그대로 넘긴다
            if ((!(shield > 0f)) || (float.IsInfinity(shield)))
            {
                remainingShield = 0f;
                return damage;
            }

            if ((!(damage > 0f)) || (float.IsInfinity(damage)))
            {
                remainingShield = shield;
                return damage;
            }

            if (damage < shield)
            {
                remainingShield = shield - damage;
                return 0f;
            }

            remainingShield = 0f;
            return Math.Max(0f, damage - shield);
        }
    }
}
