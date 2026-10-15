// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

// Third Party
using Newtonsoft.Json;
using Shinshi.Contracts;

namespace Shinshi.Data.Growth
{
    /// <summary> 성장 규칙 수치. 원본은 Unity SO(EquipmentCatalogSO, GrowthConfigSO, TalismanCatalogSO, ProgressionCatalogSO)이고
    /// 백엔드는 그 값을 옮긴 내장 JSON만 읽는다. 값이 SO와 어긋나면 Unity 일치 테스트가 실패해야 한다 </summary>
    public sealed class GrowthContent
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string RESOURCE_NAME            = "Shinshi.Data.Content.growth-content.json";
        public const int    SUPPORTED_SCHEMA_VERSION = 4;

        public const int SLOT_COUNT          = 6;
        public const int GRADE_COUNT         = 4;
        public const int MAX_ENHANCEMENT     = 25;
        public const int TRAINING_STAT_COUNT = 5;
        public const int MAX_EFFECT_LEVEL    = 5;
        public const int PLAYER_CLASS_COUNT  = 6;
        public const int EFFECTS_PER_CLASS   = 15;
        public const int EFFECT_COUNT        = PLAYER_CLASS_COUNT * EFFECTS_PER_CLASS;
        public const int REGULAR_SKILL_COUNT = 24;
        public const int TIER_COUNT          = 3;

        // 등록 성공이 레벨 1이므로 강화 비용 표는 목표 레벨 2부터다
        public const int FIRST_UPGRADE_LEVEL = 2;

        private const int UPPER_INHERITANCE_ROW_COUNT = 9;
        private const int EQUIPMENT_DEFINITION_COUNT  = (SLOT_COUNT * GRADE_COUNT) + SLOT_COUNT;
        private const string EFFECT_ID_PATTERN        = "^T[0-9]{2}-[1-3][A-Z]$";
        private const string EQUIPMENT_ID_PATTERN     = "^equip_[a-z0-9_]{1,58}$";

        private static readonly Lazy<GrowthContent> G_Embedded = new Lazy<GrowthContent>(ReadEmbedded);

        private Dictionary<string, EquipmentDefinition> _equipmentById = new Dictionary<string, EquipmentDefinition>();
        private Dictionary<string, TalismanEffect> _effectById = new Dictionary<string, TalismanEffect>();
        private Dictionary<int, OptionRange> _optionRangeByKind = new Dictionary<int, OptionRange>();

        /****************************************
        *              Properties
        ****************************************/

        [JsonProperty("characterLevel")] public CharacterLevelSettings CharacterLevel { get; set; } = null!;

        [JsonProperty("schemaVersion")]  public int              SchemaVersion  { get; set; }
        [JsonProperty("contentVersion")] public string           ContentVersion { get; set; } = string.Empty;
        [JsonProperty("equipment")]      public EquipmentSection Equipment      { get; set; } = new EquipmentSection();
        [JsonProperty("growth")]         public GrowthSection    Growth         { get; set; } = new GrowthSection();
        [JsonProperty("talismans")]      public TalismanSection  Talismans      { get; set; } = new TalismanSection();
        [JsonProperty("progression")]    public ProgressionSection Progression  { get; set; } = new ProgressionSection();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 내장 리소스를 한 번만 읽어 검증한다. 잘못된 내용이면 시작 시점에 예외로 멈춘다 </summary>
        public static GrowthContent LoadEmbedded()
        {
            return G_Embedded.Value;
        }

        /// <summary> JSON을 읽고 개수, 범위, 참조를 검증한다. 문제가 있으면 모든 오류를 담은 InvalidDataException이다 </summary>
        public static GrowthContent Parse(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                FloatParseHandling = FloatParseHandling.Decimal,
                MissingMemberHandling = MissingMemberHandling.Error,
                NullValueHandling = NullValueHandling.Ignore
            };
            GrowthContent content = JsonConvert.DeserializeObject<GrowthContent>(json, settings)
                ?? throw new InvalidDataException("Growth content is empty.");
            List<string> errors = content.Validate();
            if (errors.Count > 0)
            {
                throw new InvalidDataException("Growth content is invalid: " + string.Join("; ", errors));
            }

