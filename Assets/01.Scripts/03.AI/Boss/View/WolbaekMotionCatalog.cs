// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.AI
{
    // 값은 시험 씬(Dev_WolbaekAppearPreview)에서 사용자가 확정한 판정 맞춤 모션 값을 옮긴 것이다
    // 서버 사건에 패턴 번호가 실리는 예고(BossTelegraphStart) 기반 패턴만 넣는다. 빔, 별, 격자, 반월 연쇄, 난사는 사건에 패턴 번호가 없어 아직 연결하지 못한다
    /// <summary> 패턴 번호로 확정한 월백 모션 값을 찾는다, 표현 상수이고 밸런스 값이 아니다 </summary>
    public static class WolbaekMotionCatalog
    {
        /****************************************
        *              Constants
        ****************************************/

        public const string IDLE_STATE  = "Idle";
        public const string APPEAR_STATE = "Appear";
        public const string DEATH_STATE = "dead01_inplace";
        public const string DEATH_HOLD_STATE = "dead01_hold";
        public const string DASH_STATE  = "move_Flying_front_inplace";

        private const string FAN_STATE         = "attack09_inplace";
        private const string CIRCLE_STATE      = "combo03_3_inplace";
        private const string THROW_STATE       = "combo02_1_inplace";
        private const string AREA_STATE        = "attack10_inplace";
        private const string BEAM_STATE        = "attack03_inplace";
        private const string WARP_STATE        = "attack06_inplace";
        private const string DRAIN_STATE       = "attack_drain_inplace";
        private const string GRID_STATE        = "attack04_inplace";
        private const string SWEEP_STATE       = "attack05_inplace";

        private const float FAN_PEAK        = 0.61f;
        private const float CIRCLE_PEAK     = 0.93f;
        private const float THROW_PEAK      = 0.44f;
        private const float AREA_PEAK       = 0.47f;
        private const float BEAM_PEAK       = 2.3f;
        private const float WARP_PEAK       = 2.55f;
        private const float THREE_SHOT_SPEED = 0.49f;
        private const float DRAIN_PEAK       = 0.4f;
        private const float DRAIN_SLOW_FROM  = 0.4f;
        private const float DRAIN_SLOW_TO    = 2.2f;
        private const float DRAIN_SLOW_SPEED = 0.3f;
        private const float GRID_PEAK        = 0.8f;
        private const float GRID_CLIP_SECONDS = 1.6f; // 팔을 올리고(0~0.8) 내리는(0.8~1.6) 클립 전체, 하드 격자 빔은 시전 전체에 고르게 편다
        private const float GRID_SLOW_FROM   = 0.8f;
        private const float GRID_SLOW_TO     = 1.6f;
        private const float BEAM_PAIR_SLOW   = 0.2f;
        private const float SWEEP_PEAK       = 1.15f;
        private const float SWEEP_SLOW_FROM  = 1.15f;
        private const float SWEEP_SLOW_TO    = 1.85f;
        private const float CHAIN_SLOW_SPEED = 0.233f;
        private const float BARRAGE_SLOW_FROM = 0.44f;
        private const float BARRAGE_SLOW_TO   = 0.65f;
        private const float BARRAGE_SLOW_SPEED = 0.15f;

        private const string WOLBAEK_P02      = "wolbaek_p02";
        private const string WOLBAEK_P03      = "wolbaek_p03";
        private const string WOLBAEK_P04      = "wolbaek_p04";
        private const string WOLBAEK_P05      = "wolbaek_p05";
        private const string WOLBAEK_P06      = "wolbaek_p06";

        /// <summary> 월백 1페이즈 P01 돌진 패턴 번호, 이지와 하드 </summary>
        public static readonly string[] DashPatternIds = { "wolbaek_p01", BossPatternCatalogSO.WOLBAEK_HARD_P01_ID };

        /// <summary> P07 워프 후 충격파 패턴 번호, 이지와 하드 </summary>
        public static readonly string[] WarpPatternIds = { BossPatternCatalogSO.WOLBAEK_P2_P07_ID, BossPatternCatalogSO.WOLBAEK_HARD_P2_P07_ID };

        /****************************************
        *                Fields
        ****************************************/

        private static readonly Dictionary<string, WolbaekMotionEntry> G_Entries = BuildEntries();
        private static readonly Dictionary<EWolbaekEventMotion, WolbaekMotionEntry> G_EventEntries = BuildEventEntries();

        /****************************************
        *                Methods
        ****************************************/

        public static bool TryGet(string patternId, out WolbaekMotionEntry entry)
        {
            if (patternId == null)
            {
                entry = default;
                return false;
            }

            return G_Entries.TryGetValue(patternId, out entry);
        }

        /// <summary> 사건 종류로 정하는 패턴의 모션 값 </summary>
        public static bool TryGetEvent(EWolbaekEventMotion kind, out WolbaekMotionEntry entry)
        {
            return G_EventEntries.TryGetValue(kind, out entry);
        }

        public static bool IsDash(string patternId)
        {
            return Array.IndexOf(DashPatternIds, patternId) >= 0;
        }

        public static bool IsWarp(string patternId)
        {
            return Array.IndexOf(WarpPatternIds, patternId) >= 0;
        }

        private static Dictionary<EWolbaekEventMotion, WolbaekMotionEntry> BuildEventEntries()
        {
            return new Dictionary<EWolbaekEventMotion, WolbaekMotionEntry>
            {
                { EWolbaekEventMotion.StarRotating, new WolbaekMotionEntry(DRAIN_STATE, DRAIN_PEAK, 0f, DRAIN_SLOW_FROM, DRAIN_SLOW_TO, DRAIN_SLOW_SPEED) },
                { EWolbaekEventMotion.StarStage, new WolbaekMotionEntry(DRAIN_STATE, DRAIN_PEAK) },
                { EWolbaekEventMotion.BeamPair, new WolbaekMotionEntry(GRID_STATE, GRID_PEAK, 0f, GRID_SLOW_FROM, GRID_SLOW_TO, BEAM_PAIR_SLOW) },
                { EWolbaekEventMotion.GridBeam, new WolbaekMotionEntry(GRID_STATE, GRID_PEAK, fitClipSeconds: GRID_CLIP_SECONDS) },
                { EWolbaekEventMotion.SweepHalfMoon, new WolbaekMotionEntry(SWEEP_STATE, SWEEP_PEAK) },
                { EWolbaekEventMotion.ChainFan, new WolbaekMotionEntry(SWEEP_STATE, SWEEP_PEAK, 0f, SWEEP_SLOW_FROM, SWEEP_SLOW_TO, CHAIN_SLOW_SPEED) },
                { EWolbaekEventMotion.TrackingArea, new WolbaekMotionEntry(AREA_STATE, AREA_PEAK) },
                { EWolbaekEventMotion.Barrage, new WolbaekMotionEntry(THROW_STATE, THROW_PEAK, 0f, BARRAGE_SLOW_FROM, BARRAGE_SLOW_TO, BARRAGE_SLOW_SPEED) },
                { EWolbaekEventMotion.CircleBurst, new WolbaekMotionEntry(CIRCLE_STATE, CIRCLE_PEAK) }
            };
        }

        private static Dictionary<string, WolbaekMotionEntry> BuildEntries()
        {
            var fan    = new WolbaekMotionEntry(FAN_STATE, FAN_PEAK);
            var circle = new WolbaekMotionEntry(CIRCLE_STATE, CIRCLE_PEAK);
            var thrown = new WolbaekMotionEntry(THROW_STATE, THROW_PEAK);
            var area   = new WolbaekMotionEntry(AREA_STATE, AREA_PEAK);
            var beam   = new WolbaekMotionEntry(BEAM_STATE, BEAM_PEAK);
            var warp   = new WolbaekMotionEntry(WARP_STATE, WARP_PEAK);

            return new Dictionary<string, WolbaekMotionEntry>
            {
                // 1페이즈는 이지와 하드가 같은 실행이라 같은 모션을 쓴다
                { WOLBAEK_P02, fan },
                { BossPatternCatalogSO.WOLBAEK_HARD_P02_ID, fan },
                { WOLBAEK_P03, circle },
                { BossPatternCatalogSO.WOLBAEK_HARD_P03_ID, circle },
                { WOLBAEK_P04, thrown },
                { BossPatternCatalogSO.WOLBAEK_HARD_P04_ID, thrown },
                { WOLBAEK_P05, area },
                { BossPatternCatalogSO.WOLBAEK_HARD_P05_ID, area },
                { WOLBAEK_P06, beam },
                { BossPatternCatalogSO.WOLBAEK_HARD_P06_ID, beam },

                // 2페이즈
                { BossPatternCatalogSO.WOLBAEK_P2_P04_ID, area },
                { BossPatternCatalogSO.WOLBAEK_HARD_P2_P04_ID, area },
                { BossPatternCatalogSO.WOLBAEK_P2_P05_ID, new WolbaekMotionEntry(THROW_STATE, THROW_PEAK, THREE_SHOT_SPEED) },
                { BossPatternCatalogSO.WOLBAEK_P2_P06_ID, circle },
                { BossPatternCatalogSO.WOLBAEK_HARD_P2_P06_ID, circle },
                { BossPatternCatalogSO.WOLBAEK_P2_P07_ID, warp },
                { BossPatternCatalogSO.WOLBAEK_HARD_P2_P07_ID, warp }
            };
        }
    }
}
