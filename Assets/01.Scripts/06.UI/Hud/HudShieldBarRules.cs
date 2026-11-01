// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> 체력과 보호막을 한 막대에 나누는 비율 계산, 보호막은 체력 채움 끝에 이어 붙인다 </summary>
    public static class HudShieldBarRules
    {
        /****************************************
        *                Methods
        ****************************************/

        // 체력과 보호막의 합이 최대 체력을 넘으면 합을 막대 전체로 본다. 그러지 않으면 가득 찬 체력에서 보호막이 막대 밖으로 밀려 보이지 않는다
        /// <summary> 체력 채움 끝 비율과 보호막 끝 비율을 구한다, 최대 체력이 0 이하면 둘 다 0이다 </summary>
        public static void Compute(int health, int shield, int maxHealth, out float healthRatio, out float shieldEndRatio)
        {
            if (maxHealth <= 0)
            {
                healthRatio    = 0f;
                shieldEndRatio = 0f;
                return;
            }

            float clampedHealth = Mathf.Clamp(health, 0, maxHealth);
            float clampedShield = Mathf.Max(0, shield);
            float total         = Mathf.Max(maxHealth, clampedHealth + clampedShield);
            healthRatio    = clampedHealth / total;
            shieldEndRatio = Mathf.Clamp01((clampedHealth + clampedShield) / total);
        }
    }
}
