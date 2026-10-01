// System
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> 부적 효과 45종의 직업, 기술, 단계, 선택과 변형 수치 </summary>
    [CreateAssetMenu(fileName = "TalismanCatalog", menuName = "Shinshi/TalismanCatalogSO")]
    public class TalismanCatalogSO : GameDataSO
    {
        /// <summary> 핵심 변형과 형태 조합에 쓰는 고정 수치, 효과 레벨로 커지지 않는다. 쓰지 않는 값은 0이다 </summary>
        [Serializable]
        public class TalismanVariantSpec
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private float _baseCoefficient;  // 공격력 배율, 변형된 타격의 고정 기본 계수. 0이면 원래 기술 계수 유지
            [SerializeField] private float _fixedArcAngle;    // 도, 부채꼴 각도를 이 값으로 바꾼다
            [SerializeField] private float _fixedRadius;      // m, 원형 판정이나 추가 충격파의 반경
            [SerializeField] private float _radiusDelta;      // m, 원형 변형과 조합할 때 반경에 더하는 고정값. 음수 가능
            [SerializeField] private float _altShapeBonus;    // 원형 변형과 조합할 때 각도 대신 키우는 레벨 1 반경 보너스, m
            [SerializeField] private float _length;           // m, 직선 파동 길이
            [SerializeField] private float _width;            // m, 직선 파동 폭
            [SerializeField] private float _delay;            // 초, 첫 타격 뒤 후속 타격이나 폭발까지
            [SerializeField] private int   _projectileCount;  // 갈래 또는 대상 수
            [SerializeField] private float _spreadAngle;      // 도, 이웃한 갈래 사이 각도
            [SerializeField] private int   _maxExtraTargets;  // 관통 뒤 추가로 맞힐 최대 적 수

            public float BaseCoefficient => _baseCoefficient;
            public float FixedArcAngle   => _fixedArcAngle;
            public float FixedRadius     => _fixedRadius;
            public float RadiusDelta     => _radiusDelta;
            public float AltShapeBonus   => _altShapeBonus;
            public float Length          => _length;
            public float Width           => _width;
            public float Delay           => _delay;
            public int   ProjectileCount => _projectileCount;
            public float SpreadAngle     => _spreadAngle;
            public int   MaxExtraTargets => _maxExtraTargets;

            /****************************************
            *                Methods
            ****************************************/

            public TalismanVariantSpec(float baseCoefficient = 0f, float fixedArcAngle = 0f, float fixedRadius = 0f,
                float radiusDelta = 0f, float altShapeBonus = 0f, float length = 0f, float width = 0f, float delay = 0f,
                int projectileCount = 0, float spreadAngle = 0f, int maxExtraTargets = 0)
            {
                _baseCoefficient = baseCoefficient;
                _fixedArcAngle   = fixedArcAngle;
                _fixedRadius     = fixedRadius;
                _radiusDelta     = radiusDelta;
                _altShapeBonus   = altShapeBonus;
                _length          = length;
                _width           = width;
                _delay           = delay;
                _projectileCount = projectileCount;
                _spreadAngle     = spreadAngle;
                _maxExtraTargets = maxExtraTargets;
            }
        }

        /// <summary> 부적 효과 하나, 기술 한 개의 한 단계 후보다 </summary>
        [Serializable]
        public class TalismanEffectDefinition : IDefinition
        {
            /****************************************
            *                Fields
            ****************************************/

            private const string DISPLAY_KEY_SUFFIX = ".name";

            // 비율 보너스는 0.1이 10%다. 감소 효과도 양수로 적고 방향은 효과 종류가 정한다
            [SerializeField] private string               _definitionId;
            [SerializeField] private string               _displayKey;
            [SerializeField] private EPlayerClass         _playerClass;
            [SerializeField] private string               _skillId;       // SkillCatalogSO 일반 기술 ID
            [SerializeField] private ETalismanTier        _tier;
            [SerializeField] private ETalismanChoice      _choice;
            [SerializeField] private ETalismanEffectKind  _kind;
            [SerializeField] private ETalismanUpgradeStat _upgradeStat;
            [SerializeField] private float                _levelOneBonus; // 효과 레벨 1의 추가 보너스, 단위는 강화 항목을 따른다
            [SerializeField] private TalismanVariantSpec  _variant;

            public string               DefinitionId  => _definitionId;
            public string               DisplayKey    => _displayKey;
            public EPlayerClass         PlayerClass   => _playerClass;
            public string               SkillId       => _skillId;
            public ETalismanTier        Tier          => _tier;
            public ETalismanChoice      Choice        => _choice;
            public ETalismanEffectKind  Kind          => _kind;
            public ETalismanUpgradeStat UpgradeStat   => _upgradeStat;
            public float                LevelOneBonus => _levelOneBonus;
            public TalismanVariantSpec  Variant       => _variant;

            /****************************************
            *                Methods
            ****************************************/

            public TalismanEffectDefinition(string definitionId, EPlayerClass playerClass, string skillId, ETalismanTier tier,
                ETalismanChoice choice, ETalismanEffectKind kind, ETalismanUpgradeStat upgradeStat, float levelOneBonus,
                TalismanVariantSpec variant = null)
            {
                _definitionId  = definitionId;
                _displayKey    = definitionId + DISPLAY_KEY_SUFFIX;
                _playerClass   = playerClass;
                _skillId       = skillId;
                _tier          = tier;
                _choice        = choice;
                _kind          = kind;
                _upgradeStat   = upgradeStat;
                _levelOneBonus = levelOneBonus;
                _variant       = variant ?? new TalismanVariantSpec();
            }
        }

        /****************************************
        *              Constants
        ****************************************/

        private const int   EFFECT_COUNT           = 45;
        private const int   EFFECTS_PER_CLASS      = 15;
        private const int   MAX_EFFECT_LEVEL       = 5;
        private const int   SIGNATURE_CHOICE_COUNT = 2;   // 대표 기술의 단계당 후보 수
        private const int   NORMAL_CHOICE_COUNT    = 1;
        private const float FIRST_LEVEL_MULTIPLIER = 1f;  // 레벨 1은 표의 수치를 그대로 쓴다
        private const float MAX_REDUCTION_RATE     = 1f;  // 감소율이 1 이상이면 비용이나 시간이 0 이하가 된다
        private const float MAX_ARC_ANGLE          = 360f;

        // 효과 ID는 T + 기술 두 자리 + - + 단계 숫자 + 선택 문자다
        private const string EFFECT_ID_PATTERN = @"^T\d{2}-(?<tier>\d)(?<choice>[A-Z])$";
        private const string TIER_GROUP        = "tier";
        private const string CHOICE_GROUP      = "choice";

        private const string WIDE_SLASH     = "skill_warrior_wide_slash";
        private const string DASH           = "skill_warrior_dash";
        private const string GROUND_SLAM    = "skill_warrior_ground_slam";
        private const string COUNTER        = "skill_warrior_counter";
        private const string SPELL_BOLT     = "skill_sorcerer_spell_bolt";
        private const string FLAME_FIELD    = "skill_sorcerer_flame_field";
        private const string BINDING_FIELD  = "skill_sorcerer_binding_field";
        private const string LIGHTNING      = "skill_sorcerer_lightning";
        private const string SUMMON_BEAST   = "skill_summoner_summon_beast";
        private const string SUMMON_SPIRIT  = "skill_summoner_summon_spirit";
        private const string SPIRIT_STRIKE  = "skill_summoner_spirit_strike";
        private const string TARGET_BARRIER = "skill_summoner_target_barrier";

        /****************************************
        *                Fields
        ****************************************/

        // 레벨 1부터 순서대로, 부적이 더하는 보너스에만 곱하고 원래 기술 전체에는 곱하지 않는다
        [Header("효과 레벨 보너스 배율 (레벨 1부터)")]
        [SerializeField] private float[] _levelBonusMultipliers = { 1f, 1.15f, 1.3f, 1.45f, 1.6f };

        // TODO(민성): 기력 감소 뒤 비용의 반올림, 버림, 소수 유지는 기획 미정
        // TODO(민성): T12-3A 따르는 결계의 부착 대상 선택 기준과 반경 안에 적이 없을 때 처리는 기획 미정
        [Header("부적 효과 45종")]
        [SerializeField] private TalismanEffectDefinition[] _effects =
        {
            // 무사 넓게 베기, 대표 기술
            new TalismanEffectDefinition("T01-1A", EPlayerClass.Warrior, WIDE_SLASH, ETalismanTier.Support1, ETalismanChoice.A,
                ETalismanEffectKind.CooldownReduction, ETalismanUpgradeStat.CooldownRate, 0.1f),
            new TalismanEffectDefinition("T01-1B", EPlayerClass.Warrior, WIDE_SLASH, ETalismanTier.Support1, ETalismanChoice.B,
                ETalismanEffectKind.EnergyReduction, ETalismanUpgradeStat.EnergyCostRate, 0.2f),
            new TalismanEffectDefinition("T01-2A", EPlayerClass.Warrior, WIDE_SLASH, ETalismanTier.Support2, ETalismanChoice.A,
                ETalismanEffectKind.WidenArc, ETalismanUpgradeStat.ArcAngle, 20f,
                new TalismanVariantSpec(altShapeBonus: 0.5f)),
            new TalismanEffectDefinition("T01-2B", EPlayerClass.Warrior, WIDE_SLASH, ETalismanTier.Support2, ETalismanChoice.B,
                ETalismanEffectKind.FocusArc, ETalismanUpgradeStat.DamageRate, 0.2f,
                new TalismanVariantSpec(fixedArcAngle: 80f, radiusDelta: -0.5f)),
            // 연속 참격의 피해 보조는 두 타격 모두에 적용한다
            new TalismanEffectDefinition("T01-3A", EPlayerClass.Warrior, WIDE_SLASH, ETalismanTier.Core, ETalismanChoice.A,
                ETalismanEffectKind.FollowUpStrike, ETalismanUpgradeStat.AddedCoefficient, 0.6f,
                new TalismanVariantSpec(delay: 0.15f)),
            new TalismanEffectDefinition("T01-3B", EPlayerClass.Warrior, WIDE_SLASH, ETalismanTier.Core, ETalismanChoice.B,
                ETalismanEffectKind.CircleSweep, ETalismanUpgradeStat.AddedCoefficient, 0.2f,
                new TalismanVariantSpec(baseCoefficient: 2.2f, fixedRadius: 3f)),

            // 무사 돌진
            new TalismanEffectDefinition("T02-1A", EPlayerClass.Warrior, DASH, ETalismanTier.Support1, ETalismanChoice.A,
                ETalismanEffectKind.EnergyReduction, ETalismanUpgradeStat.EnergyCostRate, 0.2f),
            new TalismanEffectDefinition("T02-2A", EPlayerClass.Warrior, DASH, ETalismanTier.Support2, ETalismanChoice.A,
                ETalismanEffectKind.MoveDistanceIncrease, ETalismanUpgradeStat.MoveDistance, 1f),
            new TalismanEffectDefinition("T02-3A", EPlayerClass.Warrior, DASH, ETalismanTier.Core, ETalismanChoice.A,
                ETalismanEffectKind.PierceDash, ETalismanUpgradeStat.AddedCoefficient, 0.2f,
                new TalismanVariantSpec(baseCoefficient: 1.8f)),

            // 무사 내려찍기, 강한 일격은 추가 충격파에도 같은 비율을 적용한다
            new TalismanEffectDefinition("T03-1A", EPlayerClass.Warrior, GROUND_SLAM, ETalismanTier.Support1, ETalismanChoice.A,
                ETalismanEffectKind.CooldownReduction, ETalismanUpgradeStat.CooldownRate, 0.1f),
            new TalismanEffectDefinition("T03-2A", EPlayerClass.Warrior, GROUND_SLAM, ETalismanTier.Support2, ETalismanChoice.A,
                ETalismanEffectKind.DamageIncrease, ETalismanUpgradeStat.DamageRate, 0.15f),
            new TalismanEffectDefinition("T03-3A", EPlayerClass.Warrior, GROUND_SLAM, ETalismanTier.Core, ETalismanChoice.A,
                ETalismanEffectKind.Shockwave, ETalismanUpgradeStat.AddedCoefficient, 1.2f,
                new TalismanVariantSpec(fixedRadius: 4f, delay: 0.2f)),

            // 무사 반격, 방어 불가 공격 판정은 부적으로 바뀌지 않는다
            new TalismanEffectDefinition("T04-1A", EPlayerClass.Warrior, COUNTER, ETalismanTier.Support1, ETalismanChoice.A,
                ETalismanEffectKind.EnergyReduction, ETalismanUpgradeStat.EnergyCostRate, 0.2f),
            new TalismanEffectDefinition("T04-2A", EPlayerClass.Warrior, COUNTER, ETalismanTier.Support2, ETalismanChoice.A,
                ETalismanEffectKind.GuardDurationIncrease, ETalismanUpgradeStat.GuardDuration, 0.1f),
            new TalismanEffectDefinition("T04-3A", EPlayerClass.Warrior, COUNTER, ETalismanTier.Core, ETalismanChoice.A,
                ETalismanEffectKind.CounterWave, ETalismanUpgradeStat.AddedCoefficient, 0.5f,
                new TalismanVariantSpec(baseCoefficient: 3.5f, length: 8f, width: 3f)),

            // 도술사 투사체
            new TalismanEffectDefinition("T05-1A", EPlayerClass.Sorcerer, SPELL_BOLT, ETalismanTier.Support1, ETalismanChoice.A,
                ETalismanEffectKind.EnergyReduction, ETalismanUpgradeStat.EnergyCostRate, 0.2f),
            new TalismanEffectDefinition("T05-2A", EPlayerClass.Sorcerer, SPELL_BOLT, ETalismanTier.Support2, ETalismanChoice.A,
                ETalismanEffectKind.RangeIncrease, ETalismanUpgradeStat.Range, 2f),
            // 같은 발사에서 한 대상은 한 갈래만 맞는다
            new TalismanEffectDefinition("T05-3A", EPlayerClass.Sorcerer, SPELL_BOLT, ETalismanTier.Core, ETalismanChoice.A,
                ETalismanEffectKind.SpreadShot, ETalismanUpgradeStat.AddedCoefficient, 0.2f,
                new TalismanVariantSpec(baseCoefficient: 1.2f, projectileCount: 3, spreadAngle: 20f)),

            // 도술사 불꽃진, 대표 기술. 폭발 변형에도 반경과 피해 보조가 적용된다
            new TalismanEffectDefinition("T06-1A", EPlayerClass.Sorcerer, FLAME_FIELD, ETalismanTier.Support1, ETalismanChoice.A,
                ETalismanEffectKind.CooldownReduction, ETalismanUpgradeStat.CooldownRate, 0.1f),
            new TalismanEffectDefinition("T06-1B", EPlayerClass.Sorcerer, FLAME_FIELD, ETalismanTier.Support1, ETalismanChoice.B,
                ETalismanEffectKind.EnergyReduction, ETalismanUpgradeStat.EnergyCostRate, 0.2f),
            new TalismanEffectDefinition("T06-2A", EPlayerClass.Sorcerer, FLAME_FIELD, ETalismanTier.Support2, ETalismanChoice.A,
                ETalismanEffectKind.RadiusIncrease, ETalismanUpgradeStat.Radius, 0.5f),
            new TalismanEffectDefinition("T06-2B", EPlayerClass.Sorcerer, FLAME_FIELD, ETalismanTier.Support2, ETalismanChoice.B,
                ETalismanEffectKind.FocusRadius, ETalismanUpgradeStat.DamageRate, 0.2f,
                new TalismanVariantSpec(fixedRadius: 2f)),
            new TalismanEffectDefinition("T06-3A", EPlayerClass.Sorcerer, FLAME_FIELD, ETalismanTier.Core, ETalismanChoice.A,
                ETalismanEffectKind.Detonate, ETalismanUpgradeStat.AddedCoefficient, 1f,
                new TalismanVariantSpec(baseCoefficient: 4.5f, delay: 0.8f)),
            new TalismanEffectDefinition("T06-3B", EPlayerClass.Sorcerer, FLAME_FIELD, ETalismanTier.Core, ETalismanChoice.B,
                ETalismanEffectKind.EndExplosion, ETalismanUpgradeStat.AddedCoefficient, 1.5f),

            // 도술사 속박진
            new TalismanEffectDefinition("T07-1A", EPlayerClass.Sorcerer, BINDING_FIELD, ETalismanTier.Support1, ETalismanChoice.A,
                ETalismanEffectKind.EnergyReduction, ETalismanUpgradeStat.EnergyCostRate, 0.2f),
            new TalismanEffectDefinition("T07-2A", EPlayerClass.Sorcerer, BINDING_FIELD, ETalismanTier.Support2, ETalismanChoice.A,
                ETalismanEffectKind.RadiusIncrease, ETalismanUpgradeStat.Radius, 0.5f),
            // 보스는 끌어오지 않고 제어 허용 구간에서만 속박한다
            new TalismanEffectDefinition("T07-3A", EPlayerClass.Sorcerer, BINDING_FIELD, ETalismanTier.Core, ETalismanChoice.A,
                ETalismanEffectKind.PullRoot, ETalismanUpgradeStat.RootDuration, 0.2f),

            // 도술사 낙뢰, 반경 보조는 두 번째 낙뢰에도 적용한다
            new TalismanEffectDefinition("T08-1A", EPlayerClass.Sorcerer, LIGHTNING, ETalismanTier.Support1, ETalismanChoice.A,
                ETalismanEffectKind.CooldownReduction, ETalismanUpgradeStat.CooldownRate, 0.1f),
            new TalismanEffectDefinition("T08-2A", EPlayerClass.Sorcerer, LIGHTNING, ETalismanTier.Support2, ETalismanChoice.A,
                ETalismanEffectKind.RadiusIncrease, ETalismanUpgradeStat.Radius, 0.5f),
            new TalismanEffectDefinition("T08-3A", EPlayerClass.Sorcerer, LIGHTNING, ETalismanTier.Core, ETalismanChoice.A,
                ETalismanEffectKind.FollowUpStrike, ETalismanUpgradeStat.AddedCoefficient, 2f,
                new TalismanVariantSpec(delay: 0.35f)),

            // 신령사 근접 영물 소환, 대표 기술. 동료 한 동작의 공명은 다중 적중이어도 한 번이다
            new TalismanEffectDefinition("T09-1A", EPlayerClass.Summoner, SUMMON_BEAST, ETalismanTier.Support1, ETalismanChoice.A,
                ETalismanEffectKind.DurationIncrease, ETalismanUpgradeStat.EffectDuration, 4f),
            new TalismanEffectDefinition("T09-1B", EPlayerClass.Summoner, SUMMON_BEAST, ETalismanTier.Support1, ETalismanChoice.B,
                ETalismanEffectKind.EnergyReduction, ETalismanUpgradeStat.EnergyCostRate, 0.2f),
            new TalismanEffectDefinition("T09-2A", EPlayerClass.Summoner, SUMMON_BEAST, ETalismanTier.Support2, ETalismanChoice.A,
                ETalismanEffectKind.AttackIntervalReduction, ETalismanUpgradeStat.AttackIntervalRate, 0.1f),
            new TalismanEffectDefinition("T09-2B", EPlayerClass.Summoner, SUMMON_BEAST, ETalismanTier.Support2, ETalismanChoice.B,
                ETalismanEffectKind.MarkedTargetDamage, ETalismanUpgradeStat.DamageBonusPoint, 0.1f),
            // 기본 공격 주기는 그대로 둔다
            new TalismanEffectDefinition("T09-3A", EPlayerClass.Summoner, SUMMON_BEAST, ETalismanTier.Core, ETalismanChoice.A,
                ETalismanEffectKind.FollowUpStrike, ETalismanUpgradeStat.AddedCoefficient, 0.15f,
                new TalismanVariantSpec(delay: 0.12f)),
            new TalismanEffectDefinition("T09-3B", EPlayerClass.Summoner, SUMMON_BEAST, ETalismanTier.Core, ETalismanChoice.B,
                ETalismanEffectKind.CircleSweep, ETalismanUpgradeStat.AddedCoefficient, 0.05f,
                new TalismanVariantSpec(baseCoefficient: 0.25f, fixedRadius: 2.5f)),

            // 신령사 원거리 신령 소환
            new TalismanEffectDefinition("T10-1A", EPlayerClass.Summoner, SUMMON_SPIRIT, ETalismanTier.Support1, ETalismanChoice.A,
                ETalismanEffectKind.DurationIncrease, ETalismanUpgradeStat.EffectDuration, 4f),
            new TalismanEffectDefinition("T10-2A", EPlayerClass.Summoner, SUMMON_SPIRIT, ETalismanTier.Support2, ETalismanChoice.A,
                ETalismanEffectKind.RangeIncrease, ETalismanUpgradeStat.Range, 2f),
            // 적이 하나뿐이면 한 발만 쏜다
            new TalismanEffectDefinition("T10-3A", EPlayerClass.Summoner, SUMMON_SPIRIT, ETalismanTier.Core, ETalismanChoice.A,
                ETalismanEffectKind.SplitTargets, ETalismanUpgradeStat.AddedCoefficient, 0.05f,
                new TalismanVariantSpec(baseCoefficient: 0.3f, projectileCount: 2)),

            // 신령사 영격
            new TalismanEffectDefinition("T11-1A", EPlayerClass.Summoner, SPIRIT_STRIKE, ETalismanTier.Support1, ETalismanChoice.A,
                ETalismanEffectKind.EnergyReduction, ETalismanUpgradeStat.EnergyCostRate, 0.2f),
            // 표적 결계 적중 공명은 늘리지 않는다
            new TalismanEffectDefinition("T11-2A", EPlayerClass.Summoner, SPIRIT_STRIKE, ETalismanTier.Support2, ETalismanChoice.A,
                ETalismanEffectKind.ResonanceGainIncrease, ETalismanUpgradeStat.ResonanceGain, 2f),
            new TalismanEffectDefinition("T11-3A", EPlayerClass.Summoner, SPIRIT_STRIKE, ETalismanTier.Core, ETalismanChoice.A,
                ETalismanEffectKind.PierceShot, ETalismanUpgradeStat.AddedCoefficient, 1.2f,
                new TalismanVariantSpec(maxExtraTargets: 2)),

            // 신령사 표적 결계
            new TalismanEffectDefinition("T12-1A", EPlayerClass.Summoner, TARGET_BARRIER, ETalismanTier.Support1, ETalismanChoice.A,
                ETalismanEffectKind.CooldownReduction, ETalismanUpgradeStat.CooldownRate, 0.1f),
            new TalismanEffectDefinition("T12-2A", EPlayerClass.Summoner, TARGET_BARRIER, ETalismanTier.Support2, ETalismanChoice.A,
                ETalismanEffectKind.DurationIncrease, ETalismanUpgradeStat.EffectDuration, 2f),
            // 대상 사망 때 결계가 끝난다
            new TalismanEffectDefinition("T12-3A", EPlayerClass.Summoner, TARGET_BARRIER, ETalismanTier.Core, ETalismanChoice.A,
                ETalismanEffectKind.AttachBarrier, ETalismanUpgradeStat.DamageBonusPoint, 0.05f)
        };

        [NonSerialized] private DefinitionLookup<TalismanEffectDefinition> _lookup = new DefinitionLookup<TalismanEffectDefinition>();

        public IReadOnlyList<TalismanEffectDefinition> Effects        => _effects;
        public int                                     MaxEffectLevel => (_levelBonusMultipliers == null) ? 0 : _levelBonusMultipliers.Length;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _lookup.Invalidate();
        }

        protected override void OnValidate()
        {
            _lookup.Invalidate();
            base.OnValidate();
        }

        /****************************************
        *                Methods
        ****************************************/

        public bool ContainsEffect(string effectId)
        {
            return _lookup.Contains(_effects, effectId);
        }

        public bool TryGetEffect(string effectId, out TalismanEffectDefinition effect)
        {
            return _lookup.TryGet(_effects, effectId, out effect);
        }

        /// <summary> 효과 레벨(1부터)의 보너스 배율 </summary>
        public float GetLevelMultiplier(int level)
        {
            if ((_levelBonusMultipliers == null) || (level < 1) || (level > _levelBonusMultipliers.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(level), "효과 레벨 범위를 벗어났다");
            }

            return _levelBonusMultipliers[level - 1];
        }

        /// <summary> 레벨 1 보너스를 효과 레벨에 맞게 키운다, 변형의 고정 수치에는 쓰지 않는다 </summary>
        public float ScaleBonus(float levelOneBonus, int level)
        {
            return levelOneBonus * GetLevelMultiplier(level);
        }

        protected override void ValidateValues(List<string> errors)
        {
            const string OWNER = nameof(TalismanCatalogSO);

            DataValidation.RequireDefinitions(errors, OWNER, nameof(_effects), _effects);
            DataValidation.RequireCount(errors, OWNER, nameof(_effects), _effects, EFFECT_COUNT);

            ValidateLevelMultipliers(errors, OWNER);
            ValidateEffects(errors, OWNER);
            ValidateClassCounts(errors, OWNER);
        }

        // 대상 기술이 있는지, 직업이 맞는지, 대표 기술이면 단계마다 A, B 두 후보가 있는지 본다
        public override void ValidateReferences(GameDatabaseSO root, List<string> errors)
        {
            const string OWNER = nameof(TalismanCatalogSO);

            SkillCatalogSO skills = root.Skills;
            if ((skills == null) || (_effects == null))
            {
                return;
            }

            for (int i = 0; i < _effects.Length; ++i)
            {
                TalismanEffectDefinition effect = _effects[i];
                if (effect == null)
                {
                    continue;
                }

                string field  = $"{nameof(_effects)}[{effect.DefinitionId}].SkillId";
                bool   exists = skills.ContainsSkill(effect.SkillId);
                DataValidation.RequireReference(errors, OWNER, field, effect.SkillId, exists);

                if ((exists) && (skills.TryGetSkill(effect.SkillId, out SkillCatalogSO.SkillDefinition skill)) && (skill.PlayerClass != effect.PlayerClass))
                {
                    errors.Add($"{OWNER}.{field}: 효과 직업 {effect.PlayerClass} != 기술 직업 {skill.PlayerClass}");
                }
            }

            IReadOnlyList<SkillCatalogSO.SkillDefinition> skillList = skills.Skills;
            for (int i = 0; i < skillList.Count; ++i)
            {
                SkillCatalogSO.SkillDefinition skill = skillList[i];
                if (skill == null)
                {
                    continue;
                }

                int expected = skill.IsSignature ? SIGNATURE_CHOICE_COUNT : NORMAL_CHOICE_COUNT;
                for (int tier = 0; tier < Constants.Combat.TALISMAN_TIERS_PER_SKILL; ++tier)
                {
                    int count = CountEffects(skill.DefinitionId, (ETalismanTier)tier, out bool hasChoiceA, out bool hasChoiceB);
                    if ((count != expected) || (!hasChoiceA) || ((expected == SIGNATURE_CHOICE_COUNT) && (!hasChoiceB)))
                    {
                        errors.Add($"{OWNER}.{nameof(_effects)}[{skill.DefinitionId}][{(ETalismanTier)tier}]: 후보 {count}개, A부터 {expected}개여야 한다");
                    }
                }
            }
        }

        private void ValidateLevelMultipliers(List<string> errors, string owner)
        {
            DataValidation.RequireCount(errors, owner, nameof(_levelBonusMultipliers), _levelBonusMultipliers, MAX_EFFECT_LEVEL);
            if ((_levelBonusMultipliers == null) || (_levelBonusMultipliers.Length == 0))
            {
                return;
            }

            DataValidation.RequireRange(errors, owner, nameof(_levelBonusMultipliers) + "[0]", _levelBonusMultipliers[0],
                FIRST_LEVEL_MULTIPLIER, FIRST_LEVEL_MULTIPLIER);

            for (int i = 1; i < _levelBonusMultipliers.Length; ++i)
            {
                if (_levelBonusMultipliers[i] <= _levelBonusMultipliers[i - 1])
                {
                    errors.Add($"{owner}.{nameof(_levelBonusMultipliers)}[{i}] = {_levelBonusMultipliers[i]}: 이전 레벨보다 커야 한다");
                }
            }
        }

        private void ValidateEffects(List<string> errors, string owner)
        {
            if (_effects == null)
            {
                return;
            }

            float maxMultiplier = ((_levelBonusMultipliers == null) || (_levelBonusMultipliers.Length == 0))
                ? FIRST_LEVEL_MULTIPLIER
                : _levelBonusMultipliers[_levelBonusMultipliers.Length - 1];

            var idPattern = new Regex(EFFECT_ID_PATTERN);
            for (int i = 0; i < _effects.Length; ++i)
            {
                TalismanEffectDefinition effect = _effects[i];
                if (effect == null)
                {
                    continue;
                }

                string field = $"{nameof(_effects)}[{effect.DefinitionId}]";

                ValidateIdMatchesSlot(errors, owner, field, effect, idPattern);

                if (string.IsNullOrWhiteSpace(effect.SkillId))
                {
                    errors.Add($"{owner}.{field}.SkillId: 대상 기술 ID가 비어 있다");
                }

                DataValidation.RequirePositive(errors, owner, field + ".LevelOneBonus", effect.LevelOneBonus);

                bool isReductionRate = (effect.UpgradeStat == ETalismanUpgradeStat.CooldownRate)
                    || (effect.UpgradeStat == ETalismanUpgradeStat.EnergyCostRate)
                    || (effect.UpgradeStat == ETalismanUpgradeStat.AttackIntervalRate);
                if ((isReductionRate) && ((effect.LevelOneBonus * maxMultiplier) >= MAX_REDUCTION_RATE))
                {
                    errors.Add($"{owner}.{field}.LevelOneBonus = {effect.LevelOneBonus}: 최고 레벨 감소율이 {MAX_REDUCTION_RATE} 이상이 된다");
                }

                bool isCoreKind = IsCoreKind(effect.Kind);
                if (isCoreKind != (effect.Tier == ETalismanTier.Core))
                {
                    errors.Add($"{owner}.{field}: 효과 종류 {effect.Kind}와 단계 {effect.Tier}가 맞지 않는다");
                }

                if (effect.Variant == null)
                {
                    errors.Add($"{owner}.{field}.Variant: 변형 수치가 비어 있다");
                    continue;
                }

                ValidateVariant(errors, owner, field + ".Variant", effect.Kind, effect.Variant);
            }
        }

        // ID의 단계 숫자와 선택 문자가 필드 값과 어긋나면 저장 데이터가 다른 효과를 가리킨다
        private static void ValidateIdMatchesSlot(List<string> errors, string owner, string field, TalismanEffectDefinition effect, Regex idPattern)
        {
            Match match = idPattern.Match(effect.DefinitionId ?? string.Empty);
            if (!match.Success)
            {
                errors.Add($"{owner}.{field}: ID 형식이 T00-0A가 아니다");
                return;
            }

            int  tierNumber    = match.Groups[TIER_GROUP].Value[0] - '1';
            int  choiceNumber  = match.Groups[CHOICE_GROUP].Value[0] - 'A';
            bool tierMatches   = (tierNumber == (int)effect.Tier);
            bool choiceMatches = (choiceNumber == (int)effect.Choice);
            if ((!tierMatches) || (!choiceMatches))
            {
                errors.Add($"{owner}.{field}: ID와 단계 {effect.Tier}, 선택 {effect.Choice}가 맞지 않는다");
            }
        }

        private static void ValidateVariant(List<string> errors, string owner, string field, ETalismanEffectKind kind, TalismanVariantSpec variant)
        {
            DataValidation.RequireNonNegative(errors, owner, field + ".BaseCoefficient", variant.BaseCoefficient);
            DataValidation.RequireRange(errors, owner, field + ".FixedArcAngle", variant.FixedArcAngle, 0f, MAX_ARC_ANGLE);
            DataValidation.RequireNonNegative(errors, owner, field + ".FixedRadius", variant.FixedRadius);
            DataValidation.RequireNonNegative(errors, owner, field + ".AltShapeBonus", variant.AltShapeBonus);
            DataValidation.RequireNonNegative(errors, owner, field + ".Length", variant.Length);
            DataValidation.RequireNonNegative(errors, owner, field + ".Width", variant.Width);
            DataValidation.RequireNonNegative(errors, owner, field + ".Delay", variant.Delay);
            DataValidation.RequireNonNegative(errors, owner, field + ".ProjectileCount", variant.ProjectileCount);
            DataValidation.RequireNonNegative(errors, owner, field + ".SpreadAngle", variant.SpreadAngle);
            DataValidation.RequireNonNegative(errors, owner, field + ".MaxExtraTargets", variant.MaxExtraTargets);

            switch (kind)
            {
                case ETalismanEffectKind.FocusArc:
                    DataValidation.RequirePositiveAtMost(errors, owner, field + ".FixedArcAngle", variant.FixedArcAngle, MAX_ARC_ANGLE);
                    break;
                case ETalismanEffectKind.FocusRadius:
                case ETalismanEffectKind.CircleSweep:
                    DataValidation.RequirePositive(errors, owner, field + ".FixedRadius", variant.FixedRadius);
                    break;
                case ETalismanEffectKind.FollowUpStrike:
                case ETalismanEffectKind.Detonate:
                    DataValidation.RequirePositive(errors, owner, field + ".Delay", variant.Delay);
                    break;
                case ETalismanEffectKind.Shockwave:
                    DataValidation.RequirePositive(errors, owner, field + ".FixedRadius", variant.FixedRadius);
                    DataValidation.RequirePositive(errors, owner, field + ".Delay", variant.Delay);
                    break;
                case ETalismanEffectKind.CounterWave:
                    DataValidation.RequirePositive(errors, owner, field + ".Length", variant.Length);
                    DataValidation.RequirePositive(errors, owner, field + ".Width", variant.Width);
                    break;
                case ETalismanEffectKind.SpreadShot:
                    DataValidation.RequirePositive(errors, owner, field + ".ProjectileCount", variant.ProjectileCount);
                    DataValidation.RequirePositive(errors, owner, field + ".SpreadAngle", variant.SpreadAngle);
                    break;
                case ETalismanEffectKind.SplitTargets:
                    DataValidation.RequirePositive(errors, owner, field + ".ProjectileCount", variant.ProjectileCount);
                    break;
                case ETalismanEffectKind.PierceShot:
                    DataValidation.RequirePositive(errors, owner, field + ".MaxExtraTargets", variant.MaxExtraTargets);
                    break;
                default:
                    break;
            }
        }

        private void ValidateClassCounts(List<string> errors, string owner)
        {
            if (_effects == null)
            {
                return;
            }

            int classCount = Enum.GetValues(typeof(EPlayerClass)).Length;
            for (int c = 0; c < classCount; ++c)
            {
                var playerClass = (EPlayerClass)c;
                int count       = 0;
                for (int i = 0; i < _effects.Length; ++i)
                {
                    if ((_effects[i] != null) && (_effects[i].PlayerClass == playerClass))
                    {
                        ++count;
                    }
                }

                if (count != EFFECTS_PER_CLASS)
                {
                    errors.Add($"{owner}.{nameof(_effects)}[{playerClass}]: 효과 수 {count} != {EFFECTS_PER_CLASS}");
                }
            }
        }

        private int CountEffects(string skillId, ETalismanTier tier, out bool hasChoiceA, out bool hasChoiceB)
        {
            hasChoiceA = false;
            hasChoiceB = false;
            int count  = 0;

            for (int i = 0; i < _effects.Length; ++i)
            {
                TalismanEffectDefinition effect = _effects[i];
                if ((effect == null) || (effect.SkillId != skillId) || (effect.Tier != tier))
                {
                    continue;
                }

                ++count;
                hasChoiceA |= (effect.Choice == ETalismanChoice.A);
                hasChoiceB |= (effect.Choice == ETalismanChoice.B);
            }

            return count;
        }

        private static bool IsCoreKind(ETalismanEffectKind kind)
        {
            return kind >= ETalismanEffectKind.FollowUpStrike;
        }
    }
}
