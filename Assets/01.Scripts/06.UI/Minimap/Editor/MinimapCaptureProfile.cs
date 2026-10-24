#if UNITY_EDITOR
// System
using System.Collections.Generic;

namespace Shinshi.UI.Editor
{
    /// <summary> 캡처에서 한 렌더러가 들어갈 층, 뒤에 그려지는 층이 앞 층을 덮는다 </summary>
    public enum EMinimapLayer
    {
        Rim,
        Land,
        Water,
        Structure
    }

    /// <summary> 렌더러 경로와 재질 이름으로 층을 정하는 규칙 하나, 먼저 맞은 규칙이 이긴다 </summary>
    public sealed class MinimapCaptureRule
    {
        /****************************************
        *              Properties
        ****************************************/

        public EMinimapLayer Layer { get; set; }

        // 씬 루트부터 렌더러까지 이어 붙인 경로가 이 글자로 시작해야 한다
        public string PathPrefix { get; set; } = string.Empty;

        // 비어 있지 않으면 경로가 이 중 하나를 담아야 한다
        public string[] PathTokens { get; set; } = System.Array.Empty<string>();

        // 경로가 이 중 하나라도 담으면 제외한다
        public string[] SkipTokens { get; set; } = System.Array.Empty<string>();

        // 비어 있지 않으면 재질이나 셰이더 이름이 이 중 하나를 담아야 한다
        public string[] MaterialTokens { get; set; } = System.Array.Empty<string>();

        // 렌더러 바운드 조건, 0이면 검사하지 않는다
        public float MinHeight { get; set; } = 0f;

        public float MaxFootprint { get; set; } = 0f;
    }

    /// <summary> 맵 하나를 찍는 방법, 씬 이름으로 고른다 </summary>
    public sealed class MinimapCaptureProfile
    {
        /****************************************
        *              Properties
        ****************************************/

        public string SceneName { get; set; } = string.Empty;

        public string MapId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public int TextureSize { get; set; } = 1024;

        // 내 위치를 중심으로 창이 보여 줄 반경, 미터
        public float ViewRadius { get; set; } = 40f;

        public List<MinimapCaptureRule> Rules { get; } = new List<MinimapCaptureRule>();
    }

    /// <summary> 출시 전 개발 씬 네 곳의 캡처 규칙 </summary>
    public static class MinimapCaptureProfiles
    {
        /****************************************
        *                Fields
        ****************************************/

        // 걸을 수 없거나 작은 장식은 건물 실루엣에서 뺀다
        private static readonly string[] G_PropTokens =
        {
            "Tree", "Pine", "Lantern", "Flagpole", "Boulder", "Barrel", "Stair", "Paving", "Rock", "Fence", "Bush",
            "Grass", "Flower", "Rail", "Torch", "Petal", "Plant", "Herb", "Crop", "Stone"
        };

        private static readonly string[] G_TownBuildingGroups =
        {
            "Palace", "Plaza", "North", "NWFarm", "West", "SEMarket", "NEGate", "EWatch", "HerbGarden_LifeZone"
        };

        private static readonly string[] G_WaterTokens = { "Water", "Pond", "Stream" };

        /****************************************
        *                Methods
        ****************************************/

        public static MinimapCaptureProfile Find(string sceneName)
        {
            switch (sceneName)
            {
                case "Dev_ShinshiVillageQuarterView_GroundPaint":
                case "ShinshiTown":
                    return Town();
                case "Dev_ForestPlateau":
                case "Dev_ForestPlateauHitFeedback":
                case "DungeonForest":
                    return Forest();
                case "Dev_BossWolbaek":
                case "DungeonWolbaek":
                    return Wolbaek();
                case "Dev_BossMucheol":
                case "DungeonMucheol":
                    return Mucheol();
                default:
                    return null;
            }
        }

        private static MinimapCaptureProfile Town()
        {
            var profile = new MinimapCaptureProfile
            {
                SceneName   = "Dev_ShinshiVillageQuarterView_GroundPaint",
                MapId       = "town",
                DisplayName = "신시",
                TextureSize = 1024,
                ViewRadius  = 40f
            };

            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Water, PathPrefix = "02_CA07_GroundAndWater/", MaterialTokens = G_WaterTokens });
            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Water, PathPrefix = "03_CA07_Placements/Ponds/", MaterialTokens = G_WaterTokens });
            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Land, PathPrefix = "02_CA07_GroundAndWater/" });
            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Land, PathPrefix = "03_CA07_Placements/Bridges/" });
            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Rim, PathPrefix = "01_CA07_IslandBody/" });
            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Rim, PathPrefix = "07_NaturalIslandAccents/" });
            for (int i = 0; i < G_TownBuildingGroups.Length; ++i)
            {
                profile.Rules.Add(new MinimapCaptureRule
                {
                    Layer        = EMinimapLayer.Structure,
                    PathPrefix   = "03_CA07_Placements/" + G_TownBuildingGroups[i] + "/",
                    SkipTokens   = G_PropTokens,
                    MinHeight    = 1.2f,
                    MaxFootprint = 30f
                });
            }

            return profile;
        }

        private static MinimapCaptureProfile Forest()
        {
            var profile = new MinimapCaptureProfile
            {
                SceneName   = "Dev_ForestPlateau",
                MapId       = "forest",
                DisplayName = "숲 고원",
                TextureSize = 2048,
                ViewRadius  = 40f
            };

            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Water, PathPrefix = "PF_ForestPlateau_Layout/", MaterialTokens = new[] { "Stream" } });
            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Land, PathPrefix = "PF_ForestPlateau_Layout/" });
            profile.Rules.Add(new MinimapCaptureRule
            {
                Layer      = EMinimapLayer.Structure,
                PathPrefix = "20_PlacedObjects/Structures/",
                SkipTokens = new[] { "Lantern" },
                MinHeight  = 1f
            });

            return profile;
        }

        private static MinimapCaptureProfile Wolbaek()
        {
            var profile = new MinimapCaptureProfile
            {
                SceneName   = "Dev_BossWolbaek",
                MapId       = "wolbaek",
                DisplayName = "월백 결계",
                TextureSize = 1024,
                ViewRadius  = 30f
            };

            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Land, PathPrefix = "BossArenaMap/Shared/Geometry/Floor_" });
            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Land, PathPrefix = "BossArenaMap/Shared/ArenaArt_20260922/StonePaving/" });
            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Structure, PathPrefix = "BossArenaMap/Arena_Phase1/Wall_R22/" });
            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Structure, PathPrefix = "BossArenaMap/Arena_Phase1/Shrine_North_R20/" });
            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Structure, PathPrefix = "BossArenaMap/Arena_Phase1/ArenaArt_20260922/Shrine_MainHall" });
            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Structure, PathPrefix = "BossArenaMap/Arena_Phase1/Rocks/" });
            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Structure, PathPrefix = "BossArenaMap/Arena_Phase1/ArenaArt_20260922/PF_RedGate/" });

            return profile;
        }

        private static MinimapCaptureProfile Mucheol()
        {
            var profile = new MinimapCaptureProfile
            {
                SceneName   = "Dev_BossMucheol",
                MapId       = "mucheol",
                DisplayName = "무철",
                TextureSize = 1024,
                ViewRadius  = 30f
            };

            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Land, PathPrefix = "PF_MucheolArena/" });
            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Rim, PathPrefix = "Island_E_PondTerrace/" });
            profile.Rules.Add(new MinimapCaptureRule { Layer = EMinimapLayer.Structure, PathPrefix = "MucheolPillars/" });

            return profile;
        }
    }
}
#endif
