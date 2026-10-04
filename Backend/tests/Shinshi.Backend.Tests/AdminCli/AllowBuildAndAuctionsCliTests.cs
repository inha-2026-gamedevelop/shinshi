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
    /// <summary> service-state allow-build add/remove(현재 목록과 revision 기준 저장, 중복 무시, 상한, revision 충돌)와
    /// auctions list 요청 모양을 DB 없이 검사한다 </summary>
    public sealed class AllowBuildAndAuctionsCliTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TOKEN    = "stub-access-token-0123456789";
        private const string REASON   = "release 2026-10-03";
        private const long   REVISION = 7;

        private readonly List<StubHttpHandler.Request> _requests = new List<StubHttpHandler.Request>();

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task AllowBuildAdd_NewBuild_SendsWholeListWithCurrentValuesAndRevision()
        {
            using CliHarness harness = Harness(request => request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, State("build-1"))
                : Json(HttpStatusCode.OK, Committed()));

            CliRun run = await harness.RunAsync(new[] { "service-state", "allow-build", "add", "build-2", "--reason", REASON,
                "--yes" });

            Assert.Equal(CliExitCodes.SUCCESS, run.ExitCode);
            JObject body = JObject.Parse(Assert.Single(_requests, request => request.Method == HttpMethod.Put).Body);
            Assert.Equal(new[] { "build-1", "build-2" }, body["allowedBuildIds"]!.Values<string>());
            Assert.True(body["maintenance"]!.Value<bool>());
            Assert.Equal(new[] { ServiceFeatureKeys.AUCTION }, body["disabledFeatures"]!.Values<string>());
            Assert.Equal(REVISION, body["expectedRevision"]!.Value<long>());
            Assert.Equal(REASON, body["reason"]!.Value<string>());
            Assert.Contains("service-state set --maintenance on", run.Out);
        }

        [Fact]
        public async Task AllowBuildRemove_ExistingBuild_SendsListWithoutIt()
        {
            using CliHarness harness = Harness(request => request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, State("build-1", "build-2"))
                : Json(HttpStatusCode.OK, Committed()));

            CliRun run = await harness.RunAsync(new[] { "service-state", "allow-build", "remove", "build-1", "--reason",
                REASON, "--yes" });

            Assert.Equal(CliExitCodes.SUCCESS, run.ExitCode);
            JObject body = JObject.Parse(Assert.Single(_requests, request => request.Method == HttpMethod.Put).Body);
            Assert.Equal(new[] { "build-2" }, body["allowedBuildIds"]!.Values<string>());
        }

        /// <summary> 이미 있는 빌드 추가와 없는 빌드 제거는 저장하지 않고 성공으로 끝난다 </summary>
        [Fact]
        public async Task AllowBuild_NoChange_DoesNotWrite()
        {
            using CliHarness harness = Harness(request => request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, State("build-1"))
                : throw new InvalidOperationException("No write expected."));

            CliRun duplicate = await harness.RunAsync(new[] { "service-state", "allow-build", "add", "build-1",
                "--reason", REASON, "--yes" });
            CliRun absent = await harness.RunAsync(new[] { "service-state", "allow-build", "remove", "build-9",
                "--reason", REASON, "--yes" });

            Assert.Equal(CliExitCodes.SUCCESS, duplicate.ExitCode);
            Assert.Equal(CliExitCodes.SUCCESS, absent.ExitCode);
            Assert.Contains("이미 허용 목록에 있어", duplicate.Out);
            Assert.Contains("허용 목록에 없어", absent.Out);
            Assert.DoesNotContain(_requests, request => request.Method == HttpMethod.Put);
        }

        [Fact]
        public async Task AllowBuildAdd_AtLimitOrInvalidId_IsUsageErrorWithoutWrite()
        {
            string[] full = Enumerable.Range(0, AdminServiceStateUpdateRequest.MAX_BUILD_IDS)
                .Select(index => "build-" + index).ToArray();
            using CliHarness harness = Harness(request => request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, State(full))
                : throw new InvalidOperationException("No write expected."));

            CliRun limit = await harness.RunAsync(new[] { "service-state", "allow-build", "add", "build-new", "--reason",
                REASON, "--yes" });
            CliRun comma = await harness.RunAsync(new[] { "service-state", "allow-build", "add", "a,b", "--reason",
                REASON, "--yes" });
            CliRun verb = await harness.RunAsync(new[] { "service-state", "allow-build", "replace", "b", "--reason",
                REASON, "--yes" });

            Assert.Equal(CliExitCodes.USAGE, limit.ExitCode);
            Assert.Contains(AdminServiceStateUpdateRequest.MAX_BUILD_IDS + "개까지", limit.Error);
            Assert.Equal(CliExitCodes.USAGE, comma.ExitCode);
            Assert.Equal(CliExitCodes.USAGE, verb.ExitCode);
            Assert.DoesNotContain(_requests, request => request.Method == HttpMethod.Put);
        }

        /// <summary> 그 사이 다른 운영자가 저장해 409 INVALID_STATE면 자동으로 다시 보내지 않고 거절로 끝낸다 </summary>
        [Fact]
        public async Task AllowBuildAdd_RevisionConflict_ReportsAndDoesNotRetry()
        {
            using CliHarness harness = Harness(request => request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, State("build-1"))
                : Json(HttpStatusCode.Conflict, new OperationResult(Guid.NewGuid().ToString("D"),
                    ContractCodes.OperationStatus.REJECTED, AdminContractCodes.RejectReason.INVALID_STATE, REVISION + 1,
                    0, string.Empty)));

            CliRun run = await harness.RunAsync(new[] { "service-state", "allow-build", "add", "build-2", "--reason",
                REASON, "--yes" });

            Assert.Equal(CliExitCodes.REJECTED, run.ExitCode);
            Assert.Contains("먼저 저장해 거절됐습니다", run.Error);
            Assert.Single(_requests, request => request.Method == HttpMethod.Put);
        }

        [Fact]
        public async Task AuctionsList_StateAndPage_SendsQueryAndPrintsRows()
        {
            AdminAuctionPage page = new AdminAuctionPage(new[]
            {
                new AdminAuctionSession(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), 1000, true, false,
                    Guid.NewGuid().ToString("D"), AdminAuctionPage.STATE_OPEN, "item_boss_material_rare", 2, 2000, 150)
            }, null!, false);
            using CliHarness harness = Harness(_ => Json(HttpStatusCode.OK, page));

            CliRun run = await harness.RunAsync(new[] { "auctions", "list", "--state", AdminAuctionPage.STATE_OPEN,
                "--limit", "5" });

            Assert.Equal(CliExitCodes.SUCCESS, run.ExitCode);
            StubHttpHandler.Request request = Assert.Single(_requests);
            Assert.Equal(AdminApiRoutes.AUCTIONS, request.Uri.AbsolutePath);
            Assert.Contains(AdminApiRoutes.STATE_PARAMETER + "=" + AdminAuctionPage.STATE_OPEN, request.Uri.Query);
            Assert.Contains("item_boss_material_rare", run.Out);
            Assert.Contains("150", run.Out);
        }

        private CliHarness Harness(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            CliHarness harness = new CliHarness(() => new StubHttpHandler(_requests, respond));
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(5));
            return harness;
        }

        private static AdminServiceStateResponse State(params string[] builds)
        {
            return new AdminServiceStateResponse(true, new[] { ServiceFeatureKeys.AUCTION }, builds, REVISION, 0, null,
                AdminServiceStateResponse.SOURCE_DATABASE, ServiceFeatureKeys.All.ToArray());
        }

        private static HttpResponseMessage Json(HttpStatusCode status, object body)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(JsonConvert.SerializeObject(body, ContractJson.CreateSettings()), Encoding.UTF8,
                    "application/json")
            };
        }

        private static OperationResult Committed()
        {
            return new OperationResult(Guid.NewGuid().ToString("D"), ContractCodes.OperationStatus.COMMITTED, 0, REVISION + 1,
                0, Guid.NewGuid().ToString("D"));
        }
    }
}
