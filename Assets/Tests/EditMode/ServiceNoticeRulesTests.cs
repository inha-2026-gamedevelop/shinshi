// System
using System.Collections.Generic;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public sealed class ServiceNoticeRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long   NOW_MS   = 1_000_000L;
        private const long   START_MS = 900_000L;
        private const long   END_MS   = 2_000_000L;
        private const double NOW_SEC  = 100.0;

        private ServiceNoticeTracker _tracker = null;
        private List<string>         _lines   = null;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _tracker = new ServiceNoticeTracker();
            _lines   = new List<string>();
        }

        /****************************************
        *               Target
        ****************************************/

        [TestCase(ServiceNotice.TARGET_GAME, true)]
        [TestCase(ServiceNotice.TARGET_BOTH, true)]
        [TestCase(ServiceNotice.TARGET_LAUNCHER, false)]
        [TestCase("game", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsForGame_Target_FiltersLauncherOnly(string target, bool expected)
        {
            Assert.That(ServiceNoticeRules.IsForGame(Make("a", target)), Is.EqualTo(expected));
        }

        [Test]
        public void IsForGame_NullNotice_ReturnsFalse()
        {
            Assert.That(ServiceNoticeRules.IsForGame(null), Is.False);
        }

        /****************************************
        *               Period
        ****************************************/

        [TestCase(START_MS - 1, false)]
        [TestCase(START_MS, true)]
        [TestCase(END_MS - 1, true)]
        [TestCase(END_MS, false)]
        public void IsActive_Boundaries_StartInclusiveEndExclusive(long now, bool expected)
        {
            Assert.That(ServiceNoticeRules.IsActive(Make("a", ServiceNotice.TARGET_GAME), now), Is.EqualTo(expected));
        }

        [Test]
        public void IsActive_NoEnd_StaysActive()
        {
            var open = new ServiceNotice("a", "t", "b", ServiceNotice.TARGET_GAME, ServiceNotice.SEVERITY_INFO, START_MS, 0L);
            Assert.That(ServiceNoticeRules.IsActive(open, END_MS * 10), Is.True);
        }

        /****************************************
        *               Repeat
        ****************************************/

        [Test]
        public void ShouldShow_FirstTime_ShowsAnySeverity()
        {
            Assert.That(ServiceNoticeRules.ShouldShow(false, 0.0, NOW_SEC, ServiceNotice.SEVERITY_INFO), Is.True);
        }

        [Test]
        public void ShouldShow_SeenNonCritical_NeverRepeats()
        {
            double later = NOW_SEC + (Constants.Chat.NOTICE_CRITICAL_REPEAT_SECONDS * 10.0);
            Assert.That(ServiceNoticeRules.ShouldShow(true, NOW_SEC, later, ServiceNotice.SEVERITY_WARNING), Is.False);
        }

        [Test]
        public void ShouldShow_SeenCritical_RepeatsAfterInterval()
        {
            double before = NOW_SEC + Constants.Chat.NOTICE_CRITICAL_REPEAT_SECONDS - 1.0;
            double after  = NOW_SEC + Constants.Chat.NOTICE_CRITICAL_REPEAT_SECONDS;
            Assert.That(ServiceNoticeRules.ShouldShow(true, NOW_SEC, before, ServiceNotice.SEVERITY_CRITICAL), Is.False);
            Assert.That(ServiceNoticeRules.ShouldShow(true, NOW_SEC, after, ServiceNotice.SEVERITY_CRITICAL), Is.True);
        }

        /****************************************
        *               Format
        ****************************************/

        [Test]
        public void FormatLine_TitleAndBody_JoinsWithPrefix()
        {
            Assert.That(ServiceNoticeRules.FormatLine(new ServiceNotice("a", "점검", "오늘 밤", ServiceNotice.TARGET_GAME, 0, 0, 0)),
                Is.EqualTo("[공지] 점검 - 오늘 밤"));
        }

        [Test]
        public void FormatLine_TitleOnly_OmitsSeparator()
        {
            Assert.That(ServiceNoticeRules.FormatLine(new ServiceNotice("a", "점검", "  ", ServiceNotice.TARGET_GAME, 0, 0, 0)),
                Is.EqualTo("[공지] 점검"));
        }

        [Test]
        public void FormatLine_BothEmpty_ReturnsNull()
        {
            Assert.That(ServiceNoticeRules.FormatLine(new ServiceNotice("a", " ", "\n", ServiceNotice.TARGET_GAME, 0, 0, 0)), Is.Null);
        }

        [Test]
        public void FormatLine_TagsAndControlChars_BecomeSafePlainText()
        {
            string line = ServiceNoticeRules.FormatLine(
                new ServiceNotice("a", "<color=red>긴급</color>", "줄\n바꿈\t과\u0007벨​", ServiceNotice.TARGET_GAME, 0, 0, 0));
            Assert.That(line, Is.EqualTo("[공지] ＜color=red＞긴급＜/color＞ - 줄 바꿈 과 벨"));
        }

        [Test]
        public void FormatLine_TooLong_TruncatesWithEllipsisWithinLimit()
        {
            string body = new string('가', Constants.Chat.NOTICE_MAX_LENGTH * 2);
            string line = ServiceNoticeRules.FormatLine(new ServiceNotice("a", "t", body, ServiceNotice.TARGET_GAME, 0, 0, 0));
            Assert.That(line.Length, Is.EqualTo(Constants.Chat.NOTICE_MAX_LENGTH));
            Assert.That(line, Does.EndWith("..."));
        }

        [Test]
        public void FormatLine_TruncationOnSurrogatePair_DoesNotSplitPair()
        {
            // 접두사와 제목 길이에 맞춰 잘리는 자리에 이모지를 놓는다
            int    prefix = "[공지] t - ".Length;
            int    keep   = Constants.Chat.NOTICE_MAX_LENGTH - 3;
            string body   = new string('가', keep - prefix - 1) + "😀" + new string('나', 20);
            string line   = ServiceNoticeRules.FormatLine(new ServiceNotice("a", "t", body, ServiceNotice.TARGET_GAME, 0, 0, 0));
            Assert.That(char.IsHighSurrogate(line[line.Length - 4]), Is.False);
            Assert.That(line, Does.EndWith("..."));
        }

        [Test]
        public void Clean_LoneSurrogates_AreDropped()
        {
            Assert.That(ServiceNoticeRules.Clean("a\uD83Db\uDE00c"), Is.EqualTo("abc"));
        }

        /****************************************
        *               Tracker
        ****************************************/

        [Test]
        public void Collect_GameAndBothActive_AreCollectedLauncherAndExpiredAreNot()
        {
            var list = new List<ServiceNotice>
            {
                Make("game", ServiceNotice.TARGET_GAME),
                Make("both", ServiceNotice.TARGET_BOTH),
                Make("launcher", ServiceNotice.TARGET_LAUNCHER),
                new ServiceNotice("old", "t", "b", ServiceNotice.TARGET_GAME, 0, START_MS, NOW_MS - 1),
                new ServiceNotice("future", "t", "b", ServiceNotice.TARGET_GAME, 0, NOW_MS + 1, END_MS)
            };

            _tracker.Collect(list, NOW_MS, NOW_SEC, _lines);

            Assert.That(_lines.Count, Is.EqualTo(2));
            Assert.That(_tracker.TrackedCount, Is.EqualTo(2));
        }

        [Test]
        public void Collect_SameResponseAgain_DoesNotDuplicate()
        {
            var list = new List<ServiceNotice> { Make("a", ServiceNotice.TARGET_GAME) };

            _tracker.Collect(list, NOW_MS, NOW_SEC, _lines);
            _tracker.Collect(list, NOW_MS, NOW_SEC + 60.0, _lines);

            Assert.That(_lines.Count, Is.EqualTo(1));
        }

        [Test]
        public void Collect_DuplicateIdInOneResponse_ShowsOnce()
        {
            var list = new List<ServiceNotice> { Make("a", ServiceNotice.TARGET_GAME), Make("a", ServiceNotice.TARGET_BOTH) };

            _tracker.Collect(list, NOW_MS, NOW_SEC, _lines);

            Assert.That(_lines.Count, Is.EqualTo(1));
        }

        [Test]
        public void Collect_NewNoticeAppears_OnlyTheNewOneIsAdded()
        {
            _tracker.Collect(new List<ServiceNotice> { Make("a", ServiceNotice.TARGET_GAME) }, NOW_MS, NOW_SEC, _lines);
            _lines.Clear();

            _tracker.Collect(new List<ServiceNotice> { Make("b", ServiceNotice.TARGET_GAME), Make("a", ServiceNotice.TARGET_GAME) },
                NOW_MS, NOW_SEC + 60.0, _lines);

            Assert.That(_lines.Count, Is.EqualTo(1));
        }

        [Test]
        public void Collect_SeveralNew_MostImportantFirstInResponseEndsLast()
        {
            var list = new List<ServiceNotice>
            {
                new ServiceNotice("high", "높음", "", ServiceNotice.TARGET_GAME, ServiceNotice.SEVERITY_CRITICAL, START_MS, END_MS),
                new ServiceNotice("low", "낮음", "", ServiceNotice.TARGET_GAME, ServiceNotice.SEVERITY_INFO, START_MS, END_MS)
            };

            _tracker.Collect(list, NOW_MS, NOW_SEC, _lines);

            Assert.That(_lines[0], Does.Contain("낮음"));
            Assert.That(_lines[1], Does.Contain("높음"));
        }

        [Test]
        public void Collect_CriticalStillActive_RepeatsAfterIntervalButInfoDoesNot()
        {
            var list = new List<ServiceNotice>
            {
                new ServiceNotice("crit", "긴급", "", ServiceNotice.TARGET_GAME, ServiceNotice.SEVERITY_CRITICAL, START_MS, END_MS),
                new ServiceNotice("info", "안내", "", ServiceNotice.TARGET_GAME, ServiceNotice.SEVERITY_INFO, START_MS, END_MS)
            };
            _tracker.Collect(list, NOW_MS, NOW_SEC, _lines);
            _lines.Clear();

            _tracker.Collect(list, NOW_MS, NOW_SEC + Constants.Chat.NOTICE_CRITICAL_REPEAT_SECONDS, _lines);

            Assert.That(_lines.Count, Is.EqualTo(1));
            Assert.That(_lines[0], Does.Contain("긴급"));
        }

        [Test]
        public void Collect_NoticeEndsThenReturns_ShownAgainBecauseRecordWasPruned()
        {
            var list = new List<ServiceNotice> { Make("a", ServiceNotice.TARGET_GAME) };
            _tracker.Collect(list, NOW_MS, NOW_SEC, _lines);

            _tracker.Collect(new List<ServiceNotice>(), NOW_MS, NOW_SEC + 60.0, _lines);
            Assert.That(_tracker.TrackedCount, Is.EqualTo(0));

            _tracker.Collect(list, NOW_MS, NOW_SEC + 120.0, _lines);
            Assert.That(_lines.Count, Is.EqualTo(2));
        }

        [Test]
        public void Collect_AfterReset_ShowsAlreadySeenNoticeAgain()
        {
            var list = new List<ServiceNotice> { Make("a", ServiceNotice.TARGET_GAME) };
            _tracker.Collect(list, NOW_MS, NOW_SEC, _lines);

            _tracker.Reset();
            _tracker.Collect(list, NOW_MS, NOW_SEC + 60.0, _lines);

            Assert.That(_lines.Count, Is.EqualTo(2));
        }

        [Test]
        public void Collect_NullOrEmptyIdAndNullEntries_AreSkipped()
        {
            var list = new List<ServiceNotice>
            {
                null,
                new ServiceNotice(null, "t", "b", ServiceNotice.TARGET_GAME, 0, START_MS, END_MS),
                new ServiceNotice("", "t", "b", ServiceNotice.TARGET_GAME, 0, START_MS, END_MS)
            };

            _tracker.Collect(list, NOW_MS, NOW_SEC, _lines);
            _tracker.Collect(null, NOW_MS, NOW_SEC, _lines);

            Assert.That(_lines, Is.Empty);
        }

        [Test]
        public void Collect_EmptyTextNotice_IsNotMarkedSoItCanShowAfterFix()
        {
            var empty = new List<ServiceNotice> { new ServiceNotice("a", " ", " ", ServiceNotice.TARGET_GAME, 0, START_MS, END_MS) };

            _tracker.Collect(empty, NOW_MS, NOW_SEC, _lines);

            Assert.That(_lines, Is.Empty);
        }

        /****************************************
        *               Stale
        ****************************************/

        [Test]
        public void IsStaleResponse_SameState_KeepsResponse()
        {
            Assert.That(ServiceNoticeRules.IsStaleResponse(3, 3, true, 5, 5, "http://a", "http://a"), Is.False);
        }

        [TestCase(3, 4, true, 5, 5, "http://a", "http://a")]
        [TestCase(3, 3, false, 5, 5, "http://a", "http://a")]
        [TestCase(3, 3, true, 5, 6, "http://a", "http://a")]
        [TestCase(3, 3, true, 5, 5, "http://a", "http://b")]
        [TestCase(3, 3, true, 5, 5, "http://a", null)]
        public void IsStaleResponse_AnythingChanged_DiscardsResponse(int reqConn, int curConn, bool connected, int reqClear,
            int curClear, string reqUrl, string curUrl)
        {
            Assert.That(ServiceNoticeRules.IsStaleResponse(reqConn, curConn, connected, reqClear, curClear, reqUrl, curUrl), Is.True);
        }

        /****************************************
        *               ChatLog
        ****************************************/

        [Test]
        public void ChatLogClear_Called_IncrementsClearGeneration()
        {
            int before = ChatLog.ClearGeneration;

            ChatLog.Clear();

            Assert.That(ChatLog.ClearGeneration, Is.EqualTo(before + 1));
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static ServiceNotice Make(string id, string target)
        {
            return new ServiceNotice(id, "제목", "본문", target, ServiceNotice.SEVERITY_INFO, START_MS, END_MS);
        }
    }
}
