// System
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Newtonsoft.Json.Linq;
using Npgsql;
using Xunit;

// Project
using Shinshi.AdminCli;
using Shinshi.Backend.Tests.Admin;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Tests.AdminCli
{
    /// <summary> 실제 Program 파이프라인과 PostgreSQL에 TestServer 핸들러를 끼워 명령줄 도구의 왕복을 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class AdminCliHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string OWNER_NAME = "cli.owner";
        private const string REASON     = "cli round trip";
        private const long   CURRENCY   = 50;
        private const long   ADJUSTMENT = 25;
        private const int    TARGETS    = 2;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task GrantFlow_LoginCreateExecuteShowLogout_RoundTripsThroughApi()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, true);
            using CliHarness harness = new CliHarness(() => host.Server.CreateHandler());
            Guid[] accounts = await CreateAccountsAsync(database.DataSource, TARGETS);
            string targetsFile = Path.Combine(Path.GetTempPath(), "cli-targets-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(targetsFile, "# 대상\n" + string.Join("\n", accounts));
            try
            {
                await LoginAsync(host, harness);
                string token = harness.Store.Load()!.AccessToken;

                CliRun created = await harness.RunAsync(new[]
                {
                    "grants", "create", "--accounts", "@" + targetsFile, "--reward", "Currency=" + CURRENCY,
                    "--reward", "AccountItem:item_herb=3", "--claim-days", "7", "--reason", REASON, "--yes", "--json"
                });
                Assert.True(created.ExitCode == CliExitCodes.SUCCESS, created.ToString());
                string grantId = JObject.Parse(created.Out.Trim())["grantId"]!.Value<string>()!;

                CliRun executed = await harness.RunAsync(new[] { "grants", "execute", grantId, "--reason", REASON },
                    "y");
                Assert.True(executed.ExitCode == CliExitCodes.SUCCESS, executed.ToString());
                Assert.Contains(AdminGrantCodes.STATE_EXECUTED, executed.Out);

                CliRun shown = await harness.RunAsync(new[] { "grants", "show", grantId, "--json" });
                AdminGrantDetail detail = Newtonsoft.Json.JsonConvert.DeserializeObject<AdminGrantDetail>(shown.Out.Trim(),
                    ContractJson.CreateSettings())!;
                Assert.Equal(AdminGrantCodes.STATE_EXECUTED, detail.State);
                Assert.Equal(TARGETS, detail.OpenCount);
                Assert.Equal(7, detail.ClaimDays);

                CliRun again = await harness.RunAsync(new[] { "grants", "execute", grantId, "--reason", REASON, "--yes" });
                Assert.Equal(CliExitCodes.REJECTED, again.ExitCode);
                Assert.Contains("INVALID_STATE (9005)", again.Error);
                Assert.Contains("HTTP 409", again.Error);

                CliRun logout = await harness.RunAsync(new[] { "logout" });
                CliRun afterLogout = await harness.RunAsync(new[] { "grants", "show", grantId });
                Assert.Equal(CliExitCodes.SUCCESS, logout.ExitCode);
                Assert.Null(harness.Store.Load());
                Assert.Equal(CliExitCodes.UNAVAILABLE, afterLogout.ExitCode);
                Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                    "SELECT count(*) FROM admin_sessions WHERE revoked_at IS NULL;"));
                foreach (CliRun run in new[] { created, executed, shown, again, logout })
                {
                    Assert.DoesNotContain(token, run.Out + run.Error);
                }
            }
            finally
            {
                File.Delete(targetsFile);
            }
        }

        [PostgresFact]
        public async Task AdjustAndLedger_RetryWithSameOperationId_AppliesOnceAndShowsInLedger()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, true);
            using CliHarness harness = new CliHarness(() => host.Server.CreateHandler());
            Guid account = (await CreateAccountsAsync(database.DataSource, 1))[0];
            await LoginAsync(host, harness);
            string operationId = Guid.NewGuid().ToString("D");
            string[] adjust =
            {
                "adjust", account.ToString("D"), "--asset", "Currency", "--delta", ADJUSTMENT.ToString(),
                "--reason", REASON, "--operation-id", operationId, "--yes"
            };

            CliRun first = await harness.RunAsync(adjust);
            CliRun retry = await harness.RunAsync(adjust);
            CliRun ledger = await harness.RunAsync(new[] { "ledger", "--account", account.ToString("D"), "--all",
                "--reason", REASON });
            CliRun ledgerJson = await harness.RunAsync(new[] { "ledger", "--account", account.ToString("D"),
                "--economy-reason", "ADMIN_GRANT", "--limit", "1", "--all", "--json", "--reason", REASON });
            CliRun state = await harness.RunAsync(new[] { "service-state", "set", "--maintenance", "off",
                "--allow-builds", "cli-build", "--reason", REASON, "--yes" });
            CliRun shown = await harness.RunAsync(new[] { "service-state", "show", "--json" });
            CliRun kicked = await harness.RunAsync(new[] { "accounts", "kick", account.ToString("D"), "--keep-invites",
                "--reason", REASON, "--yes" });

            Assert.True(first.ExitCode == CliExitCodes.SUCCESS, first.ToString());
            Assert.True(retry.ExitCode == CliExitCodes.SUCCESS, retry.ToString());
            Assert.Contains(operationId, retry.Out);
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM economy_ledger WHERE account_id = @AccountId;", new { AccountId = account }));
            Assert.True(ledger.ExitCode == CliExitCodes.SUCCESS, ledger.ToString());
            Assert.Contains("ADMIN_GRANT", ledger.Out);
            JObject page = JObject.Parse(ledgerJson.Out.Trim());
            JToken entry = Assert.Single(page["entries"]!);
            Assert.Equal(ADJUSTMENT, entry["delta"]!.Value<long>());
            Assert.Equal(ContractCodes.EconomyReason.ADMIN_GRANT, entry["reason"]!.Value<int>());
            Assert.Equal(operationId, entry["operationId"]!.Value<string>());
            Assert.True(state.ExitCode == CliExitCodes.SUCCESS, state.ToString());
            JObject serviceState = JObject.Parse(shown.Out.Trim());
            Assert.False(serviceState["maintenance"]!.Value<bool>());
            Assert.Equal(new[] { "cli-build" }, serviceState["allowedBuildIds"]!.Values<string>());
            Assert.True(kicked.ExitCode == CliExitCodes.SUCCESS, kicked.ToString());
            Assert.Contains("현재 접속은 유지", kicked.Out);
        }

        private static async Task LoginAsync(AdminApiTestHost host, CliHarness harness)
        {
            (Guid _, byte[] secret) = await host.CreateAdminAsync(OWNER_NAME, EAdminRole.Owner);
            harness.Environment[LoginCommand.PASSWORD_ENVIRONMENT] = AdminTestSupport.TEST_PASSWORD;
            harness.Environment[LoginCommand.TOTP_ENVIRONMENT] = AdminTestSupport.CodeAt(secret, DateTimeOffset.UtcNow);
            CliRun login = await harness.RunAsync(new[] { "login", "--user", OWNER_NAME });
            Assert.True(login.ExitCode == CliExitCodes.SUCCESS, login.ToString());
            harness.Environment.Clear();
        }

        private static async Task<Guid[]> CreateAccountsAsync(NpgsqlDataSource dataSource, int count)
        {
            Guid[] accounts = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            foreach (Guid account in accounts)
            {
                await connection.ExecuteAsync("INSERT INTO accounts (account_id) VALUES (@AccountId);",
                    new { AccountId = account });
            }

            return accounts;
        }
    }
}
