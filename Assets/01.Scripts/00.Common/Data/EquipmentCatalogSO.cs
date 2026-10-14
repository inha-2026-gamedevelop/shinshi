// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> 장비 6부위와 4등급의 기본 수치, 제작, 강화, 계승표 </summary>
    [CreateAssetMenu(fileName = "EquipmentCatalog", menuName = "Shinshi/EquipmentCatalogSO")]
    public class EquipmentCatalogSO : GameDataSO
    {
        /// <summary> 장비 한 종류의 정의, 부위와 등급, 제작 재료 </summary>
        [Serializable]
        public class EquipmentDefinition : IDefinition
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private string         _definitionId;
            [SerializeField] private string         _displayKey;
            [SerializeField] private EEquipmentSlot _slot;
            [SerializeField] private EItemGrade     _grade;
            [SerializeField] private bool           _isTraining;           // 신규 캐릭터 지급 수련용, 제작, 분해, 거래 불가
            [SerializeField] private string         _bossMaterialId;       // 해당 등급 일반 보스 재료, 수련용은 빈 문자열
            [SerializeField] private int            _bossMaterialCount;
            [SerializeField] private string         _auctionMaterialId;    // 경매 전용 희귀 장신구 재료, 장신구만 쓴다
            [SerializeField] private int            _auctionMaterialCount;

            public string         DefinitionId         => _definitionId;
            public string         DisplayKey           => _displayKey;
            public EEquipmentSlot Slot                 => _slot;
            public EItemGrade     Grade                => _grade;
            public bool           IsTraining           => _isTraining;
            public string         BossMaterialId       => _bossMaterialId;
            public int            BossMaterialCount    => _bossMaterialCount;
            public string         AuctionMaterialId    => _auctionMaterialId;
            public int            AuctionMaterialCount => _auctionMaterialCount;
            public bool           NeedsAuctionMaterial => _auctionMaterialCount > 0;

            /****************************************
            *                Methods
            ****************************************/

            public EquipmentDefinition(string definitionId, EEquipmentSlot slot, EItemGrade grade, bool isTraining,
                string bossMaterialId, int bossMaterialCount, string auctionMaterialId, int auctionMaterialCount)
            {
                _definitionId         = definitionId;
                _displayKey           = definitionId + DISPLAY_KEY_SUFFIX;
                _slot                 = slot;
                _grade                = grade;
                _isTraining           = isTraining;
                _bossMaterialId       = bossMaterialId;
                _bossMaterialCount    = bossMaterialCount;
                _auctionMaterialId    = auctionMaterialId;
                _auctionMaterialCount = auctionMaterialCount;
            }

            public static EquipmentDefinition Crafted(string definitionId, EEquipmentSlot slot, EItemGrade grade,
                string bossMaterialId, int bossMaterialCount)
            {
                return new EquipmentDefinition(definitionId, slot, grade, false, bossMaterialId, bossMaterialCount, string.Empty, 0);
            }

            public static EquipmentDefinition CraftedAccessory(string definitionId, EItemGrade grade,
                string bossMaterialId, int bossMaterialCount, string auctionMaterialId)
            {
                return new EquipmentDefinition(definitionId, EEquipmentSlot.Accessory, grade, false,
                    bossMaterialId, bossMaterialCount, auctionMaterialId, ACCESSORY_AUCTION_MATERIAL_COUNT);
            }

            public static EquipmentDefinition Training(string definitionId, EEquipmentSlot slot)
            {
                return new EquipmentDefinition(definitionId, slot, EItemGrade.Common, true, string.Empty, 0, string.Empty, 0);
            }
        }

        /// <summary> 부위별 일반 제작 장비의 강화 전 기본값 </summary>
        [Serializable]
        public class SlotBaseStats
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private EEquipmentSlot _slot;
            [SerializeField] private float          _attack;
            [SerializeField] private float          _health;
            [SerializeField] private float          _defense;
            [SerializeField] private float          _critChance; // 0에서 1 사이 비율, 등급과 강화 배율을 곱하지 않는 고정 옵션
            [SerializeField] private float          _lifeSteal;  // 0에서 1 사이 비율, 일반 등급의 생명력 흡수. 0이면 이 부위는 등급이 올라도 흡수가 없다

            public EEquipmentSlot Slot       => _slot;
            public float          Attack     => _attack;
            public float          Health     => _health;
            public float          Defense    => _defense;
            public float          CritChance => _critChance;
            public float          LifeSteal  => _lifeSteal;

            /****************************************
            *                Methods
            ****************************************/

            public SlotBaseStats(EEquipmentSlot slot, float attack, float health, float defense, float critChance,
                float lifeSteal = 0f)
            {
                _slot       = slot;
                _attack     = attack;
                _health     = health;
                _defense    = defense;
                _critChance = critChance;
                _lifeSteal  = lifeSteal;
            }
        }

        /// <summary> 강화 목표 단계 하나의 시도 비용과 성공률 </summary>
        [Serializable]
        public class EnhancementStep
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private int   _essenceCost;     // 정기, 시도마다
            [SerializeField] private int   _shardCost;       // 정련편, 시도마다
            [SerializeField] private float _baseSuccessRate; // 0에서 1 사이, 실패 기록이 없을 때
            [SerializeField] private float _failBonusRate;   // 실패 1회마다 더하는 성공률

            public int   EssenceCost     => _essenceCost;
            public int   ShardCost       => _shardCost;
            public float BaseSuccessRate => _baseSuccessRate;
            public float FailBonusRate   => _failBonusRate;

            /****************************************
            *                Methods
            ****************************************/

            public EnhancementStep(int essenceCost, int shardCost, float baseSuccessRate, float failBonusRate)
            {
                _essenceCost     = essenceCost;
                _shardCost       = shardCost;
                _baseSuccessRate = baseSuccessRate;
                _failBonusRate   = failBonusRate;
            }

            /// <summary> 캐릭터, 부위, 현재 단계에 쌓인 실패 횟수를 반영한 성공률 </summary>
            public float GetSuccessRate(int failCount)
            {
                if (failCount < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(failCount), "실패 횟수는 0 이상이어야 한다");
                }

                // 0.1을 여러 번 더하면 float 오차로 1에 못 미쳐 확정 성공 시도가 한 번 밀린다
                float rate = _baseSuccessRate + (_failBonusRate * failCount);
                if (rate >= (MAX_RATE - RATE_EPSILON))
                {
                    return MAX_RATE;
                }

                return rate;
            }
        }

        /// <summary> 부위 하나에서 나올 수 있는 옵션 종류 </summary>
        [Serializable]
        public class OptionSlotPool
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private EEquipmentSlot         _slot;
            [SerializeField] private EEquipmentOptionKind[] _kinds;

            public EEquipmentSlot                      Slot  => _slot;
            public IReadOnlyList<EEquipmentOptionKind> Kinds => _kinds;

            /****************************************
            *                Methods
            ****************************************/

            public OptionSlotPool(EEquipmentSlot slot, params EEquipmentOptionKind[] kinds)
            {
                _slot  = slot;
                _kinds = kinds;
            }
        }

        /// <summary> 옵션 종류 하나의 등급별 값 범위, 값은 만분율 정수다 </summary>
        [Serializable]
        public class OptionRange
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private EEquipmentOptionKind _kind;
            [SerializeField] private int[]                _min; // EItemGrade 순서
            [SerializeField] private int[]                _max; // EItemGrade 순서

            public EEquipmentOptionKind Kind => _kind;
            public IReadOnlyList<int>   Min  => _min;
            public IReadOnlyList<int>   Max  => _max;

            /****************************************
            *                Methods
            ****************************************/

            public OptionRange(EEquipmentOptionKind kind, int[] min, int[] max)
            {
                _kind = kind;
                _min  = min;
                _max  = max;
            }
        }

        /// <summary> 상위 등급 계승의 원본 단계와 결과 단계 </summary>
        [Serializable]
        public class InheritanceRow
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private int _sourceLevel;
            [SerializeField] private int _resultLevel;

            public int SourceLevel => _sourceLevel;
            public int ResultLevel => _resultLevel;

            /****************************************
            *                Methods
            ****************************************/

            public InheritanceRow(int sourceLevel, int resultLevel)
            {
                _sourceLevel = sourceLevel;
                _resultLevel = resultLevel;
            }
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string DISPLAY_KEY_SUFFIX = ".name";

        private const int   SLOT_COUNT                       = 6;
        private const int   GRADE_COUNT                      = 4;
        private const int   DEFINITION_COUNT                 = (SLOT_COUNT * GRADE_COUNT) + SLOT_COUNT; // 제작 24, 수련용 6
        private const int   MAX_ENHANCEMENT                  = 25;
        private const int   UPPER_INHERITANCE_ROW_COUNT      = 9;
        private const int   ACCESSORY_AUCTION_MATERIAL_COUNT = 1;
        private const float MAX_RATE                         = 1f;
        private const float RATE_EPSILON                     = 0.0001f;
        private const float BASE_STAT_SCALE                  = 1f; // 강화 +0의 기본값 배율, 계승 전후 비율도 이 값을 넘어야 한다

        private const string BOSS_MATERIAL_COMMON       = "item_boss_material_common";
        private const string BOSS_MATERIAL_RARE         = "item_boss_material_rare";
        private const string BOSS_MATERIAL_EPIC         = "item_boss_material_epic";
        private const string BOSS_MATERIAL_LEGENDARY    = "item_boss_material_legendary";
        private const string AUCTION_MATERIAL_COMMON    = "item_auction_material_common";
        private const string AUCTION_MATERIAL_RARE      = "item_auction_material_rare";
        private const string AUCTION_MATERIAL_EPIC      = "item_auction_material_epic";
        private const string AUCTION_MATERIAL_LEGENDARY = "item_auction_material_legendary";

        /****************************************
        *                Fields
        ****************************************/

        // 제작 재료는 정기나 이전 장비 없이 보스 재료만 쓴다
        [Header("장비 정의")]
        [SerializeField] private EquipmentDefinition[] _equipment =
        {
            EquipmentDefinition.Crafted("equip_weapon_common", EEquipmentSlot.Weapon, EItemGrade.Common, BOSS_MATERIAL_COMMON, 4),
            EquipmentDefinition.Crafted("equip_weapon_rare", EEquipmentSlot.Weapon, EItemGrade.Rare, BOSS_MATERIAL_RARE, 6),
            EquipmentDefinition.Crafted("equip_weapon_epic", EEquipmentSlot.Weapon, EItemGrade.Epic, BOSS_MATERIAL_EPIC, 8),
            EquipmentDefinition.Crafted("equip_weapon_legendary", EEquipmentSlot.Weapon, EItemGrade.Legendary, BOSS_MATERIAL_LEGENDARY, 10),

            EquipmentDefinition.Crafted("equip_head_common", EEquipmentSlot.Head, EItemGrade.Common, BOSS_MATERIAL_COMMON, 3),
            EquipmentDefinition.Crafted("equip_head_rare", EEquipmentSlot.Head, EItemGrade.Rare, BOSS_MATERIAL_RARE, 4),
            EquipmentDefinition.Crafted("equip_head_epic", EEquipmentSlot.Head, EItemGrade.Epic, BOSS_MATERIAL_EPIC, 6),
            EquipmentDefinition.Crafted("equip_head_legendary", EEquipmentSlot.Head, EItemGrade.Legendary, BOSS_MATERIAL_LEGENDARY, 8),

            EquipmentDefinition.Crafted("equip_body_common", EEquipmentSlot.Body, EItemGrade.Common, BOSS_MATERIAL_COMMON, 4),
            EquipmentDefinition.Crafted("equip_body_rare", EEquipmentSlot.Body, EItemGrade.Rare, BOSS_MATERIAL_RARE, 6),
            EquipmentDefinition.Crafted("equip_body_epic", EEquipmentSlot.Body, EItemGrade.Epic, BOSS_MATERIAL_EPIC, 8),
            EquipmentDefinition.Crafted("equip_body_legendary", EEquipmentSlot.Body, EItemGrade.Legendary, BOSS_MATERIAL_LEGENDARY, 10),

            EquipmentDefinition.Crafted("equip_hands_common", EEquipmentSlot.Hands, EItemGrade.Common, BOSS_MATERIAL_COMMON, 3),
            EquipmentDefinition.Crafted("equip_hands_rare", EEquipmentSlot.Hands, EItemGrade.Rare, BOSS_MATERIAL_RARE, 4),
            EquipmentDefinition.Crafted("equip_hands_epic", EEquipmentSlot.Hands, EItemGrade.Epic, BOSS_MATERIAL_EPIC, 6),
            EquipmentDefinition.Crafted("equip_hands_legendary", EEquipmentSlot.Hands, EItemGrade.Legendary, BOSS_MATERIAL_LEGENDARY, 8),

            EquipmentDefinition.Crafted("equip_feet_common", EEquipmentSlot.Feet, EItemGrade.Common, BOSS_MATERIAL_COMMON, 3),
            EquipmentDefinition.Crafted("equip_feet_rare", EEquipmentSlot.Feet, EItemGrade.Rare, BOSS_MATERIAL_RARE, 4),
            EquipmentDefinition.Crafted("equip_feet_epic", EEquipmentSlot.Feet, EItemGrade.Epic, BOSS_MATERIAL_EPIC, 6),
            EquipmentDefinition.Crafted("equip_feet_legendary", EEquipmentSlot.Feet, EItemGrade.Legendary, BOSS_MATERIAL_LEGENDARY, 8),

            EquipmentDefinition.CraftedAccessory("equip_accessory_common", EItemGrade.Common, BOSS_MATERIAL_COMMON, 3, AUCTION_MATERIAL_COMMON),
            EquipmentDefinition.CraftedAccessory("equip_accessory_rare", EItemGrade.Rare, BOSS_MATERIAL_RARE, 4, AUCTION_MATERIAL_RARE),
            EquipmentDefinition.CraftedAccessory("equip_accessory_epic", EItemGrade.Epic, BOSS_MATERIAL_EPIC, 6, AUCTION_MATERIAL_EPIC),
            EquipmentDefinition.CraftedAccessory("equip_accessory_legendary", EItemGrade.Legendary, BOSS_MATERIAL_LEGENDARY, 8, AUCTION_MATERIAL_LEGENDARY),

            EquipmentDefinition.Training("equip_weapon_training", EEquipmentSlot.Weapon),
            EquipmentDefinition.Training("equip_head_training", EEquipmentSlot.Head),
            EquipmentDefinition.Training("equip_body_training", EEquipmentSlot.Body),
            EquipmentDefinition.Training("equip_hands_training", EEquipmentSlot.Hands),
            EquipmentDefinition.Training("equip_feet_training", EEquipmentSlot.Feet),
            EquipmentDefinition.Training("equip_accessory_training", EEquipmentSlot.Accessory)
        };

        // EEquipmentSlot 값을 인덱스로 쓴다
        [Header("부위별 일반 제작 기본값 (EEquipmentSlot 순서)")]
        [SerializeField] private SlotBaseStats[] _slotBaseStats =
        {
            new SlotBaseStats(EEquipmentSlot.Weapon, 200f, 0f, 0f, 0f),
            new SlotBaseStats(EEquipmentSlot.Head, 0f, 180f, 20f, 0f),
            new SlotBaseStats(EEquipmentSlot.Body, 0f, 300f, 40f, 0f),
            new SlotBaseStats(EEquipmentSlot.Hands, 0f, 120f, 20f, 0f),
            new SlotBaseStats(EEquipmentSlot.Feet, 0f, 150f, 20f, 0f),
            new SlotBaseStats(EEquipmentSlot.Accessory, 40f, 150f, 0f, 0.02f, 0.01f)
        };

        // EItemGrade 값을 인덱스로 쓰고 공격, 생명, 방어에만 곱한다
        [Header("등급과 강화 배율")]
        [SerializeField] private float[] _gradeStatMultipliers = { 1f, 1.35f, 1.8225f, 2.460375f };
        [SerializeField] private float   _enhancementStatRate  = 0.04f; // 강화 1단계마다 기본값에 더하는 비율
        [SerializeField] private float   _trainingStatRate     = 0.5f;  // 수련용 = 같은 부위 일반 제작값의 비율, 장신구 치명과 흡수 포함

        // 등급 배율을 곱하지 않고 더한다. 기본 흡수가 0인 부위에는 더하지 않으며 강화와도 무관하다
        [SerializeField] private float   _lifeStealPerGrade    = 0.005f; // 등급 한 단계마다 기본 흡수에 더하는 비율

        // 인덱스 0이 목표 +1이다. 비용은 등급, 부위와 무관하게 같아야 싼 장비로 보정을 쌓아 옮기는 차익이 생기지 않는다
        [Header("강화 (목표 +1부터 +25 순서)")]
        [SerializeField] private EnhancementStep[] _enhancementSteps =
        {
            new EnhancementStep(150, 2, 1f, 0f),
            new EnhancementStep(200, 2, 1f, 0f),
            new EnhancementStep(250, 2, 1f, 0f),
            new EnhancementStep(300, 2, 1f, 0f),
            new EnhancementStep(350, 2, 1f, 0f),
            new EnhancementStep(400, 3, 0.8f, 0.1f),
            new EnhancementStep(450, 3, 0.8f, 0.1f),
            new EnhancementStep(500, 3, 0.8f, 0.1f),
            new EnhancementStep(550, 3, 0.8f, 0.1f),
            new EnhancementStep(600, 3, 0.8f, 0.1f),
            new EnhancementStep(650, 4, 0.65f, 0.1f),
            new EnhancementStep(700, 4, 0.65f, 0.1f),
            new EnhancementStep(750, 4, 0.65f, 0.1f),
            new EnhancementStep(800, 4, 0.65f, 0.1f),
            new EnhancementStep(850, 4, 0.65f, 0.1f),
            new EnhancementStep(900, 5, 0.5f, 0.1f),
            new EnhancementStep(950, 5, 0.5f, 0.1f),
            new EnhancementStep(1000, 5, 0.3f, 0.1f),
            new EnhancementStep(1050, 5, 0.3f, 0.1f),
            new EnhancementStep(1100, 5, 0.3f, 0.1f),
            new EnhancementStep(1150, 6, 0.2f, 0.1f),
            new EnhancementStep(1200, 6, 0.2f, 0.1f),
            new EnhancementStep(1250, 6, 0.2f, 0.1f),
            new EnhancementStep(1300, 6, 0.1f, 0.1f),
            new EnhancementStep(1350, 6, 0.1f, 0.1f)
        };

        [Header("계승")]
        [SerializeField] private int _sameGradeInheritCost  = 500;  // 정기, 원본 단계를 그대로 옮긴다
        [SerializeField] private int _upperGradeInheritCost = 1500; // 정기, 바로 한 등급 위로만

        // 원본 단계 오름차순으로 빈틈 없이 +25까지 둔다. 표에 없는 원본 단계는 상위 계승 불가다
        [SerializeField] private InheritanceRow[] _upperGradeInheritance =
        {
            new InheritanceRow(17, 10),
            new InheritanceRow(18, 11),
            new InheritanceRow(19, 12),
            new InheritanceRow(20, 13),
            new InheritanceRow(21, 14),
            new InheritanceRow(22, 15),
            new InheritanceRow(23, 16),
            new InheritanceRow(24, 18),
            new InheritanceRow(25, 20)
        };

        // 옵션은 장비마다 두 줄이고 재설정하면 두 줄을 함께 새로 뽑는다. 값은 만분율 정수이며 수련과 같은 단계에서 더한다.
        // 비용은 등급, 부위와 무관하게 같다. 상급 줄 없이 연속으로 뽑은 횟수가 천장에 닿으면 첫 줄의 값을 최고로 확정한다
        [Header("옵션 재설정")]
        [SerializeField] private int _optionRerollEssenceCost   = 300;  // 정기, 재설정마다
        [SerializeField] private int _optionRerollShardCost     = 3;    // 정련편, 재설정마다
        [SerializeField] private int _optionPityThreshold       = 10;   // 이 횟수째 재설정에서 첫 줄 값을 최고로 확정한다
        [SerializeField] private int _optionHighTierBasisPoints = 7500; // 만분율, 범위에서 이 비율 이상이면 상급 줄
        [SerializeField] private int _optionValueStep           = 10;   // 만분율, 값이 나오는 간격

        // EEquipmentSlot 값을 인덱스로 쓴다
        [SerializeField] private OptionSlotPool[] _optionSlotPools =
        {
            new OptionSlotPool(EEquipmentSlot.Weapon, EEquipmentOptionKind.AttackRate, EEquipmentOptionKind.CritChance),
            new OptionSlotPool(EEquipmentSlot.Head, EEquipmentOptionKind.HealthRate, EEquipmentOptionKind.DefenseRate),
            new OptionSlotPool(EEquipmentSlot.Body, EEquipmentOptionKind.HealthRate, EEquipmentOptionKind.DefenseRate),
            new OptionSlotPool(EEquipmentSlot.Hands, EEquipmentOptionKind.HealthRate, EEquipmentOptionKind.DefenseRate),
            new OptionSlotPool(EEquipmentSlot.Feet, EEquipmentOptionKind.HealthRate, EEquipmentOptionKind.DefenseRate),
            new OptionSlotPool(EEquipmentSlot.Accessory, EEquipmentOptionKind.AttackRate, EEquipmentOptionKind.HealthRate,
                EEquipmentOptionKind.DefenseRate, EEquipmentOptionKind.CritChance)
        };

        // 종류 1부터 순서대로 둔다. 치명 확률은 %p라 비율 옵션의 절반 수준이다
        [SerializeField] private OptionRange[] _optionRanges =
        {
            new OptionRange(EEquipmentOptionKind.AttackRate, new[] { 50, 100, 150, 200 }, new[] { 100, 200, 300, 400 }),
            new OptionRange(EEquipmentOptionKind.HealthRate, new[] { 50, 100, 150, 200 }, new[] { 100, 200, 300, 400 }),
            new OptionRange(EEquipmentOptionKind.DefenseRate, new[] { 50, 100, 150, 200 }, new[] { 100, 200, 300, 400 }),
            new OptionRange(EEquipmentOptionKind.CritChance, new[] { 20, 50, 70, 100 }, new[] { 50, 100, 150, 200 })
        };

        // 경매 재료, 강화와 계승 비용은 돌려주지 않는다
        [Header("분해")]
        [SerializeField] private float _disassembleRefundRate = 0.25f; // 일반 보스 재료 반환 비율, 버림

        // 장비는 수량형이 아니라 개체 하나가 한 칸이라 아이템 스택 상한이 아닌 칸 수로 둔다
        [Header("보관")]
        [SerializeField] private int _storageSlotsPerCharacter = 100; // 칸, 캐릭터별, 착용 중인 6부위는 세지 않는다

        [NonSerialized] private DefinitionLookup<EquipmentDefinition> _lookup = new DefinitionLookup<EquipmentDefinition>();

        // 판정과 검증이 정수 목록을 받으므로 부위별 후보를 한 번만 정수 배열로 바꿔 둔다
        [NonSerialized] private int[][] _optionPoolCache;

        public int OptionRerollEssenceCost   => _optionRerollEssenceCost;
        public int OptionRerollShardCost     => _optionRerollShardCost;
        public int OptionPityThreshold       => _optionPityThreshold;
        public int OptionHighTierBasisPoints => _optionHighTierBasisPoints;
        public int OptionValueStep           => _optionValueStep;
        public IReadOnlyList<OptionSlotPool> OptionSlotPools => _optionSlotPools;
        public IReadOnlyList<OptionRange>    OptionRanges    => _optionRanges;

        public IReadOnlyList<EquipmentDefinition> Equipment                => _equipment;
        public IReadOnlyList<EnhancementStep>     EnhancementSteps         => _enhancementSteps;
        public IReadOnlyList<InheritanceRow>      UpperGradeInheritance    => _upperGradeInheritance;
        public int                                MaxEnhancement           => MAX_ENHANCEMENT;
        public float                              EnhancementStatRate      => _enhancementStatRate;
        public float                              TrainingStatRate         => _trainingStatRate;
        public float                              LifeStealPerGrade        => _lifeStealPerGrade;
        public int                                SameGradeInheritCost     => _sameGradeInheritCost;
        public int                                UpperGradeInheritCost    => _upperGradeInheritCost;
        public float                              DisassembleRefundRate    => _disassembleRefundRate;
        public int                                StorageSlotsPerCharacter => _storageSlotsPerCharacter;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _lookup.Invalidate();
            _optionPoolCache = null;
        }

        protected override void OnValidate()
        {
            _lookup.Invalidate();
            _optionPoolCache = null;
            base.OnValidate();
        }

        /****************************************
        *                Methods
        ****************************************/

        public bool ContainsEquipment(string definitionId)
        {
            return _lookup.Contains(_equipment, definitionId);
        }

        public bool TryGetEquipment(string definitionId, out EquipmentDefinition definition)
        {
            return _lookup.TryGet(_equipment, definitionId, out definition);
        }

        /// <summary> 부위와 등급의 제작 장비 정의, 수련용은 찾지 않는다 </summary>
        public bool TryGetCrafted(EEquipmentSlot slot, EItemGrade grade, out EquipmentDefinition definition)
        {
            for (int i = 0; i < _equipment.Length; ++i)
            {
                EquipmentDefinition item = _equipment[i];
                if ((item != null) && (!item.IsTraining) && (item.Slot == slot) && (item.Grade == grade))
                {
                    definition = item;
                    return true;
                }
            }

            definition = null;
            return false;
        }

        public bool TryGetTraining(EEquipmentSlot slot, out EquipmentDefinition definition)
        {
            for (int i = 0; i < _equipment.Length; ++i)
            {
                EquipmentDefinition item = _equipment[i];
                if ((item != null) && (item.IsTraining) && (item.Slot == slot))
                {
                    definition = item;
                    return true;
                }
            }

            definition = null;
            return false;
        }

        public SlotBaseStats GetSlotBase(EEquipmentSlot slot)
        {
            int index = (int)slot;
            if ((index < 0) || (index >= _slotBaseStats.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(slot), "기본값이 없는 부위다");
            }

            return _slotBaseStats[index];
        }

        public float GetGradeMultiplier(EItemGrade grade)
        {
            int index = (int)grade;
            if ((index < 0) || (index >= _gradeStatMultipliers.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(grade), "배율이 없는 등급이다");
            }

            return _gradeStatMultipliers[index];
        }

        /// <summary> 부위에서 나올 수 있는 옵션 종류를 EquipmentOptionRules의 KIND 값으로, 정의가 없으면 빈 목록 </summary>
        public IReadOnlyList<int> GetOptionPool(EEquipmentSlot slot)
        {
            if (_optionPoolCache == null)
            {
                _optionPoolCache = BuildOptionPoolCache();
            }

            int index = (int)slot;
            if ((index < 0) || (index >= _optionPoolCache.Length))
            {
                return Array.Empty<int>();
            }

            return _optionPoolCache[index];
        }

        /// <summary> 종류와 등급의 값 범위(만분율), 정의가 없으면 false </summary>
        public bool TryGetOptionRange(int kind, EItemGrade grade, out int min, out int max)
        {
            min = 0;
            max = 0;
            int gradeIndex = (int)grade;
            if ((_optionRanges == null) || (gradeIndex < 0))
            {
                return false;
            }

            for (int i = 0; i < _optionRanges.Length; ++i)
            {
                OptionRange range = _optionRanges[i];
                if ((range == null) || ((int)range.Kind != kind) || (range.Min == null) || (range.Max == null)
                    || (gradeIndex >= range.Min.Count) || (gradeIndex >= range.Max.Count))
                {
                    continue;
                }

                min = range.Min[gradeIndex];
                max = range.Max[gradeIndex];
                return true;
            }

            return false;
        }

        private int[][] BuildOptionPoolCache()
        {
            var cache = new int[SLOT_COUNT][];
            for (int i = 0; i < cache.Length; ++i)
            {
                cache[i] = Array.Empty<int>();
            }

            if (_optionSlotPools == null)
            {
                return cache;
            }

            for (int i = 0; i < _optionSlotPools.Length; ++i)
            {
                OptionSlotPool pool = _optionSlotPools[i];
                int            slot = (pool == null) ? -1 : (int)pool.Slot;
                if ((slot < 0) || (slot >= SLOT_COUNT) || (pool.Kinds == null))
                {
                    continue;
                }

                var kinds = new int[pool.Kinds.Count];
                for (int k = 0; k < kinds.Length; ++k)
                {
                    kinds[k] = (int)pool.Kinds[k];
                }

                cache[slot] = kinds;
            }

            return cache;
        }

        /// <summary> 목표 단계(현재 단계 + 1)의 시도 비용과 성공률, 1부터 MaxEnhancement까지 </summary>
        public EnhancementStep GetEnhancementStep(int targetLevel)
        {
            if ((targetLevel < 1) || (targetLevel > _enhancementSteps.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(targetLevel), "강화 목표 단계가 범위를 벗어났다");
            }

            return _enhancementSteps[targetLevel - 1];
        }

        /// <summary> 상위 등급 계승의 결과 단계, 표에 없는 원본 단계면 false </summary>
        public bool TryGetUpperInheritResult(int sourceLevel, out int resultLevel)
        {
            for (int i = 0; i < _upperGradeInheritance.Length; ++i)
            {
                InheritanceRow row = _upperGradeInheritance[i];
                if ((row != null) && (row.SourceLevel == sourceLevel))
                {
                    resultLevel = row.ResultLevel;
                    return true;
                }
            }

            resultLevel = 0;
            return false;
        }

        protected override void ValidateValues(List<string> errors)
        {
            const string OWNER = nameof(EquipmentCatalogSO);

            DataValidation.RequirePositiveAtMost(errors, OWNER, nameof(_trainingStatRate), _trainingStatRate, MAX_RATE);
            DataValidation.RequireRange(errors, OWNER, nameof(_disassembleRefundRate), _disassembleRefundRate, 0f, MAX_RATE);
            DataValidation.RequireRange(errors, OWNER, nameof(_lifeStealPerGrade), _lifeStealPerGrade, 0f, MAX_RATE);
            DataValidation.RequirePositive(errors, OWNER, nameof(_storageSlotsPerCharacter), _storageSlotsPerCharacter);

            ValidateDefinitions(errors);
            ValidateSlotBaseStats(errors);
            bool scaleValid = ValidateStatScales(errors);
            ValidateEnhancementSteps(errors);
            ValidateInheritance(errors, scaleValid);
            ValidateOptions(errors);
        }

        private void ValidateOptions(List<string> errors)
        {
            const string OWNER = nameof(EquipmentCatalogSO);

            DataValidation.RequirePositive(errors, OWNER, nameof(_optionRerollEssenceCost), _optionRerollEssenceCost);
            DataValidation.RequirePositive(errors, OWNER, nameof(_optionRerollShardCost), _optionRerollShardCost);
            DataValidation.RequirePositive(errors, OWNER, nameof(_optionPityThreshold), _optionPityThreshold);
            DataValidation.RequireRange(errors, OWNER, nameof(_optionHighTierBasisPoints), _optionHighTierBasisPoints, 0f,
                Shinshi.Contracts.EquipmentOptionRules.VALUE_SCALE);
            DataValidation.RequirePositive(errors, OWNER, nameof(_optionValueStep), _optionValueStep);
            DataValidation.RequireCount(errors, OWNER, nameof(_optionSlotPools), _optionSlotPools, SLOT_COUNT);
            DataValidation.RequireCount(errors, OWNER, nameof(_optionRanges), _optionRanges,
                Shinshi.Contracts.EquipmentOptionRules.KIND_MAX);
            if ((_optionRanges == null) || (_optionSlotPools == null))
            {
                return;
            }

            for (int i = 0; i < _optionRanges.Length; ++i)
            {
                OptionRange range = _optionRanges[i];
                string      field = $"{nameof(_optionRanges)}[{i}]";
                if ((range == null) || (range.Min == null) || (range.Max == null) || (range.Min.Count != GRADE_COUNT)
                    || (range.Max.Count != GRADE_COUNT))
                {
                    errors.Add($"{OWNER}.{field}: 등급 {GRADE_COUNT}개의 최소, 최대가 있어야 한다");
                    continue;
                }

                // 종류 1부터 빈틈 없이 두어야 내보낸 표와 백엔드 검증이 같은 순서를 본다
                if ((int)range.Kind != (i + 1))
                {
                    errors.Add($"{OWNER}.{field}: 종류 순서가 다르다 {range.Kind} != {(EEquipmentOptionKind)(i + 1)}");
                }

                for (int g = 0; g < GRADE_COUNT; ++g)
                {
                    if (!Shinshi.Contracts.EquipmentOptionRules.IsValidRange(range.Min[g], range.Max[g], _optionValueStep))
                    {
                        errors.Add($"{OWNER}.{field}[{(EItemGrade)g}]: 범위 {range.Min[g]}~{range.Max[g]}가 간격 {_optionValueStep}에 맞지 않는다");
                    }
                }
            }

            for (int i = 0; i < _optionSlotPools.Length; ++i)
            {
                OptionSlotPool pool  = _optionSlotPools[i];
                string         field = $"{nameof(_optionSlotPools)}[{(EEquipmentSlot)i}]";
                if ((pool == null) || (pool.Kinds == null) || (pool.Kinds.Count == 0))
                {
                    errors.Add($"{OWNER}.{field}: 후보가 비어 있다");
                    continue;
                }

                if ((int)pool.Slot != i)
                {
                    errors.Add($"{OWNER}.{field}: 부위 순서가 EEquipmentSlot과 다르다 {pool.Slot}");
                }

                for (int k = 0; k < pool.Kinds.Count; ++k)
                {
                    if (!TryGetOptionRange((int)pool.Kinds[k], EItemGrade.Common, out _, out _))
                    {
                        errors.Add($"{OWNER}.{field}: 범위 표에 없는 종류다 {pool.Kinds[k]}");
                    }
                }
            }
        }

        public override void ValidateReferences(GameDatabaseSO root, List<string> errors)
        {
            const string OWNER = nameof(EquipmentCatalogSO);

            RewardCatalogSO rewards = root.Rewards;
            if ((rewards == null) || (_equipment == null))
            {
                return;
            }

            for (int i = 0; i < _equipment.Length; ++i)
            {
                EquipmentDefinition item = _equipment[i];
                if ((item == null) || (item.IsTraining))
                {
                    continue;
                }

                string field = $"{nameof(_equipment)}[{item.DefinitionId}]";
                DataValidation.RequireReference(errors, OWNER, field + ".BossMaterialId", item.BossMaterialId, rewards.ContainsItem(item.BossMaterialId));

                if (item.NeedsAuctionMaterial)
                {
                    DataValidation.RequireReference(errors, OWNER, field + ".AuctionMaterialId", item.AuctionMaterialId, rewards.ContainsItem(item.AuctionMaterialId));
                }
            }
        }

        private void ValidateDefinitions(List<string> errors)
        {
            const string OWNER = nameof(EquipmentCatalogSO);

            DataValidation.RequireDefinitions(errors, OWNER, nameof(_equipment), _equipment);
            DataValidation.RequireCount(errors, OWNER, nameof(_equipment), _equipment, DEFINITION_COUNT);
            if (_equipment == null)
            {
                return;
            }

            var crafted  = new bool[SLOT_COUNT, GRADE_COUNT];
            var training = new bool[SLOT_COUNT];

            for (int i = 0; i < _equipment.Length; ++i)
            {
                EquipmentDefinition item = _equipment[i];
                if (item == null)
                {
                    continue;
                }

                string field = $"{nameof(_equipment)}[{item.DefinitionId}]";
                int    slot  = (int)item.Slot;
                int    grade = (int)item.Grade;

                if ((slot < 0) || (slot >= SLOT_COUNT) || (grade < 0) || (grade >= GRADE_COUNT))
                {
                    errors.Add($"{OWNER}.{field}: 부위 {item.Slot} 또는 등급 {item.Grade}가 범위를 벗어났다");
                    continue;
                }

                if (item.IsTraining)
                {
                    ValidateTrainingDefinition(errors, field, item);
                    if (training[slot])
                    {
                        errors.Add($"{OWNER}.{field}: {item.Slot} 수련용 장비가 중복된다");
                    }

                    training[slot] = true;
                    continue;
                }

                ValidateCraftedDefinition(errors, field, item);
                if (crafted[slot, grade])
                {
                    errors.Add($"{OWNER}.{field}: {item.Slot} {item.Grade} 제작 장비가 중복된다");
                }

                crafted[slot, grade] = true;
            }

            for (int s = 0; s < SLOT_COUNT; ++s)
            {
                if (!training[s])
                {
                    errors.Add($"{OWNER}.{nameof(_equipment)}: {(EEquipmentSlot)s} 수련용 장비가 없다");
                }

                for (int g = 0; g < GRADE_COUNT; ++g)
                {
                    if (!crafted[s, g])
                    {
                        errors.Add($"{OWNER}.{nameof(_equipment)}: {(EEquipmentSlot)s} {(EItemGrade)g} 제작 장비가 없다");
                    }
                }
            }
        }

        private static void ValidateTrainingDefinition(List<string> errors, string field, EquipmentDefinition item)
        {
            const string OWNER = nameof(EquipmentCatalogSO);

            if (item.Grade != EItemGrade.Common)
            {
                errors.Add($"{OWNER}.{field}: 수련용은 일반 등급이어야 한다");
            }

            if ((item.BossMaterialCount != 0) || (item.AuctionMaterialCount != 0))
            {
                errors.Add($"{OWNER}.{field}: 수련용은 제작 재료를 두지 않는다");
            }
        }

        private static void ValidateCraftedDefinition(List<string> errors, string field, EquipmentDefinition item)
        {
            const string OWNER = nameof(EquipmentCatalogSO);

            if (string.IsNullOrWhiteSpace(item.BossMaterialId))
            {
                errors.Add($"{OWNER}.{field}.BossMaterialId: 일반 보스 재료 ID가 비어 있다");
            }

            DataValidation.RequirePositive(errors, OWNER, field + ".BossMaterialCount", item.BossMaterialCount);

            // 경매 희귀 재료는 장신구에만 요구하고 다른 다섯 부위의 필수 재료로 넣지 않는다
            if (item.Slot != EEquipmentSlot.Accessory)
            {
                if (item.AuctionMaterialCount != 0)
                {
                    errors.Add($"{OWNER}.{field}: 장신구가 아닌 부위에 경매 재료를 요구한다");
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(item.AuctionMaterialId))
            {
                errors.Add($"{OWNER}.{field}.AuctionMaterialId: 장신구 경매 재료 ID가 비어 있다");
            }

            DataValidation.RequirePositive(errors, OWNER, field + ".AuctionMaterialCount", item.AuctionMaterialCount);
        }

        private void ValidateSlotBaseStats(List<string> errors)
        {
            const string OWNER = nameof(EquipmentCatalogSO);

            DataValidation.RequireCount(errors, OWNER, nameof(_slotBaseStats), _slotBaseStats, SLOT_COUNT);
            if ((_slotBaseStats == null) || (_slotBaseStats.Length != SLOT_COUNT))
            {
                return;
            }

            for (int i = 0; i < _slotBaseStats.Length; ++i)
            {
                SlotBaseStats stats = _slotBaseStats[i];
                string        field = $"{nameof(_slotBaseStats)}[{(EEquipmentSlot)i}]";

                if (stats == null)
                {
                    errors.Add($"{OWNER}.{field}: 기본값이 비어 있다");
                    continue;
                }

                if ((int)stats.Slot != i)
                {
                    errors.Add($"{OWNER}.{field}: 부위 순서가 EEquipmentSlot과 다르다 {stats.Slot}");
                }

                DataValidation.RequireNonNegative(errors, OWNER, field + ".Attack", stats.Attack);
                DataValidation.RequireNonNegative(errors, OWNER, field + ".Health", stats.Health);
                DataValidation.RequireNonNegative(errors, OWNER, field + ".Defense", stats.Defense);
                DataValidation.RequireRange(errors, OWNER, field + ".CritChance", stats.CritChance, 0f, MAX_RATE);
                DataValidation.RequireRange(errors, OWNER, field + ".LifeSteal", stats.LifeSteal, 0f, MAX_RATE);

                // 최고 등급에서도 비율 범위를 넘지 않아야 한다
                float topLifeSteal = stats.LifeSteal + (_lifeStealPerGrade * (GRADE_COUNT - 1));
                if ((stats.LifeSteal > 0f) && (topLifeSteal > MAX_RATE))
                {
                    errors.Add($"{OWNER}.{field}.LifeSteal: 최고 등급 흡수 {topLifeSteal}가 1을 넘는다");
                }

                if ((stats.Attack + stats.Health + stats.Defense) <= 0f)
                {
                    errors.Add($"{OWNER}.{field}: 공격, 생명, 방어가 모두 0이다");
                }
            }
        }

        // 계승 능력치 검사가 이 값들을 쓰므로 통과 여부를 돌려준다
        private bool ValidateStatScales(List<string> errors)
        {
            const string OWNER = nameof(EquipmentCatalogSO);

            int errorCountBefore = errors.Count;

            DataValidation.RequireCount(errors, OWNER, nameof(_gradeStatMultipliers), _gradeStatMultipliers, GRADE_COUNT);
            DataValidation.RequirePositive(errors, OWNER, nameof(_enhancementStatRate), _enhancementStatRate);

            if ((_gradeStatMultipliers == null) || (_gradeStatMultipliers.Length != GRADE_COUNT))
            {
                return false;
            }

            for (int i = 0; i < _gradeStatMultipliers.Length; ++i)
            {
                string field = $"{nameof(_gradeStatMultipliers)}[{(EItemGrade)i}]";
                DataValidation.RequirePositive(errors, OWNER, field, _gradeStatMultipliers[i]);

                if ((i > 0) && (_gradeStatMultipliers[i] <= _gradeStatMultipliers[i - 1]))
                {
                    errors.Add($"{OWNER}.{field}: 등급 배율이 아래 등급보다 커야 한다");
                }
            }

            return errors.Count == errorCountBefore;
        }

        private void ValidateEnhancementSteps(List<string> errors)
        {
            const string OWNER = nameof(EquipmentCatalogSO);

            DataValidation.RequireCount(errors, OWNER, nameof(_enhancementSteps), _enhancementSteps, MAX_ENHANCEMENT);
            if (_enhancementSteps == null)
            {
                return;
            }

            for (int i = 0; i < _enhancementSteps.Length; ++i)
            {
                EnhancementStep step  = _enhancementSteps[i];
                string          field = $"{nameof(_enhancementSteps)}[+{i + 1}]";

                if (step == null)
                {
                    errors.Add($"{OWNER}.{field}: 단계가 비어 있다");
                    continue;
                }

                DataValidation.RequirePositive(errors, OWNER, field + ".EssenceCost", step.EssenceCost);
                DataValidation.RequirePositive(errors, OWNER, field + ".ShardCost", step.ShardCost);
                DataValidation.RequirePositiveAtMost(errors, OWNER, field + ".BaseSuccessRate", step.BaseSuccessRate, MAX_RATE);
                DataValidation.RequireRange(errors, OWNER, field + ".FailBonusRate", step.FailBonusRate, 0f, MAX_RATE);

                // 일정 실패 뒤 확정 성공이 기획 계약이다
                if ((step.BaseSuccessRate < MAX_RATE) && (step.FailBonusRate <= 0f))
                {
                    errors.Add($"{OWNER}.{field}: 성공률이 100% 미만인데 실패 보정이 없어 확정 성공에 닿지 않는다");
                }
            }
        }

        private void ValidateInheritance(List<string> errors, bool scaleValid)
        {
            const string OWNER = nameof(EquipmentCatalogSO);

            DataValidation.RequirePositive(errors, OWNER, nameof(_sameGradeInheritCost), _sameGradeInheritCost);
            DataValidation.RequirePositive(errors, OWNER, nameof(_upperGradeInheritCost), _upperGradeInheritCost);
            DataValidation.RequireCount(errors, OWNER, nameof(_upperGradeInheritance), _upperGradeInheritance, UPPER_INHERITANCE_ROW_COUNT);
            if ((_upperGradeInheritance == null) || (_upperGradeInheritance.Length != UPPER_INHERITANCE_ROW_COUNT))
            {
                return;
            }

            int firstSource = MAX_ENHANCEMENT - UPPER_INHERITANCE_ROW_COUNT + 1;
            int lastResult  = -1;

            for (int i = 0; i < _upperGradeInheritance.Length; ++i)
            {
                InheritanceRow row   = _upperGradeInheritance[i];
                string         field = $"{nameof(_upperGradeInheritance)}[{i}]";

                if (row == null)
                {
                    errors.Add($"{OWNER}.{field}: 행이 비어 있다");
                    continue;
                }

                if (row.SourceLevel != (firstSource + i))
                {
                    errors.Add($"{OWNER}.{field}: 원본 단계 {row.SourceLevel} != {firstSource + i}, +{firstSource}부터 +{MAX_ENHANCEMENT}까지 빈틈 없이 둔다");
                }

                DataValidation.RequireRange(errors, OWNER, field + ".ResultLevel", row.ResultLevel, 0f, MAX_ENHANCEMENT);
                if (row.ResultLevel <= lastResult)
                {
                    errors.Add($"{OWNER}.{field}: 결과 단계가 앞 행보다 커야 한다");
                }

                lastResult = row.ResultLevel;

                if (scaleValid)
                {
                    ValidateInheritanceGain(errors, field, row);
                }
            }
        }

        // 계승 뒤 공격, 생명, 방어가 원본보다 낮아지면 서버가 거절해야 하므로 표 자체가 그런 행을 갖지 않게 막는다
        private void ValidateInheritanceGain(List<string> errors, string field, InheritanceRow row)
        {
            const string OWNER = nameof(EquipmentCatalogSO);

            float sourceScale = BASE_STAT_SCALE + (_enhancementStatRate * row.SourceLevel);
            float resultScale = BASE_STAT_SCALE + (_enhancementStatRate * row.ResultLevel);

            for (int g = 0; g < (GRADE_COUNT - 1); ++g)
            {
                float ratio = (_gradeStatMultipliers[g + 1] * resultScale) / (_gradeStatMultipliers[g] * sourceScale);
                if (ratio <= BASE_STAT_SCALE)
                {
                    errors.Add($"{OWNER}.{field}: {(EItemGrade)g} +{row.SourceLevel} -> {(EItemGrade)(g + 1)} +{row.ResultLevel} 계승 뒤 능력치 비율 {ratio}가 1 이하다");
                }
            }
        }
    }
}
