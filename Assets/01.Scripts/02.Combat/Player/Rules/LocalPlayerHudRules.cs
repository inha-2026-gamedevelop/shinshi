// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary> 로컬 플레이어의 전투 상태를 HUD 값으로 바꾸는 순수 계산, 쿨다운 남은 시간, 기술 칸 대응, 게이지 정수 표기 </summary>
    public static class LocalPlayerHudRules
    {
        /****************************************
        *              Constants
        ****************************************/

        // HUD 칸 이름 접두사. 뒤에 기술 키 글자 하나가 붙는다
        private const string SLOT_NAME_PREFIX = "SkillSlot";

        // 서버가 계산한 부동소수점 잡음이 한 칸 올림으로 보이지 않게 하는 여유
        private const float DISPLAY_EPSILON = 0.0001f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 시각 기준 남은 쿨다운 초, 이미 지났거나 값이 올바르지 않으면 0이다 </summary>
        public static float Remaining(double readyAt, double now)
        {
            if ((double.IsNaN(readyAt)) || (double.IsNaN(now)) || (readyAt <= now))
            {
                return 0f;
            }

            return (float)(readyAt - now);
        }

        /// <summary>
        /// 게이지 비율 계산에 쓸 쿨다운 전체 길이. 새로 시작한 쿨다운은 처음 본 남은 시간이 부적 보정까지 반영한 길이이고,
        /// 접속 직후 이미 진행 중인 쿨다운은 남은 시간이 전체가 아니므로 카탈로그 길이를 쓴다
        /// </summary>
        public static float ResolveDuration(double readyAt, double now, float catalogCooldown, bool isNewCooldown)
        {
            float catalog   = Mathf.Max(0f, catalogCooldown);
            float remaining = Remaining(readyAt, now);
            if (remaining <= 0f)
            {
                return catalog;
            }

            return isNewCooldown ? remaining : Mathf.Max(catalog, remaining);
        }

        /// <summary> HUD 기술 칸 오브젝트 이름에서 기술 키를 읽는다, 접두사와 Q W E R Z X C 한 글자가 아니면 거짓 </summary>
        public static bool TryParseSlotKey(string objectName, out ESkillInputKey key)
        {
            key = ESkillInputKey.None;
            if ((string.IsNullOrEmpty(objectName)) || (objectName.Length != (SLOT_NAME_PREFIX.Length + 1))
                || (!objectName.StartsWith(SLOT_NAME_PREFIX, StringComparison.Ordinal)))
            {
                return false;
            }

            switch (objectName[SLOT_NAME_PREFIX.Length])
            {
                case 'Q':
                    key = ESkillInputKey.Q;
                    return true;
                case 'W':
                    key = ESkillInputKey.W;
                    return true;
                case 'E':
                    key = ESkillInputKey.E;
                    return true;
                case 'R':
                    key = ESkillInputKey.R;
                    return true;
                case 'Z':
                    key = ESkillInputKey.Z;
                    return true;
                case 'X':
                    key = ESkillInputKey.X;
                    return true;
                case 'C':
                    key = ESkillInputKey.C;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 직업과 키로 기술 칸이 보여 줄 기술을 찾는다. 키와 직업 대응은 입력과 같은 SkillInputRules를 쓴다.
        /// 직업에 없는 칸(신령사가 아닌 직업의 Z X C)이거나 카탈로그에 없으면 거짓이다
        /// </summary>
        public static bool TryResolveSlot(SkillCatalogSO catalog, EPlayerClass playerClass, ESkillInputKey key,
            out ESkillRequestKind kind, out byte slot, out float cooldown, out Sprite icon)
        {
            cooldown = 0f;
            icon     = null;
            if ((catalog == null) || (!SkillInputRules.TryMapKey(playerClass, key, out kind, out slot)))
            {
                kind = ESkillRequestKind.Regular;
                slot = 0;
                return false;
            }

            if (kind == ESkillRequestKind.Regular)
            {
                if (!catalog.TryGetSkillBySlot(playerClass, slot, out SkillCatalogSO.SkillDefinition skill))
                {
                    return false;
                }

                cooldown = skill.Cooldown;
                icon     = skill.Icon;
                return true;
            }

            for (int i = 0; i < catalog.Ascensions.Count; ++i)
            {
                SkillCatalogSO.AscensionDefinition ascension = catalog.Ascensions[i];
                if ((ascension != null) && (ascension.Slot == slot))
                {
                    cooldown = catalog.AscensionSharedCooldown;
                    icon     = ascension.Icon;
                    return true;
                }
            }

            return false;
        }

        /// <summary> 공명 게이지는 신령사만 보인다 </summary>
        public static bool HasResonance(EPlayerClass playerClass)
        {
            return playerClass == EPlayerClass.Summoner;
        }

        /// <summary> 게이지 값을 최대값 안으로 가두어 HUD 정수로 올린다, 살아 있는 동안 체력이 0으로 보이지 않게 올림이다 </summary>
        public static int ToDisplayInt(float value, float max)
        {
            if ((float.IsNaN(value)) || (float.IsNaN(max)) || (max <= 0f))
            {
                return 0;
            }

            float clamped = Mathf.Clamp(value, 0f, max);
            return Mathf.Max(0, Mathf.CeilToInt(clamped - DISPLAY_EPSILON));
        }

        /// <summary> 복제된 값으로 게이지를 만든다, 공명은 신령사가 아니면 0과 숨김으로 둔다 </summary>
        public static LocalPlayerGauges BuildGauges(float health, float maxHealth, float energy, float maxEnergy,
            float resonance, float maxResonance, EPlayerClass playerClass)
        {
            bool  hasResonance = HasResonance(playerClass);
            float resonanceNow = hasResonance ? resonance : 0f;
            float resonanceMax = hasResonance ? maxResonance : 0f;
            return new LocalPlayerGauges(health, maxHealth, energy, maxEnergy, resonanceNow, resonanceMax, hasResonance);
        }
    }
}
