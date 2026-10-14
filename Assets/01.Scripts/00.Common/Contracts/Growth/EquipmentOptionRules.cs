// System
using System.Collections.Generic;

namespace Shinshi.Contracts
{
    /// <summary> 장비 옵션의 종류 코드와 뽑기, 검증 계산. 백엔드와 전투 서버, 클라이언트 표시가 같은 식을 쓴다 </summary>
    public static class EquipmentOptionRules
    {
        // 값은 만분율 정수다(100 = 1%). 종류 0과 값 0은 옵션이 없는 정상 상태이고, 후보와 범위 검사는 종류가 0이 아닌 줄에만 한다.
        // 난수는 마을 서버가 뽑은 0에서 9999 사이 정수이며 여기서는 만들지 않는다. 같은 난수는 항상 같은 결과가 된다

        /****************************************
        *                Fields
        ****************************************/

        // EEquipmentOptionKind와 같은 값, 저장 데이터가 참조하므로 바꾸지 않는다
        public const int KIND_NONE         = 0;
        public const int KIND_ATTACK_RATE  = 1;
        public const int KIND_HEALTH_RATE  = 2;
        public const int KIND_DEFENSE_RATE = 3;
        public const int KIND_CRIT_CHANCE  = 4;
        public const int KIND_MAX          = KIND_CRIT_CHANCE;

        public const int LINE_COUNT  = 2;
        public const int ROLL_COUNT  = LINE_COUNT * 2; // 줄마다 종류와 값
        public const int ROLL_RANGE  = 10000;          // 난수는 0 이상 이 값 미만
        public const int VALUE_SCALE = 10000;          // 값을 비율로 바꿀 때 나누는 수
        public const int MAX_VALUE   = VALUE_SCALE;

        // rolls 배열 안의 자리
        public const int ROLL_LINE1_KIND  = 0;
        public const int ROLL_LINE1_VALUE = 1;
        public const int ROLL_LINE2_KIND  = 2;
        public const int ROLL_LINE2_VALUE = 3;

        /****************************************
        *                Methods
        ****************************************/

        public static bool IsValidRoll(int roll)
        {
            return (roll >= 0) && (roll < ROLL_RANGE);
        }

        /// <summary> 요청의 난수 묶음이 정확히 네 개이고 모두 범위 안인지 </summary>
        public static bool IsValidRolls(IReadOnlyList<int> rolls)
        {
            if ((rolls == null) || (rolls.Count != ROLL_COUNT))
            {
                return false;
            }

            for (int i = 0; i < rolls.Count; ++i)
            {
                if (!IsValidRoll(rolls[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary> 후보 가운데 하나, 후보가 비었거나 난수가 범위 밖이면 KIND_NONE </summary>
        public static int PickKind(int roll, IReadOnlyList<int> pool)
        {
            if ((pool == null) || (pool.Count == 0) || (!IsValidRoll(roll)))
            {
                return KIND_NONE;
            }

            return pool[(int)(((long)roll * pool.Count) / ROLL_RANGE)];
        }

        /// <summary> min부터 max까지 step 간격의 값 가운데 하나, 범위가 잘못됐으면 0 </summary>
        public static int PickValue(int roll, int min, int max, int step)
        {
            if ((!IsValidRange(min, max, step)) || (!IsValidRoll(roll)))
            {
                return 0;
            }

            int stepCount = ((max - min) / step) + 1;
            return min + ((int)(((long)roll * stepCount) / ROLL_RANGE) * step);
        }

        /// <summary> 값이 범위의 위쪽 구간에 드는지, ratioBasisPoints는 만분율(7500이면 위쪽 25%) </summary>
        public static bool IsHighTier(int value, int min, int max, int ratioBasisPoints)
        {
            return ((long)(value - min) * VALUE_SCALE) >= ((long)ratioBasisPoints * (max - min));
        }

        /// <summary> 저장된 줄 하나가 그 부위 후보와 등급 범위에 맞는지, 종류 0과 값 0은 옵션 없음으로 통과한다 </summary>
        public static bool IsValidLine(int kind, int value, IReadOnlyList<int> pool, int min, int max, int step)
        {
            if (kind == KIND_NONE)
            {
                return value == 0;
            }

            if ((!Contains(pool, kind)) || (!IsValidRange(min, max, step)))
            {
                return false;
            }

            return (value >= min) && (value <= max) && (((value - min) % step) == 0);
        }

        public static bool IsValidRange(int min, int max, int step)
        {
            return (step > 0) && (min > 0) && (max >= min) && (max <= MAX_VALUE) && (((max - min) % step) == 0);
        }

        public static bool Contains(IReadOnlyList<int> pool, int kind)
        {
            if (pool == null)
            {
                return false;
            }

            for (int i = 0; i < pool.Count; ++i)
            {
                if (pool[i] == kind)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
