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

        /// <summary> 카탈로그 항목의 빈 칸, ID 공백과 중복, 표시 키 누락을 본다 </summary>
        public static void RequireDefinitions<T>(List<string> errors, string owner, string field, IReadOnlyList<T> definitions) where T : class, IDefinition
        {
            if ((definitions == null) || (definitions.Count == 0))
            {
                errors.Add($"{owner}.{field}: 항목이 없다");
                return;
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < definitions.Count; ++i)
            {
                T item = definitions[i];
                if (item == null)
                {
                    errors.Add($"{owner}.{field}[{i}]: 항목이 비어 있다");
                    continue;
                }

                string id = item.DefinitionId;
                if (string.IsNullOrWhiteSpace(id) || id.Contains(" "))
                {
                    errors.Add($"{owner}.{field}[{i}]: definitionId '{id}'가 비었거나 공백을 포함한다");
                    continue;
                }

                if (!seen.Add(id))
                {
                    errors.Add($"{owner}.{field}[{i}]: definitionId '{id}'가 중복된다");
                }

                if (string.IsNullOrWhiteSpace(item.DisplayKey))
                {
                    errors.Add($"{owner}.{field}[{id}]: 표시 키가 비어 있다");
                }
            }
        }

        /// <summary> 항목 수가 기획에서 정한 개수와 같은지 본다 </summary>
        public static void RequireCount<T>(List<string> errors, string owner, string field, IReadOnlyList<T> items, int expected)
        {
            int count = (items == null) ? 0 : items.Count;
            if (count == expected)
            {
                return;
            }

            errors.Add($"{owner}.{field}: 항목 수 {count} != {expected}");
        }

        /// <summary> 다른 카탈로그 ID 참조가 실제로 있는지 본다 </summary>
        public static void RequireReference(List<string> errors, string owner, string field, string definitionId, bool exists)
        {
            if (exists)
            {
                return;
            }

            errors.Add($"{owner}.{field}: 참조 ID '{definitionId}'가 대상 카탈로그에 없다");
        }
    }
}
