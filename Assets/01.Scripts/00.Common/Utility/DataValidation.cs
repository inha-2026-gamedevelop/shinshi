// System
using System.Collections.Generic;

namespace Shinshi.Common
{
    /// <summary> GameDB 검증에서 쓰는 값 범위 검사, 오류 문장 형식을 한곳에서 맞춘다 </summary>
    public static class DataValidation
    {
        /****************************************
        *                Methods
        ****************************************/

        public static void RequirePositive(List<string> errors, string owner, string field, float value)
        {
            if (value > 0f)
            {
                return;
            }

            errors.Add($"{owner}.{field} = {value}: 0보다 커야 한다");
        }

        public static void RequirePositive(List<string> errors, string owner, string field, int value)
        {
            if (value > 0)
            {
                return;
            }

            errors.Add($"{owner}.{field} = {value}: 0보다 커야 한다");
        }

        public static void RequireNonNegative(List<string> errors, string owner, string field, float value)
        {
            if (value >= 0f)
            {
                return;
            }

            errors.Add($"{owner}.{field} = {value}: 0 이상이어야 한다");
        }

        public static void RequireNonNegative(List<string> errors, string owner, string field, int value)
        {
            if (value >= 0)
            {
                return;
            }

            errors.Add($"{owner}.{field} = {value}: 0 이상이어야 한다");
        }

        /// <summary> min 이상 max 이하인지 본다 </summary>
        public static void RequireRange(List<string> errors, string owner, string field, float value, float min, float max)
        {
            if ((value >= min) && (value <= max))
            {
                return;
            }

            errors.Add($"{owner}.{field} = {value}: {min} 이상 {max} 이하여야 한다");
        }

        /// <summary> 0보다 크고 max 이하인지 본다. 비율처럼 0이면 의미가 없는 값에 쓴다 </summary>
        public static void RequirePositiveAtMost(List<string> errors, string owner, string field, float value, float max)
        {
            if ((value > 0f) && (value <= max))
            {
                return;
            }

            errors.Add($"{owner}.{field} = {value}: 0보다 크고 {max} 이하여야 한다");
        }
    }
}
