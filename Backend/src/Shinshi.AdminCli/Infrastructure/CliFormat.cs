// System
using System;
using System.Globalization;

// Third Party
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Shinshi.AdminCli
{
    /// <summary> 사람이 읽는 출력의 값 표기와 입력 값 해석. 시각은 모두 UTC로 다룬다 </summary>
    internal static class CliFormat
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string NONE = "-";

        private const string TIME_FORMAT     = "yyyy-MM-dd HH:mm:ss'Z'";
        private const char   TIME_SEPARATOR  = 'T';
        private const char   UTC_DESIGNATOR  = 'Z';
        private const int    OFFSET_LENGTH   = 6;
        private const char   OFFSET_COLON    = ':';
        private const int    OFFSET_COLON_AT = 3;

        /****************************************
        *                Methods
        ****************************************/

        public static string Time(long unixMs)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime.ToString(TIME_FORMAT,
                CultureInfo.InvariantCulture);
        }

        public static string Time(long? unixMs)
        {
            return unixMs.HasValue ? Time(unixMs.Value) : NONE;
        }

        public static string Text(string? value)
        {
            return string.IsNullOrEmpty(value) ? NONE : value;
        }

        public static string Number(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string Number(long? value)
        {
            return value.HasValue ? Number(value.Value) : NONE;
        }

        public static string YesNo(bool value)
        {
            return value ? "예" : "아니오";
        }

        public static string Json(JToken? token)
        {
            return ((token == null) || (token.Type == JTokenType.Null)) ? NONE : token.ToString(Formatting.None);
        }

        /// <summary> 끝에 Z나 +09:00 같은 오프셋이 있는 ISO 8601만 받는다. 오프셋 없는 시각은 지역 시각과 헷갈리므로 거절한다 </summary>
        public static long ParseUtc(string text, string optionName)
        {
            string trimmed = text.Trim();
            bool hasOffset = trimmed.EndsWith(UTC_DESIGNATOR) || HasNumericOffset(trimmed);
            if ((trimmed.IndexOf(TIME_SEPARATOR) < 0) || (!hasOffset)
                || (!DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset parsed)))
            {
                throw new CliUsageException("--" + optionName + "는 2026-10-02T09:00:00Z 처럼 Z나 오프셋이 붙은 ISO 8601 시각이어야 합니다.");
            }

            return parsed.ToUnixTimeMilliseconds();
        }

        public static Guid ParseGuid(string text, string label)
        {
            if ((!Guid.TryParse(text, out Guid value)) || (value == Guid.Empty))
            {
                throw new CliUsageException(label + "는 GUID여야 합니다: " + text);
            }

            return value;
        }

        public static int ParseInt(string text, string optionName)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                throw new CliUsageException("--" + optionName + "는 정수여야 합니다: " + text);
            }

            return value;
        }

        public static long ParseLong(string text, string optionName)
        {
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            {
                throw new CliUsageException("--" + optionName + "는 정수여야 합니다: " + text);
            }

            return value;
        }

        // +09:00, -05:30 꼴인지 본다
        private static bool HasNumericOffset(string text)
        {
            if (text.Length < OFFSET_LENGTH)
            {
                return false;
            }

            string offset = text.Substring(text.Length - OFFSET_LENGTH);
            if (((offset[0] != '+') && (offset[0] != '-')) || (offset[OFFSET_COLON_AT] != OFFSET_COLON))
            {
                return false;
            }

            for (int i = 1; i < OFFSET_LENGTH; ++i)
            {
                if ((i != OFFSET_COLON_AT) && (!char.IsDigit(offset[i])))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
