// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

// 서드파티
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> 성장 SO 값을 백엔드 growth-content.json(스키마 1)으로 옮기고, 파일이 SO와 같은지 대조한다 </summary>
    public static class GrowthContentExport
    {
        // 규칙 수치의 원본은 SO이고 백엔드는 이 파일만 읽는다. 에디터 메뉴(Shinshi/Export Growth Content)가 파일을 다시 쓰고
        // EditMode 테스트가 Compare로 둘이 어긋났는지 본다. 키 이름과 순서는 백엔드 GrowthContent의 JsonProperty와 같다.
        // 스키마를 바꾸면 백엔드 SUPPORTED_SCHEMA_VERSION과 함께 올린다.
        //
        // float는 decimal로 바꿔 쓴다. 바꾸는 순간 유효 숫자 7자리로 반올림되어 0.04f가 0.04로, 2.460375f가 2.460375로
        // 적힌다. double로 쓰면 0.039999999105930328 같은 값이 남아 백엔드 decimal 계산과 어긋난다.
        // 서버 어셈블리에 두는 이유: 에디터 메뉴와 EditMode 테스트가 함께 참조할 수 있고 클라이언트 빌드에는 들어가지 않는다

        /****************************************
        *                Fields
        ****************************************/

        public const int    SCHEMA_VERSION = 3;
        public const string RELATIVE_PATH  = "Backend/src/Shinshi.Data/Content/growth-content.json";

        private const string WINDOWS_NEW_LINE = "\r\n";
        private const string UNIX_NEW_LINE    = "\n";
        private const decimal NUMBER_TOLERANCE = 0.000001m;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 프로젝트 루트 기준 파일 절대 경로, projectRoot는 Assets의 상위 폴더다 </summary>
        public static string GetPath(string projectRoot)
        {
            return Path.Combine(projectRoot, RELATIVE_PATH);
        }

        /// <summary> 백엔드 파일과 같은 형식의 JSON 문자열, 줄 끝은 CRLF이고 마지막에 줄바꿈이 있다 </summary>
        public static string Serialize(GameDatabaseSO root)
        {
            string text = Build(root).ToString(Formatting.Indented);
            return text.Replace(WINDOWS_NEW_LINE, UNIX_NEW_LINE).Replace(UNIX_NEW_LINE, WINDOWS_NEW_LINE) + WINDOWS_NEW_LINE;
        }

        /// <summary> SO 값과 파일 JSON의 차이를 경로별로 돌려준다, 같으면 빈 목록 </summary>
        public static List<string> Compare(GameDatabaseSO root, string json)
        {
            var differences = new List<string>();
            JToken file;
            using (var reader = new JsonTextReader(new StringReader(json)) { FloatParseHandling = FloatParseHandling.Decimal })
            {
                file = JToken.ReadFrom(reader);
            }

            Diff(Build(root), file, "$", differences);
            return differences;
        }

        /// <summary> 스키마 1 문서 </summary>
        public static JObject Build(GameDatabaseSO root)
        {
            if ((root == null) || (root.Equipment == null) || (root.Growth == null) || (root.Talismans == null)
                || (root.Progression == null) || (root.CharacterLevel == null))
            {
                throw new ArgumentException("성장 콘텐츠를 만들 GameDB 참조가 없다", nameof(root));
            }

            return new JObject
            {
                ["schemaVersion"]  = SCHEMA_VERSION,
                ["contentVersion"] = root.ContentVersion,
                ["equipment"]      = BuildEquipment(root.Equipment),
                ["growth"]         = BuildGrowth(root.Growth),
                ["talismans"]      = BuildTalismans(root.Talismans),
                ["progression"]    = BuildProgression(root.Progression),
                ["characterLevel"] = JObject.FromObject(root.CharacterLevel.ToSettings())
            };
        }

        private static JObject BuildEquipment(EquipmentCatalogSO equipment)
        {
            var definitions = new JArray();
            for (int i = 0; i < equipment.Equipment.Count; ++i)
            {
                EquipmentCatalogSO.EquipmentDefinition item = equipment.Equipment[i];
                definitions.Add(new JObject
                {
                    ["definitionId"]         = item.DefinitionId,
                    ["slot"]                 = (int)item.Slot,
                    ["grade"]                = (int)item.Grade,
                    ["isTraining"]           = item.IsTraining,
                    ["bossMaterialId"]       = item.BossMaterialId ?? string.Empty,
                    ["bossMaterialCount"]    = item.BossMaterialCount,
                    ["auctionMaterialId"]    = item.AuctionMaterialId ?? string.Empty,
                    ["auctionMaterialCount"] = item.AuctionMaterialCount
                });
            }

            var slotBase = new JArray();
            for (int slot = 0; slot < Constants.Combat.EQUIPMENT_SLOT_COUNT; ++slot)
            {
                EquipmentCatalogSO.SlotBaseStats stats = equipment.GetSlotBase((EEquipmentSlot)slot);
                slotBase.Add(new JObject
                {
                    ["slot"]       = (int)stats.Slot,
                    ["attack"]     = Number(stats.Attack),
                    ["health"]     = Number(stats.Health),
                    ["defense"]    = Number(stats.Defense),
                    ["critChance"] = Number(stats.CritChance),
                    ["lifeSteal"]  = Number(stats.LifeSteal)
                });
            }

            var grades = new JArray();
            foreach (EItemGrade grade in Enum.GetValues(typeof(EItemGrade)))
            {
                grades.Add(Number(equipment.GetGradeMultiplier(grade)));
            }

            var steps = new JArray();
            for (int i = 0; i < equipment.EnhancementSteps.Count; ++i)
            {
                EquipmentCatalogSO.EnhancementStep step = equipment.EnhancementSteps[i];
                steps.Add(new JObject
                {
                    ["essenceCost"]     = step.EssenceCost,
                    ["shardCost"]       = step.ShardCost,
                    ["baseSuccessRate"] = Number(step.BaseSuccessRate),
                    ["failBonusRate"]   = Number(step.FailBonusRate)
                });
            }

            var inheritance = new JArray();
            for (int i = 0; i < equipment.UpperGradeInheritance.Count; ++i)
            {
                EquipmentCatalogSO.InheritanceRow row = equipment.UpperGradeInheritance[i];
                inheritance.Add(new JObject
                {
                    ["sourceLevel"] = row.SourceLevel,
                    ["resultLevel"] = row.ResultLevel
                });
            }

            return new JObject
            {
                ["definitions"]              = definitions,
                ["slotBaseStats"]            = slotBase,
                ["gradeStatMultipliers"]     = grades,
                ["enhancementStatRate"]      = Number(equipment.EnhancementStatRate),
                ["trainingStatRate"]         = Number(equipment.TrainingStatRate),
                ["lifeStealPerGrade"]        = Number(equipment.LifeStealPerGrade),
                ["enhancementSteps"]         = steps,
                ["sameGradeInheritCost"]     = equipment.SameGradeInheritCost,
                ["upperGradeInheritCost"]    = equipment.UpperGradeInheritCost,
                ["upperGradeInheritance"]    = inheritance,
                ["disassembleRefundRate"]    = Number(equipment.DisassembleRefundRate),
                ["storageSlotsPerCharacter"] = equipment.StorageSlotsPerCharacter
            };
        }

        private static JObject BuildGrowth(GrowthConfigSO growth)
        {
            var training = new JArray();
            for (int stat = 0; stat < Constants.Combat.TRAINING_STAT_COUNT; ++stat)
            {
                GrowthConfigSO.TrainingStatSpec spec = growth.GetTrainingStat((ETrainingStat)stat);
                training.Add(new JObject
                {
                    ["stat"]        = (int)spec.Stat,
                    ["gainPerStep"] = Number(spec.GainPerStep)
                });
            }

            var rates = new JArray();
            for (int i = 0; i < growth.RegisterSuccessRates.Count; ++i)
            {
                rates.Add(Number(growth.RegisterSuccessRates[i]));
            }

            var upgrades = new JArray();
            for (int i = 0; i < growth.EffectUpgradeCosts.Count; ++i)
            {
                GrowthConfigSO.EffectUpgradeCost cost = growth.EffectUpgradeCosts[i];
                upgrades.Add(new JObject
                {
                    ["cardCost"]    = cost.CardCost,
                    ["essenceCost"] = cost.EssenceCost
                });
            }

            return new JObject
            {
                ["trainingTotalCap"]           = growth.TrainingTotalCap,
                ["trainingStatCap"]            = growth.TrainingStatCap,
                ["trainingBaseCost"]           = growth.TrainingBaseCost,
                ["trainingCostStep"]           = growth.TrainingCostStep,
                ["trainingStats"]              = training,
                ["respecBaseCost"]             = growth.RespecBaseCost,
                ["respecCostPerStep"]          = growth.RespecCostPerStep,
                ["registerCardCost"]           = growth.RegisterCardCost,
                ["registerEssenceCost"]        = growth.RegisterEssenceCost,
                ["registerSuccessRates"]       = rates,
                ["effectUpgradeCosts"]         = upgrades,
                ["cardDisassembleEssence"]     = growth.CardDisassembleEssence,
                ["unregisteredCardStackLimit"] = growth.UnregisteredCardStackLimit
            };
        }

        private static JObject BuildTalismans(TalismanCatalogSO talismans)
        {
            var multipliers = new JArray();
            for (int level = 1; level <= talismans.MaxEffectLevel; ++level)
            {
                multipliers.Add(Number(talismans.GetLevelMultiplier(level)));
            }

            var effects = new JArray();
            for (int i = 0; i < talismans.Effects.Count; ++i)
            {
                TalismanCatalogSO.TalismanEffectDefinition effect = talismans.Effects[i];
                effects.Add(new JObject
                {
                    ["definitionId"] = effect.DefinitionId,
                    ["playerClass"]  = (int)effect.PlayerClass,
                    ["skillId"]      = effect.SkillId,
                    ["tier"]         = (int)effect.Tier,
                    ["choice"]       = (int)effect.Choice
                });
            }

            return new JObject
            {
                ["levelBonusMultipliers"] = multipliers,
                ["effects"]               = effects
            };
        }

        private static JObject BuildProgression(ProgressionCatalogSO progression)
        {
            var regions = new JArray();
            for (int i = 0; i < progression.Regions.Count; ++i)
            {
                ProgressionCatalogSO.RegionDefinition region = progression.Regions[i];
                regions.Add(new JObject
                {
                    ["definitionId"]      = region.DefinitionId,
                    ["hasWaveBattle"]     = region.HasWaveBattle,
                    ["foxTracesPerClear"] = region.FoxTracesPerClear,
                    ["availableInTrial"]  = region.AvailableInTrial,
                    ["trialPreUnlocked"]  = region.TrialPreUnlocked,
                    ["requirement"]       = BuildRequirement(region.Requirement)
                });
            }

            var battles = new JArray();
            for (int i = 0; i < progression.BossBattles.Count; ++i)
            {
                ProgressionCatalogSO.BossBattleDefinition battle = progression.BossBattles[i];
                battles.Add(new JObject
                {
                    ["definitionId"]     = battle.DefinitionId,
                    ["bossId"]           = battle.BossId,
                    ["difficulty"]       = (int)battle.Difficulty,
                    ["ticketCost"]       = battle.TicketCost,
                    ["availableInTrial"] = battle.AvailableInTrial,
                    ["trialPreUnlocked"] = battle.TrialPreUnlocked,
                    ["requirement"]      = BuildRequirement(battle.Requirement)
                });
            }

            return new JObject
            {
                ["trialEdition"]           = progression.TrialEdition,
                ["foxTraceCap"]            = progression.FoxTraceCap,
                ["regions"]                = regions,
                ["bossBattles"]            = battles,
                ["standardCharacterGrant"] = BuildGrant(progression.StandardCharacterGrant),
                ["standardAccountGrant"]   = BuildGrant(progression.StandardAccountGrant),
                ["trialCharacterGrant"]    = BuildGrant(progression.TrialCharacterGrant),
                ["trialAccountGrant"]      = BuildGrant(progression.TrialAccountGrant)
            };
        }

        private static JObject BuildRequirement(ProgressionCatalogSO.UnlockRequirement requirement)
        {
            return new JObject
            {
                ["requiresIntro"]    = (requirement != null) && (requirement.RequiresIntro),
                ["clearedRegionId"]  = requirement?.ClearedRegionId ?? string.Empty,
                ["unlockedRegionId"] = requirement?.UnlockedRegionId ?? string.Empty,
                ["clearedBattleId"]  = requirement?.ClearedBattleId ?? string.Empty,
                ["foxTraceCount"]    = (requirement != null) ? requirement.FoxTraceCount : 0
            };
        }

        private static JObject BuildGrant(ProgressionCatalogSO.GrantBundle grant)
        {
            var items = new JArray();
            int itemCount = (grant?.Items == null) ? 0 : grant.Items.Count;
            for (int i = 0; i < itemCount; ++i)
            {
                items.Add(new JObject
                {
                    ["itemId"]   = grant.Items[i].ItemId,
                    ["quantity"] = grant.Items[i].Quantity
                });
            }

            var equipment = new JArray();
            int equipmentCount = (grant?.Equipment == null) ? 0 : grant.Equipment.Count;
            for (int i = 0; i < equipmentCount; ++i)
            {
                equipment.Add(new JObject
                {
                    ["equipmentId"] = grant.Equipment[i].EquipmentId,
                    ["enhancement"] = grant.Equipment[i].Enhancement
                });
            }

            return new JObject
            {
                ["grantId"]      = grant?.GrantId ?? string.Empty,
                ["items"]        = items,
                ["equipment"]    = equipment,
                ["actionEnergy"] = (grant != null) ? grant.ActionEnergy : 0
            };
        }

        // 정수면 정수로 적어 파일과 같은 모양(200, 1)을 만든다
        private static JToken Number(float value)
        {
            decimal exact = (decimal)value;
            if (exact == decimal.Truncate(exact))
            {
                return new JValue((long)exact);
            }

            return new JValue(exact);
        }

        private static void Diff(JToken expected, JToken actual, string path, List<string> differences)
        {
            if ((IsNumber(expected)) && (IsNumber(actual)))
            {
                decimal left  = expected.Value<decimal>();
                decimal right = actual.Value<decimal>();
                if (Math.Abs(left - right) > NUMBER_TOLERANCE)
                {
                    differences.Add(string.Format(CultureInfo.InvariantCulture, "{0}: SO={1}, file={2}", path, left, right));
                }

                return;
            }

            if (expected.Type != actual.Type)
            {
                differences.Add($"{path}: SO 형식 {expected.Type}, 파일 형식 {actual.Type}");
                return;
            }

            if (expected is JObject expectedObject)
            {
                var actualObject = (JObject)actual;
                foreach (KeyValuePair<string, JToken> pair in expectedObject)
                {
                    if (!actualObject.TryGetValue(pair.Key, out JToken value))
                    {
                        differences.Add($"{path}.{pair.Key}: 파일에 없다");
                        continue;
                    }

                    Diff(pair.Value, value, path + "." + pair.Key, differences);
                }

                foreach (KeyValuePair<string, JToken> pair in actualObject)
                {
                    if (!expectedObject.ContainsKey(pair.Key))
                    {
                        differences.Add($"{path}.{pair.Key}: SO에 없는 키다");
                    }
                }

                return;
            }

            if (expected is JArray expectedArray)
            {
                var actualArray = (JArray)actual;
                if (expectedArray.Count != actualArray.Count)
                {
                    differences.Add($"{path}: 길이 SO={expectedArray.Count}, file={actualArray.Count}");
                    return;
                }

                for (int i = 0; i < expectedArray.Count; ++i)
                {
                    Diff(expectedArray[i], actualArray[i], path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", differences);
                }

                return;
            }

            if (!JToken.DeepEquals(expected, actual))
            {
                differences.Add($"{path}: SO={expected}, file={actual}");
            }
        }

        private static bool IsNumber(JToken token)
        {
            return (token.Type == JTokenType.Integer) || (token.Type == JTokenType.Float);
        }
    }
}
