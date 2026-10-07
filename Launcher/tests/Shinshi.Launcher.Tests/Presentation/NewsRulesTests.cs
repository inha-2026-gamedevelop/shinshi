// System
using System;
using System.Collections.Generic;
using System.Linq;

// Third-party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Launcher.Presentation;

namespace Shinshi.Launcher.Tests.Presentation
{
    /// <summary> 서비스 공지를 소식 카드로 바꾸는 규칙, 탭별 분배, 안내 문구, 갱신 판단 </summary>
    public sealed class NewsRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long BASE_MS = 1_800_000_000_000L;
        private const long HOUR_MS = 60L * 60L * 1000L;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void ToItems_FiltersToLauncherAndBothTargets()
        {
            ServiceNotice[] notices =
            {
                Notice("launcher", ServiceNotice.TARGET_LAUNCHER, 1),
                Notice("both", ServiceNotice.TARGET_BOTH, 2),
                Notice("game", ServiceNotice.TARGET_GAME, 3)
            };

            IReadOnlyList<NewsItem> items = NewsRules.ToItems(notices);

            Assert.Equal(new[] { "both", "launcher" }, items.Select(item => item.Id));
        }

        [Fact]
        public void ToItems_SortsByStartTimeDescendingThenId()
        {
            ServiceNotice[] notices =
            {
                Notice("b-old", ServiceNotice.TARGET_BOTH, 1),
                Notice("z-new", ServiceNotice.TARGET_BOTH, 5),
                Notice("b-tie", ServiceNotice.TARGET_BOTH, 3),
                Notice("a-tie", ServiceNotice.TARGET_BOTH, 3)
            };

            IReadOnlyList<NewsItem> items = NewsRules.ToItems(notices);

            Assert.Equal(new[] { "z-new", "a-tie", "b-tie", "b-old" }, items.Select(item => item.Id));
        }

        [Fact]
        public void ToItems_MissingCategory_DefaultsToNotice()
        {
            ServiceNotice withoutCategory = new ServiceNotice("a", "title", "body", ServiceNotice.TARGET_LAUNCHER,
                ServiceNotice.SEVERITY_INFO, BASE_MS, BASE_MS + HOUR_MS);
            ServiceNotice unknownCategory = new ServiceNotice("b", "title", "body", ServiceNotice.TARGET_LAUNCHER,
                ServiceNotice.SEVERITY_INFO, BASE_MS, BASE_MS + HOUR_MS, "Rumor");

            IReadOnlyList<NewsItem> items = NewsRules.ToItems(new[] { withoutCategory, unknownCategory });

            Assert.All(items, item => Assert.Equal(ENewsCategory.Notice, item.Category));
        }

        [Theory]
        [InlineData(ServiceNotice.CATEGORY_NOTICE, ENewsCategory.Notice)]
        [InlineData(ServiceNotice.CATEGORY_UPDATE, ENewsCategory.Update)]
        [InlineData(ServiceNotice.CATEGORY_EVENT, ENewsCategory.Event)]
        [InlineData(ServiceNotice.CATEGORY_MAINTENANCE, ENewsCategory.Maintenance)]
        [InlineData("", ENewsCategory.Notice)]
        [InlineData(null, ENewsCategory.Notice)]
        public void ParseCategory_MapsKnownNamesAndDefaultsToNotice(string? category, ENewsCategory expected)
        {
            Assert.Equal(expected, NewsRules.ParseCategory(category));
        }

