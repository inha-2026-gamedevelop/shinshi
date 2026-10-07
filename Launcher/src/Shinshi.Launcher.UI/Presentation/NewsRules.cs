// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Project
using Shinshi.Contracts;

namespace Shinshi.Launcher.Presentation
{
    /// <summary> 서비스 공지를 소식 카드로 바꾸고 홈, 소식, 패치노트 탭에 나눠 담는 순수 계산 </summary>
    public static class NewsRules
    {
        /****************************************
        *                Fields
        ****************************************/

        // 홈 화면에 보이는 카드 수
        public const int HOME_COUNT = 3;

        // 마지막 수신이 이보다 오래됐으면 소식 탭을 열 때 다시 받는다
        public const int REFRESH_SECONDS = 60;

        public const string EMPTY_TEXT   = "새 소식이 없습니다";
        public const string FAILED_TEXT  = "소식을 불러오지 못했습니다";
        public const string LOADING_TEXT = "소식을 불러오는 중입니다";

        private const string DATE_FORMAT = "yyyy.MM.dd";

        // DateTimeOffset이 표현하는 유닉스 ms 범위(0001-01-01 ~ 9999-12-31)
        private const long MIN_UNIX_MS = -62135596800000L;
        private const long MAX_UNIX_MS = 253402300799999L;

        private const char CARRIAGE_RETURN = '\r';
        private const char LINE_FEED       = '\n';
        private const char TAB             = '\t';
        private const char SPACE           = ' ';

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 런처 대상(Launcher, Both) 공지만 시작 시각 내림차순으로 카드로 바꾼다. 시작 시각이 같으면 공지 ID 순이다.
        /// 제목이 비었거나 시각이 범위를 벗어난 공지는 건너뛴다 </summary>
        public static IReadOnlyList<NewsItem> ToItems(IReadOnlyList<ServiceNotice>? notices)
        {
            List<NewsItem> items = new List<NewsItem>();
            if (notices == null)
            {
                return items;
            }

            foreach (ServiceNotice notice in notices)
            {
                if (TryToItem(notice, out NewsItem? item))
                {
                    items.Add(item);
                }
            }

            items.Sort(CompareNewestFirst);
            return items;
        }

        /// <summary> 홈 화면에 보일 앞쪽 카드 </summary>
        public static IReadOnlyList<NewsItem> TakeHome(IReadOnlyList<NewsItem> items)
        {
            ArgumentNullException.ThrowIfNull(items);

            List<NewsItem> home = new List<NewsItem>();
            for (int i = 0; (i < items.Count) && (i < HOME_COUNT); ++i)
            {
                home.Add(items[i]);
            }

            return home;
        }

        /// <summary> 패치노트 탭은 Update 분류만 보인다 </summary>
        public static IReadOnlyList<NewsItem> FilterPatchNotes(IReadOnlyList<NewsItem> items)
        {
            ArgumentNullException.ThrowIfNull(items);

            List<NewsItem> notes = new List<NewsItem>();
            foreach (NewsItem item in items)
            {
                if (item.Category == ENewsCategory.Update)
                {
                    notes.Add(item);
                }
            }

            return notes;
        }

        /// <summary> category 문자열을 분류로 바꾼다. 비었거나 모르는 값은 Notice다 </summary>
        public static ENewsCategory ParseCategory(string? category)
        {
            switch (category)
            {
                case ServiceNotice.CATEGORY_UPDATE:
                    return ENewsCategory.Update;
                case ServiceNotice.CATEGORY_EVENT:
                    return ENewsCategory.Event;
                case ServiceNotice.CATEGORY_MAINTENANCE:
                    return ENewsCategory.Maintenance;
                default:
                    return ENewsCategory.Notice;
            }
        }

