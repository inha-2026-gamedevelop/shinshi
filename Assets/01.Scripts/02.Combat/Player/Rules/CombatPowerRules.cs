// System
using System;

// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary> 최종 능력치를 표시용 종합 전투력으로 바꾼다, 판정에는 쓰지 않는다 </summary>
    public static class CombatPowerRules
    {
        // 환산 공격과 환산 생존은 실제 피해 공식에서 그대로 뽑는다. 전투력이 오르면 실제 기대 피해와 버티는 양도 같은 비율로
        // 오르고, 치명과 감소율 상한을 같이 잘라 버려지는 능력치가 숫자를 부풀리지 않는다.
        // 기력과 직업별 기술 계수는 넣지 않는다. 피해 공식에 직접 들어가지 않거나 직업 간 비교를 흔든다.
        // 부적 효과는 아직 넣지 않는다. 효과별 기여율 표가 없다
        // TODO(민성): 부적 기여율 표가 정해지면 환산 공격에 배율로 곱한다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 기술 계수 1당 기대 피해, 치명 확률은 이미 상한을 적용한 값이다 </summary>
        public static float Offense(in CharacterStats stats, PlayerConfigSO config)
        {
            if ((config == null) || (!(stats.Attack > 0f)))
            {
                return 0f;
            }

            float crit = Math.Max(0f, Math.Min(stats.CritChance, config.CritChanceCap));
            return stats.Attack * (1f + (crit * (config.CritDamageMultiplier - 1f)));
        }

        /// <summary> 방어와 흡수를 생명으로 환산한 유효 체력, 플레이어가 맞을 때의 방어 상수를 쓴다 </summary>
        public static float Survival(in CharacterStats stats, PlayerConfigSO config)
        {
            if ((config == null) || (!(stats.MaxHealth > 0f)))
            {
                return 0f;
            }

            float reduction = DamageRules.DefenseReductionRate(stats.Defense, config.PlayerDefenseReductionConstant,
                config.DamageReductionCap);
            float taken     = 1f - reduction;
            if (!(taken > 0f))
            {
                return 0f;
            }

            float lifeSteal = Math.Max(0f, stats.LifeSteal);
            return (stats.MaxHealth / taken) * (1f + (config.CombatPowerLifeStealFactor * lifeSteal));
        }

        /// <summary> 종합 전투력, 계산할 수 없으면 0 </summary>
        public static int Compute(in CharacterStats stats, PlayerConfigSO config)
        {
            if (config == null)
            {
                return 0;
            }

            double offense  = Offense(in stats, config);
            double survival = Survival(in stats, config);
            double weight   = config.CombatPowerOffenseWeight;
            if ((!(offense > 0.0)) || (!(survival > 0.0)) || (!(weight >= 0.0)) || (!(weight <= 1.0)))
            {
                return 0;
            }

            double power = config.CombatPowerScale * Math.Pow(offense, weight) * Math.Pow(survival, 1.0 - weight);
            if ((double.IsNaN(power)) || (power <= 0.0))
            {
                return 0;
            }

            return (power >= int.MaxValue) ? int.MaxValue : (int)Math.Floor(power);
        }
    }
}
