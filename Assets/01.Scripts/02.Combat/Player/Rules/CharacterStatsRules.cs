// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary> 출정 고정 전투 준비(장비, 수련)를 최종 능력치로 바꾼다, 서버 판정과 클라이언트 미리보기가 같이 쓴다 </summary>
    public static class CharacterStatsRules
    {
        // B = 육체 기본값 + 장비 합. 장비 하나는 부위 기본값 x 등급 배율 x (1 + 강화율 x 단계)이고 수련용은 여기에 수련용 비율을
        // 곱한다. 장신구 치명은 등급, 강화 배율을 곱하지 않는 고정값(수련용은 비율만)이다. 생명력 흡수는 기본 흡수가 있는
        // 부위에만 등급 단계마다 더하고 강화와 수련은 올리지 않는다. 수련은 B에 한 번만 곱하거나 더하며,
        // 수련 결과를 다시 수련의 기초로 쓰지 않는다. 치명 상한은 마지막에 자르고 최대 생명, 최대 기력은 버림 정수다.
        //
        // 중간 계산은 decimal로 한다. SO의 float(0.04, 1.35)를 그대로 곱하면 2130 같은 정수 결과가 2129.9999로 내려가
        // 버림에서 하나가 빠진다. float를 decimal로 바꾸면 유효 숫자 7자리로 반올림돼 표에 적은 값과 같아지고, 백엔드의
        // decimal 계산과도 같은 결과가 된다. 출정 입장 때 한 번만 도는 계산이라 비용은 문제가 되지 않는다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 장비와 수련이 없는 육체 기본값, 전투 준비가 없는 개발 입장에서만 쓴다 </summary>
        public static CharacterStats Base(PlayerConfigSO player)
        {
            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }

            return Finish(player, (decimal)player.BaseAttack, (decimal)player.BaseHealth, (decimal)player.BaseDefense,
                (decimal)player.BaseCritChance, (decimal)player.BaseMaxEnergy, 0m);
        }

        public static CharacterStats Base(PlayerConfigSO player, CharacterLevelSettings levels, long totalExperience)
        {
            if (levels == null)
            {
                return Base(player);
            }
            int gainedLevels = CharacterLevelRules.Level(levels, totalExperience) - 1;
            return Finish(player, (decimal)player.BaseAttack + ((decimal)levels.AttackPerLevel * gainedLevels),
                (decimal)player.BaseHealth + ((decimal)levels.HealthPerLevel * gainedLevels), (decimal)player.BaseDefense,
                (decimal)player.BaseCritChance, (decimal)player.BaseMaxEnergy, 0m);
        }

        /// <summary> 전투 준비가 콘텐츠와 맞지 않으면 ArgumentException이다 </summary>
        public static CharacterStats Compute(PlayerConfigSO player, EquipmentCatalogSO equipment, GrowthConfigSO growth,
            CharacterCombatLoadout loadout)
        {
            if (!TryCompute(player, equipment, growth, loadout, out CharacterStats stats, out string error))
            {
                throw new ArgumentException(error, nameof(loadout));
            }

            return stats;
        }

        /// <summary> 통과하면 참과 최종 능력치, 모르는 장비, 부위나 등급 불일치, 범위 밖 강화와 수련이면 거짓과 이유 </summary>
        public static bool TryCompute(PlayerConfigSO player, EquipmentCatalogSO equipment, GrowthConfigSO growth,
            CharacterCombatLoadout loadout, out CharacterStats stats, out string error)
        {
            return TryCompute(player, equipment, growth, loadout, null, 0, out stats, out error);
        }

        public static bool TryCompute(PlayerConfigSO player, EquipmentCatalogSO equipment, GrowthConfigSO growth,
            CharacterCombatLoadout loadout, CharacterLevelSettings levels, long totalExperience,
            out CharacterStats stats, out string error)
        {
            stats = default;
            if ((totalExperience < 0) || ((levels != null) && !levels.IsValid()))
            {
                error = "경험치 설정이나 누적 경험치가 유효하지 않다";
                return false;
            }
            if ((player == null) || (equipment == null) || (growth == null))
            {
                error = "능력치 계산에 필요한 GameDB 참조가 없다";
                return false;
            }

            if (loadout == null)
            {
                error = "전투 준비가 없다";
                return false;
            }

            if ((loadout.Equipped == null) || (loadout.Equipped.Length != Constants.Combat.EQUIPMENT_SLOT_COUNT))
            {
                error = "착용 장비 칸 수가 부위 수와 다르다";
                return false;
            }

            if ((loadout.Training == null) || (loadout.Training.Length != Constants.Combat.TRAINING_STAT_COUNT))
            {
                error = "수련 칸 수가 수련 항목 수와 다르다";
                return false;
            }

            decimal attack  = (decimal)player.BaseAttack;
            decimal health  = (decimal)player.BaseHealth;
            decimal defense = (decimal)player.BaseDefense;
            decimal crit    = (decimal)player.BaseCritChance;
            decimal energy  = (decimal)player.BaseMaxEnergy;
            decimal lifeSteal = 0m;
            if (levels != null)
            {
                int gainedLevels = CharacterLevelRules.Level(levels, totalExperience) - 1;
                attack += (decimal)levels.AttackPerLevel * gainedLevels;
                health += (decimal)levels.HealthPerLevel * gainedLevels;
            }

            for (int i = 0; i < Constants.Combat.EQUIPMENT_SLOT_COUNT; ++i)
            {
                CombatEquipmentEntry entry = loadout.Equipped[i];
                if (entry == null)
                {
                    continue;
                }

                if (!TryReadEquipment(equipment, (EEquipmentSlot)i, entry, out decimal statScale, out decimal critScale,
                    out decimal gradeLifeSteal, out error))
                {
                    return false;
                }

                EquipmentCatalogSO.SlotBaseStats slotBase = equipment.GetSlotBase((EEquipmentSlot)i);
                attack  += (decimal)slotBase.Attack * statScale;
                health  += (decimal)slotBase.Health * statScale;
                defense += (decimal)slotBase.Defense * statScale;
                crit    += (decimal)slotBase.CritChance * critScale;

                // 기본 흡수가 없는 부위에 등급 증가분만 붙으면 장신구 없이도 흡수가 생긴다
                decimal baseLifeSteal = (decimal)slotBase.LifeSteal;
                if (baseLifeSteal > 0m)
                {
                    lifeSteal += (baseLifeSteal + gradeLifeSteal) * critScale;
                }
            }

            if (!TryReadTraining(growth, loadout.Training, out error))
            {
                return false;
            }

            attack  *= 1m + (Gain(growth, ETrainingStat.Attack) * loadout.Training[(int)ETrainingStat.Attack]);
            health  *= 1m + (Gain(growth, ETrainingStat.Health) * loadout.Training[(int)ETrainingStat.Health]);
            defense *= 1m + (Gain(growth, ETrainingStat.Defense) * loadout.Training[(int)ETrainingStat.Defense]);
            crit    += Gain(growth, ETrainingStat.CritChance) * loadout.Training[(int)ETrainingStat.CritChance];
            energy  += Gain(growth, ETrainingStat.MaxEnergy) * loadout.Training[(int)ETrainingStat.MaxEnergy];

            stats = Finish(player, attack, health, defense, crit, energy, lifeSteal);
            error = null;
            return true;
        }

        private static CharacterStats Finish(PlayerConfigSO player, decimal attack, decimal health, decimal defense,
            decimal crit, decimal energy, decimal lifeSteal)
        {
            decimal critCap      = (decimal)player.CritChanceCap;
            decimal lifeStealCap = (decimal)player.LifeStealRateCap;
            return new CharacterStats((float)attack, (float)decimal.Floor(health), (float)defense,
                (float)Math.Min(crit, critCap), (float)decimal.Floor(energy), player.MoveSpeed,
                (float)Math.Min(lifeSteal, lifeStealCap));
        }

        // 계약의 등급과 수련용 표시가 카탈로그 정의와 다르면 저장 데이터와 콘텐츠가 어긋난 것이다. 어느 쪽을 믿을지 정할 수 없어 거절한다
        private static bool TryReadEquipment(EquipmentCatalogSO equipment, EEquipmentSlot slot, CombatEquipmentEntry entry,
            out decimal statScale, out decimal critScale, out decimal gradeLifeSteal, out string error)
        {
            statScale      = 0m;
            critScale      = 0m;
            gradeLifeSteal = 0m;
            if (!equipment.TryGetEquipment(entry.DefinitionId, out EquipmentCatalogSO.EquipmentDefinition definition))
            {
                error = $"모르는 장비다, slot={slot}, definitionId={entry.DefinitionId}";
                return false;
            }

            if ((definition.Slot != slot) || ((int)definition.Grade != entry.Grade) || (definition.IsTraining != entry.IsTraining))
            {
                error = $"장비 정의와 계약 값이 다르다, slot={slot}, definitionId={entry.DefinitionId}, grade={entry.Grade}, " +
                    $"isTraining={entry.IsTraining}";
                return false;
            }

            if ((entry.Enhancement < 0) || (entry.Enhancement > equipment.MaxEnhancement))
            {
                error = $"강화 단계가 범위를 벗어났다, slot={slot}, enhancement={entry.Enhancement}";
                return false;
            }

            decimal trainingScale = definition.IsTraining ? (decimal)equipment.TrainingStatRate : 1m;
            decimal enhancement   = 1m + ((decimal)equipment.EnhancementStatRate * entry.Enhancement);
            statScale = (decimal)equipment.GetGradeMultiplier(definition.Grade) * enhancement * trainingScale;
            critScale = trainingScale;

            // 등급 순번은 일반 0부터다. 등급 배율이 아니라 단계 수를 곱해 더한다
            gradeLifeSteal = (decimal)equipment.LifeStealPerGrade * (int)definition.Grade;
            error          = null;
            return true;
        }

        private static bool TryReadTraining(GrowthConfigSO growth, int[] training, out string error)
        {
            int total = 0;
            for (int i = 0; i < training.Length; ++i)
            {
                int steps = training[i];
                if ((steps < 0) || (steps > growth.TrainingStatCap))
                {
                    error = $"수련 단계가 항목 상한을 벗어났다, stat={(ETrainingStat)i}, steps={steps}";
                    return false;
                }

                total += steps;
            }

            if (total > growth.TrainingTotalCap)
            {
                error = $"수련 총 투자가 상한을 넘는다, total={total}";
                return false;
            }

            error = null;
            return true;
        }

        private static decimal Gain(GrowthConfigSO growth, ETrainingStat stat)
        {
            return (decimal)growth.GetTrainingStat(stat).GainPerStep;
        }
    }
}
