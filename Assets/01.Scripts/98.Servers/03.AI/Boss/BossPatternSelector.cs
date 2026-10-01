// System
using System.Collections.Generic;

using BossDefinition    = Shinshi.Data.BossPatternCatalogSO.BossDefinition;
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    /// <summary> 상황 구간 계산과 가중치 선택, 상태가 없는 순수 계산이다 </summary>
    public static class BossPatternSelector
    {
        /****************************************
        *                Fields
        ****************************************/

        // 거리와 방향과 주변 인원 구간의 열 번호
        private const int BAND_FIRST  = 0;
        private const int BAND_SECOND = 1;
        private const int BAND_THIRD  = 2;

        private const int NEARBY_SECOND_COUNT = 2;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 수평 거리로 구간을 정한다, 근거리는 NearDistanceMax 미만, 중거리는 MidDistanceMax 미만이다 </summary>
        public static int GetDistanceBand(float distance, BossDefinition boss)
        {
            if (distance < boss.NearDistanceMax)
            {
                return BAND_FIRST;
            }

            return (distance < boss.MidDistanceMax) ? BAND_SECOND : BAND_THIRD;
        }

        /// <summary> 정면 대비 절대각으로 구간을 정한다, 경계값은 앞 구간에 포함한다 </summary>
        public static int GetDirectionBand(float angleDegrees, BossDefinition boss)
        {
            if (angleDegrees <= boss.FrontAngleMax)
            {
                return BAND_FIRST;
            }

            return (angleDegrees <= boss.SideAngleMax) ? BAND_SECOND : BAND_THIRD;
        }

        // 0명은 1명 열, 3명 이상은 3명 열을 쓴다
        public static int GetNearbyBand(int count)
        {
            if (count < NEARBY_SECOND_COUNT)
            {
                return BAND_FIRST;
            }

            return (count == NEARBY_SECOND_COUNT) ? BAND_SECOND : BAND_THIRD;
        }

        /// <summary> 기본값 x 거리 x 방향 x 주변 x 최근 기록 배율, 배율 표가 비어 있으면 0이다 </summary>
        public static float ComputeWeight(PatternDefinition pattern, int distanceBand, int directionBand, int nearbyBand, int recentSlot)
        {
            float distance = Multiplier(pattern.DistanceMultipliers, distanceBand);
            float direction = Multiplier(pattern.DirectionMultipliers, directionBand);
            float nearby = Multiplier(pattern.NearbyMultipliers, nearbyBand);
            float recent = Multiplier(pattern.RecentUseMultipliers, recentSlot);

            return pattern.BaseWeight * distance * direction * nearby * recent;
        }

        // 합이 0 이하이면 -1이다. roll01은 0 이상 1 미만이고 가중치 비율대로 뽑으며 최고값을 고정 선택하지 않는다
        public static int Pick(IReadOnlyList<float> weights, float roll01)
        {
            float total = 0f;
            for (int i = 0; i < weights.Count; ++i)
            {
                total += weights[i];
            }

            if (total <= 0f)
            {
                return -1;
            }

            float target = roll01 * total;
            float cumulative = 0f;
            int lastPositive = -1;
            for (int i = 0; i < weights.Count; ++i)
            {
                if (weights[i] <= 0f)
                {
                    continue;
                }

                cumulative += weights[i];
                lastPositive = i;
                if (target < cumulative)
                {
                    return i;
                }
            }

            // 부동소수 누적 오차로 끝까지 닿지 못한 경우
            return lastPositive;
        }

        private static float Multiplier(IReadOnlyList<float> table, int index)
        {
            if ((table == null) || (index < 0) || (index >= table.Count))
            {
                return 0f;
            }

            return table[index];
        }
    }
}
