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
    /// <summary> 계정 이름 검색 옵션, 삭제 정책, 본문 전달과 입력을 숨기는 사용법 오류를 DB 없이 검사한다 </summary>
    public sealed class AccountsSearchCliTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TOKEN = "stub-access-token-0123456789";
        private const string NAME  = "여우ＦＯＸ１２";

        private readonly List<StubHttpHandler.Request> _requests = new List<StubHttpHandler.Request>();

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Search_CharacterName_SendsPostBodyAndDisplaysDeletionPolicy(bool includeDeleted)
        {
            string accountId = Guid.NewGuid().ToString("D");
            string characterId = Guid.NewGuid().ToString("D");
            using CliHarness harness = Harness(_ => Json(HttpStatusCode.OK, new AdminAccountSearchResponse(new[]
            {
                new AdminAccountSummary(accountId, AdminAccountQuery.MATCH_CHARACTER_NAME, characterId, 0, 1, false,
                    includeDeleted)
            }, includeDeleted)));
            List<string> args = new List<string> { "accounts", "search", "--character-name", NAME };
            if (includeDeleted)
            {
                args.Add("--include-deleted");
            }

            CliRun run = await harness.RunAsync(args.ToArray());

            Assert.Equal(CliExitCodes.SUCCESS, run.ExitCode);
            StubHttpHandler.Request request = Assert.Single(_requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(AdminApiRoutes.ACCOUNTS_SEARCH, request.Uri.AbsolutePath);
            Assert.Equal(string.Empty, request.Uri.Query);
            JObject body = JObject.Parse(request.Body);
            Assert.Null(body["query"]!.Value<string>());
            Assert.Equal(NAME, body["characterName"]!.Value<string>());
            Assert.Equal(includeDeleted, body["includeDeletedCharacters"]!.Value<bool>());
            Assert.Contains(accountId, run.Out);
            Assert.Contains(characterId, run.Out);
            Assert.Contains("매칭 캐릭터 삭제", run.Out);
            Assert.Contains("삭제된 캐릭터 포함: " + (includeDeleted ? "예" : "아니오"), run.Out);
            Assert.DoesNotContain(NAME, run.Out + run.Error);
        }

        [Fact]
        public async Task Search_CharacterNameJson_PreservesDeletionFieldsAndCharacterId()
        {
            string characterId = Guid.NewGuid().ToString("D");
            using CliHarness harness = Harness(_ => Json(HttpStatusCode.OK, new AdminAccountSearchResponse(new[]
            {
                new AdminAccountSummary(Guid.NewGuid().ToString("D"), AdminAccountQuery.MATCH_CHARACTER_NAME, characterId,
                    0, 0, false, true)
            }, true)));

            CliRun run = await harness.RunAsync(new[] { "accounts", "search", "--character-name", NAME,
                "--include-deleted", "--json" });

            Assert.Equal(CliExitCodes.SUCCESS, run.ExitCode);
            JObject body = JObject.Parse(run.Out.Trim());
            Assert.True(body["includeDeletedCharacters"]!.Value<bool>());
            Assert.Equal(characterId, body["accounts"]![0]!["matchedId"]!.Value<string>());
            Assert.True(body["accounts"]![0]!["matchedCharacterDeleted"]!.Value<bool>());
            Assert.DoesNotContain(NAME, run.Out + run.Error);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("힣")]
        [InlineData("여우!")]
        [InlineData("fox fox")]
        [InlineData("abcdefghijklm")]
        public async Task Search_InvalidCharacterName_IsUsageErrorWithoutRequestOrInputEcho(string name)
        {
            using CliHarness harness = Harness(_ => throw new InvalidOperationException("No HTTP expected."));

            CliRun run = await harness.RunAsync(new[] { "accounts", "search", "--character-name", name });

            Assert.Equal(CliExitCodes.USAGE, run.ExitCode);
            Assert.Empty(_requests);
            if (!string.IsNullOrWhiteSpace(name))
            {
                Assert.DoesNotContain(name, run.Out + run.Error);
            }
        }

        [Fact]
        public async Task Search_ConflictingOrRepeatedNameOptions_RejectsWithoutEchoOrHttp()
        {
            using CliHarness harness = Harness(_ => throw new InvalidOperationException("No HTTP expected."));
            string id = Guid.NewGuid().ToString("D");

            CliRun mixed = await harness.RunAsync(new[] { "accounts", "search", id, "--character-name", NAME });
            CliRun extra = await harness.RunAsync(new[] { "accounts", "search", "--character-name", NAME, NAME });
            CliRun repeated = await harness.RunAsync(new[] { "accounts", "search", "--character-name", NAME,
                "--character-name", "다른여우" });
            CliRun queryDeletion = await harness.RunAsync(new[] { "accounts", "search", id, "--include-deleted" });
            CliRun missing = await harness.RunAsync(new[] { "accounts", "search", "--character-name" });

            foreach (CliRun run in new[] { mixed, extra, repeated, queryDeletion, missing })
            {
                Assert.Equal(CliExitCodes.USAGE, run.ExitCode);
                Assert.DoesNotContain(NAME, run.Out + run.Error);
                Assert.DoesNotContain(id, run.Out + run.Error);
            }
            Assert.Empty(_requests);
        }

        [Fact]
        public async Task Search_OverlongOrInvalidUnicodeName_RejectsBeforeHttp()
        {
            using CliHarness harness = Harness(_ => throw new InvalidOperationException("No HTTP expected."));
            string overlong = new string('a', CharacterNameRules.MAX_INPUT_LENGTH + 1);
            string surrogate = new string(new[] { '\ud800', 'a', 'b' });

            CliRun length = await harness.RunAsync(new[] { "accounts", "search", "--character-name", overlong });
            CliRun unicode = await harness.RunAsync(new[] { "accounts", "search", "--character-name", surrogate });

            Assert.Equal(CliExitCodes.USAGE, length.ExitCode);
            Assert.Equal(CliExitCodes.USAGE, unicode.ExitCode);
            Assert.DoesNotContain(overlong, length.Out + length.Error);
            Assert.DoesNotContain(surrogate, unicode.Out + unicode.Error);
            Assert.Empty(_requests);
        }

        [Fact]
        public async Task Search_LegacyQuery_KeepsOriginalPostContract()
        {
            string query = Guid.NewGuid().ToString("D");
            using CliHarness harness = Harness(_ => Json(HttpStatusCode.OK,
                new AdminAccountSearchResponse(Array.Empty<AdminAccountSummary>())));

            CliRun run = await harness.RunAsync(new[] { "accounts", "search", query, "--json" });

            Assert.Equal(CliExitCodes.SUCCESS, run.ExitCode);
            StubHttpHandler.Request request = Assert.Single(_requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            JObject body = JObject.Parse(request.Body);
            Assert.Equal(query, body["query"]!.Value<string>());
            Assert.Null(body["characterName"]!.Value<string>());
            Assert.False(body["includeDeletedCharacters"]!.Value<bool>());
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
    }
}
