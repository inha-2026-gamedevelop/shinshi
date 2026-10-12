// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 장비 강화 창이 표시하는 성공률, 실패 보정, 능력치 전후와 이름 계산 </summary>
    public static class EquipmentEnhanceRules
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int NEVER_GUARANTEED = -1;

        private const float FULL_RATE       = 1f;
        private const int   CURRENT_ATTEMPT = 1;

        private static readonly string[] G_SlotNames  = { "무기", "머리", "몸통", "손", "발", "장신구" };
        private static readonly string[] G_GradeNames = { "일반", "희귀", "영웅", "전설" };

        private const string TRAINING_PREFIX = "수련용";
        private const string UNKNOWN_NAME    = "알 수 없는 장비";

        /****************************************
        *                Methods
        ****************************************/

        public static bool IsMaxLevel(EquipmentCatalogSO catalog, int level)
        {
            return level >= catalog.MaxEnhancement;
        }

        /// <summary> 캐릭터, 부위, 현재 단계에 쌓인 실패 횟수, 기록이 없으면 0 </summary>
        public static int FindFailures(IReadOnlyList<TownPityEntry> pity, EEquipmentSlot slot, int currentLevel)
        {
            if (pity == null)
            {
                return 0;
            }

            for (int i = 0; i < pity.Count; ++i)
            {
                if ((pity[i].Slot == (byte)slot) && (pity[i].Stage == currentLevel))
                {
                    return Math.Max(0, pity[i].Failures);
                }
            }

            return 0;
        }

        /// <summary> 현재 단계에서 한 번 시도할 때의 성공률(0에서 1) </summary>
        public static float SuccessRate(EquipmentCatalogSO catalog, int currentLevel, int failures)
        {
            return catalog.GetEnhancementStep(currentLevel + 1).GetSuccessRate(Math.Max(0, failures));
        }

        /// <summary> 지금부터 계속 실패해도 몇 번째 시도에서 확정 성공하는지, 이번 시도가 확정이면 1. 보정이 없어 확정되지 않으면 NEVER_GUARANTEED </summary>
        public static int AttemptsUntilGuaranteed(EquipmentCatalogSO catalog, int currentLevel, int failures)
        {
            EquipmentCatalogSO.EnhancementStep step = catalog.GetEnhancementStep(currentLevel + 1);
            int current = Math.Max(0, failures);
            if (step.GetSuccessRate(current) >= FULL_RATE)
            {
                return CURRENT_ATTEMPT;
            }

            if (step.FailBonusRate <= 0f)
            {
                return NEVER_GUARANTEED;
            }

            // 기본 성공률과 보정 폭은 0 초과 1 이하라 확정까지의 시도 수는 보정 폭의 역수를 넘지 않는다
            int limit = (int)Math.Ceiling(FULL_RATE / step.FailBonusRate) + CURRENT_ATTEMPT;
            for (int extra = 1; extra <= limit; ++extra)
            {
                if (step.GetSuccessRate(current + extra) >= FULL_RATE)
                {
                    return extra + CURRENT_ATTEMPT;
                }
            }

            return NEVER_GUARANTEED;
        }

        /// <summary> 0에서 1, 기본 성공률에서 확정 성공까지 실패 보정이 얼마나 찼는지 </summary>
        public static float PityProgress(EquipmentCatalogSO catalog, int currentLevel, int failures)
        {
            EquipmentCatalogSO.EnhancementStep step = catalog.GetEnhancementStep(currentLevel + 1);
            float span = FULL_RATE - step.BaseSuccessRate;
            if (span <= 0f)
            {
                return FULL_RATE;
            }

            float gained = step.GetSuccessRate(Math.Max(0, failures)) - step.BaseSuccessRate;
            return Math.Min(FULL_RATE, Math.Max(0f, gained / span));
        }

        /// <summary> 장비 한 개가 주는 공격, 생명, 방어 중 하나. CharacterStatsRules와 같은 배율이며 화면 표시용이다 </summary>
        public static float ScaledStat(EquipmentCatalogSO catalog, EquipmentCatalogSO.EquipmentDefinition definition,
            float baseValue, int level)
        {
            float trainingScale    = definition.IsTraining ? catalog.TrainingStatRate : FULL_RATE;
            float enhancementScale = FULL_RATE + (catalog.EnhancementStatRate * level);
            return baseValue * catalog.GetGradeMultiplier(definition.Grade) * enhancementScale * trainingScale;
        }

        public static string SlotName(EEquipmentSlot slot)
        {
            int index = (int)slot;
            return ((index >= 0) && (index < G_SlotNames.Length)) ? G_SlotNames[index] : string.Empty;
        }

        public static string GradeName(EItemGrade grade)
        {
            int index = (int)grade;
            return ((index >= 0) && (index < G_GradeNames.Length)) ? G_GradeNames[index] : string.Empty;
        }

        /// <summary> 장비 표시 이름. 정의에 표시 문자열이 아직 없어 등급과 부위로 만든다 </summary>
        public static string DisplayName(EquipmentCatalogSO catalog, string definitionId)
        {
            if ((catalog == null) || (!catalog.TryGetEquipment(definitionId, out EquipmentCatalogSO.EquipmentDefinition definition)))
            {
                return UNKNOWN_NAME;
            }

            string prefix = definition.IsTraining ? TRAINING_PREFIX : GradeName(definition.Grade);
            return prefix + " " + SlotName(definition.Slot);
        }
    }
}
