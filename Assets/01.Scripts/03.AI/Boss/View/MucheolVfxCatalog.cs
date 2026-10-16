// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 프리뷰(Dev_MucheolMotionVfxPreview)에서 확정한 패턴별 VFX 원본과 크기, 각도다.
    // 크기는 서버 형상과 맞춘 값이다: 원본 01은 반경 5m 120도, x 0.4043은 70도, x 0.3333은 60도 근사다.
    // 원본 03은 3x18m 직선이라 z 0.3333이 6m, 원본 06은 3x18m 경로라 x 0.8333, z 0.4444가 2.5x8m다
    // 돌진(06)은 프리뷰가 고정 왕복 좌표로 적은 위치와 각도를 쓰지 않고 서버가 알린 돌진 시작점과 방향에 놓는다.
    // 순차 타격의 단계별 각도는 서버 단계 각도(AimOffsetDegrees)와 같은 값이다
    /// <summary> 무철 패턴 VFX 배정표, 패턴 번호와 단계 또는 돌진 순번으로 찾는다 </summary>
    public static class MucheolVfxCatalog
    {
        /****************************************
        *              Constants
        ****************************************/

        public const float FAN_70_WIDTH   = 0.4043f;
        public const float FAN_60_WIDTH   = 0.33333f;
        public const float LINE_6M_LENGTH = 0.33333f;
        public const float DASH_8M_WIDTH  = 0.83333f;
        public const float DASH_8M_LENGTH = 0.44444f;
        public const float SIDE_YAW       = 30f;    // 도, 좌우 60도 구역의 중심 각도
        public const float GROUND_LIFT    = 0.025f; // m, 바닥 겹침을 피하는 높이, 프리뷰와 같다
        public const float DASH_PATH_LENGTH = 18f;  // m, 원본 06 경로 길이(크기 1)

        private struct Entry
        {
            public EMucheolVfxSource Source;
            public MucheolVfxCue[]   Cues;
        }

        /****************************************
        *                Fields
        ****************************************/

        private static readonly Dictionary<string, Entry> G_Entries = new Dictionary<string, Entry>
        {
            { MucheolMotionCatalog.P1_P01_ID, Make(EMucheolVfxSource.BasicStrike, Fan(1f)) },
            { MucheolMotionCatalog.P1_P02_ID, Make(EMucheolVfxSource.BasicStrike, Fan(FAN_70_WIDTH)) },
            { MucheolMotionCatalog.P1_P03_ID, Make(EMucheolVfxSource.BasicDash, Dash(DASH_8M_WIDTH, DASH_8M_LENGTH)) },
            { MucheolMotionCatalog.P1_P04_ID, Make(EMucheolVfxSource.BasicStrike, Uniform(EMucheolVfxTemplate.Circle02)) },
            { MucheolMotionCatalog.P1_P05_ID, Make(EMucheolVfxSource.StrikeChain, Fan(1f), Fan(FAN_70_WIDTH)) },
            { MucheolMotionCatalog.P1_P06_ID, Make(EMucheolVfxSource.BasicDash, Dash(1f, 1f)) },

            { MucheolMotionCatalog.P2_P01_ID, Make(EMucheolVfxSource.StrikeChain, Fan(FAN_60_WIDTH, -SIDE_YAW), Fan(FAN_60_WIDTH, SIDE_YAW)) },
            { MucheolMotionCatalog.P2_P02_ID, Make(EMucheolVfxSource.StrikeChain, Fan(FAN_70_WIDTH),
                new MucheolVfxCue(EMucheolVfxTemplate.Line03, new Vector3(1f, 1f, LINE_6M_LENGTH))) },
            { MucheolMotionCatalog.P2_P03_ID, Make(EMucheolVfxSource.MultiDash, Dash(DASH_8M_WIDTH, DASH_8M_LENGTH)) },
            { MucheolMotionCatalog.P2_P04_ID, Make(EMucheolVfxSource.StrikeChain, Uniform(EMucheolVfxTemplate.SmallWave04),
                Uniform(EMucheolVfxTemplate.LargeWave05)) },
            { MucheolMotionCatalog.P2_P05_ID, Make(EMucheolVfxSource.StrikeChain, Fan(FAN_60_WIDTH, -SIDE_YAW), Fan(FAN_60_WIDTH, SIDE_YAW),
                Fan(FAN_70_WIDTH)) },
            { MucheolMotionCatalog.P2_P06_ID, Make(EMucheolVfxSource.MultiDash, Dash(1f, 1f)) },
            { MucheolMotionCatalog.P2_P07_ID, Make(EMucheolVfxSource.Battlefield, Uniform(EMucheolVfxTemplate.Battlefield07)) },
        };

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 배정된 패턴 번호 전체, 시험과 기본 예고 숨김 등록에 쓴다 </summary>
        public static IEnumerable<string> PatternIds => G_Entries.Keys;

        /****************************************
        *                Methods
        ****************************************/

        public static bool Handles(string patternId)
        {
            return (patternId != null) && G_Entries.ContainsKey(patternId);
        }

        public static bool TryGetSource(string patternId, out EMucheolVfxSource source)
        {
            source = EMucheolVfxSource.BasicStrike;
            if ((patternId == null) || (!G_Entries.TryGetValue(patternId, out Entry entry)))
            {
                return false;
            }

            source = entry.Source;
            return true;
        }

        // 연속 돌진은 강화 시전(3회)처럼 배정보다 순번이 많으면 마지막 배정을 다시 쓴다
        /// <summary> 단계나 돌진 순번의 VFX, 순차 타격에서 배정보다 많은 단계는 없다고 답한다 </summary>
        public static bool TryGetCue(string patternId, int index, out MucheolVfxCue cue)
        {
            cue = default;
            if ((patternId == null) || (index < 0) || (!G_Entries.TryGetValue(patternId, out Entry entry)))
            {
                return false;
            }

            if (index < entry.Cues.Length)
            {
                cue = entry.Cues[index];
                return true;
            }

            if (entry.Source == EMucheolVfxSource.MultiDash)
            {
                cue = entry.Cues[entry.Cues.Length - 1];
                return true;
            }

            return false;
        }

        public static int CueCount(string patternId)
        {
            return ((patternId != null) && G_Entries.TryGetValue(patternId, out Entry entry)) ? entry.Cues.Length : 0;
        }

        private static Entry Make(EMucheolVfxSource source, params MucheolVfxCue[] cues)
        {
            return new Entry { Source = source, Cues = cues };
        }

        private static MucheolVfxCue Fan(float width, float yaw = 0f)
        {
            return new MucheolVfxCue(EMucheolVfxTemplate.Fan01, new Vector3(width, 1f, 1f), yaw);
        }

        private static MucheolVfxCue Dash(float width, float length)
        {
            return new MucheolVfxCue(EMucheolVfxTemplate.DashPath06, new Vector3(width, 1f, length));
        }

        private static MucheolVfxCue Uniform(EMucheolVfxTemplate template)
        {
            return new MucheolVfxCue(template, Vector3.one);
        }
    }
}
