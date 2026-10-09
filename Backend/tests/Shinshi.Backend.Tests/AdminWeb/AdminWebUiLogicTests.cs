// System
using System;
using System.Linq;

// Third Party
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Admin.Web.Models;

namespace Shinshi.Backend.Tests.AdminWeb
{
    /// <summary> 운영 웹 화면의 순수 로직: KST 변환, 기본값 적용, 이름/UUID 판별, 대상 목록 파싱, 검색 응답 해석 </summary>
    public sealed class AdminWebUiLogicTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ACCOUNT = "22222222-2222-4222-8222-222222222222";
        private const string CHARACTER = "11111111-1111-4111-8111-111111111111";

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void ParseUnixMs_KstInput_ConvertsToUtcMilliseconds()
        {
            // 2026-10-07 15:30 KST = 2026-10-07 06:30 UTC
            long? parsed = AdminTime.ParseUnixMs("2026-10-07T15:30", "시작");

            Assert.Equal(new DateTimeOffset(2026, 10, 7, 6, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), parsed);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void ParseUnixMs_Blank_IsNull(string text)
        {
            Assert.Null(AdminTime.ParseUnixMs(text, "시작"));
        }

        [Theory]
        [InlineData("2026-10-07")]
        [InlineData("내일")]
        [InlineData("2026-13-40T99:99")]
        public void ParseUnixMs_InvalidText_Throws(string text)
        {
            Assert.Throws<ArgumentException>(() => AdminTime.ParseUnixMs(text, "종료"));
        }

        [Fact]
        public void ToInput_RoundTripsThroughParse()
        {
            DateTimeOffset moment = new(2026, 10, 7, 23, 45, 0, TimeSpan.Zero);

            string input = AdminTime.ToInput(moment);

            Assert.Equal("2026-10-08T08:45", input);
            Assert.Equal(moment.ToUnixTimeMilliseconds(), AdminTime.ParseUnixMs(input, "시각"));
        }

        [Fact]
        public void Format_ShowsKstAndUtc()
        {
            long unixMs = new DateTimeOffset(2026, 10, 7, 6, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

            Assert.Equal("2026-10-07 15:30 KST", AdminTime.FormatKst(unixMs));
            Assert.Equal("2026-10-07 06:30:00 UTC", AdminTime.FormatUtc(unixMs));
        }

        [Fact]
        public void ResolveOrDefault_BlankUsesDefault_FilledUsesInput()
        {
            long fallback = 1_000;

            Assert.Equal(fallback, AdminTime.ResolveOrDefault("", fallback, "종료", out bool blankDefaulted));
            Assert.True(blankDefaulted);
            long filled = AdminTime.ResolveOrDefault("2026-10-07T09:00", fallback, "종료", out bool filledDefaulted);
            Assert.False(filledDefaulted);
            Assert.Equal(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), filled);
        }

        [Fact]
        public void AddToInput_UsesStartWhenPresentOtherwiseNow()
        {
            DateTimeOffset now = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);

            Assert.Equal("2026-10-07T10:00", AdminTime.AddToInput("2026-10-07T09:00", TimeSpan.FromHours(1), now));
            Assert.Equal("2026-10-08T09:00", AdminTime.AddToInput("", TimeSpan.FromDays(1), now));
        }

        [Fact]
        public void Preset_TodayAndYesterday_UseKstDayBoundaries()
        {
            // 2026-10-07 20:00 UTC = 2026-10-08 05:00 KST
            DateTimeOffset now = new(2026, 10, 7, 20, 0, 0, TimeSpan.Zero);

            Assert.Equal(("2026-10-08T00:00", "2026-10-09T00:00"), AdminTime.Preset("today", now));
            Assert.Equal(("2026-10-07T00:00", "2026-10-08T00:00"), AdminTime.Preset("yesterday", now));
            Assert.Equal(("2026-10-01T05:00", string.Empty), AdminTime.Preset("7d", now));
        }

        [Fact]
        public void Reason_BlankUsesFallback_AndFlagsIt()
        {
            Assert.Equal("공지 작성", AdminDefaults.Reason("  ", "공지 작성", out bool defaulted));
            Assert.True(defaulted);
            Assert.Equal("직접 입력", AdminDefaults.Reason(" 직접 입력 ", "공지 작성", out bool typed));
            Assert.False(typed);
            Assert.Throws<ArgumentException>(() => AdminDefaults.Reason(new string('가', 501), "공지 작성", out _));
        }

        [Fact]
        public void Int_BlankUsesDefault_InvalidOrOutOfRangeThrows()
        {
            Assert.Equal(30, AdminDefaults.Int("", 30, 1, 365, "수령 기한", out bool defaulted));
            Assert.True(defaulted);
            Assert.Equal(7, AdminDefaults.Int("7", 30, 1, 365, "수령 기한", out bool typed));
            Assert.False(typed);
            Assert.Throws<ArgumentException>(() => AdminDefaults.Int("0", 30, 1, 365, "수령 기한", out _));
            Assert.Throws<ArgumentException>(() => AdminDefaults.Int("abc", 30, 1, 365, "수령 기한", out _));
        }

