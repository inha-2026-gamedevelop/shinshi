// System
using System;
using System.Globalization;

namespace Shinshi.Admin.Web.Models
{
    /// <summary> 운영 웹의 시각 기준(KST, UTC+9) 변환. 입력은 datetime-local 문자열, API는 UTC 유닉스 밀리초다 </summary>
    public static class AdminTime
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string INPUT_FORMAT = "yyyy-MM-dd'T'HH:mm";
        /// <summary> 달력 입력이 받는 범위. datetime-local의 min/max와 같다 </summary>
        public const string MIN_INPUT = "2000-01-01T00:00";
        public const string MAX_INPUT = "2100-12-31T23:59";

        private static readonly string[] G_InputFormats =
        {
            "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFF"
        };

        /****************************************
        *              Properties
        ****************************************/

        public static TimeSpan KstOffset { get; } = TimeSpan.FromHours(9);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 시각을 datetime-local 입력 값(KST, 분 단위)으로 바꾼다 </summary>
        public static string ToInput(DateTimeOffset moment)
        {
            return moment.ToOffset(KstOffset).ToString(INPUT_FORMAT, CultureInfo.InvariantCulture);
        }

        /// <summary> datetime-local 값(KST)을 시각으로 바꾼다. 형식이 틀리면 false </summary>
        public static bool TryParseInput(string? text, out DateTimeOffset moment)
        {
            moment = default;
            if ((string.IsNullOrWhiteSpace(text))
                || (!DateTime.TryParseExact(text.Trim(), G_InputFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime local)))
            {
                return false;
            }
            if ((local.Year < 2000) || (local.Year > 2100))
            {
                return false;
            }
            moment = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), KstOffset);
            return true;
        }

        /// <summary> 빈 값은 null, 형식이 틀리면 ArgumentException </summary>
        public static long? ParseUnixMs(string? text, string label)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            if (!TryParseInput(text, out DateTimeOffset moment))
            {
                throw new ArgumentException(label + " 시각 형식을 확인하세요. 달력에서 날짜와 시간을 선택하세요.");
            }
            return moment.ToUnixTimeMilliseconds();
        }

        /// <summary> 빈 값이면 기본 시각을 쓰고 적용했는지 알려 준다 </summary>
        public static long ResolveOrDefault(string? text, long defaultUnixMs, string label, out bool defaulted)
        {
            long? parsed = ParseUnixMs(text, label);
            defaulted = !parsed.HasValue;
            return parsed ?? defaultUnixMs;
        }

        public static string FormatKst(long unixMs)
        {
            return IsValid(unixMs) ? Format(unixMs, "yyyy-MM-dd HH:mm") + " KST"
                : unixMs.ToString(CultureInfo.InvariantCulture);
        }

        public static string FormatKstSeconds(long unixMs)
        {
            return IsValid(unixMs) ? Format(unixMs, "yyyy-MM-dd HH:mm:ss") + " KST"
                : unixMs.ToString(CultureInfo.InvariantCulture);
        }

        public static string FormatUtc(long unixMs)
        {
            return IsValid(unixMs)
                ? DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
                : unixMs.ToString(CultureInfo.InvariantCulture);
        }

        public static string FormatIso(long unixMs)
        {
            return IsValid(unixMs)
                ? DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        /// <summary> 시작 입력(비면 지금)에 간격을 더한 종료 입력 값 </summary>
        public static string AddToInput(string? baseInput, TimeSpan delta, DateTimeOffset now)
        {
            DateTimeOffset origin = TryParseInput(baseInput, out DateTimeOffset parsed) ? parsed : now;
            DateTimeOffset result;
            try
            {
                result = origin + delta;
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new ArgumentException("지원하는 날짜 범위(2000~2100년)를 벗어났습니다.");
            }
            if ((result.Year > 2100) || (result.Year < 2000))
            {
                throw new ArgumentException("지원하는 날짜 범위(2000~2100년)를 벗어났습니다.");
            }
            return ToInput(result);
        }

        /// <summary> 로그 기간 빠른 선택. 종료가 비면 지금까지다 </summary>
        public static (string From, string To) Preset(string key, DateTimeOffset now)
        {
            DateTimeOffset kstNow = now.ToOffset(KstOffset);
            DateTimeOffset today = new(kstNow.Year, kstNow.Month, kstNow.Day, 0, 0, 0, KstOffset);
            return key switch
            {
                "today" => (ToInput(today), ToInput(today.AddDays(1))),
                "yesterday" => (ToInput(today.AddDays(-1)), ToInput(today)),
                "7d" => (ToInput(now.AddDays(-7)), string.Empty),
                "30d" => (ToInput(now.AddDays(-30)), string.Empty),
                _ => (string.Empty, string.Empty)
            };
        }

        private static string Format(long unixMs, string format)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToOffset(KstOffset)
                .ToString(format, CultureInfo.InvariantCulture);
        }

        private static bool IsValid(long unixMs)
        {
            return (unixMs >= DateTimeOffset.MinValue.ToUnixTimeMilliseconds())
                && (unixMs <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds() - 24L * 3600 * 1000);
        }
    }
}