        [Fact]
        public void ToItems_CarriesFieldsAndSkipsUnusableNotices()
        {
            ServiceNotice good = new ServiceNotice("id-1", "  제목  ", "본문", ServiceNotice.TARGET_LAUNCHER,
                ServiceNotice.SEVERITY_WARNING, BASE_MS, BASE_MS + HOUR_MS, ServiceNotice.CATEGORY_UPDATE);
            ServiceNotice blankTitle = Notice("blank", ServiceNotice.TARGET_BOTH, 1, title: "  ");
            ServiceNotice badTime = new ServiceNotice("bad", "t", "b", ServiceNotice.TARGET_BOTH, 0, long.MaxValue, long.MaxValue);

            IReadOnlyList<NewsItem> items = NewsRules.ToItems(new[] { good, blankTitle, badTime, null! });

            NewsItem item = Assert.Single(items);
            Assert.Equal("id-1", item.Id);
            Assert.Equal("제목", item.Title);
            Assert.Equal("본문", item.Body);
            Assert.Equal(ServiceNotice.SEVERITY_WARNING, item.Severity);
            Assert.Equal(ENewsCategory.Update, item.Category);
            Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(BASE_MS), item.StartsAt);
        }

        [Fact]
        public void ToItems_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Empty(NewsRules.ToItems(null));
            Assert.Empty(NewsRules.ToItems(Array.Empty<ServiceNotice>()));
        }

        [Fact]
        public void TakeHome_ReturnsFirstThree()
        {
            IReadOnlyList<NewsItem> items = NewsRules.ToItems(new[]
            {
                Notice("1", ServiceNotice.TARGET_BOTH, 1), Notice("2", ServiceNotice.TARGET_BOTH, 2),
                Notice("3", ServiceNotice.TARGET_BOTH, 3), Notice("4", ServiceNotice.TARGET_BOTH, 4),
                Notice("5", ServiceNotice.TARGET_BOTH, 5)
            });

            IReadOnlyList<NewsItem> home = NewsRules.TakeHome(items);

            Assert.Equal(NewsRules.HOME_COUNT, home.Count);
            Assert.Equal(new[] { "5", "4", "3" }, home.Select(item => item.Id));
            Assert.Equal(5, items.Count);
        }

        [Fact]
        public void FilterPatchNotes_KeepsOnlyUpdateCategoryInOrder()
        {
            IReadOnlyList<NewsItem> items = NewsRules.ToItems(new[]
            {
                Notice("event", ServiceNotice.TARGET_BOTH, 4, ServiceNotice.CATEGORY_EVENT),
                Notice("update-new", ServiceNotice.TARGET_BOTH, 3, ServiceNotice.CATEGORY_UPDATE),
                Notice("notice", ServiceNotice.TARGET_BOTH, 2),
                Notice("update-old", ServiceNotice.TARGET_BOTH, 1, ServiceNotice.CATEGORY_UPDATE)
            });

            IReadOnlyList<NewsItem> notes = NewsRules.FilterPatchNotes(items);

            Assert.Equal(new[] { "update-new", "update-old" }, notes.Select(item => item.Id));
        }

        [Fact]
        public void SanitizeBody_KeepsLineBreaksAndStripsControlCharacters()
        {
            string body = "첫 줄\r\n둘째 줄\r셋째\u0007 줄​\n\n\t들여쓰기";

            string text = NewsRules.SanitizeBody(body);

            Assert.Equal("첫 줄\n둘째 줄\n셋째 줄​\n\n\t들여쓰기", text);
        }

        [Fact]
        public void SanitizeBody_DoesNotInterpretMarkupOrLinks()
        {
            const string BODY = "<b>굵게</b> https://example.test/x <a href=\"javascript:1\">링크</a>";

            Assert.Equal(BODY, NewsRules.SanitizeBody(BODY));
        }

        [Fact]
        public void SanitizeTitle_CollapsesLineBreaksToSpaces()
        {
            Assert.Equal("가 나", NewsRules.SanitizeTitle("\n가\r나\n"));
            Assert.Equal("가 나", NewsRules.SanitizeTitle("가\n나"));
        }

        [Fact]
        public void FormatDate_UsesGivenTimeZone()
        {
            DateTimeOffset startsAt = new DateTimeOffset(2026, 10, 7, 23, 30, 0, TimeSpan.Zero);
            TimeZoneInfo seoul = TimeZoneInfo.CreateCustomTimeZone("seoul", TimeSpan.FromHours(9), "seoul", "seoul");

            Assert.Equal("2026.10.07", NewsRules.FormatDate(startsAt, TimeZoneInfo.Utc));
            Assert.Equal("2026.10.08", NewsRules.FormatDate(startsAt, seoul));
        }

        [Theory]
        [InlineData(ENewsLoadState.Loaded, 0, NewsRules.EMPTY_TEXT)]
        [InlineData(ENewsLoadState.Loaded, 2, "")]
        [InlineData(ENewsLoadState.Failed, 0, NewsRules.FAILED_TEXT)]
        [InlineData(ENewsLoadState.Failed, 2, NewsRules.FAILED_TEXT)]
        [InlineData(ENewsLoadState.Loading, 0, NewsRules.LOADING_TEXT)]
        [InlineData(ENewsLoadState.Loading, 2, "")]
        [InlineData(ENewsLoadState.Idle, 0, "")]
        public void GetStatusText_ByStateAndCount(ENewsLoadState state, int count, string expected)
        {
            Assert.Equal(expected, NewsRules.GetStatusText(state, count));
        }

        [Fact]
        public void IsRetryVisible_OnlyWhenFailed()
        {
            Assert.True(NewsRules.IsRetryVisible(ENewsLoadState.Failed));
            Assert.False(NewsRules.IsRetryVisible(ENewsLoadState.Loaded));
            Assert.False(NewsRules.IsRetryVisible(ENewsLoadState.Loading));
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData(0, false)]
        [InlineData(60, false)]
        [InlineData(61, true)]
        public void ShouldRefresh_WhenNeverFetchedOrOlderThanSixtySeconds(int? seconds, bool expected)
        {
            TimeSpan? since = seconds.HasValue ? TimeSpan.FromSeconds(seconds.Value) : null;

            Assert.Equal(expected, NewsRules.ShouldRefresh(since));
        }

        private static ServiceNotice Notice(string id, string target, int hoursAfterBase,
            string category = ServiceNotice.CATEGORY_NOTICE, string title = "제목")
        {
            long startsAt = BASE_MS + (hoursAfterBase * HOUR_MS);
            return new ServiceNotice(id, title, "본문", target, ServiceNotice.SEVERITY_INFO, startsAt, startsAt + HOUR_MS, category);
        }
    }
}
