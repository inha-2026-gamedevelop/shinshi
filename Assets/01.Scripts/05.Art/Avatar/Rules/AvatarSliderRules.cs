// System
using System;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Art
{
    /// <summary> 저장된 외형 값을 UMA에 넣을 값으로 바꾸는 계산 </summary>
    public static class AvatarSliderRules
    {
        // 캐릭터 생성 화면과 원격 표시가 같은 결과를 내야 하므로 변환을 이곳 하나에 둔다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 0~255 저장값을 DNA 최소, 중립, 최대 사이 값으로 바꾼다, 128이 중립이다 </summary>
        public static float ToDnaValue(int stored, float dnaMin, float dnaNeutral, float dnaMax, bool inverted)
        {
            float weight = ToSignedWeight(stored);
            if (inverted)
            {
                weight = -weight;
            }

            if (weight < 0f)
            {
                return dnaNeutral + (weight * (dnaNeutral - dnaMin));
            }

            return dnaNeutral + (weight * (dnaMax - dnaNeutral));
        }

        /// <summary> 0~255 저장값을 0~1로 바꾼다, 피부색 그라데이션 위치에 쓴다 </summary>
        public static float ToUnitValue(int stored)
        {
            return (float)ClampStored(stored) / CharacterAppearance.VALUE_MAX;
        }

        /// <summary> 팔레트 밖 ID는 첫 색으로 대신한다, 팔레트가 비면 -1 </summary>
        public static int ClampPaletteIndex(int paletteId, int paletteCount)
        {
            if (paletteCount <= 0)
            {
                return -1;
            }

            if ((paletteId < 0) || (paletteId >= paletteCount))
            {
                return 0;
            }

            return paletteId;
        }

        // 중립 아래는 128칸, 위는 127칸이라 양쪽 끝이 정확히 -1과 1이 되도록 나눠서 잰다
        private static float ToSignedWeight(int stored)
        {
            int value = ClampStored(stored);
            if (value < CharacterAppearance.SLIDER_NEUTRAL)
            {
                return -(float)(CharacterAppearance.SLIDER_NEUTRAL - value) / CharacterAppearance.SLIDER_NEUTRAL;
            }

            return (float)(value - CharacterAppearance.SLIDER_NEUTRAL)
                / (CharacterAppearance.VALUE_MAX - CharacterAppearance.SLIDER_NEUTRAL);
        }

        private static int ClampStored(int stored)
        {
            return Math.Max(CharacterAppearance.VALUE_MIN, Math.Min(CharacterAppearance.VALUE_MAX, stored));
        }
    }
}
