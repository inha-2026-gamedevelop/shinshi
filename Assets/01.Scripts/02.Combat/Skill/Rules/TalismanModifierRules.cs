// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary> 기술 하나에 걸린 부적 효과(보조 2, 핵심 1)를 SkillModifiers로 합성하고 실제 판정 수치를 읽는다 </summary>
    public static class TalismanModifierRules
    {
        // 합성 순서는 원본 -> 핵심 변형 -> 보조 -> 레벨 보너스다. 핵심 변형이 판정 형태를 먼저 정해야 보조가 그 형태에 맞게
        // 붙는다(넓게 베기의 원형 변형에는 넓은 검세가 각도 대신 반경을, 집중 검세가 각도 고정 대신 반경 감소를 준다).
        // 레벨 배율(1, 1.15, 1.30, 1.45, 1.60)은 부적이 더하는 보너스에만 곱하고 변형의 고정 계수, 반경, 타격 수와
        // 원래 기술 전체에는 곱하지 않는다. 쿨다운, 기력 감소는 원래 기술 값에, 피해 증가는 해당 타격 계수에 적용한다.
        //
        // 이 클래스는 참가자 입장 때 한 번 돌고 결과는 TalismanModifierResolver가 기술 칸마다 들고 있는다. 틱에서는 Get 메서드만
        // 부르고 그것들은 할당하지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const float FULL_ARC_ANGLE = 360f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> talismans 중 skill에 걸린 효과만 골라 합성한다. 모르는 효과, 다른 기술, 같은 단계 중복, 범위 밖 레벨이면 거짓과 이유 </summary>
        public static bool TryCompile(TalismanCatalogSO catalog, SkillCatalogSO.SkillDefinition skill,
            IReadOnlyList<CombatTalismanEntry> talismans, out SkillModifiers modifiers, out string error)
        {
            modifiers = SkillModifiers.Identity;
            if ((catalog == null) || (skill == null))
            {
                error = "부적 합성에 필요한 카탈로그나 기술이 없다";
                return false;
            }

            TalismanCatalogSO.TalismanEffectDefinition support1 = null;
            TalismanCatalogSO.TalismanEffectDefinition support2 = null;
            TalismanCatalogSO.TalismanEffectDefinition core     = null;
            int level1    = 0;
            int level2    = 0;
            int coreLevel = 0;

            int count = (talismans == null) ? 0 : talismans.Count;
            for (int i = 0; i < count; ++i)
            {
                CombatTalismanEntry entry = talismans[i];
                if ((entry == null) || (!string.Equals(entry.SkillId, skill.DefinitionId, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (!TryReadEntry(catalog, skill, entry, out TalismanCatalogSO.TalismanEffectDefinition effect, out error))
                {
                    return false;
                }

                switch (effect.Tier)
                {
                    case ETalismanTier.Support1:
                        if (support1 != null)
                        {
                            error = $"같은 단계 부적이 둘이다, skill={skill.DefinitionId}, tier={effect.Tier}";
                            return false;
                        }

                        support1 = effect;
                        level1   = entry.Level;
                        break;
                    case ETalismanTier.Support2:
                        if (support2 != null)
                        {
                            error = $"같은 단계 부적이 둘이다, skill={skill.DefinitionId}, tier={effect.Tier}";
                            return false;
                        }

                        support2 = effect;
                        level2   = entry.Level;
                        break;
                    default:
                        if (core != null)
                        {
                            error = $"같은 단계 부적이 둘이다, skill={skill.DefinitionId}, tier={effect.Tier}";
                            return false;
                        }

                        core      = effect;
                        coreLevel = entry.Level;
                        break;
                }
            }

            modifiers = Compile(catalog, core, coreLevel, support1, level1, support2, level2);
            error     = null;
            return true;
        }

        /// <summary> 이미 고른 효과로 합성한다, 없는 단계는 null. 효과가 같은 기술의 해당 단계라는 것은 호출자가 보장한다 </summary>
        public static SkillModifiers Compile(TalismanCatalogSO catalog,
            TalismanCatalogSO.TalismanEffectDefinition core, int coreLevel,
            TalismanCatalogSO.TalismanEffectDefinition support1, int level1,
            TalismanCatalogSO.TalismanEffectDefinition support2, int level2)
        {
            var builder = new Builder();
            builder.Reset();

            // 핵심 변형이 판정 형태를 먼저 정한다
            if (core != null)
            {
                builder.ApplyCore(core, catalog.ScaleBonus(core.LevelOneBonus, coreLevel));
            }

            if (support1 != null)
            {
                builder.ApplySupport(support1, catalog.GetLevelMultiplier(level1));
            }

            if (support2 != null)
            {
                builder.ApplySupport(support2, catalog.GetLevelMultiplier(level2));
            }

            return builder.Build();
        }

        /// <summary> 기력 비용, 감소 뒤 소수는 유지한다 </summary>
        public static float GetEnergyCost(SkillCatalogSO.SkillDefinition skill, in SkillModifiers modifiers)
        {
            return skill.EnergyCost * modifiers.EnergyScale;
        }

        /// <summary> 재사용 대기, 초 </summary>
        public static float GetCooldown(SkillCatalogSO.SkillDefinition skill, in SkillModifiers modifiers)
        {
            return skill.Cooldown * modifiers.CooldownScale;
        }

        /// <summary> 실제 판정 형태, 넓게 베기의 회전 참격은 시전자 중심 원형, 반격 파동은 직선이다 </summary>
        public static ESkillShape GetShape(SkillCatalogSO.SkillDefinition skill, in SkillModifiers modifiers)
        {
            ESkillShape shape = skill.Area.Shape;
            if ((shape == ESkillShape.Cone) && (modifiers.Core.Is(ETalismanEffectKind.CircleSweep)))
            {
                return ESkillShape.Circle;
            }

            if ((shape == ESkillShape.Cone) && (modifiers.Core.Is(ETalismanEffectKind.CounterWave)))
            {
                return ESkillShape.Line;
            }

            return shape;
        }

        /// <summary> 원 반경이나 부채꼴 길이(m), 0 아래로 내려가지 않는다 </summary>
        public static float GetRadius(SkillCatalogSO.SkillDefinition skill, in SkillModifiers modifiers)
        {
            float radius;
            if ((skill.Area.Shape != ESkillShape.Summon) && (modifiers.Core.Is(ETalismanEffectKind.CircleSweep)))
            {
                radius = modifiers.Core.Radius;
            }
            else
            {
                radius = (modifiers.FixedRadius > 0f) ? modifiers.FixedRadius : skill.Area.Radius;
            }

            return Math.Max(0f, radius + modifiers.RadiusDelta);
        }

        /// <summary> 부채꼴 전체 각도(도), 360을 넘지 않는다 </summary>
        public static float GetArcAngle(SkillCatalogSO.SkillDefinition skill, in SkillModifiers modifiers)
        {
            float angle = (modifiers.FixedArcAngle > 0f) ? modifiers.FixedArcAngle : skill.Area.ArcAngle + modifiers.ArcAngleDelta;
            return Math.Min(FULL_ARC_ANGLE, Math.Max(0f, angle));
        }

        /// <summary> 지정 거리, 투사체 사거리, 돌진 거리(m). 소환은 0이다 </summary>
        public static float GetCastRange(SkillCatalogSO.SkillDefinition skill, in SkillModifiers modifiers)
        {
            if (skill.Area.Shape == ESkillShape.Summon)
            {
                return skill.Area.CastRange;
            }

            return Math.Max(0f, skill.Area.CastRange + modifiers.RangeDelta + modifiers.MoveDistanceDelta);
        }

        /// <summary> 장판, 결계, 소환 유지 시간(초) </summary>
        public static float GetDuration(SkillCatalogSO.SkillDefinition skill, in SkillModifiers modifiers)
        {
            if (skill.Effect.Duration <= 0f)
            {
                return skill.Effect.Duration;
            }

            return skill.Effect.Duration + modifiers.DurationDelta;
        }

        /// <summary> 발동 구간(초), 반격은 이 동안이 전방 방어 창이다 </summary>
        public static float GetActiveSeconds(SkillCatalogSO.SkillDefinition skill, in SkillModifiers modifiers)
        {
            if (skill.Timeline.Active <= 0f)
            {
                return skill.Timeline.Active;
            }

            return skill.Timeline.Active + modifiers.GuardDurationDelta;
        }

        /// <summary> 동료 한 번 공격의 간격(초) </summary>
        public static float GetCompanionAttackInterval(SkillCatalogSO.SkillDefinition skill, in SkillModifiers modifiers)
        {
            return skill.Effect.CompanionAttackInterval * modifiers.AttackIntervalScale;
        }

        /// <summary> 동료 공격 사거리(m) </summary>
        public static float GetCompanionAttackRange(SkillCatalogSO.SkillDefinition skill, in SkillModifiers modifiers)
        {
            return Math.Max(0f, skill.Effect.CompanionAttackRange + modifiers.RangeDelta);
        }

        /// <summary> 첫 타격 계수(A), 피해 보조 전 값. 변형이 계수를 바꾸면 고정 기본 + 레벨 추가다 </summary>
        public static float GetPrimaryCoefficient(SkillCatalogSO.SkillDefinition skill, in SkillModifiers modifiers)
        {
            if (modifiers.Core.ReplacesCoefficient)
            {
                return modifiers.Core.BaseCoefficient + modifiers.Core.AddedCoefficient;
            }

            return (skill.Area.Shape == ESkillShape.Summon) ? skill.Effect.CompanionAttackCoefficient : skill.Effect.DamageCoefficient;
        }

        private static bool TryReadEntry(TalismanCatalogSO catalog, SkillCatalogSO.SkillDefinition skill, CombatTalismanEntry entry,
            out TalismanCatalogSO.TalismanEffectDefinition effect, out string error)
        {
            if (!catalog.TryGetEffect(entry.EffectId, out effect))
            {
                error = $"모르는 부적 효과다, effectId={entry.EffectId}";
                return false;
            }

            if ((!string.Equals(effect.SkillId, skill.DefinitionId, StringComparison.Ordinal))
                || (effect.PlayerClass != skill.PlayerClass) || ((int)effect.Tier != entry.Tier))
            {
                error = $"부적 효과의 기술, 직업, 단계가 계약과 다르다, effectId={entry.EffectId}, skill={entry.SkillId}, tier={entry.Tier}";
                return false;
            }

            if ((entry.Level < 1) || (entry.Level > catalog.MaxEffectLevel))
            {
                error = $"부적 효과 레벨이 범위를 벗어났다, effectId={entry.EffectId}, level={entry.Level}";
                return false;
            }

            error = null;
            return true;
        }

        /****************************************
        *               Builder
        ****************************************/

        // 합성 중간값. 입장 때 한 번 쓰는 지역 값이라 클래스 밖으로 내보내지 않는다
        private struct Builder
        {
            public float            EnergyScale;
            public float            CooldownScale;
            public float            DamageScale;
            public float            ResonanceBonus;
            public float            RadiusDelta;
            public float            FixedRadius;
            public float            ArcAngleDelta;
            public float            FixedArcAngle;
            public float            RangeDelta;
            public float            MoveDistanceDelta;
            public float            DurationDelta;
            public float            GuardDurationDelta;
            public float            AttackIntervalScale;
            public float            MarkedDamageBonus;
            public SkillCoreVariant Core;

            public void Reset()
            {
                EnergyScale         = 1f;
                CooldownScale       = 1f;
                DamageScale         = 1f;
                AttackIntervalScale = 1f;
                Core                = default;
            }

            public void ApplyCore(TalismanCatalogSO.TalismanEffectDefinition effect, float bonus)
            {
                TalismanCatalogSO.TalismanVariantSpec variant = effect.Variant;
                float rootBonus = (effect.UpgradeStat == ETalismanUpgradeStat.RootDuration) ? bonus : 0f;
                float added     = (effect.UpgradeStat == ETalismanUpgradeStat.AddedCoefficient) ? bonus : 0f;

                // TODO(민성): 따르는 결계(T12-3A)는 부착 대상 선택 기준과 반경 안에 적이 없을 때 처리가 기획 미정이다.
                // 종류만 기록하고 피해 보너스 +5%p도 붙이지 않는다. 실행 쪽도 이 종류를 원래 결계로 다룬다
                if (effect.Kind == ETalismanEffectKind.AttachBarrier)
                {
                    added = 0f;
                }

                Core = new SkillCoreVariant(effect.Kind, variant.BaseCoefficient, added, variant.Delay, variant.FixedRadius,
                    variant.Length, variant.Width, variant.ProjectileCount, variant.SpreadAngle, variant.MaxExtraTargets, rootBonus);
            }

            public void ApplySupport(TalismanCatalogSO.TalismanEffectDefinition effect, float multiplier)
            {
                TalismanCatalogSO.TalismanVariantSpec variant = effect.Variant;
                float bonus      = effect.LevelOneBonus * multiplier;
                bool  circleCore = Core.Is(ETalismanEffectKind.CircleSweep);
                switch (effect.Kind)
                {
                    case ETalismanEffectKind.CooldownReduction:
                        CooldownScale *= 1f - bonus;
                        break;
                    case ETalismanEffectKind.EnergyReduction:
                        EnergyScale *= 1f - bonus;
                        break;
                    case ETalismanEffectKind.WidenArc:
                        if (circleCore)
                        {
                            RadiusDelta += variant.AltShapeBonus * multiplier;
                        }
                        else
                        {
                            ArcAngleDelta += bonus;
                        }

                        break;
                    case ETalismanEffectKind.FocusArc:
                        // 원형 변형의 반경 감소는 레벨로 커지지 않는 고정값이고 피해 증가만 레벨로 커진다
                        if (circleCore)
                        {
                            RadiusDelta += variant.RadiusDelta;
                        }
                        else
                        {
                            FixedArcAngle = variant.FixedArcAngle;
                        }

                        DamageScale *= 1f + bonus;
                        break;
                    case ETalismanEffectKind.RadiusIncrease:
                        RadiusDelta += bonus;
                        break;
                    case ETalismanEffectKind.FocusRadius:
                        FixedRadius  = variant.FixedRadius;
                        DamageScale *= 1f + bonus;
                        break;
                    case ETalismanEffectKind.RangeIncrease:
                        RangeDelta += bonus;
                        break;
                    case ETalismanEffectKind.MoveDistanceIncrease:
                        MoveDistanceDelta += bonus;
                        break;
                    case ETalismanEffectKind.DamageIncrease:
                        DamageScale *= 1f + bonus;
                        break;
                    case ETalismanEffectKind.GuardDurationIncrease:
                        GuardDurationDelta += bonus;
                        break;
                    case ETalismanEffectKind.DurationIncrease:
                        DurationDelta += bonus;
                        break;
                    case ETalismanEffectKind.AttackIntervalReduction:
                        AttackIntervalScale *= 1f - bonus;
                        break;
                    case ETalismanEffectKind.MarkedTargetDamage:
                        MarkedDamageBonus += bonus;
                        break;
                    case ETalismanEffectKind.ResonanceGainIncrease:
                        ResonanceBonus += bonus;
                        break;
                    default:
                        break;
                }
            }

            public SkillModifiers Build()
            {
                return new SkillModifiers(EnergyScale, CooldownScale, DamageScale, ResonanceBonus, RadiusDelta, FixedRadius,
                    ArcAngleDelta, FixedArcAngle, RangeDelta, MoveDistanceDelta, DurationDelta, GuardDurationDelta,
                    AttackIntervalScale, MarkedDamageBonus, in Core);
            }
        }
    }
}
