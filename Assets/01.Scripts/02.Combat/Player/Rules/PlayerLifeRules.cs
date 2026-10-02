// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 사망 뒤 부활 대기와 부활 체력, 서버 승인과 클라이언트 부활 카운트다운이 같이 쓴다 </summary>
    public static class PlayerLifeRules
    {
        // 일반전은 SessionConfigSO.NormalReviveDelay와 NormalReviveHealthRate, 협동 보스는 Boss 값을 넘긴다.
        // 횟수 제한은 일반전에 없고 보스 공용 횟수는 서버의 ReviveBudget이 맡는다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 부활 요청을 받을 수 있게 되는 서버 시각 </summary>
        public static double ReviveReadyTime(double deathTime, float reviveDelay)
        {
            return deathTime + reviveDelay;
        }

        /// <summary> 대기 시간이 지났는지, 경계 시각을 포함한다 </summary>
        public static bool IsReviveReady(double deathTime, double now, float reviveDelay)
        {
            return now >= ReviveReadyTime(deathTime, reviveDelay);
        }

        /// <summary> 부활 직후 체력, 최대 체력에 비율을 곱하고 0과 최대 체력 사이로 자른다 </summary>
        public static float ReviveHealth(float maxHealth, float healthRate)
        {
            if (maxHealth <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp(maxHealth * healthRate, 0f, maxHealth);
        }
    }
}