            content.BuildLookups();
            return content;
        }

        public bool TryGetEquipment(string definitionId, out EquipmentDefinition definition)
        {
            if (definitionId == null)
            {
                definition = null!;
                return false;
            }

            return _equipmentById.TryGetValue(definitionId, out definition!);
        }

        public bool TryGetEffect(string effectId, out TalismanEffect effect)
        {
            if (effectId == null)
            {
                effect = null!;
                return false;
            }

            return _effectById.TryGetValue(effectId, out effect!);
        }

        public SlotBaseStats GetSlotBase(int slot)
        {
            return Equipment.SlotBaseStats[slot];
        }

        public decimal GetGradeMultiplier(int grade)
        {
            return Equipment.GradeStatMultipliers[grade];
        }

        /// <summary> 목표 단계(현재 + 1)의 시도 비용과 성공률, 1부터 MAX_ENHANCEMENT까지 </summary>
        public EnhancementStep GetEnhancementStep(int targetLevel)
        {
            if ((targetLevel < 1) || (targetLevel > MAX_ENHANCEMENT))
            {
                throw new ArgumentOutOfRangeException(nameof(targetLevel));
            }

            return Equipment.EnhancementSteps[targetLevel - 1];
        }

        /// <summary> 그 부위에서 나올 수 있는 옵션 종류, EquipmentOptionRules의 KIND_* 값 </summary>
        public IReadOnlyList<int> GetOptionPool(int slot)
        {
            return Equipment.OptionSlotPools[slot];
        }

        /// <summary> 옵션 종류와 등급의 값 범위(만분율). 종류 0이나 모르는 종류는 범위가 없다 </summary>
        public bool TryGetOptionRange(int kind, int grade, out int min, out int max)
        {
            if (!IsGrade(grade) || !_optionRangeByKind.TryGetValue(kind, out OptionRange? range))
            {
                min = 0;
                max = 0;
                return false;
            }

            min = range.Min[grade];
            max = range.Max[grade];
            return true;
        }

        public bool TryGetUpperInheritResult(int sourceLevel, out int resultLevel)
        {
            InheritanceRow? row = Equipment.UpperGradeInheritance.FirstOrDefault(entry => entry.SourceLevel == sourceLevel);
            resultLevel = row?.ResultLevel ?? 0;
            return row != null;
        }

        /// <summary> 목표 효과 레벨(2부터 MAX_EFFECT_LEVEL까지)의 확정 강화 비용 </summary>
        public EffectUpgradeCost GetEffectUpgradeCost(int targetLevel)
        {
            if ((targetLevel < FIRST_UPGRADE_LEVEL) || (targetLevel > MAX_EFFECT_LEVEL))
            {
                throw new ArgumentOutOfRangeException(nameof(targetLevel));
            }

            return Growth.EffectUpgradeCosts[targetLevel - FIRST_UPGRADE_LEVEL];
        }

        public GrantBundle GetCharacterGrant(EGrowthContentProfile profile)
        {
            return profile == EGrowthContentProfile.Standard
                ? Progression.StandardCharacterGrant : Progression.TrialCharacterGrant;
        }

        public GrantBundle GetAccountGrant(EGrowthContentProfile profile)
        {
            return profile == EGrowthContentProfile.Standard
                ? Progression.StandardAccountGrant : Progression.TrialAccountGrant;
        }

        private static GrowthContent ReadEmbedded()
        {
            Assembly assembly = typeof(GrowthContent).Assembly;
            using Stream stream = assembly.GetManifestResourceStream(RESOURCE_NAME)
                ?? throw new InvalidDataException("Embedded growth content is missing.");
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            return Parse(reader.ReadToEnd());
        }

        private void BuildLookups()
        {
            _equipmentById = Equipment.Definitions.ToDictionary(definition => definition.DefinitionId, StringComparer.Ordinal);
            _effectById = Talismans.Effects.ToDictionary(effect => effect.DefinitionId, StringComparer.Ordinal);
            _optionRangeByKind = Equipment.OptionRanges.ToDictionary(range => range.Kind);
        }

        private List<string> Validate()
        {
            List<string> errors = new List<string>();
            if (SchemaVersion != SUPPORTED_SCHEMA_VERSION)
            {
                errors.Add($"schemaVersion {SchemaVersion} != {SUPPORTED_SCHEMA_VERSION}");
            }
            if (string.IsNullOrWhiteSpace(ContentVersion))
            {
                errors.Add("contentVersion is empty");
            }

            if ((CharacterLevel == null) || !CharacterLevel.IsValid())
            {
                errors.Add("characterLevel is invalid");
            }
            ValidateEquipment(errors);
            ValidateGrowth(errors);
            ValidateTalismans(errors);
            ValidateProgression(errors);
            return errors;
        }

        private void ValidateEquipment(List<string> errors)
        {
            EquipmentSection section = Equipment;
            RequireCount(errors, "equipment.definitions", section.Definitions.Length, EQUIPMENT_DEFINITION_COUNT);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<(int, int)> crafted = new HashSet<(int, int)>();
            HashSet<int> training = new HashSet<int>();
            foreach (EquipmentDefinition definition in section.Definitions)
            {
                string field = "equipment.definitions[" + definition.DefinitionId + "]";
                if (!Regex.IsMatch(definition.DefinitionId ?? string.Empty, EQUIPMENT_ID_PATTERN) || !ids.Add(definition.DefinitionId!))
                {
                    errors.Add(field + ": invalid or duplicate ID");
                }
                if (!IsSlot(definition.Slot) || !IsGrade(definition.Grade))
                {
                    errors.Add(field + ": slot or grade out of range");
                    continue;
                }
                if (definition.IsTraining)
                {
                    if ((definition.Grade != 0) || (definition.BossMaterialCount != 0) || (definition.AuctionMaterialCount != 0)
                        || !training.Add(definition.Slot))
                    {
                        errors.Add(field + ": training equipment must be common, materialless and unique per slot");
                    }
                    continue;
                }
                if (string.IsNullOrWhiteSpace(definition.BossMaterialId) || (definition.BossMaterialCount <= 0)
                    || !crafted.Add((definition.Slot, definition.Grade)))
                {
                    errors.Add(field + ": crafted equipment needs a boss material and a unique slot and grade");
                }
                if ((definition.AuctionMaterialCount < 0)
                    || ((definition.AuctionMaterialCount > 0) && string.IsNullOrWhiteSpace(definition.AuctionMaterialId)))
                {
                    errors.Add(field + ": auction material is invalid");
                }
            }

            RequireCount(errors, "equipment.slotBaseStats", section.SlotBaseStats.Length, SLOT_COUNT);
            for (int index = 0; index < section.SlotBaseStats.Length; ++index)
            {
                SlotBaseStats stats = section.SlotBaseStats[index];
                if ((stats.Slot != index) || (stats.Attack < 0) || (stats.Health < 0) || (stats.Defense < 0)
                    || (stats.CritChance < 0) || (stats.CritChance > 1) || (stats.LifeSteal < 0)
                    || ((stats.LifeSteal > 0) && ((stats.LifeSteal + (section.LifeStealPerGrade * (GRADE_COUNT - 1))) > 1)))
                {
                    errors.Add("equipment.slotBaseStats[" + index + "]: slot order or values are invalid");
                }
            }
            if ((section.LifeStealPerGrade < 0) || (section.LifeStealPerGrade > 1))
            {
                errors.Add("equipment.lifeStealPerGrade is out of range");
            }

            RequireCount(errors, "equipment.gradeStatMultipliers", section.GradeStatMultipliers.Length, GRADE_COUNT);
            for (int index = 1; index < section.GradeStatMultipliers.Length; ++index)
            {
                if ((section.GradeStatMultipliers[index - 1] <= 0)
                    || (section.GradeStatMultipliers[index] <= section.GradeStatMultipliers[index - 1]))
                {
                    errors.Add("equipment.gradeStatMultipliers must increase");
                }
            }
            if ((section.EnhancementStatRate <= 0) || (section.TrainingStatRate <= 0) || (section.TrainingStatRate > 1)
                || (section.DisassembleRefundRate < 0) || (section.DisassembleRefundRate > 1)
                || (section.StorageSlotsPerCharacter <= 0) || (section.SameGradeInheritCost <= 0)
                || (section.UpperGradeInheritCost <= 0))
            {
                errors.Add("equipment rates, costs or storage are out of range");
            }

            RequireCount(errors, "equipment.enhancementSteps", section.EnhancementSteps.Length, MAX_ENHANCEMENT);
            foreach (EnhancementStep step in section.EnhancementSteps)
            {
                // 확정 성공에 닿지 않는 단계가 있으면 실패 보정 계약이 깨진다
                if ((step.EssenceCost <= 0) || (step.ShardCost <= 0) || (step.BaseSuccessRate <= 0) || (step.BaseSuccessRate > 1)
                    || (step.FailBonusRate < 0) || ((step.BaseSuccessRate < 1) && (step.FailBonusRate <= 0)))
                {
                    errors.Add("equipment.enhancementSteps: invalid step");
                }
            }

            RequireCount(errors, "equipment.upperGradeInheritance", section.UpperGradeInheritance.Length,
                UPPER_INHERITANCE_ROW_COUNT);
            int firstSource = MAX_ENHANCEMENT - UPPER_INHERITANCE_ROW_COUNT + 1;
            for (int index = 0; index < section.UpperGradeInheritance.Length; ++index)
            {
                InheritanceRow row = section.UpperGradeInheritance[index];
                if ((row.SourceLevel != firstSource + index) || (row.ResultLevel < 0) || (row.ResultLevel > MAX_ENHANCEMENT))
                {
                    errors.Add("equipment.upperGradeInheritance[" + index + "]: invalid row");
                }
            }

            ValidateOptions(errors, section);
        }

        private static void ValidateOptions(List<string> errors, EquipmentSection section)
        {
            if ((section.OptionRerollEssenceCost <= 0) || (section.OptionRerollShardCost <= 0)
                || (section.OptionPityThreshold < 1) || (section.OptionHighTierBasisPoints < 0)
                || (section.OptionHighTierBasisPoints > EquipmentOptionRules.VALUE_SCALE) || (section.OptionValueStep <= 0))
            {
                errors.Add("equipment option reroll cost, pity, high tier ratio or value step is out of range");
            }

            RequireCount(errors, "equipment.optionRanges", section.OptionRanges.Length, EquipmentOptionRules.KIND_MAX);
            HashSet<int> kinds = new HashSet<int>();
            foreach (OptionRange range in section.OptionRanges)
            {
                string field = "equipment.optionRanges[" + range.Kind + "]";
                if ((range.Kind <= EquipmentOptionRules.KIND_NONE) || (range.Kind > EquipmentOptionRules.KIND_MAX)
                    || !kinds.Add(range.Kind))
                {
                    errors.Add(field + ": kind is out of range or duplicated");
                }
                if ((range.Min.Length != GRADE_COUNT) || (range.Max.Length != GRADE_COUNT))
                {
                    errors.Add(field + ": min and max need one value per grade");
                    continue;
                }

                for (int grade = 0; grade < GRADE_COUNT; ++grade)
                {
                    if (!EquipmentOptionRules.IsValidRange(range.Min[grade], range.Max[grade], section.OptionValueStep))
                    {
                        errors.Add(field + ": grade " + grade + " range is invalid");
                        continue;
                    }

                    // 상위 등급 계승은 값이 대상 등급의 min보다 작을 때만 min으로 올린다. 등급이 오를 때 범위가 내려가거나
                    // 단계 눈금이 어긋나면 옮긴 값이 대상 범위를 벗어나 전투 준비 검증에서 거절된다
                    if ((grade > 0) && ((range.Min[grade] < range.Min[grade - 1]) || (range.Max[grade] < range.Max[grade - 1])
                        || (((range.Min[grade] - range.Min[grade - 1]) % section.OptionValueStep) != 0)))
                    {
                        errors.Add(field + ": grade " + grade + " range must not fall below the previous grade or leave its step grid");
                    }
                }
            }

            RequireCount(errors, "equipment.optionSlotPools", section.OptionSlotPools.Length, SLOT_COUNT);
            for (int slot = 0; slot < section.OptionSlotPools.Length; ++slot)
            {
                int[] pool = section.OptionSlotPools[slot];
                if ((pool == null) || (pool.Length == 0) || pool.Any(kind => !kinds.Contains(kind)))
                {
                    errors.Add("equipment.optionSlotPools[" + slot + "]: pool is empty or names a kind without a range");
                }
            }
        }

        private void ValidateGrowth(List<string> errors)
        {
            GrowthSection section = Growth;
            if ((section.TrainingTotalCap <= 0) || (section.TrainingStatCap <= 0)
                || (section.TrainingStatCap > section.TrainingTotalCap)
                || ((section.TrainingStatCap * TRAINING_STAT_COUNT) < section.TrainingTotalCap)
                || (section.TrainingBaseCost <= 0) || (section.TrainingCostStep < 0)
                || (section.RespecBaseCost < 0) || (section.RespecCostPerStep < 0))
            {
                errors.Add("growth training caps or costs are invalid");
            }

            RequireCount(errors, "growth.trainingStats", section.TrainingStats.Length, TRAINING_STAT_COUNT);
            for (int index = 0; index < section.TrainingStats.Length; ++index)
            {
                if ((section.TrainingStats[index].Stat != index) || (section.TrainingStats[index].GainPerStep <= 0))
                {
                    errors.Add("growth.trainingStats[" + index + "]: invalid order or gain");
                }
            }

            if ((section.RegisterCardCost <= 0) || (section.RegisterEssenceCost < 0) || (section.CardDisassembleEssence < 0)
                || (section.UnregisteredCardStackLimit <= 0) || (section.RegisterSuccessRates.Length == 0)
                || (section.RegisterSuccessRates[^1] != 1)
                || section.RegisterSuccessRates.Any(rate => (rate <= 0) || (rate > 1)))
            {
                errors.Add("growth register settings are invalid");
            }

            RequireCount(errors, "growth.effectUpgradeCosts", section.EffectUpgradeCosts.Length,
                MAX_EFFECT_LEVEL - FIRST_UPGRADE_LEVEL + 1);
            if (section.EffectUpgradeCosts.Any(cost => (cost.CardCost <= 0) || (cost.EssenceCost < 0)))
            {
                errors.Add("growth.effectUpgradeCosts: invalid cost");
            }
        }

        private void ValidateTalismans(List<string> errors)
        {
            RequireCount(errors, "talismans.levelBonusMultipliers", Talismans.LevelBonusMultipliers.Length, MAX_EFFECT_LEVEL);
            RequireCount(errors, "talismans.effects", Talismans.Effects.Length, EFFECT_COUNT);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> skills = new HashSet<string>(StringComparer.Ordinal);
            int[] classCounts = new int[PLAYER_CLASS_COUNT];
            foreach (TalismanEffect effect in Talismans.Effects)
            {
                bool idValid = Regex.IsMatch(effect.DefinitionId ?? string.Empty, EFFECT_ID_PATTERN);
                if (!idValid || !ids.Add(effect.DefinitionId!)
                    || (effect.PlayerClass < 0) || (effect.PlayerClass >= PLAYER_CLASS_COUNT)
                    || string.IsNullOrWhiteSpace(effect.SkillId)
                    || (effect.Tier < 0) || (effect.Tier >= TIER_COUNT) || (effect.Choice < 0)
                    || !IsIdConsistent(effect.DefinitionId!, effect.Tier, effect.Choice))
                {
                    errors.Add("talismans.effects[" + effect.DefinitionId + "]: invalid effect");
                    continue;
                }

                ++classCounts[effect.PlayerClass];
                skills.Add(effect.SkillId);
            }

            // 직업마다 효과 15개를 가져야 한다. 한 직업의 부적 풀이 다른 직업과 섞이지 않는다
            for (int playerClass = 0; playerClass < PLAYER_CLASS_COUNT; ++playerClass)
            {
                RequireCount(errors, "talismans.effects[class " + playerClass + "]", classCounts[playerClass], EFFECTS_PER_CLASS);
            }
            RequireCount(errors, "talismans.effects skills", skills.Count, REGULAR_SKILL_COUNT);
        }

        // ID 접미사의 숫자-1이 단계, 문자(A=0, B=1)가 선택이어야 한다. 접두 번호(Tnn)는 직업에 묶지 않는다
        private static bool IsIdConsistent(string definitionId, int tier, int choice)
        {
            const int SUFFIX_INDEX = 4;
            const int CHOICE_INDEX = 5;
            return ((definitionId[SUFFIX_INDEX] - '1') == tier) && ((definitionId[CHOICE_INDEX] - 'A') == choice);
        }

        private void ValidateProgression(List<string> errors)
        {
            ProgressionSection section = Progression;
            GrantBundle[] grants =
            {
                section.StandardCharacterGrant, section.StandardAccountGrant, section.TrialCharacterGrant,
                section.TrialAccountGrant
            };
            foreach (GrantBundle grant in grants)
            {
                if (string.IsNullOrWhiteSpace(grant.GrantId) || (grant.ActionEnergy < 0)
                    || grant.Items.Any(item => string.IsNullOrWhiteSpace(item.ItemId) || (item.Quantity <= 0))
                    || grant.Equipment.Any(equipment => (equipment.Enhancement < 0) || (equipment.Enhancement > MAX_ENHANCEMENT)
                        || !Equipment.Definitions.Any(definition => definition.DefinitionId == equipment.EquipmentId)))
                {
                    errors.Add("progression grant " + grant.GrantId + " is invalid");
                }
            }

            if ((section.FoxTraceCap < 0) || (section.Regions.Length == 0) || (section.BossBattles.Length == 0))
            {
                errors.Add("progression regions or battles are missing");
            }
        }

        private static void RequireCount(List<string> errors, string field, int actual, int expected)
        {
            if (actual != expected)
            {
                errors.Add($"{field}: count {actual} != {expected}");
            }
        }

        private static bool IsSlot(int slot)
        {
            return (slot >= 0) && (slot < SLOT_COUNT);
        }

        private static bool IsGrade(int grade)
        {
            return (grade >= 0) && (grade < GRADE_COUNT);
        }

        /// <summary> EquipmentCatalogSO의 장비 표, 기본값, 강화, 계승, 분해, 보관 </summary>
        public sealed class EquipmentSection
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("definitions")]              public EquipmentDefinition[] Definitions              { get; set; } = Array.Empty<EquipmentDefinition>();
            [JsonProperty("slotBaseStats")]            public SlotBaseStats[]       SlotBaseStats            { get; set; } = Array.Empty<SlotBaseStats>();
            [JsonProperty("gradeStatMultipliers")]     public decimal[]             GradeStatMultipliers     { get; set; } = Array.Empty<decimal>();
            [JsonProperty("enhancementStatRate")]      public decimal               EnhancementStatRate      { get; set; }
            [JsonProperty("trainingStatRate")]         public decimal               TrainingStatRate         { get; set; }
            [JsonProperty("lifeStealPerGrade")]        public decimal               LifeStealPerGrade        { get; set; }

            // 옵션 재설정 한 번의 비용, 천장 횟수, 상급 줄 기준(만분율), 값 간격(만분율)
            [JsonProperty("optionRerollEssenceCost")]   public long          OptionRerollEssenceCost   { get; set; }
            [JsonProperty("optionRerollShardCost")]     public long          OptionRerollShardCost     { get; set; }
            [JsonProperty("optionPityThreshold")]       public int           OptionPityThreshold       { get; set; }
            [JsonProperty("optionHighTierBasisPoints")] public int           OptionHighTierBasisPoints { get; set; }
            [JsonProperty("optionValueStep")]           public int           OptionValueStep           { get; set; }

            // 부위 순서(0 무기, 1 머리, 2 몸통, 3 손, 4 발, 5 장신구)의 후보 종류
            [JsonProperty("optionSlotPools")]           public int[][]       OptionSlotPools           { get; set; } = Array.Empty<int[]>();
            [JsonProperty("optionRanges")]              public OptionRange[] OptionRanges              { get; set; } = Array.Empty<OptionRange>();

            [JsonProperty("enhancementSteps")]         public EnhancementStep[]     EnhancementSteps         { get; set; } = Array.Empty<EnhancementStep>();
            [JsonProperty("sameGradeInheritCost")]     public long                  SameGradeInheritCost     { get; set; }
            [JsonProperty("upperGradeInheritCost")]    public long                  UpperGradeInheritCost    { get; set; }
            [JsonProperty("upperGradeInheritance")]    public InheritanceRow[]      UpperGradeInheritance    { get; set; } = Array.Empty<InheritanceRow>();
            [JsonProperty("disassembleRefundRate")]    public decimal               DisassembleRefundRate    { get; set; }
            [JsonProperty("storageSlotsPerCharacter")] public int                   StorageSlotsPerCharacter { get; set; }
        }

        /// <summary> 장비 한 종류, 수련용은 재료가 없다 </summary>
        public sealed class EquipmentDefinition
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("definitionId")]         public string DefinitionId         { get; set; } = string.Empty;
            [JsonProperty("slot")]                 public int    Slot                 { get; set; }
            [JsonProperty("grade")]                public int    Grade                { get; set; }
            [JsonProperty("isTraining")]           public bool   IsTraining           { get; set; }
            [JsonProperty("bossMaterialId")]       public string BossMaterialId       { get; set; } = string.Empty;
            [JsonProperty("bossMaterialCount")]    public int    BossMaterialCount    { get; set; }
            [JsonProperty("auctionMaterialId")]    public string AuctionMaterialId    { get; set; } = string.Empty;
            [JsonProperty("auctionMaterialCount")] public int    AuctionMaterialCount { get; set; }
        }

        /// <summary> 옵션 종류 하나의 등급별 값 범위(만분율), 배열은 등급 순서(0 일반부터 3 전설) </summary>
        public sealed class OptionRange
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("kind")] public int   Kind { get; set; }
            [JsonProperty("min")]  public int[] Min  { get; set; } = Array.Empty<int>();
            [JsonProperty("max")]  public int[] Max  { get; set; } = Array.Empty<int>();
        }

        /// <summary> 부위별 일반 제작 장비의 강화 전 기본값, 치명은 0에서 1 사이 비율 </summary>
        public sealed class SlotBaseStats
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("slot")]       public int     Slot       { get; set; }
            [JsonProperty("attack")]     public decimal Attack     { get; set; }
            [JsonProperty("health")]     public decimal Health     { get; set; }
            [JsonProperty("defense")]    public decimal Defense    { get; set; }
            [JsonProperty("critChance")] public decimal CritChance { get; set; }

            // 일반 등급의 생명력 흡수, 0이면 이 부위는 등급이 올라도 흡수가 없다
            [JsonProperty("lifeSteal")]  public decimal LifeSteal  { get; set; }
        }

        /// <summary> 강화 목표 단계 하나의 시도 비용과 성공률(0에서 1) </summary>
        public sealed class EnhancementStep
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("essenceCost")]     public long    EssenceCost     { get; set; }
            [JsonProperty("shardCost")]       public long    ShardCost       { get; set; }
            [JsonProperty("baseSuccessRate")] public decimal BaseSuccessRate { get; set; }
            [JsonProperty("failBonusRate")]   public decimal FailBonusRate   { get; set; }
        }

        /// <summary> 상위 등급 계승의 원본 단계와 결과 단계 </summary>
        public sealed class InheritanceRow
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("sourceLevel")] public int SourceLevel { get; set; }
            [JsonProperty("resultLevel")] public int ResultLevel { get; set; }
        }

        /// <summary> GrowthConfigSO의 수련, 재분배, 부적 등록과 강화, 분해 </summary>
        public sealed class GrowthSection
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("trainingTotalCap")]           public int                 TrainingTotalCap           { get; set; }
            [JsonProperty("trainingStatCap")]            public int                 TrainingStatCap            { get; set; }
            [JsonProperty("trainingBaseCost")]           public long                TrainingBaseCost           { get; set; }
            [JsonProperty("trainingCostStep")]           public long                TrainingCostStep           { get; set; }
            [JsonProperty("trainingStats")]              public TrainingStatSpec[]  TrainingStats              { get; set; } = Array.Empty<TrainingStatSpec>();
            [JsonProperty("respecBaseCost")]             public long                RespecBaseCost             { get; set; }
            [JsonProperty("respecCostPerStep")]          public long                RespecCostPerStep          { get; set; }
            [JsonProperty("registerCardCost")]           public long                RegisterCardCost           { get; set; }
            [JsonProperty("registerEssenceCost")]        public long                RegisterEssenceCost        { get; set; }
            [JsonProperty("registerSuccessRates")]       public decimal[]           RegisterSuccessRates       { get; set; } = Array.Empty<decimal>();
            [JsonProperty("effectUpgradeCosts")]         public EffectUpgradeCost[] EffectUpgradeCosts         { get; set; } = Array.Empty<EffectUpgradeCost>();
            [JsonProperty("cardDisassembleEssence")]     public long                CardDisassembleEssence     { get; set; }
            [JsonProperty("unregisteredCardStackLimit")] public long                UnregisteredCardStackLimit { get; set; }
        }

        /// <summary> 수련 항목 하나의 단계당 증가량 </summary>
        public sealed class TrainingStatSpec
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("stat")]        public int     Stat        { get; set; }
            [JsonProperty("gainPerStep")] public decimal GainPerStep { get; set; }
        }

        /// <summary> 부적 효과 한 레벨 확정 강화 비용 </summary>
        public sealed class EffectUpgradeCost
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("cardCost")]    public long CardCost    { get; set; }
            [JsonProperty("essenceCost")] public long EssenceCost { get; set; }
        }

        /// <summary> TalismanCatalogSO에서 서버 판정에 쓰는 항목만 옮긴다 </summary>
        public sealed class TalismanSection
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("levelBonusMultipliers")] public decimal[]        LevelBonusMultipliers { get; set; } = Array.Empty<decimal>();
            [JsonProperty("effects")]               public TalismanEffect[] Effects               { get; set; } = Array.Empty<TalismanEffect>();
        }

        /// <summary> 부적 효과 하나의 직업, 기술, 단계(0 보조1, 1 보조2, 2 핵심), 선택 </summary>
        public sealed class TalismanEffect
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("definitionId")] public string DefinitionId { get; set; } = string.Empty;
            [JsonProperty("playerClass")]  public int    PlayerClass  { get; set; }
            [JsonProperty("skillId")]      public string SkillId      { get; set; } = string.Empty;
            [JsonProperty("tier")]         public int    Tier         { get; set; }
            [JsonProperty("choice")]       public int    Choice       { get; set; }
        }

        /// <summary> ProgressionCatalogSO의 지역, 보스전, 최초 지원 </summary>
        public sealed class ProgressionSection
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("trialEdition")]           public bool                   TrialEdition           { get; set; }
            [JsonProperty("foxTraceCap")]            public int                    FoxTraceCap            { get; set; }
            [JsonProperty("regions")]                public RegionDefinition[]     Regions                { get; set; } = Array.Empty<RegionDefinition>();
            [JsonProperty("bossBattles")]            public BossBattleDefinition[] BossBattles            { get; set; } = Array.Empty<BossBattleDefinition>();
            [JsonProperty("standardCharacterGrant")] public GrantBundle            StandardCharacterGrant { get; set; } = new GrantBundle();
            [JsonProperty("standardAccountGrant")]   public GrantBundle            StandardAccountGrant   { get; set; } = new GrantBundle();
            [JsonProperty("trialCharacterGrant")]    public GrantBundle            TrialCharacterGrant    { get; set; } = new GrantBundle();
            [JsonProperty("trialAccountGrant")]      public GrantBundle            TrialAccountGrant      { get; set; } = new GrantBundle();
        }

        /// <summary> 지역 하나와 해금 조건 </summary>
        public sealed class RegionDefinition
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("definitionId")]      public string            DefinitionId      { get; set; } = string.Empty;
            [JsonProperty("hasWaveBattle")]     public bool              HasWaveBattle     { get; set; }
            [JsonProperty("foxTracesPerClear")] public int               FoxTracesPerClear { get; set; }
            [JsonProperty("availableInTrial")]  public bool              AvailableInTrial  { get; set; }
            [JsonProperty("trialPreUnlocked")]  public bool              TrialPreUnlocked  { get; set; }
            [JsonProperty("requirement")]       public UnlockRequirement Requirement       { get; set; } = new UnlockRequirement();
        }

        /// <summary> 보스전 하나와 해금 조건 </summary>
        public sealed class BossBattleDefinition
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("definitionId")]     public string            DefinitionId     { get; set; } = string.Empty;
            [JsonProperty("bossId")]           public string            BossId           { get; set; } = string.Empty;
            [JsonProperty("difficulty")]       public int               Difficulty       { get; set; }
            [JsonProperty("ticketCost")]       public int               TicketCost       { get; set; }
            [JsonProperty("availableInTrial")] public bool              AvailableInTrial { get; set; }
            [JsonProperty("trialPreUnlocked")] public bool              TrialPreUnlocked { get; set; }
            [JsonProperty("requirement")]      public UnlockRequirement Requirement      { get; set; } = new UnlockRequirement();
        }

        /// <summary> 해금 조건, 빈 칸은 보지 않고 채운 조건은 모두 만족해야 한다 </summary>
        public sealed class UnlockRequirement
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("requiresIntro")]    public bool   RequiresIntro    { get; set; }
            [JsonProperty("clearedRegionId")]  public string ClearedRegionId  { get; set; } = string.Empty;
            [JsonProperty("unlockedRegionId")] public string UnlockedRegionId { get; set; } = string.Empty;
            [JsonProperty("clearedBattleId")]  public string ClearedBattleId  { get; set; } = string.Empty;
            [JsonProperty("foxTraceCount")]    public int    FoxTraceCount    { get; set; }
        }

        /// <summary> 최초 지원 묶음 </summary>
        public sealed class GrantBundle
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("grantId")]      public string           GrantId      { get; set; } = string.Empty;
            [JsonProperty("items")]        public ItemGrant[]      Items        { get; set; } = Array.Empty<ItemGrant>();
            [JsonProperty("equipment")]    public EquipmentGrant[] Equipment    { get; set; } = Array.Empty<EquipmentGrant>();
            [JsonProperty("actionEnergy")] public int              ActionEnergy { get; set; }
        }

        /// <summary> 지급 수량형 아이템 한 줄. item_essence 등 지갑 자산 ID는 해당 잔액으로 지급한다 </summary>
        public sealed class ItemGrant
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("itemId")]   public string ItemId   { get; set; } = string.Empty;
            [JsonProperty("quantity")] public long   Quantity { get; set; }
        }

        /// <summary> 지급 장비 한 부위와 강화 단계 </summary>
        public sealed class EquipmentGrant
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("equipmentId")] public string EquipmentId { get; set; } = string.Empty;
            [JsonProperty("enhancement")] public int    Enhancement { get; set; }
        }
    }
}
