// System
using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary> DB 없이 기록용 HTTP 핸들러로 명령 분기, 확인, 세션, 거절과 연결 실패의 종료 코드를 검사한다 </summary>
    public sealed class AdminCliAppTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TOKEN    = "stub-access-token-0123456789";
        private const string PASSWORD = "stub password value";
        private const string TOTP     = "123456";
        private const string REASON   = "cli test";
        private const char   NEW_LINE = '\n';

        private readonly List<StubHttpHandler.Request> _requests = new List<StubHttpHandler.Request>();

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task RunAsync_HelpAndUnknown_ReturnHelpAndUsageCodes()
        {
            using CliHarness harness = Harness(_ => throw new InvalidOperationException("No HTTP expected."));

            CliRun help = await harness.RunAsync(new[] { "--help" });
            CliRun empty = await harness.RunAsync(Array.Empty<string>());
            CliRun unknown = await harness.RunAsync(new[] { "frobnicate" });
            CliRun commandHelp = await harness.RunAsync(new[] { "grants", "--help" });

            Assert.Equal(CliExitCodes.SUCCESS, help.ExitCode);
            Assert.Contains("grants create", help.Out);
            Assert.Equal(CliExitCodes.USAGE, empty.ExitCode);
            Assert.Equal(CliExitCodes.USAGE, unknown.ExitCode);
            Assert.Equal(CliExitCodes.SUCCESS, commandHelp.ExitCode);
            Assert.Contains("grants execute", commandHelp.Out);
            Assert.Empty(_requests);
        }

        [Fact]
        public async Task RunAsync_NoUsableSession_ReturnsUnavailableWithoutSendingToken()
        {
            using CliHarness harness = Harness(_ => throw new InvalidOperationException("No HTTP expected."));
            string grantId = Guid.NewGuid().ToString("D");

            CliRun missing = await harness.RunAsync(new[] { "grants", "show", grantId });
            harness.Store.Save(new AdminSession("http://other.example:5080", TOKEN,
                DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds(), Guid.NewGuid().ToString("D"), "tester", 3));
            CliRun otherApi = await harness.RunAsync(new[] { "grants", "show", grantId });
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(-1));
            CliRun expired = await harness.RunAsync(new[] { "grants", "show", grantId });

            Assert.Equal(CliExitCodes.UNAVAILABLE, missing.ExitCode);
            Assert.Contains("login", missing.Error);
            Assert.Equal(CliExitCodes.UNAVAILABLE, otherApi.ExitCode);
            Assert.Contains("other.example", otherApi.Error);
            Assert.Equal(CliExitCodes.UNAVAILABLE, expired.ExitCode);
            Assert.Null(harness.Store.Load());
            Assert.Empty(_requests);
        }

        [Fact]
        public async Task Login_SecretsFromEnvironmentOrPrompt_SavesSessionAndNeverPrintsToken()
        {
            using CliHarness harness = Harness(_ => Json(HttpStatusCode.OK, new AdminLoginResponse(TOKEN,
                DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeMilliseconds(), Guid.NewGuid().ToString("D"),
                AdminContractCodes.Role.ADMIN)));
            harness.Environment[LoginCommand.PASSWORD_ENVIRONMENT] = PASSWORD;
            harness.Environment[LoginCommand.TOTP_ENVIRONMENT] = TOTP;

            CliRun json = await harness.RunAsync(new[] { "login", "--user", "Ops.One", "--json" });
            harness.Environment.Clear();
            CliRun prompted = await harness.RunAsync(new[] { "login", "--user", "ops.one" }, PASSWORD, TOTP);

            Assert.Equal(CliExitCodes.SUCCESS, json.ExitCode);
            Assert.Equal(0, json.SecretReads);
            Assert.DoesNotContain(TOKEN, json.Out + json.Error);
            Assert.Equal(AdminContractCodes.Role.ADMIN, JObject.Parse(json.Out.Trim())["role"]!.Value<int>());
            Assert.Equal(CliExitCodes.SUCCESS, prompted.ExitCode);
            Assert.Equal(2, prompted.SecretReads);
            Assert.DoesNotContain(TOKEN, prompted.Out + prompted.Error);
            Assert.DoesNotContain(PASSWORD, prompted.Out + prompted.Error);
            Assert.Equal(TOKEN, harness.Store.Load()!.AccessToken);
            Assert.All(_requests, request => Assert.False(request.HasToken));
            JObject body = JObject.Parse(_requests[0].Body);
            Assert.Equal(PASSWORD, body["password"]!.Value<string>());
            Assert.Equal(TOTP, body["totpCode"]!.Value<string>());
            Assert.EndsWith(AdminApiRoutes.AUTH_LOGIN, _requests[0].Uri.AbsolutePath);
        }

        [Fact]
        public async Task Login_InvalidCredentials_ReturnsRejectedWithCodeName()
        {
            using CliHarness harness = Harness(_ => Json(HttpStatusCode.Unauthorized,
                new ApiRejection(AdminContractCodes.RejectReason.INVALID_CREDENTIALS, string.Empty, 0)));

            CliRun run = await harness.RunAsync(new[] { "login", "--user", "ops" }, PASSWORD, TOTP);

            Assert.Equal(CliExitCodes.REJECTED, run.ExitCode);
            Assert.Contains("INVALID_CREDENTIALS (9001)", run.Error);
            Assert.Contains("HTTP 401", run.Error);
            Assert.Null(harness.Store.Load());
        }

        [Fact]
        public async Task Write_Declined_SendsNothing()
        {
            using CliHarness harness = Harness(_ => throw new InvalidOperationException("No HTTP expected."));
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(5));

            CliRun declined = await harness.RunAsync(new[] { "invites", "revoke", Guid.NewGuid().ToString("D"),
                "--reason", REASON }, "n");
            CliRun noInput = await harness.RunAsync(new[] { "invites", "revoke", Guid.NewGuid().ToString("D"),
                "--reason", REASON });

            Assert.Equal(CliExitCodes.USAGE, declined.ExitCode);
            Assert.Equal(CliExitCodes.USAGE, noInput.ExitCode);
            Assert.Empty(_requests);
        }

        [Fact]
        public async Task Write_ConnectionLost_PrintsOperationIdRetryHint()
        {
            using CliHarness harness = Harness(_ => throw new HttpRequestException("connection refused"));
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(5));

            CliRun run = await harness.RunAsync(new[] { "notices", "end", Guid.NewGuid().ToString("D"), "--reason",
                REASON }, "y");

            Assert.Equal(CliExitCodes.UNAVAILABLE, run.ExitCode);
            string operationId = JObject.Parse(Assert.Single(_requests).Body)["operationId"]!.Value<string>()!;
            Assert.Contains(operationId, run.Out);
            Assert.Contains("--operation-id " + operationId, run.Error);
            Assert.DoesNotContain(TOKEN, run.Out + run.Error);
        }

        [Fact]
        public async Task Write_OperationIdOption_IsSentAsIs()
        {
            string operationId = Guid.NewGuid().ToString("D");
            using CliHarness harness = Harness(_ => Json(HttpStatusCode.OK, Committed(operationId)));
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(5));

            CliRun run = await harness.RunAsync(new[] { "invites", "revoke", Guid.NewGuid().ToString("D"), "--reason",
                REASON, "--operation-id", operationId, "--yes" });

            Assert.Equal(CliExitCodes.SUCCESS, run.ExitCode);
            StubHttpHandler.Request request = Assert.Single(_requests);
            Assert.True(request.HasToken);
            Assert.Equal(operationId, JObject.Parse(request.Body)["operationId"]!.Value<string>());
            Assert.Equal(REASON, JObject.Parse(request.Body)["reason"]!.Value<string>());
        }

        [Fact]
        public async Task Rejections_MapToNamesAndExitCodes()
        {
            string operationId = Guid.NewGuid().ToString("D");
            HttpStatusCode status = HttpStatusCode.Conflict;
            object body = new OperationResult(operationId, ContractCodes.OperationStatus.REJECTED,
                AdminContractCodes.RejectReason.INVALID_STATE, 0, 0, null);
            using CliHarness harness = Harness(_ => Json(status, body));
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(5));
            string[] revoke = { "invites", "revoke", Guid.NewGuid().ToString("D"), "--reason", REASON, "--yes" };

            CliRun conflict = await harness.RunAsync(revoke);
            status = HttpStatusCode.ServiceUnavailable;
            body = new ApiRejection(ContractCodes.RejectReason.BACKEND_UNAVAILABLE, string.Empty, 0);
            CliRun unavailable = await harness.RunAsync(revoke);
            status = HttpStatusCode.Unauthorized;
            body = new ApiRejection(ContractCodes.RejectReason.TOKEN_REVOKED, string.Empty, 0);
            CliRun revoked = await harness.RunAsync(revoke);

            Assert.Equal(CliExitCodes.REJECTED, conflict.ExitCode);
            Assert.Contains("INVALID_STATE (9005)", conflict.Error);
            Assert.Contains("HTTP 409", conflict.Error);
            Assert.Contains(operationId, conflict.Error);
            Assert.Equal(CliExitCodes.UNAVAILABLE, unavailable.ExitCode);
            Assert.Contains("BACKEND_UNAVAILABLE (810)", unavailable.Error);
            Assert.Equal(CliExitCodes.UNAVAILABLE, revoked.ExitCode);
            Assert.Null(harness.Store.Load());
        }

        [Fact]
        public async Task ServiceStateSet_UnspecifiedValues_KeepsCurrentAndSendsExpectedRevision()
        {
            const long REVISION = 7;
            using CliHarness harness = Harness(request => request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, new AdminServiceStateResponse(true, new[] { ServiceFeatureKeys.AUCTION },
                    new[] { "build-1" }, REVISION, 0, null, AdminServiceStateResponse.SOURCE_DATABASE,
                    ServiceFeatureKeys.All.ToArray()))
                : Json(HttpStatusCode.OK, Committed(Guid.NewGuid().ToString("D"))));
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(5));

            CliRun run = await harness.RunAsync(new[] { "service-state", "set", "--maintenance", "off", "--reason",
                REASON, "--yes" });
            CliRun unknownFeature = await harness.RunAsync(new[] { "service-state", "set", "--disable", "teleport",
                "--reason", REASON, "--yes" });

            Assert.Equal(CliExitCodes.SUCCESS, run.ExitCode);
            Assert.Equal(CliExitCodes.USAGE, unknownFeature.ExitCode);
            StubHttpHandler.Request put = Assert.Single(_requests, request => request.Method == HttpMethod.Put);
            JObject body = JObject.Parse(put.Body);
            Assert.False(body["maintenance"]!.Value<bool>());
            Assert.Equal(new[] { ServiceFeatureKeys.AUCTION }, body["disabledFeatures"]!.Values<string>());
            Assert.Equal(new[] { "build-1" }, body["allowedBuildIds"]!.Values<string>());
            Assert.Equal(REVISION, body["expectedRevision"]!.Value<long>());
            Assert.Contains("service-state set --maintenance off --disable='" + ServiceFeatureKeys.AUCTION
                + "' --allow-builds='build-1' --expected-revision 7 --operation-id " + body["operationId"]!.Value<string>()
                + " --reason='" + REASON + "'", run.Out);
        }

        /// <summary> 응답을 잃은 뒤 다른 운영자가 생략했던 항목을 바꿔도, 출력된 재시도 명령은 현재 값을 읽지 않고 처음과 같은 본문을
        /// 보낸다. 생략 항목을 다시 GET으로 채우면 본문이 달라져 IDEMPOTENCY_CONFLICT가 된다 </summary>
        [Theory]
        [InlineData("build-1", REASON, "'build-1'", "'cli test'")]
        [InlineData("build-\"quote\"-$HOME;'one'`tick`\\path&|<>@#{}[]()",
            "ops \"$env:PATH\"; $(throw 'injected') &|<>@#{}[]() `tick` \\path",
            "'build-\"quote\"-$HOME;''one''`tick`\\path&|<>@#{}[]()'",
            "'ops \"$env:PATH\"; $(throw ''injected'') &|<>@#{}[]() `tick` \\path'")]
        [InlineData("--build", "--reason", "'--build'", "'--reason'")]
        [InlineData("build-\u2018one\u2019-\u201Atwo\u201B", "운영자 \u2018재지급\u2019 \u201A확인\u201B",
            "'build-\u2018\u2018one\u2019\u2019-\u201A\u201Atwo\u201B\u201B'",
            "'운영자 \u2018\u2018재지급\u2019\u2019 \u201A\u201A확인\u201B\u201B'")]
        [InlineData(null, REASON, "'none'", "'cli test'")]
        public async Task ServiceStateSet_RetryCommandAfterOtherChange_SendsSameBodyWithoutReading(string? buildId,
            string reason, string expectedBuild, string expectedReason)
        {
            AdminServiceStateResponse state = new AdminServiceStateResponse(true, new[] { ServiceFeatureKeys.AUCTION },
                buildId == null ? Array.Empty<string>() : new[] { buildId }, 7, 0, null,
                AdminServiceStateResponse.SOURCE_DATABASE, ServiceFeatureKeys.All.ToArray());
            using CliHarness harness = Harness(request => request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, state)
                : throw new HttpRequestException("response lost"));
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(5));

            CliRun lost = await harness.RunAsync(new[] { "service-state", "set", "--maintenance", "off", "--reason="
                + reason, "--yes" });
            string firstBody = Assert.Single(_requests, request => request.Method == HttpMethod.Put).Body;
            string retryLine = lost.Out.Split(NEW_LINE).Select(line => line.Trim())
                .Single(line => line.StartsWith("service-state set ", StringComparison.Ordinal));
            string operationId = JObject.Parse(firstBody)["operationId"]!.Value<string>()!;
            Assert.Equal("service-state set --maintenance off --disable='" + ServiceFeatureKeys.AUCTION
                + "' --allow-builds=" + expectedBuild + " --expected-revision 7 --operation-id " + operationId
                + " --reason=" + expectedReason, retryLine);
            Assert.Contains("PowerShell 7.3 이상(기본 Windows/Standard 인자 전달 모드)", lost.Out);
            state = new AdminServiceStateResponse(true, new[] { ServiceFeatureKeys.CRAFTING }, new[] { "build-2" }, 8, 0,
                null, AdminServiceStateResponse.SOURCE_DATABASE, ServiceFeatureKeys.All.ToArray());
            _requests.Clear();
            using CliHarness retryHarness = Harness(request => request.Method == HttpMethod.Get
                ? throw new InvalidOperationException("The retry command must not read the current state.")
                : Json(HttpStatusCode.OK, Committed(Guid.NewGuid().ToString("D"))));
            retryHarness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(5));
            CliRun retried = await retryHarness.RunAsync(new[] { "service-state", "set", "--maintenance", "off",
                "--disable=" + ServiceFeatureKeys.AUCTION, "--allow-builds=" + (buildId ?? "none"),
                "--expected-revision", "7", "--operation-id", operationId, "--reason=" + reason, "--yes" });

            Assert.Equal(CliExitCodes.UNAVAILABLE, lost.ExitCode);
            Assert.Equal(CliExitCodes.SUCCESS, retried.ExitCode);
            StubHttpHandler.Request put = Assert.Single(_requests);
            Assert.Equal(HttpMethod.Put, put.Method);
            Assert.True(JToken.DeepEquals(JObject.Parse(firstBody), JObject.Parse(put.Body)));
        }

        [Theory]
        [InlineData("none", REASON)]
        [InlineData("build,1", REASON)]
        [InlineData(" build-1", REASON)]
        [InlineData("build-1 ", REASON)]
        [InlineData("", REASON)]
        [InlineData("build\r\nline", REASON)]
        [InlineData("build\tline", REASON)]
        [InlineData("build-1", "first\r\nsecond")]
        [InlineData("build-1", "first\tsecond")]
        public async Task ServiceStateSet_UnrepresentableRetryValue_WarnsWithoutPrintingCommand(string buildId,
            string reason)
        {
            using CliHarness harness = Harness(request => request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, new AdminServiceStateResponse(true, new[] { ServiceFeatureKeys.AUCTION },
                    new[] { buildId }, 7, 0, null, AdminServiceStateResponse.SOURCE_DATABASE,
                    ServiceFeatureKeys.All.ToArray()))
                : throw new HttpRequestException("response lost"));
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(5));

            CliRun run = await harness.RunAsync(new[] { "service-state", "set", "--maintenance", "off", "--reason",
                reason, "--yes" });

            Assert.Equal(CliExitCodes.UNAVAILABLE, run.ExitCode);
            Assert.DoesNotContain("service-state set --", run.Out + run.Error);
            Assert.Contains("재시도 명령으로 옮길 수 없습니다", run.Error);
            Assert.Contains("service-state show", run.Error);
            JObject body = JObject.Parse(Assert.Single(_requests, request => request.Method == HttpMethod.Put).Body);
            Assert.Equal(new[] { buildId }, body["allowedBuildIds"]!.Values<string>());
            Assert.Equal(reason, body["reason"]!.Value<string>());
        }

        /// <summary> 로그아웃이 500이나 503을 받아도 세션 파일은 지우고 서버 토큰 폐기는 확인하지 못했다고 알린다 </summary>
        [Fact]
        public async Task Logout_ServerError_DeletesSessionFileAndWarnsUnconfirmed()
        {
            HttpStatusCode status = HttpStatusCode.InternalServerError;
            using CliHarness harness = Harness(_ => Json(status,
                new ApiRejection(ContractCodes.RejectReason.BACKEND_UNAVAILABLE, string.Empty, 0)));
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(5));

            CliRun serverError = await harness.RunAsync(new[] { "logout" });
            bool fileAfterServerError = harness.Store.Load() != null;
            status = HttpStatusCode.ServiceUnavailable;
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(5));
            CliRun unavailable = await harness.RunAsync(new[] { "logout" });

            Assert.Equal(CliExitCodes.REJECTED, serverError.ExitCode);
            Assert.False(fileAfterServerError);
            Assert.Contains("서버 토큰 폐기는 확인하지 못했습니다", serverError.Error);
            Assert.Equal(CliExitCodes.UNAVAILABLE, unavailable.ExitCode);
            Assert.Null(harness.Store.Load());
            Assert.Contains("서버 토큰 폐기는 확인하지 못했습니다", unavailable.Error);
            Assert.DoesNotContain(TOKEN, serverError.Out + serverError.Error + unavailable.Out + unavailable.Error);
        }

        [Fact]
        public async Task AccountsSearch_InviteCode_IsSentInBodyNotUrl()
        {
            const string CODE = "invite-code-raw-value";
            using CliHarness harness = Harness(_ => Json(HttpStatusCode.OK,
                new AdminAccountSearchResponse(Array.Empty<AdminAccountSummary>())));
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(5));

            CliRun run = await harness.RunAsync(new[] { "accounts", "search", CODE });

            Assert.Equal(CliExitCodes.SUCCESS, run.ExitCode);
            StubHttpHandler.Request request = Assert.Single(_requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.DoesNotContain(CODE, request.Uri.ToString());
            Assert.Equal(CODE, JObject.Parse(request.Body)["query"]!.Value<string>());
        }

        [Fact]
        public async Task Ledger_AllFlag_FollowsCursorAndMergesJson()
        {
            string accountId = Guid.NewGuid().ToString("D");
            using CliHarness harness = Harness(request => Json(HttpStatusCode.OK, request.RequestUri!.Query.Contains("cursor=c1")
                ? new AdminLedgerPage(new[] { Entry(accountId, 2) }, Array.Empty<AdminLedgerReasonTotal>(), null)
                : new AdminLedgerPage(new[] { Entry(accountId, 1) }, Array.Empty<AdminLedgerReasonTotal>(), "c1")));
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(5));

            CliRun noScope = await harness.RunAsync(new[] { "ledger", "--reason", REASON });
            CliRun run = await harness.RunAsync(new[] { "ledger", "--account", accountId, "--all", "--json", "--reason",
                REASON, "--economy-reason", "ADMIN_GRANT" });

            Assert.Equal(CliExitCodes.USAGE, noScope.ExitCode);
            Assert.Equal(CliExitCodes.SUCCESS, run.ExitCode);
            Assert.Equal(2, _requests.Count);
            Assert.Contains(AdminLogQueryParameters.REASON + "=cli%20test", _requests[0].Uri.Query);
            Assert.Contains(AdminLogQueryParameters.REASON_CODE + "=" + ContractCodes.EconomyReason.ADMIN_GRANT,
                _requests[0].Uri.Query);
            Assert.Contains("accountId=" + accountId, _requests[1].Uri.Query);
            JObject merged = JObject.Parse(run.Out.Trim());
            Assert.Equal(new long[] { 1, 2 }, merged["entries"]!.Select(entry => entry["delta"]!.Value<long>()));
            Assert.Equal(JTokenType.Null, merged["nextCursor"]!.Type);
        }

        [Fact]
        public async Task Kick_KeepInvites_SendsFalseAndReportsCurrentSessionKept()
        {
            string accountId = Guid.NewGuid().ToString("D");
            using CliHarness harness = Harness(_ => Json(HttpStatusCode.OK, new AdminAccountKickResponse(
                Committed(Guid.NewGuid().ToString("D")), accountId, false, true)));
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(5));

            CliRun run = await harness.RunAsync(new[] { "accounts", "kick", accountId, "--keep-invites", "--reason",
                REASON, "--yes" });

            Assert.Equal(CliExitCodes.SUCCESS, run.ExitCode);
            Assert.Contains("기존 토큰을 폐기했고 현재 접속은 유지", run.Out);
            Assert.Contains("비밀번호 로그인은 막지 않습니다", run.Out);
            Assert.False(JObject.Parse(Assert.Single(_requests).Body)["revokeInvites"]!.Value<bool>());
        }

        [Fact]
        public async Task Sessions_LivenessDeferred_WarnsThatCancelIsBlocked()
        {
            using CliHarness harness = Harness(_ => Json(HttpStatusCode.OK,
                new AdminRecoverySessionPage(Array.Empty<AdminRecoverySession>(), null, true)));
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(5));

            CliRun run = await harness.RunAsync(new[] { "sessions" });

            Assert.Equal(CliExitCodes.SUCCESS, run.ExitCode);
            Assert.Contains("생존 판정 보류", run.Error);
        }

        private CliHarness Harness(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            return new CliHarness(() => new StubHttpHandler(_requests, respond));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, object body)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(JsonConvert.SerializeObject(body, ContractJson.CreateSettings()), Encoding.UTF8,
                    "application/json")
            };
        }

        private static OperationResult Committed(string operationId)
        {
            return new OperationResult(operationId, ContractCodes.OperationStatus.COMMITTED, 0, 0, 0,
                Guid.NewGuid().ToString("D"));
        }

        private static AdminLedgerEntry Entry(string accountId, long delta)
        {
            return new AdminLedgerEntry(Guid.NewGuid().ToString("D"), 0, accountId, null, AdminGrantCodes.ASSET_CURRENCY,
                string.Empty, string.Empty, delta, delta, ContractCodes.EconomyReason.ADMIN_GRANT, "admin_adjustment",
                Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), "Admin", Guid.NewGuid().ToString("D"));
        }
    }
}