        public static string ToCategoryText(ENewsCategory category)
        {
            switch (category)
            {
                case ENewsCategory.Notice:
                    return "공지";
                case ENewsCategory.Update:
                    return "업데이트";
                case ENewsCategory.Event:
                    return "이벤트";
                case ENewsCategory.Maintenance:
                    return "점검";
                default:
                    return string.Empty;
            }
        }

        public static string FormatDate(DateTimeOffset startsAt, TimeZoneInfo zone)
        {
            ArgumentNullException.ThrowIfNull(zone);

            return TimeZoneInfo.ConvertTime(startsAt, zone).ToString(DATE_FORMAT, CultureInfo.InvariantCulture);
        }

        /// <summary> 목록 영역에 카드 대신 보일 안내. 실패하면 이전 목록이 있어도 실패 안내를 보인다 </summary>
        public static string GetStatusText(ENewsLoadState state, int itemCount)
        {
            if (state == ENewsLoadState.Failed)
            {
                return FAILED_TEXT;
            }

            if (itemCount > 0)
            {
                return string.Empty;
            }

            switch (state)
            {
                case ENewsLoadState.Loaded:
                    return EMPTY_TEXT;
                case ENewsLoadState.Loading:
                    return LOADING_TEXT;
                default:
                    return string.Empty;
            }
        }

        public static bool IsRetryVisible(ENewsLoadState state) { return state == ENewsLoadState.Failed; }

        /// <summary> 소식 탭을 열 때 다시 받을지. 받은 적이 없으면(null) 받는다 </summary>
        public static bool ShouldRefresh(TimeSpan? sinceLastFetch)
        {
            return !sinceLastFetch.HasValue || (sinceLastFetch.Value > TimeSpan.FromSeconds(REFRESH_SECONDS));
        }

        /// <summary> 일반 텍스트로 정리한다. 줄바꿈은 LF로 통일해 유지하고 탭을 뺀 제어 문자는 지운다.
        /// HTML과 링크는 해석하지 않고 글자 그대로 보인다 </summary>
        public static string SanitizeBody(string? body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(body.Length);
            for (int i = 0; i < body.Length; ++i)
            {
                char c = body[i];
                if (c == CARRIAGE_RETURN)
                {
                    // CRLF는 줄바꿈 하나로 센다
                    if (((i + 1) < body.Length) && (body[i + 1] == LINE_FEED))
                    {
                        ++i;
                    }

                    builder.Append(LINE_FEED);
                    continue;
                }

                if ((c == LINE_FEED) || (c == TAB) || !char.IsControl(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Trim();
        }

        /// <summary> 제목은 한 줄이다. 줄바꿈과 제어 문자는 공백 하나로 바꾼다 </summary>
        public static string SanitizeTitle(string? title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(title.Length);
            foreach (char c in title)
            {
                builder.Append(char.IsControl(c) ? SPACE : c);
            }

            return builder.ToString().Trim();
        }

        private static bool TryToItem(ServiceNotice? notice, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out NewsItem? item)
        {
            item = null;
            if (notice == null)
            {
                return false;
            }

            if ((notice.Target != ServiceNotice.TARGET_LAUNCHER) && (notice.Target != ServiceNotice.TARGET_BOTH))
            {
                return false;
            }

            string title = SanitizeTitle(notice.Title);
            if ((title.Length == 0) || (notice.StartsAtUnixMs < MIN_UNIX_MS) || (notice.StartsAtUnixMs > MAX_UNIX_MS))
            {
                return false;
            }

            item = new NewsItem(notice.NoticeId ?? string.Empty, ParseCategory(notice.Category), title,
                SanitizeBody(notice.Body), notice.Severity, DateTimeOffset.FromUnixTimeMilliseconds(notice.StartsAtUnixMs));
            return true;
        }

        private static int CompareNewestFirst(NewsItem left, NewsItem right)
        {
            int byTime = right.StartsAt.CompareTo(left.StartsAt);
            return (byTime != 0) ? byTime : string.CompareOrdinal(left.Id, right.Id);
        }
    }
}
