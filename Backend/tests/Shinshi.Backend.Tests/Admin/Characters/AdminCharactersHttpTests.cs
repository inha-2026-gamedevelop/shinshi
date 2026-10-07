// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json.Linq;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Backend.Tests.Auth;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data;
using Shinshi.Data.Admin;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 캐릭터 민감 조회의 인증, 사유 검증과 읽기 전에 감사 확정이 끝나야 하는 경계를 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class AdminCharactersHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string REASON             = "support character profile";
        private const string INVALID_TOKEN      = "invalid-token";
        private const int    AUDIT_WAIT_SECONDS = 5;

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData(EAdminRole.Viewer)]
        [InlineData(EAdminRole.Operator)]
        [InlineData(EAdminRole.Admin)]
        [InlineData(EAdminRole.Owner)]
        public async Task Detail_ViewerOrHigher_AuditsBeforeMissingCharacterResponse(EAdminRole role)
        {
            using CharacterTestFactory factory = new CharacterTestFactory(role);
            using HttpClient client = factory.CreateHttpsClient();
            Guid characterId = Guid.NewGuid();
            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                DetailRoute(characterId, REASON), factory.Token);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            Assert.Equal(AdminContractCodes.RejectReason.NOT_FOUND,
                (await AdminTestSupport.ReadAsync<ApiRejection>(response)).ReasonCode);
            Assert.Equal(new[] { "audit.committed", "character.read" }, factory.Events);
            Assert.Equal(characterId, Assert.Single(factory.Reader.Calls));
            AdminAuditEntry audit = Assert.Single(factory.Audit.Entries);
            Assert.Equal(AdminAuditActions.CHARACTER_VIEW, audit.Action);
            Assert.Equal(AdminAuditActions.TARGET_CHARACTER, audit.TargetKind);
            Assert.Equal(characterId.ToString("D"), audit.TargetId);
            Assert.Equal(REASON, audit.Reason);
            Assert.Equal(factory.Identity.AdminId, audit.AdminId);
            Assert.Equal(role, audit.AdminRole);
            Assert.Null(audit.OperationId);
            Assert.Null(audit.BeforeState);
            Assert.Null(audit.AfterState);
            Assert.False(audit.SelfApproved);
            Assert.Equal(AdminAuditEntry.REQUEST_IP_HASH_BYTES, audit.RequestIpHash!.Length);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(INVALID_TOKEN)]
        [InlineData("unknown-session")]
        [InlineData("game-token")]
        public async Task Detail_MissingInvalidOrNonAdminSession_RejectsBeforeAuditAndRead(string? kind)
        {
            using CharacterTestFactory factory = new CharacterTestFactory();
            using HttpClient client = factory.CreateHttpsClient();
            string? token = kind switch
            {
                "unknown-session" => new AdminTokenHasher(factory.Auth).CreateToken(),
                "game-token"      => new AuthTokenHasher(factory.Auth).CreateToken(),
                _                 => kind
            };
            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                DetailRoute(Guid.NewGuid(), REASON), token);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await AdminTestSupport.ReadAsync<ApiRejection>(response)).ReasonCode);
            Assert.Empty(factory.Audit.Entries);
            Assert.Empty(factory.Reader.Calls);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t\r\n")]
        public async Task Detail_MissingOrBlankReason_RejectsBeforeAuditAndRead(string? reason)
        {
            using CharacterTestFactory factory = new CharacterTestFactory();
            using HttpClient client = factory.CreateHttpsClient();
            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                DetailRoute(Guid.NewGuid(), reason), factory.Token);

            await AssertInvalidAsync(response, factory);
        }

        [Fact]
        public async Task Detail_ReasonOverLimit_RejectsBeforeAuditAndRead()
        {
            using CharacterTestFactory factory = new CharacterTestFactory();
            using HttpClient client = factory.CreateHttpsClient();
            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                DetailRoute(Guid.NewGuid(), new string('a', AdminContractCodes.MAX_REASON_LENGTH + 1)), factory.Token);

            await AssertInvalidAsync(response, factory);
        }

        [Fact]
        public async Task Detail_ReasonAtLimit_AcceptsAndPreservesAuditReason()
        {
            using CharacterTestFactory factory = new CharacterTestFactory();
            using HttpClient client = factory.CreateHttpsClient();
            string reason = new string('a', AdminContractCodes.MAX_REASON_LENGTH);
            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                DetailRoute(Guid.NewGuid(), reason), factory.Token);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(reason, Assert.Single(factory.Audit.Entries).Reason);
            Assert.Single(factory.Reader.Calls);
        }

        [Fact]
        public async Task Detail_EmptyCharacterId_RejectsBeforeAuditAndRead()
        {
            using CharacterTestFactory factory = new CharacterTestFactory();
            using HttpClient client = factory.CreateHttpsClient();
            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                DetailRoute(Guid.Empty, REASON), factory.Token);

            await AssertInvalidAsync(response, factory);
        }

        [Fact]
        public async Task Detail_AuditCommitPending_DoesNotReadUntilCommitCompletes()
        {
            using CharacterTestFactory factory = new CharacterTestFactory();
            using HttpClient client = factory.CreateHttpsClient();
            factory.Audit.PauseCommit = true;
            Task<HttpResponseMessage> pending = AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                DetailRoute(Guid.NewGuid(), REASON), factory.Token);
            try
            {
                await factory.Audit.Entered.Task.WaitAsync(TimeSpan.FromSeconds(AUDIT_WAIT_SECONDS));
                Assert.Empty(factory.Reader.Calls);
                Assert.False(pending.IsCompleted);
            }
            finally
            {
                factory.Audit.Release.TrySetResult(true);
            }

            using HttpResponseMessage response = await pending;
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(new[] { "audit.committed", "character.read" }, factory.Events);
        }

        [Fact]
        public async Task Detail_AuditDatabaseFailure_ReturnsUnavailableWithoutReading()
        {
            using CharacterTestFactory factory = new CharacterTestFactory();
            using HttpClient client = factory.CreateHttpsClient();
            factory.Audit.Failure = new NpgsqlException("audit unavailable");
            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                DetailRoute(Guid.NewGuid(), REASON), factory.Token);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.BACKEND_UNAVAILABLE,
                (await AdminTestSupport.ReadAsync<ApiRejection>(response)).ReasonCode);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            Assert.Single(factory.Audit.Entries);
            Assert.Empty(factory.Reader.Calls);
            Assert.Empty(factory.Events);
        }

        [Fact]
        public async Task Detail_ExistingCharacter_ReturnsPrivateDataMemberContractAfterAudit()
        {
            const long SNAPSHOT_UNIX_MS = 1767323045000;
            const long REVISION = 4294967301;
            using CharacterTestFactory factory = new CharacterTestFactory();
            using HttpClient client = factory.CreateHttpsClient();
            Guid characterId = Guid.NewGuid();
            string id = characterId.ToString("D");
            string accountId = Guid.NewGuid().ToString("D");
            factory.Reader.Result = new AdminCharacterDetailResponse(
                characterId: id, accountId: accountId, name: "FoxProfile", classId: 2,
                classDisplayName: null, appearanceJson: "{\"hair\":2}", createdAtUnixMs: null, deletedAtUnixMs: null,
                essence: 123, shards: 45, revision: REVISION, loadoutRevision: REVISION,
                training: new AdminCharacterTrainingView(10, 20, 3, 4, 5, 42, 40, 100), combatStats: null,
                equipment: new[] { new AdminCharacterEquipmentView(id, "equip_profile", 2, 3, false,
                    "Craft", 12, true, true, true, SNAPSHOT_UNIX_MS) }, highestEnhanceLevel: 12,
                talismanProgress: new[] { new AdminCharacterTalismanProgressView("T01-1A", 3, 0, SNAPSHOT_UNIX_MS) },
                talismanLoadout: new[] { new AdminCharacterTalismanLoadoutView("skill_profile", 0, "T01-1A") },
                accountProgress: new AdminCharacterAccountProgressView(null, 13, REVISION, SNAPSHOT_UNIX_MS,
                    Array.Empty<AdminCharacterUnlockView>()),
                life: new AdminCharacterLifeView(20, 100, SNAPSHOT_UNIX_MS, SNAPSHOT_UNIX_MS,
                    SNAPSHOT_UNIX_MS, Array.Empty<AdminAccountItemView>(), Array.Empty<AdminAccountItemView>()),
                wallet: new AdminCharacterWalletView(1234, 50, 8, 2, 18, SNAPSHOT_UNIX_MS, REVISION),
                items: new[] { new AdminAccountItemView("item_herb", "Account", 12, 2) },
                lease: null, online: false, recentSessions: Array.Empty<AdminAccountSessionView>(),
                otherCharacters: Array.Empty<AdminCharacterSummaryView>());

            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                DetailRoute(characterId, REASON), factory.Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            Assert.Equal(new[] { "audit.committed", "character.read" }, factory.Events);
            AdminCharacterDetailResponse detail = await AdminTestSupport.ReadAsync<AdminCharacterDetailResponse>(response);
            Assert.Equal(id, detail.CharacterId);
            Assert.Equal(accountId, detail.AccountId);
            Assert.Equal("FoxProfile", detail.Name);
            Assert.Equal(REVISION, detail.Revision);
            Assert.Equal(REVISION, detail.LoadoutRevision);
            Assert.Equal(42, detail.Training.Total);
            Assert.Equal(12, Assert.Single(detail.Equipment).Enhancement);
            Assert.Equal("T01-1A", Assert.Single(detail.TalismanProgress).EffectId);
            Assert.Equal("skill_profile", Assert.Single(detail.TalismanLoadout).SkillId);
            Assert.Equal(13, detail.AccountProgress.FoxTraces);
            Assert.Equal(50, detail.Wallet.CurrencyReserved);
            Assert.Equal(20, detail.Life.ActionValue);
            Assert.Equal(2, Assert.Single(detail.Items).Reserved);
            JObject json = await AdminTestSupport.ReadAsync<JObject>(response);
            string[] expectedNames =
            {
                "characterId", "accountId", "name", "classId", "classDisplayName", "appearanceJson",
                "createdAtUnixMs", "deletedAtUnixMs", "essence", "shards", "revision", "loadoutRevision",
                "training", "combatStats", "equipment", "highestEnhanceLevel", "talismanProgress", "talismanLoadout",
                "accountProgress", "life", "wallet", "items", "lease", "online", "recentSessions", "otherCharacters"
            };
            Assert.Equal(expectedNames.OrderBy(name => name, StringComparer.Ordinal),
                json.Properties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
            foreach (string nullable in new[] { "classDisplayName", "createdAtUnixMs", "deletedAtUnixMs", "combatStats", "lease" })
            {
                Assert.Equal(JTokenType.Null, json[nullable]!.Type);
            }
            Assert.Equal(REVISION, json["revision"]!.Value<long>());
            Assert.Equal(42, json["training"]!["total"]!.Value<int>());
            Assert.Equal(50, json["wallet"]!["currencyReserved"]!.Value<long>());
            Assert.True(json["equipment"]![0]!["locked"]!.Value<bool>());
        }

        private static string DetailRoute(Guid characterId, string? reason)
        {
            string route = string.Format(AdminApiRoutes.CHARACTER_FORMAT, characterId.ToString("D"));
            return reason == null ? route : route + "?reason=" + Uri.EscapeDataString(reason);
        }

        private static async Task AssertInvalidAsync(HttpResponseMessage response, CharacterTestFactory factory)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            Assert.Equal(AdminContractCodes.RejectReason.INVALID_REQUEST,
                (await AdminTestSupport.ReadAsync<ApiRejection>(response)).ReasonCode);
            Assert.Empty(factory.Audit.Entries);
            Assert.Empty(factory.Reader.Calls);
        }

        private sealed class CharacterTestFactory : WebApplicationFactory<Program>
        {
            private readonly NpgsqlDataSource _unreachable = NpgsqlDataSource.Create(
                "Host=127.0.0.1;Port=1;Username=test;Database=test");

            public AuthTokenOptions Auth { get; } = AuthTokenHasherTests.CreateOptions();
            public List<string> Events { get; } = new List<string>();
            public AdminIdentity Identity { get; }
            public string Token { get; }
            public RecordingCharacterReader Reader { get; }
            public RecordingAuditWriter Audit { get; }

            public CharacterTestFactory(EAdminRole role = EAdminRole.Viewer)
            {
                Identity = new AdminIdentity(Guid.NewGuid(), role, Guid.NewGuid());
                Token    = new AdminTokenHasher(Auth).CreateToken();
                Reader   = new RecordingCharacterReader(Events);
                Audit    = new RecordingAuditWriter(Events);
            }

            public HttpClient CreateHttpsClient()
            {
                return CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<NpgsqlDataSource>();
                    services.AddSingleton(_unreachable);
                    services.PostConfigure<AuthTokenOptions>(options => options.HashKeyBase64 = Auth.HashKeyBase64);
                    services.PostConfigure<AdminOptions>(options =>
                    {
                        options.TotpKeyBase64       = AdminTestSupport.CreateKeyBase64();
                        options.PasswordIterations = AdminTestSupport.TEST_ITERATIONS;
                    });
                    services.RemoveAll<IAdminSessionValidator>();
                    services.AddSingleton<IAdminSessionValidator>(new CharacterSessionValidator(Token, Identity));
                    services.RemoveAll<IAdminCharacterRepository>();
                    services.AddSingleton<IAdminCharacterRepository>(Reader);
                    services.RemoveAll<IAdminAuditWriter>();
                    services.AddSingleton<IAdminAuditWriter>(Audit);
                    services.AddTransient<IStartupFilter, TestRemoteIpStartupFilter>();
                });
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing)
                {
                    _unreachable.Dispose();
                }
            }
        }

        private sealed class CharacterSessionValidator : IAdminSessionValidator
        {
            private readonly string _token;
            private readonly AdminIdentity _identity;

            public CharacterSessionValidator(string token, AdminIdentity identity)
            {
                _token    = token;
                _identity = identity;
            }

            public Task<AdminIdentity?> ValidateAsync(string token, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(string.Equals(token, _token, StringComparison.Ordinal) ? _identity : null);
            }
        }

        private sealed class RecordingCharacterReader : IAdminCharacterRepository
        {
            private readonly List<string> _events;

            public List<Guid> Calls { get; } = new List<Guid>();
            public AdminCharacterDetailResponse? Result { get; set; }

            public RecordingCharacterReader(List<string> events) { _events = events; }

            public Task<AdminCharacterDetailResponse?> ReadDetailAsync(Guid characterId,
                CancellationToken cancellationToken = default)
            {
                Calls.Add(characterId);
                _events.Add("character.read");
                return Task.FromResult(Result);
            }
        }

        private sealed class RecordingAuditWriter : IAdminAuditWriter
        {
            private readonly List<string> _events;

            public List<AdminAuditEntry> Entries { get; } = new List<AdminAuditEntry>();
            public TaskCompletionSource<bool> Entered { get; } = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Release { get; } = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            public bool PauseCommit { get; set; }
            public Exception? Failure { get; set; }

            public RecordingAuditWriter(List<string> events) { _events = events; }

            public Task AppendAsync(OperationExecutionContext context, AdminAuditEntry entry,
                CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("Character reads must use standalone audit commits.");
            }

            public async Task AppendStandaloneAsync(AdminAuditEntry entry,
                CancellationToken cancellationToken = default)
            {
                Entries.Add(entry);
                Entered.TrySetResult(true);
                if (Failure != null)
                {
                    throw Failure;
                }
                if (PauseCommit)
                {
                    await Release.Task.WaitAsync(cancellationToken);
                }

                _events.Add("audit.committed");
            }
        }
    }
}
