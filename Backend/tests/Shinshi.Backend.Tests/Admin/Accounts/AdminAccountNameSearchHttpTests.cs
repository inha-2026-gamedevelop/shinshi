// System
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Backend.Tests.Auth;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 이름 검색의 정확 일치, 정규화, 삭제 정책과 원문 없는 형식 오류를 실제 HTTP 파이프라인에서 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class AdminAccountNameSearchHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string NAME = "여우Fox12";
        private const string KEY  = "여우fox12";

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task SearchByBody_NormalizedName_ReturnsMatchedCharacterWithoutLoggingInput()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, false);
            RecordingLoggerProvider logs = new RecordingLoggerProvider();
            using WebApplicationFactory<Program> logged = host.WithWebHostBuilder(builder =>
                builder.ConfigureLogging(logging => logging.AddProvider(logs)));
            using HttpClient client = logged.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
            (Guid _, byte[] secret) = await host.CreateAdminAsync("name.viewer", EAdminRole.Viewer);
            string token = await AdminApiTestHost.LoginAsync(client, "name.viewer", secret);
            (Guid accountId, Guid characterId) = await SeedCharacterAsync(database.DataSource, false);

            foreach (string input in new[] { NAME, "  여우ＦＯＸ１２  ", "여우fOx12", "여우FOX12" })
            {
                using HttpResponseMessage response = await SearchNameAsync(client, token, input);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
                AdminAccountSearchResponse found = await AdminTestSupport.ReadAsync<AdminAccountSearchResponse>(response);
                AdminAccountSummary account = Assert.Single(found.Accounts);
                Assert.Equal(accountId.ToString("D"), account.AccountId);
                Assert.Equal(characterId.ToString("D"), account.MatchedId);
                Assert.Equal(characterId.ToString("D"), account.MatchedCharacterId);
                Assert.Equal(NAME, account.MatchedCharacterName);
                Assert.Equal(0, account.MatchedCharacterClassId);
                Assert.Equal(AdminAccountQuery.MATCH_CHARACTER_NAME, account.MatchedBy);
                Assert.Equal(false, account.MatchedCharacterDeleted);
                Assert.False(found.IncludeDeletedCharacters);
                Assert.Equal(1, account.CharacterCount);
            }

            foreach (string input in new[] { "여우Fox", "다른여우" })
            {
                using HttpResponseMessage response = await SearchNameAsync(client, token, input);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Empty((await AdminTestSupport.ReadAsync<AdminAccountSearchResponse>(response)).Accounts);
            }

            Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(NAME, StringComparison.Ordinal)
                || entry.Message.Contains("여우ＦＯＸ１２", StringComparison.Ordinal)
                || entry.Message.Contains(KEY, StringComparison.Ordinal));
            // 요약 검색은 감사 대상이 아니다. 이미 기록한 로그인 감사에도 검색 이름을 넣지 않는다
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_audit_log WHERE CAST(before_state AS text) LIKE @Name OR CAST(after_state AS text) LIKE @Name OR reason LIKE @Name;",
                new { Name = "%" + NAME + "%" }));
        }

        [PostgresFact]
        public async Task SearchByBody_DeletedCharacter_DefaultExcludesAndOptInReportsDeletion()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, false);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] secret) = await host.CreateAdminAsync("deleted.viewer", EAdminRole.Viewer);
            string token = await AdminApiTestHost.LoginAsync(client, "deleted.viewer", secret);
            (Guid accountId, Guid characterId) = await SeedCharacterAsync(database.DataSource, true);

            using HttpResponseMessage activeOnly = await SearchNameAsync(client, token, NAME);
            using HttpResponseMessage included = await SearchNameAsync(client, token, NAME, true);
            Assert.Equal(HttpStatusCode.OK, activeOnly.StatusCode);
            Assert.Equal(HttpStatusCode.OK, included.StatusCode);
            AdminAccountSearchResponse excluded = await AdminTestSupport.ReadAsync<AdminAccountSearchResponse>(activeOnly);
            Assert.Empty(excluded.Accounts);
            Assert.False(excluded.IncludeDeletedCharacters);
            AdminAccountSearchResponse found = await AdminTestSupport.ReadAsync<AdminAccountSearchResponse>(included);
            Assert.True(found.IncludeDeletedCharacters);
            AdminAccountSummary account = Assert.Single(found.Accounts);
            Assert.Equal(accountId.ToString("D"), account.AccountId);
            Assert.Equal(characterId.ToString("D"), account.MatchedId);
            Assert.Equal(characterId.ToString("D"), account.MatchedCharacterId);
            Assert.Equal(NAME, account.MatchedCharacterName);
            Assert.Equal(0, account.MatchedCharacterClassId);
            Assert.Equal(true, account.MatchedCharacterDeleted);
            Assert.Equal(0, account.CharacterCount);

            // 기존 UUID 경로는 삭제된 캐릭터를 계속 제외하고, 계정 매칭은 삭제 상태가 null이다
            using HttpResponseMessage byCharacter = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.ACCOUNTS_SEARCH, token, new AdminAccountSearchRequest(characterId.ToString("D")));
            using HttpResponseMessage byAccount = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.ACCOUNTS_SEARCH, token, new AdminAccountSearchRequest(accountId.ToString("D")));
            Assert.Empty((await AdminTestSupport.ReadAsync<AdminAccountSearchResponse>(byCharacter)).Accounts);
            Assert.Null(Assert.Single((await AdminTestSupport.ReadAsync<AdminAccountSearchResponse>(byAccount)).Accounts)
                .MatchedCharacterDeleted);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("가")]
        [InlineData("여우!")]
        [InlineData("fox fox")]
        [InlineData("abcdefghijklm")]
        [InlineData("abcdefghijk㎏")]
        public async Task SearchByBody_InvalidName_RejectsWithoutDatabaseOrInputInResponse(string input)
        {
            using InvalidSearchFactory factory = new InvalidSearchFactory();
            using HttpClient client = factory.CreateHttpsClient();
            using HttpResponseMessage response = await SearchNameAsync(client, factory.Token, input);

            await AssertInvalidAsync(response, string.IsNullOrWhiteSpace(input) ? null : input);
            Assert.DoesNotContain(factory.Logs.Entries, entry => !string.IsNullOrWhiteSpace(input)
                && entry.Message.Contains(input, StringComparison.Ordinal));
        }

        [Fact]
        public async Task SearchByBody_ConflictingFiltersOrUnsupportedDeletionFlag_RejectsWithoutInput()
        {
            using InvalidSearchFactory factory = new InvalidSearchFactory();
            using HttpClient client = factory.CreateHttpsClient();
            string id = Guid.NewGuid().ToString("D");

            using HttpResponseMessage mixed = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.ACCOUNTS_SEARCH, factory.Token, new AdminAccountSearchRequest(id, NAME));
            using HttpResponseMessage queryDeletion = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.ACCOUNTS_SEARCH, factory.Token, new AdminAccountSearchRequest(id, null, true));
            using HttpResponseMessage missing = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.ACCOUNTS_SEARCH, factory.Token, new { includeDeletedCharacters = true });

            await AssertInvalidAsync(mixed, NAME);
            await AssertInvalidAsync(queryDeletion, id);
            await AssertInvalidAsync(missing, null);
        }

        [Fact]
        public async Task SearchByBody_OverlongOrMalformedBody_RejectsWithGenericReason()
        {
            using InvalidSearchFactory factory = new InvalidSearchFactory();
            using HttpClient client = factory.CreateHttpsClient();
            string overlong = new string('a', CharacterNameRules.MAX_INPUT_LENGTH + 1);
            using HttpResponseMessage length = await SearchNameAsync(client, factory.Token, overlong);
            using HttpResponseMessage malformed = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.ACCOUNTS_SEARCH, factory.Token, new
                {
                    characterName = NAME,
                    includeDeletedCharacters = NAME
                });

            await AssertInvalidAsync(length, overlong);
            await AssertInvalidAsync(malformed, NAME);
            Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Message.Contains(NAME, StringComparison.Ordinal)
                || entry.Message.Contains(overlong, StringComparison.Ordinal));
        }

        [Fact]
        public async Task Search_NameInGetQuery_IsRejectedWithGenericReason()
        {
            using InvalidSearchFactory factory = new InvalidSearchFactory();
            using HttpClient client = factory.CreateHttpsClient();
            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                AdminApiRoutes.ACCOUNTS + "?" + AdminApiRoutes.QUERY_PARAMETER + "=" + Uri.EscapeDataString(NAME),
                factory.Token);

            await AssertInvalidAsync(response, NAME);
        }

        private static Task<HttpResponseMessage> SearchNameAsync(HttpClient client, string token, string name,
            bool includeDeleted = false)
        {
            return AdminApiTestHost.SendAsync(client, HttpMethod.Post, AdminApiRoutes.ACCOUNTS_SEARCH, token,
                new AdminAccountSearchRequest(null, name, includeDeleted));
        }

        private static async Task AssertInvalidAsync(HttpResponseMessage response, string? input)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            string raw = await response.Content.ReadAsStringAsync();
            if (input != null)
            {
                Assert.DoesNotContain(input, raw);
            }

            Assert.Equal(AdminContractCodes.RejectReason.INVALID_REQUEST,
                (await AdminTestSupport.ReadAsync<ApiRejection>(response)).ReasonCode);
        }

        private static async Task<(Guid AccountId, Guid CharacterId)> SeedCharacterAsync(NpgsqlDataSource dataSource,
            bool deleted)
        {
            Guid accountId = Guid.NewGuid();
            Guid characterId = Guid.NewGuid();
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO accounts (account_id) VALUES (@AccountId);
                INSERT INTO characters (character_id, account_id, class_id, name, name_key, deleted_at, delete_operation_id)
                VALUES (@CharacterId, @AccountId, 0, @Name, @NameKey,
                    CASE WHEN @Deleted THEN clock_timestamp() ELSE NULL END,
                    CASE WHEN @Deleted THEN @DeleteOperationId ELSE NULL END);
                """, new { AccountId = accountId, CharacterId = characterId, Name = NAME, NameKey = KEY, Deleted = deleted,
                    DeleteOperationId = Guid.NewGuid() });
            return (accountId, characterId);
        }

        /// <summary> 이름 형식 거절이 DB 연결 전에 끝나는지 보려고 닿을 수 없는 DB와 Viewer 인증 대역을 넣는다 </summary>
        private sealed class InvalidSearchFactory : WebApplicationFactory<Program>
        {
            private readonly AuthTokenOptions _auth = AuthTokenHasherTests.CreateOptions();

            public string Token { get; }
            public RecordingLoggerProvider Logs { get; } = new RecordingLoggerProvider();

            public InvalidSearchFactory()
            {
                Token = new AdminTokenHasher(_auth).CreateToken();
            }

            public HttpClient CreateHttpsClient()
            {
                return CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(Logs));
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=test;Database=test"));
                    services.PostConfigure<AuthTokenOptions>(options => options.HashKeyBase64 = _auth.HashKeyBase64);
                    services.PostConfigure<AdminOptions>(options =>
                    {
                        options.TotpKeyBase64       = AdminTestSupport.CreateKeyBase64();
                        options.PasswordIterations = AdminTestSupport.TEST_ITERATIONS;
                    });
                    services.RemoveAll<IAdminSessionValidator>();
                    services.AddSingleton<IAdminSessionValidator>(new ViewerSessionValidator(Token));
                    services.AddTransient<IStartupFilter, TestRemoteIpStartupFilter>();
                });
            }
        }

        private sealed class ViewerSessionValidator : IAdminSessionValidator
        {
            private readonly string _token;
            private readonly AdminIdentity _identity = new AdminIdentity(Guid.NewGuid(), EAdminRole.Viewer, Guid.NewGuid());

            public ViewerSessionValidator(string token) { _token = token; }

            public Task<AdminIdentity?> ValidateAsync(string token, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(string.Equals(token, _token, StringComparison.Ordinal) ? _identity : null);
            }
        }
    }
}
