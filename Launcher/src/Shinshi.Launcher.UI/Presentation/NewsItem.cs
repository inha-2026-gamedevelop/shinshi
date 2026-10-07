// System
using System;

namespace Shinshi.Launcher.Presentation
{
    /// <summary> 소식 카드 하나. 서비스 공지 한 건을 화면용으로 옮긴 값이며 본문은 일반 텍스트다 </summary>
    public sealed class NewsItem
    {
        /****************************************
        *              Properties
        ****************************************/

        public string         Id         { get; }
        public ENewsCategory  Category   { get; }
        public string         Title      { get; }
        public string         Body       { get; }
        public int            Severity   { get; }
        public DateTimeOffset StartsAt   { get; }

        // 화면에 보일 날짜. 이 PC의 시간대 기준이다
        public string         Date         => NewsRules.FormatDate(StartsAt, TimeZoneInfo.Local);
        public string         CategoryText => NewsRules.ToCategoryText(Category);
        public bool           HasBody      => Body.Length > 0;

        /****************************************
        *                Methods
        ****************************************/

        public NewsItem(string id, ENewsCategory category, string title, string body, int severity, DateTimeOffset startsAt)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(title);
            ArgumentNullException.ThrowIfNull(body);

            Id       = id;
            Category = category;
            Title    = title;
            Body     = body;
            Severity = severity;
            StartsAt = startsAt;
        }
    }
}