        [Fact]
        public void WriteDefaults_RecordsEveryAppliedDefaultForTheConfirmation()
        {
            WriteDefaults defaults = new();

            string reason = defaults.Reason("", "운영 지급");
            int days = defaults.Int("", 30, 1, 365, "수령 기한", "일");
            int typedDays = defaults.Int("10", 30, 1, 365, "다른 값", "일");

            Assert.Equal("운영 지급", reason);
            Assert.True(defaults.ReasonDefaulted);
            Assert.Equal(30, days);
            Assert.Equal(10, typedDays);
            Assert.Equal("수령 기한: 30일 (기본값)", Assert.Single(defaults.Notes));
        }

        [Theory]
        [InlineData("22222222-2222-4222-8222-222222222222", true)]
        [InlineData("  22222222-2222-4222-8222-222222222222  ", true)]
        [InlineData("00000000-0000-0000-0000-000000000000", false)]
        [InlineData("FoxMain", false)]
        [InlineData("", false)]
        public void IsUuid_DistinguishesIdsFromNames(string value, bool expected)
        {
            Assert.Equal(expected, TargetInput.IsUuid(value, out _));
        }

        [Fact]
        public void Classify_NormalizesUuidAndKeepsNames()
        {
            TargetToken id = TargetInput.Classify(" 22222222-2222-4222-8222-222222222222 ");
            TargetToken name = TargetInput.Classify(" 여우 ");

            Assert.Equal(new TargetToken(ACCOUNT, ETargetKind.Uuid), id);
            Assert.Equal(new TargetToken("여우", ETargetKind.Name), name);
        }

        [Fact]
        public void Parse_SplitsLinesCommasAndDeduplicates()
        {
            string text = "여우\n" + ACCOUNT + ", 여우 ;\r\n" + ACCOUNT.ToUpperInvariant() + "\n\n호랑이";

            TargetToken[] tokens = TargetInput.Parse(text).ToArray();

            Assert.Equal(new[] { "여우", ACCOUNT, "호랑이" }, tokens.Select(token => token.Text).ToArray());
            Assert.Equal(new[] { ETargetKind.Name, ETargetKind.Uuid, ETargetKind.Name }, tokens.Select(token => token.Kind).ToArray());
        }

        [Fact]
        public void Parse_SpaceSeparatedUuids_AreSplitButSpacedNamesStayWhole()
        {
            TargetToken[] ids = TargetInput.Parse(ACCOUNT + " " + CHARACTER).ToArray();
            TargetToken[] spaced = TargetInput.Parse("김 여우").ToArray();

            Assert.Equal(2, ids.Length);
            Assert.All(ids, token => Assert.Equal(ETargetKind.Uuid, token.Kind));
            Assert.Equal("김 여우", Assert.Single(spaced).Text);
        }

        [Fact]
        public void ParseMatches_CharacterNameMatch_UsesMatchedCharacterFields()
        {
            JObject response = JObject.Parse("""
                { "accounts": [ {
                    "accountId": "22222222-2222-4222-8222-222222222222", "matchedBy": "CharacterName",
                    "matchedId": "11111111-1111-4111-8111-111111111111", "characterCount": 2, "online": true,
                    "matchedCharacterDeleted": false, "matchedCharacterId": "11111111-1111-4111-8111-111111111111",
                    "matchedCharacterName": "FoxMain", "matchedCharacterClassId": 2 } ] }
                """);

            ResolvedAccount match = Assert.Single(AccountLookup.ParseMatches(response, "foxmain"));

            Assert.Equal(ACCOUNT, match.AccountId);
            Assert.Equal(CHARACTER, match.CharacterId);
            Assert.Equal("FoxMain", match.CharacterName);
            Assert.Equal(2, match.ClassId);
            Assert.True(match.Online);
            Assert.Equal(2, match.CharacterCount);
            Assert.False(match.CharacterDeleted);
        }

        [Fact]
        public void ParseMatches_AccountMatchAndBadRows_AreHandled()
        {
            JObject response = JObject.Parse("""
                { "accounts": [
                    { "accountId": "22222222-2222-4222-8222-222222222222", "matchedBy": "AccountId",
                      "matchedId": "22222222-2222-4222-8222-222222222222", "characterCount": 1, "online": false },
                    { "accountId": "not-a-uuid", "matchedBy": "AccountId" } ] }
                """);

            ResolvedAccount match = Assert.Single(AccountLookup.ParseMatches(response, null));

            Assert.Null(match.CharacterId);
            Assert.Null(match.CharacterName);
            Assert.Empty(AccountLookup.ParseMatches(null, null));
            Assert.Empty(AccountLookup.ParseMatches(new JObject(), null));
        }

        [Theory]
        [InlineData(0, "무사")]
        [InlineData(1, "도술사")]
        [InlineData(2, "신령사")]
        [InlineData(3, "검사")]
        [InlineData(4, "마검사")]
        [InlineData(5, "궁수")]
        [InlineData(6, "알 수 없는 직업")]
        [InlineData(9, "알 수 없는 직업")]
        public void ClassCatalog_NamesKnownClassIds(int classId, string expected)
        {
            Assert.Equal(expected, AdminClassCatalog.Name(classId));
        }
    }
}
