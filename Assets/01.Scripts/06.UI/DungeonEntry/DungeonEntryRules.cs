// System
using System;
using System.Collections.Generic;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 진행 카탈로그를 입장 창 목록으로 옮기는 순수 규칙 </summary>
    // 표시 이름은 아직 현지화 키를 풀 곳이 없어 이 표를 쓴다. 표에 없는 ID는 ID 그대로 보인다.
    // 이 규칙은 해금, 입장권, 파티 조건을 판정하지 않는다. 방 생성 요청을 받은 마을 서버가 정한다.
    // 체험판 포함 여부만 서버(ReadyRules.CheckContent)와 같은 기준으로 먼저 걸러 서버가 어차피 거절할 요청을 보내지 않게 한다
    public static class DungeonEntryRules
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string CATEGORY_CHAOS = "카오스 던전";
        private const string CATEGORY_BOSS  = "보스 맵";

        private const string CHAOS_SUMMARY         = "산귀 무리를 웨이브마다 물리치며 나아가는 일반전이다. 몇 명이든 파티를 꾸려 함께 들어갈 수 있다.";
        private const string BOSS_SUMMARY_FORMAT   = "보스 {0} 토벌전이다. 난이도는 {1}이다.";
        private const string REFERENCE_FORMAT      = "권장 공격력 {0:N0}  /  권장 체력 {1:N0}";
        private const string TICKET_FORMAT         = "입장권 {0}장 (참가자 각자)";
        private const string NO_COST               = "입장권 필요 없음";
        private const string NO_REQUIREMENT        = "별도 선행 조건 없음";
        private const string REQUIREMENT_SEPARATOR = ", ";
        private const string UNAVAILABLE_TRIAL     = "체험판에는 들어 있지 않은 콘텐츠입니다";

        private const string REQ_INTRO        = "신시 안내 완료";
        private const string REQ_REGION_CLEAR = "{0} 카오스 던전 성공";
        private const string REQ_REGION_OPEN  = "{0} 개방";
        private const string REQ_BATTLE_CLEAR = "{0} 처치";
        private const string REQ_FOX_TRACE    = "여우의 흔적 {0}개";

        private const string LABEL_EASY         = "쉬움";
        private const string LABEL_HARD         = "어려움";
        private const string BATTLE_NAME_FORMAT = "{0} ({1})";

        // 해금 조건 문구의 최대 조각 수(선행 안내, 지역 성공, 지역 개방, 보스전 처치, 여우의 흔적)
        private const int REQUIREMENT_PART_CAPACITY = 5;

        // 지역 ID, 표시 이름
        private static readonly string[] G_RegionIds   = { "region_forest", "region_ruins", "region_altar", "region_wolbaek_barrier" };
        private static readonly string[] G_RegionNames = { "숲", "인간 마을 폐허", "오래된 제단", "월백 결계" };

        // 보스 ID, 표시 이름
        private static readonly string[] G_BossIds   = { "boss_mucheol", "boss_wolbaek" };
        private static readonly string[] G_BossNames = { "무철", "월백" };

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 카오스 던전(일반전 지역)을 앞에, 보스 맵(보스전)을 뒤에 카탈로그 순서대로 담는다 </summary>
        public static void Build(ProgressionCatalogSO catalog, List<DungeonEntryData> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            if (catalog == null)
            {
                return;
            }

            IReadOnlyList<ProgressionCatalogSO.RegionDefinition> regions = catalog.Regions;
            for (int i = 0; i < regions.Count; ++i)
            {
                ProgressionCatalogSO.RegionDefinition region = regions[i];
                if ((region != null) && (region.HasWaveBattle))
                {
                    into.Add(BuildRegion(catalog, region));
                }
            }

            IReadOnlyList<ProgressionCatalogSO.BossBattleDefinition> battles = catalog.BossBattles;
            for (int i = 0; i < battles.Count; ++i)
            {
                if (battles[i] != null)
                {
                    into.Add(BuildBattle(catalog, battles[i]));
                }
            }
        }

        /// <summary> 파티 방의 콘텐츠 ID를 화면에 보일 이름으로 바꾼다, 카탈로그에 없으면 ID 그대로다 </summary>
        public static string ContentName(ProgressionCatalogSO catalog, EPartyMode mode, string contentId)
        {
            if (string.IsNullOrEmpty(contentId))
            {
                return string.Empty;
            }

            if (mode == EPartyMode.Boss)
            {
                return BattleName(catalog, contentId);
            }

            return RegionName(contentId);
        }

        public static string RegionName(string regionId)
        {
            return Lookup(G_RegionIds, G_RegionNames, regionId);
        }

        /// <summary> "보스 (난이도)" 꼴, 카탈로그에 없는 보스전이면 ID 그대로다 </summary>
        public static string BattleName(ProgressionCatalogSO catalog, string battleId)
        {
            if ((catalog == null) || (!catalog.TryGetBattle(battleId, out ProgressionCatalogSO.BossBattleDefinition battle)))
            {
                return battleId;
            }

            return string.Format(CultureInfo.InvariantCulture, BATTLE_NAME_FORMAT, BossName(battle.BossId),
                DifficultyLabel(battle.Difficulty));
        }

        public static string BossName(string bossId)
        {
            return Lookup(G_BossIds, G_BossNames, bossId);
        }

        public static string DifficultyLabel(EBossDifficulty difficulty)
        {
            return (difficulty == EBossDifficulty.Hard) ? LABEL_HARD : LABEL_EASY;
        }

        /// <summary> 해금 조건을 한 줄 글로 옮긴다, 조건이 없으면 안내 문구다 </summary>
        public static string DescribeRequirement(ProgressionCatalogSO catalog, ProgressionCatalogSO.UnlockRequirement requirement)
        {
            if (requirement == null)
            {
                return NO_REQUIREMENT;
            }

            var parts = new List<string>(REQUIREMENT_PART_CAPACITY);
            if (requirement.RequiresIntro)
            {
                parts.Add(REQ_INTRO);
            }

            if (!string.IsNullOrEmpty(requirement.ClearedRegionId))
            {
                parts.Add(string.Format(CultureInfo.InvariantCulture, REQ_REGION_CLEAR, RegionName(requirement.ClearedRegionId)));
            }

            if (!string.IsNullOrEmpty(requirement.UnlockedRegionId))
            {
                parts.Add(string.Format(CultureInfo.InvariantCulture, REQ_REGION_OPEN, RegionName(requirement.UnlockedRegionId)));
            }

            if (!string.IsNullOrEmpty(requirement.ClearedBattleId))
            {
                parts.Add(string.Format(CultureInfo.InvariantCulture, REQ_BATTLE_CLEAR, BattleName(catalog, requirement.ClearedBattleId)));
            }

            if (requirement.FoxTraceCount > 0)
            {
                parts.Add(string.Format(CultureInfo.InvariantCulture, REQ_FOX_TRACE, requirement.FoxTraceCount));
            }

            return (parts.Count == 0) ? NO_REQUIREMENT : string.Join(REQUIREMENT_SEPARATOR, parts);
        }

        private static DungeonEntryData BuildRegion(ProgressionCatalogSO catalog, ProgressionCatalogSO.RegionDefinition region)
        {
            bool   available = (!catalog.TrialEdition) || (region.AvailableInTrial);
            string reference = string.Format(CultureInfo.InvariantCulture, REFERENCE_FORMAT, catalog.GetReferenceAttack(region),
                catalog.GetReferenceHealth(region));

            return new DungeonEntryData(region.DefinitionId, EPartyMode.Normal, RegionName(region.DefinitionId), CATEGORY_CHAOS,
                CHAOS_SUMMARY + "\n" + reference, DescribeRequirement(catalog, region.Requirement), NO_COST, available,
                available ? string.Empty : UNAVAILABLE_TRIAL);
        }

        private static DungeonEntryData BuildBattle(ProgressionCatalogSO catalog, ProgressionCatalogSO.BossBattleDefinition battle)
        {
            bool   available = (!catalog.TrialEdition) || (battle.AvailableInTrial);
            string summary   = string.Format(CultureInfo.InvariantCulture, BOSS_SUMMARY_FORMAT, BossName(battle.BossId),
                DifficultyLabel(battle.Difficulty));
            string cost      = (battle.TicketCost > 0)
                ? string.Format(CultureInfo.InvariantCulture, TICKET_FORMAT, battle.TicketCost)
                : NO_COST;

            return new DungeonEntryData(battle.DefinitionId, EPartyMode.Boss, BattleName(catalog, battle.DefinitionId), CATEGORY_BOSS,
                summary, DescribeRequirement(catalog, battle.Requirement), cost, available, available ? string.Empty : UNAVAILABLE_TRIAL);
        }

        private static string Lookup(string[] ids, string[] names, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            for (int i = 0; i < ids.Length; ++i)
            {
                if (string.Equals(ids[i], id, StringComparison.Ordinal))
                {
                    return names[i];
                }
            }

            return id;
        }
    }
}
