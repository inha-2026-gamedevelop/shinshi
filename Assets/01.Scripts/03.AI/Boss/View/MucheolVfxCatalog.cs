// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Data;

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
        public const float GROUND_LIFT    = 0.08f;  // m, 바닥 겹침을 피하는 높이, 실전 맵 바닥에 묻혀 프리뷰(0.025)보다 올렸다
        public const float DASH_PATH_LENGTH = 18f;  // m, 원본 06 경로 길이(크기 1)

        // 하드 정의(mucheol_hard_...)는 이지와 같은 원본을 쓰되 범위가 커서 크기를 곱한다. 부채꼴 사거리 5m는 7m(1.4배, 폭도 같은 비율),
        // 원형은 5m가 6.5m(1.3배), 돌진 폭은 2.5m가 3m(1.2배)와 3m가 3.5m(7/6배), 순차 타격의 직선 길이 6m는 8m(4/3배)다.
        // 서버 하드 정의와 실행 데이터의 값과 같아야 하며, 값을 바꾸면 함께 바꾼다
        public  const float HARD_FAN_SCALE      = 1.4f;
        private const float HARD_CIRCLE_SCALE   = 1.3f;
        private const float HARD_DASH_8M_WIDTH  = 1.2f;
        private const float HARD_DASH_18M_WIDTH = 3.5f / 3f;
        private const float HARD_LINE_LENGTH    = 8f / 6f;

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
            { MucheolMotionCatalog.P1_P01_ID, Make(EMucheolVfxSource.ArmSwing, Fan(1f)) },
            { MucheolMotionCatalog.P1_P02_ID, Make(EMucheolVfxSource.BasicStrike, Fan(FAN_70_WIDTH)) },
            { MucheolMotionCatalog.P1_P03_ID, Make(EMucheolVfxSource.BasicDash, Dash(DASH_8M_WIDTH, DASH_8M_LENGTH)) },
            { MucheolMotionCatalog.P1_P04_ID, Make(EMucheolVfxSource.BasicStrike, Uniform(EMucheolVfxTemplate.Circle02)) },
            { MucheolMotionCatalog.P1_P05_ID, Make(EMucheolVfxSource.ArmChain, Fan(1f), Fan(FAN_70_WIDTH)) },
            { MucheolMotionCatalog.P1_P06_ID, Make(EMucheolVfxSource.BasicDash, Dash(1f, 1f)) },

            { MucheolMotionCatalog.P2_P01_ID, Make(EMucheolVfxSource.ArmChain, Fan(FAN_60_WIDTH, -SIDE_YAW), Fan(FAN_60_WIDTH, SIDE_YAW)) },
            { MucheolMotionCatalog.P2_P02_ID, Make(EMucheolVfxSource.StrikeChain, Fan(FAN_70_WIDTH),
                new MucheolVfxCue(EMucheolVfxTemplate.Line03, new Vector3(1f, 1f, LINE_6M_LENGTH))) },
            { MucheolMotionCatalog.P2_P03_ID, Make(EMucheolVfxSource.MultiDash, Dash(DASH_8M_WIDTH, DASH_8M_LENGTH)) },
            { MucheolMotionCatalog.P2_P04_ID, Make(EMucheolVfxSource.StrikeChain, Uniform(EMucheolVfxTemplate.SmallWave04),
                Uniform(EMucheolVfxTemplate.LargeWave05)) },
            { MucheolMotionCatalog.P2_P05_ID, Make(EMucheolVfxSource.ArmChain, Fan(FAN_60_WIDTH, -SIDE_YAW), Fan(FAN_60_WIDTH, SIDE_YAW),
                Fan(FAN_70_WIDTH)) },
            { MucheolMotionCatalog.P2_P06_ID, Make(EMucheolVfxSource.MultiDash, Dash(1f, 1f)) },
            { MucheolMotionCatalog.P2_P07_ID, Make(EMucheolVfxSource.Battlefield, Uniform(EMucheolVfxTemplate.Battlefield07)) },
        };

        // 하드 정의가 있는 이지 ID별 단계 크기 배율. 단계가 배율 수보다 많으면 마지막 배율을 쓴다
        private static readonly Dictionary<string, Vector3[]> G_HardScales = new Dictionary<string, Vector3[]>
        {
            { MucheolMotionCatalog.P1_P01_ID, new[] { FanScale() } },
            { MucheolMotionCatalog.P1_P02_ID, new[] { FanScale() } },
            { MucheolMotionCatalog.P1_P03_ID, new[] { DashScale(HARD_DASH_8M_WIDTH) } },
            { MucheolMotionCatalog.P1_P04_ID, new[] { CircleScale() } },
            { MucheolMotionCatalog.P1_P05_ID, new[] { FanScale() } },
            { MucheolMotionCatalog.P1_P06_ID, new[] { DashScale(HARD_DASH_18M_WIDTH) } },

            { MucheolMotionCatalog.P2_P01_ID, new[] { FanScale() } },
            { MucheolMotionCatalog.P2_P02_ID, new[] { FanScale(), new Vector3(1f, 1f, HARD_LINE_LENGTH) } },
            { MucheolMotionCatalog.P2_P03_ID, new[] { DashScale(HARD_DASH_8M_WIDTH) } },
            { MucheolMotionCatalog.P2_P04_ID, new[] { CircleScale() } },
            { MucheolMotionCatalog.P2_P05_ID, new[] { FanScale() } },
            { MucheolMotionCatalog.P2_P06_ID, new[] { DashScale(HARD_DASH_18M_WIDTH) } },
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
            patternId = BossPatternCatalogSO.ToMucheolBaseId(patternId);
            return (patternId != null) && G_Entries.ContainsKey(patternId);
        }

        /// <summary> 공격 범위 배율, 하드 정의는 부채꼴 사거리 배율이고 이지는 1이다 </summary>
        public static float GetRangeScale(string patternId)
        {
            return (BossPatternCatalogSO.ToMucheolBaseId(patternId) != patternId) ? HARD_FAN_SCALE : 1f;
        }

        public static bool TryGetSource(string patternId, out EMucheolVfxSource source)
        {
            source = EMucheolVfxSource.BasicStrike;
            patternId = BossPatternCatalogSO.ToMucheolBaseId(patternId);
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
            string baseId = BossPatternCatalogSO.ToMucheolBaseId(patternId);
            if ((baseId == null) || (index < 0) || (!G_Entries.TryGetValue(baseId, out Entry entry)))
            {
                return false;
            }

            int cueIndex = index;
            if (index >= entry.Cues.Length)
            {
                if (entry.Source != EMucheolVfxSource.MultiDash)
                {
                    return false;
                }

                cueIndex = entry.Cues.Length - 1;
            }

            cue = entry.Cues[cueIndex];

            // 하드 전용 ID면 범위가 커진 만큼 크기를 키운다
            if ((baseId != patternId) && G_HardScales.TryGetValue(baseId, out Vector3[] scales))
            {
                Vector3 multiplier = scales[Mathf.Min(cueIndex, scales.Length - 1)];
                cue = new MucheolVfxCue(cue.Template, Vector3.Scale(cue.Scale, multiplier), cue.YawDegrees);
            }

            return true;
        }

        public static int CueCount(string patternId)
        {
            patternId = BossPatternCatalogSO.ToMucheolBaseId(patternId);
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

        private static Vector3 FanScale()
        {
            return new Vector3(HARD_FAN_SCALE, 1f, HARD_FAN_SCALE);
        }

        private static Vector3 CircleScale()
        {
            return new Vector3(HARD_CIRCLE_SCALE, HARD_CIRCLE_SCALE, HARD_CIRCLE_SCALE);
        }

        private static Vector3 DashScale(float widthScale)
        {
            return new Vector3(widthScale, 1f, 1f);
        }

        private static MucheolVfxCue Uniform(EMucheolVfxTemplate template)
        {
            return new MucheolVfxCue(template, Vector3.one);
        }
    }
}
