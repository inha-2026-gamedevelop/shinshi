// System
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

// Third Party
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.AdminCli;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Tests.AdminCli
{
    /// <summary> 캐릭터 상세 경로, 이름 검색 결속, 전체 출력과 오류를 대역 HTTP로 검사한다 </summary>
    public sealed class CharactersCliTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TOKEN  = "stub-access-token-0123456789";
        private const string NAME   = "여우ＦＯＸ１２";
        private const string REASON = "캐릭터 확인 & 재현=?";

        private readonly List<StubHttpHandler.Request> _requests = new List<StubHttpHandler.Request>();

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task Show_CharacterId_UsesCanonicalIdAuditedGetAndDisplaysProfile()
        {
            Guid characterId = Guid.NewGuid();
            JObject detail = Detail(characterId.ToString("D"));
            using CliHarness harness = Harness(_ => Json(HttpStatusCode.OK, detail));

            CliRun run = await harness.RunAsync(new[] { "characters", "show", characterId.ToString("B").ToUpperInvariant(),
                "--reason", REASON });

            Assert.Equal(CliExitCodes.SUCCESS, run.ExitCode);
            StubHttpHandler.Request request = Assert.Single(_requests);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(string.Format(AdminApiRoutes.CHARACTER_FORMAT, characterId.ToString("D")), request.Uri.AbsolutePath);
            Assert.Equal("?reason=" + Uri.EscapeDataString(REASON), request.Uri.Query);
            Assert.Empty(request.Body);
            Assert.True(request.HasToken);
            Assert.Contains(characterId.ToString("D"), run.Out);
            Assert.Contains("홍길동", run.Out);
            Assert.Contains("최고 강화 단계", run.Out);
            Assert.Contains("정기", run.Out);
            Assert.Contains("파편", run.Out);
            Assert.Contains("수련", run.Out);
            Assert.Contains("공격", run.Out);
            Assert.Contains("체력", run.Out);
            Assert.Contains("방어", run.Out);
            Assert.Contains("치명타", run.Out);
            Assert.Contains("기력", run.Out);
            Assert.Contains("항목 상한", run.Out);
            Assert.Contains("합계 상한", run.Out);
            Assert.Contains("장비", run.Out);
            Assert.Contains("생활", run.Out);
            Assert.Contains("다른 캐릭터", run.Out);
            Assert.DoesNotContain(TOKEN, run.Out + run.Error);
            Assert.DoesNotContain("계속할까요", run.Out + run.Error);
        }

        [Fact]
        public async Task Show_Json_PreservesCompleteNestedResponseWithoutSummary()
        {
            string characterId = Guid.NewGuid().ToString("D");
            JObject detail = Detail(characterId);
            using CliHarness harness = Harness(_ => Json(HttpStatusCode.OK, detail));

            CliRun run = await harness.RunAsync(new[] { "characters", "show", characterId, "--reason", REASON, "--json" });

            Assert.Equal(CliExitCodes.SUCCESS, run.ExitCode);
            Assert.True(JToken.DeepEquals(detail, JObject.Parse(run.Out.Trim())));
            Assert.Empty(run.Error);
            Assert.Single(_requests);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Show_Name_PostsNameAndFollowsExplicitMatchedCharacterId(bool includeDeleted)
        {
            string characterId = Guid.NewGuid().ToString("D");
            JObject match = Match(characterId, includeDeleted);
            match["matchedId"] = Guid.NewGuid().ToString("D");
            using CliHarness harness = Harness(request => Json(HttpStatusCode.OK, request.Method == HttpMethod.Post
                ? Search(match) : Detail(characterId)));
            List<string> args = new List<string> { "characters", "show", "--name", NAME, "--reason", REASON, "--json" };
            if (includeDeleted)
            {
                args.Add("--include-deleted");
            }

            CliRun run = await harness.RunAsync(args.ToArray());

            Assert.Equal(CliExitCodes.SUCCESS, run.ExitCode);
            Assert.Equal(2, _requests.Count);
            StubHttpHandler.Request search = _requests[0];
            Assert.Equal(HttpMethod.Post, search.Method);
            Assert.Equal(AdminApiRoutes.ACCOUNTS_SEARCH, search.Uri.AbsolutePath);
            Assert.Empty(search.Uri.Query);
            JObject searchBody = JObject.Parse(search.Body);
            Assert.Null(searchBody["query"]!.Value<string>());
            Assert.Equal(NAME, searchBody["characterName"]!.Value<string>());
            Assert.Equal(includeDeleted, searchBody["includeDeletedCharacters"]!.Value<bool>());
            StubHttpHandler.Request detail = _requests[1];
            Assert.Equal(HttpMethod.Get, detail.Method);
            Assert.Equal(string.Format(AdminApiRoutes.CHARACTER_FORMAT, characterId), detail.Uri.AbsolutePath);
            Assert.Equal("?reason=" + Uri.EscapeDataString(REASON), detail.Uri.Query);
            Assert.All(_requests, request => Assert.True(request.HasToken));
            Assert.DoesNotContain(NAME, run.Out + run.Error);
            Assert.Equal(characterId, JObject.Parse(run.Out.Trim())["characterId"]!.Value<string>());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        public async Task Show_NoOrMultipleNameMatches_RejectsBeforeDetailGet(int matches)
        {
            JArray accounts = new JArray();
            for (int i = 0; i < matches; ++i)
            {
                accounts.Add(Match(Guid.NewGuid().ToString("D")));
            }
            using CliHarness harness = Harness(_ => Json(HttpStatusCode.OK, new JObject { ["accounts"] = accounts }));

            CliRun run = await harness.RunAsync(new[] { "characters", "show", "--name", NAME, "--reason", REASON });

            Assert.Equal(CliExitCodes.REJECTED, run.ExitCode);
            Assert.Equal(HttpMethod.Post, Assert.Single(_requests).Method);
            Assert.Empty(run.Out);
            Assert.Contains(matches == 0 ? "일치하는 캐릭터가 없습니다" : "여러 캐릭터", run.Error);
            Assert.DoesNotContain(NAME, run.Error);
        }

        [Theory]
        [InlineData("missingId")]
        [InlineData("invalidId")]
        [InlineData("emptyId")]
        [InlineData("wrongMatch")]
        [InlineData("deleted")]
        [InlineData("invalidType")]
        [InlineData("missingAccounts")]
        public async Task Show_InvalidSearchResponse_StopsBeforeDetailGet(string defect)
        {
            JObject match = Match(Guid.NewGuid().ToString("D"));
            switch (defect)
            {
                case "missingId": match.Remove("matchedCharacterId"); break;
                case "invalidId": match["matchedCharacterId"] = "not-a-guid"; break;
                case "emptyId": match["matchedCharacterId"] = Guid.Empty.ToString("D"); break;
                case "wrongMatch": match["matchedBy"] = "AccountId"; break;
                case "deleted": match["matchedCharacterDeleted"] = true; break;
                case "invalidType": match["matchedCharacterId"] = new JObject(); break;
            }
            JObject response = defect == "missingAccounts" ? new JObject() : Search(match);
            using CliHarness harness = Harness(_ => Json(HttpStatusCode.OK, response));

            CliRun run = await harness.RunAsync(new[] { "characters", "show", "--name", NAME, "--reason", REASON });

            Assert.Equal(CliExitCodes.UNAVAILABLE, run.ExitCode);
            Assert.Equal(HttpMethod.Post, Assert.Single(_requests).Method);
            Assert.Empty(run.Out);
            Assert.DoesNotContain(NAME, run.Error);
        }

        [Fact]
        public async Task Show_ConflictingMissingOrRepeatedArguments_RejectsBeforeHttpWithoutNameEcho()
        {
            string characterId = Guid.NewGuid().ToString("D");
            using CliHarness harness = Harness(_ => throw new InvalidOperationException("No HTTP expected."));
            string[][] arguments =
            {
                new[] { "characters", "show", characterId, "--name", NAME, "--reason", REASON },
                new[] { "characters", "show", "--name", NAME, NAME, "--reason", REASON },
                new[] { "characters", "show", "--name", NAME, "--name", "다른여우", "--reason", REASON },
                new[] { "characters", "show", characterId, "--include-deleted", "--reason", REASON },
                new[] { "characters", "show", "--reason", REASON },
                new[] { "characters", "show", "--name", NAME },
                new[] { "characters", "show", "--name", "--reason", REASON },
                new[] { "characters", "show", "invalid-id", "--reason", REASON },
                new[] { "characters", "show", Guid.Empty.ToString("D"), "--reason", REASON },
                new[] { "characters", "search", "--name", NAME, "--reason", REASON },
                new[] { "characters", "show", "--name", NAME, "--reason", new string('a', AdminContractCodes.MAX_REASON_LENGTH + 1) }
            };

            foreach (string[] args in arguments)
            {
                CliRun run = await harness.RunAsync(args);
                Assert.Equal(CliExitCodes.USAGE, run.ExitCode);
                Assert.DoesNotContain(NAME, run.Out + run.Error);
            }
            Assert.Empty(_requests);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("힣")]
        [InlineData("여우!")]
        [InlineData("fox fox")]
        [InlineData("abcdefghijklm")]
        public async Task Show_InvalidName_RejectsBeforeHttpWithoutInputEcho(string name)
        {
            using CliHarness harness = Harness(_ => throw new InvalidOperationException("No HTTP expected."));

            CliRun run = await harness.RunAsync(new[] { "characters", "show", "--name", name, "--reason", REASON });

            Assert.Equal(CliExitCodes.USAGE, run.ExitCode);
            Assert.Empty(_requests);
            if (!string.IsNullOrWhiteSpace(name))
            {
                Assert.DoesNotContain(name, run.Out + run.Error);
            }
        }

        [Theory]
        [InlineData(HttpStatusCode.Forbidden, CliExitCodes.REJECTED)]
        [InlineData(HttpStatusCode.NotFound, CliExitCodes.REJECTED)]
        [InlineData(HttpStatusCode.ServiceUnavailable, CliExitCodes.UNAVAILABLE)]
        [InlineData(HttpStatusCode.Unauthorized, CliExitCodes.UNAVAILABLE)]
        public async Task Show_ApiRejection_PreservesExitCodeAndDoesNotPrintProfile(HttpStatusCode status, int expectedCode)
        {
            string characterId = Guid.NewGuid().ToString("D");
            using CliHarness harness = Harness(_ => Json(status,
                new ApiRejection(status == HttpStatusCode.Unauthorized ? ContractCodes.RejectReason.TOKEN_REVOKED
                    : AdminContractCodes.RejectReason.FORBIDDEN_ROLE, string.Empty, 0)));

            CliRun run = await harness.RunAsync(new[] { "characters", "show", characterId, "--reason", REASON });

            Assert.Equal(expectedCode, run.ExitCode);
            Assert.Single(_requests);
            Assert.Empty(run.Out);
            Assert.Contains("HTTP " + (int)status, run.Error);
            Assert.DoesNotContain(TOKEN, run.Error);
            if (status == HttpStatusCode.Unauthorized)
            {
                Assert.Null(harness.Store.Load());
            }
        }

        [Fact]
        public async Task Show_OverlongOrInvalidUnicodeName_RejectsBeforeHttpWithoutInputEcho()
        {
            using CliHarness harness = Harness(_ => throw new InvalidOperationException("No HTTP expected."));
            string overlong = new string('a', CharacterNameRules.MAX_INPUT_LENGTH + 1);
            string surrogate = new string(new[] { '\ud800', 'a', 'b' });

            CliRun length = await harness.RunAsync(new[] { "characters", "show", "--name", overlong, "--reason", REASON });
            CliRun unicode = await harness.RunAsync(new[] { "characters", "show", "--name", surrogate, "--reason", REASON });

            Assert.Equal(CliExitCodes.USAGE, length.ExitCode);
            Assert.Equal(CliExitCodes.USAGE, unicode.ExitCode);
            Assert.Empty(_requests);
            Assert.DoesNotContain(overlong, length.Out + length.Error);
            Assert.DoesNotContain(surrogate, unicode.Out + unicode.Error);
        }
        private CliHarness Harness(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            CliHarness harness = new CliHarness(() => new StubHttpHandler(_requests, respond));
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(5));
            return harness;
        }

        private static HttpResponseMessage Json(HttpStatusCode status, object body)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(JsonConvert.SerializeObject(body, ContractJson.CreateSettings()), Encoding.UTF8,
                    "application/json")
            };
        }

        private static JObject Match(string characterId, bool deleted = false)
        {
            return new JObject
            {
                ["accountId"] = Guid.NewGuid().ToString("D"),
                ["matchedBy"] = AdminAccountQuery.MATCH_CHARACTER_NAME,
                ["matchedId"] = characterId,
                ["matchedCharacterId"] = characterId,
                ["matchedCharacterName"] = "홍길동",
                ["matchedCharacterClassId"] = 1,
                ["matchedCharacterDeleted"] = deleted
            };
        }

        private static JObject Search(JObject match)
        {
            return new JObject { ["accounts"] = new JArray(match), ["includeDeletedCharacters"] = false };
        }

        private static JObject Detail(string characterId)
        {
            return new JObject
            {
                ["characterId"] = characterId,
                ["accountId"] = Guid.NewGuid().ToString("D"),
                ["name"] = "홍길동",
                ["classId"] = 1,
                ["classDisplayName"] = "도술사",
                ["essence"] = 321,
                ["shards"] = 45,
                ["highestEnhanceLevel"] = 8,
                ["training"] = new JObject
                {
                    ["attack"] = 1, ["health"] = 2, ["defense"] = 3, ["crit"] = 4, ["energy"] = 5,
                    ["total"] = 15, ["itemCap"] = 100, ["totalCap"] = 200
                },
                ["combatStats"] = new JObject { ["attack"] = 123.45, ["health"] = 987.6 },
                ["equipment"] = new JArray(new JObject { ["itemId"] = "equipment-id", ["enhanceLevel"] = 8 }),
                ["talismanProgress"] = new JArray(new JObject { ["definitionId"] = "talisman_01", ["level"] = 2 }),
                ["talismanLoadout"] = new JArray(new JObject { ["slot"] = 0, ["definitionId"] = "talisman_01" }),
                ["accountProgress"] = new JObject { ["foxTraces"] = 17 },
                ["life"] = new JObject { ["actionValue"] = 83 },
                ["wallet"] = new JObject { ["currency"] = 1000, ["currencyReserved"] = 10 },
                ["items"] = new JArray(new JObject { ["definitionId"] = "item_herb", ["quantity"] = 3 }),
                ["lease"] = JValue.CreateNull(),
                ["recentSessions"] = new JArray(new JObject { ["sessionId"] = "session-id", ["mode"] = "Normal" }),
                ["otherCharacters"] = new JArray(new JObject { ["characterId"] = "other-character-id", ["name"] = "다른여우" })
            };
        }
    }
}
