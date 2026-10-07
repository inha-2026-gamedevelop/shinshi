// System
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 실제 PostgreSQL과 Program 파이프라인에서 서비스 상태 저장, 역할, revision 충돌, 공개 응답과 로그인 차단, 공지를 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class AdminServiceStateHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string REASON      = "open the trial";
        private const string BUILD       = "trial-build-1";
        private const string OTHER_BUILD = "trial-build-2";

        // 재전송 시험의 공지 노출 길이와, 끝 시각을 확실히 넘기려고 더 기다리는 여유
        private const int SHORT_NOTICE_MS  = 2000;
        private const int EXPIRY_MARGIN_MS = 1000;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task ServiceState_RolesRevisionAndPublicView_AppliesImmediately()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, true);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] viewerSecret) = await host.CreateAdminAsync("state.viewer", EAdminRole.Viewer);
            (Guid _, byte[] operatorSecret) = await host.CreateAdminAsync("state.operator", EAdminRole.Operator);
            (Guid adminId, byte[] adminSecret) = await host.CreateAdminAsync("state.admin", EAdminRole.Admin);
            string viewer = await AdminApiTestHost.LoginAsync(client, "state.viewer", viewerSecret);
            string operatorToken = await AdminApiTestHost.LoginAsync(client, "state.operator", operatorSecret);
            string admin = await AdminApiTestHost.LoginAsync(client, "state.admin", adminSecret);
            string invite = await IssueRawInviteAsync(database.DataSource, host.AuthOptions);

            // 0008 초기 행: 점검 중, 빈 허용 빌드. 공개 응답과 로그인 모두 DB 값을 따른다
            ServiceStateResponse seed = await GetPublicAsync(client);
            Assert.True(seed.Maintenance);
            Assert.Empty(seed.AllowedBuildIds);
            Assert.Empty(seed.Notices);
            Assert.Equal(ContractCodes.RejectReason.MAINTENANCE, await ExchangeRejectionAsync(client, invite, BUILD));

            using HttpResponseMessage viewed = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                AdminApiRoutes.SERVICE_STATE, viewer);
            Assert.Equal(HttpStatusCode.OK, viewed.StatusCode);
            AdminServiceStateResponse state = await AdminTestSupport.ReadAsync<AdminServiceStateResponse>(viewed);
            Assert.Equal(0, state.Revision);
            Assert.Equal(AdminServiceStateResponse.SOURCE_DATABASE, state.Source);
            Assert.Null(state.UpdatedBy);
            Assert.Contains(ServiceFeatureKeys.BOSS_ENTRY, state.KnownFeatures);

            AdminServiceStateUpdateRequest open = Update(false, new[] { ServiceFeatureKeys.BOSS_ENTRY,
                ServiceFeatureKeys.AUCTION, ServiceFeatureKeys.AUCTION }, new[] { OTHER_BUILD, BUILD }, 0);
            using HttpResponseMessage byViewer = await AdminApiTestHost.SendAsync(client, HttpMethod.Put,
                AdminApiRoutes.SERVICE_STATE, viewer, open);
            using HttpResponseMessage byOperator = await AdminApiTestHost.SendAsync(client, HttpMethod.Put,
                AdminApiRoutes.SERVICE_STATE, operatorToken, open);
            using HttpResponseMessage anonymous = await AdminApiTestHost.SendAsync(client, HttpMethod.Put,
                AdminApiRoutes.SERVICE_STATE, null, open);
            Assert.Equal(HttpStatusCode.Forbidden, byViewer.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, byOperator.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            using HttpResponseMessage unknownFeature = await AdminApiTestHost.SendAsync(client, HttpMethod.Put,
                AdminApiRoutes.SERVICE_STATE, admin, Update(false, new[] { "teleport" }, new[] { BUILD }, 0));
            using HttpResponseMessage missingMaintenance = await AdminApiTestHost.SendAsync(client, HttpMethod.Put,
                AdminApiRoutes.SERVICE_STATE, admin, Update(null, Array.Empty<string>(), new[] { BUILD }, 0));
            using HttpResponseMessage blankBuild = await AdminApiTestHost.SendAsync(client, HttpMethod.Put,
                AdminApiRoutes.SERVICE_STATE, admin, Update(false, Array.Empty<string>(), new[] { " " }, 0));
            Assert.Equal(HttpStatusCode.BadRequest, unknownFeature.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, missingMaintenance.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, blankBuild.StatusCode);
            Assert.Equal(AdminContractCodes.RejectReason.INVALID_REQUEST,
                (await AdminTestSupport.ReadAsync<ApiRejection>(unknownFeature)).ReasonCode);

            using HttpResponseMessage saved = await AdminApiTestHost.SendAsync(client, HttpMethod.Put,
                AdminApiRoutes.SERVICE_STATE, admin, open);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            OperationResult savedResult = await AdminTestSupport.ReadAsync<OperationResult>(saved);
            Assert.Equal(1, savedResult.CommittedRevision);

            // 이 인스턴스가 쓴 값은 캐시 주기를 기다리지 않고 공개 응답과 로그인 판정에 바로 보인다
            ServiceStateResponse opened = await GetPublicAsync(client);
            Assert.False(opened.Maintenance);
            Assert.Equal(new[] { BUILD, OTHER_BUILD }, opened.AllowedBuildIds);
            Assert.Equal(new[] { ServiceFeatureKeys.AUCTION, ServiceFeatureKeys.BOSS_ENTRY }, opened.DisabledFeatures);
            Assert.Equal(ContractCodes.RejectReason.BUILD_NOT_ALLOWED,
                await ExchangeRejectionAsync(client, invite, "unknown-build"));

            using HttpResponseMessage stale = await AdminApiTestHost.SendAsync(client, HttpMethod.Put,
                AdminApiRoutes.SERVICE_STATE, admin, Update(true, Array.Empty<string>(), new[] { BUILD }, 0));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            OperationResult staleResult = await AdminTestSupport.ReadAsync<OperationResult>(stale);
            Assert.Equal(AdminContractCodes.RejectReason.INVALID_STATE, staleResult.ReasonCode);
            Assert.Equal(1, staleResult.CommittedRevision);
            Assert.False((await GetPublicAsync(client)).Maintenance);

            // 버전 차단: 허용 목록에서 빼면 그 빌드의 로그인이 바로 거절된다
            using HttpResponseMessage dropped = await AdminApiTestHost.SendAsync(client, HttpMethod.Put,
                AdminApiRoutes.SERVICE_STATE, admin, Update(false, Array.Empty<string>(), new[] { OTHER_BUILD }, 1));
            Assert.Equal(HttpStatusCode.OK, dropped.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.BUILD_NOT_ALLOWED, await ExchangeRejectionAsync(client, invite, BUILD));
            Assert.Equal(new[] { OTHER_BUILD }, (await GetPublicAsync(client)).AllowedBuildIds);

            Assert.Equal(2L, await AdminTestSupport.CountAsync(database.DataSource,
                """
                SELECT count(*) FROM admin_audit_log
                WHERE action = @Action AND admin_id = @AdminId AND target_kind = 'service_state' AND target_id = '1'
                    AND before_state IS NOT NULL AND after_state IS NOT NULL;
                """, new { Action = AdminServiceStateAuditActions.SERVICE_STATE_UPDATE, AdminId = adminId }));
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                """
                SELECT count(*) FROM admin_audit_log
                WHERE after_state ->> 'maintenance' = 'false' AND before_state ->> 'maintenance' = 'true';
                """));
            Assert.Equal(adminId, await ReadUpdatedByAsync(database.DataSource));
        }

        [PostgresFact]
        public async Task Notices_CreateListEnd_FiltersPublicViewByPeriod()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, true);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] viewerSecret) = await host.CreateAdminAsync("notice.viewer", EAdminRole.Viewer);
            (Guid operatorId, byte[] operatorSecret) = await host.CreateAdminAsync("notice.operator",
                EAdminRole.Operator);
            string viewer = await AdminApiTestHost.LoginAsync(client, "notice.viewer", viewerSecret);
            string operatorToken = await AdminApiTestHost.LoginAsync(client, "notice.operator", operatorSecret);
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long hour = (long)TimeSpan.FromHours(1).TotalMilliseconds;

            AdminNoticeCreateRequest active = new AdminNoticeCreateRequest(AdminApiTestHost.NewOperationId(), REASON,
                "  Maintenance tonight  ", "Servers restart at 02:00.", ServiceNotice.TARGET_LAUNCHER,
                ServiceNotice.SEVERITY_WARNING, null, now + hour, ServiceNotice.CATEGORY_UPDATE);
            AdminNoticeCreateRequest scheduled = new AdminNoticeCreateRequest(AdminApiTestHost.NewOperationId(), REASON,
                "Next event", string.Empty, ServiceNotice.TARGET_BOTH, ServiceNotice.SEVERITY_CRITICAL, now + hour,
                now + (2 * hour));
            using HttpResponseMessage byViewer = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.NOTICES, viewer, active);
            AdminNoticeCreateRequest late = new AdminNoticeCreateRequest(AdminApiTestHost.NewOperationId(), REASON,
                "late", string.Empty, ServiceNotice.TARGET_GAME, ServiceNotice.SEVERITY_INFO, null, now - 1);
            using HttpResponseMessage pastEnd = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.NOTICES, operatorToken, late);
            using HttpResponseMessage pastEndReplay = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.NOTICES, operatorToken, late);
            using HttpResponseMessage badTarget = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.NOTICES, operatorToken, new AdminNoticeCreateRequest(AdminApiTestHost.NewOperationId(),
                    REASON, "title", string.Empty, "Everyone", ServiceNotice.SEVERITY_INFO, null, now + hour));
            using HttpResponseMessage badCategory = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.NOTICES, operatorToken, new AdminNoticeCreateRequest(AdminApiTestHost.NewOperationId(),
                    REASON, "title", string.Empty, ServiceNotice.TARGET_BOTH, ServiceNotice.SEVERITY_INFO, null,
                    now + hour, "Rumor"));
            Assert.Equal(HttpStatusCode.BadRequest, badCategory.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, byViewer.StatusCode);
            // 지금과 비교하는 검사는 효과 안의 DB 시각으로 해 거절도 작업 결과로 저장된다. 형식 오류는 작업 행 없이 400이다
            Assert.Equal(HttpStatusCode.Conflict, pastEnd.StatusCode);
            OperationResult pastEndResult = await AdminTestSupport.ReadAsync<OperationResult>(pastEnd);
            Assert.Equal(AdminContractCodes.RejectReason.INVALID_REQUEST, pastEndResult.ReasonCode);
            Assert.Equal(pastEndResult.ServerTimeUnixMs,
                (await AdminTestSupport.ReadAsync<OperationResult>(pastEndReplay)).ServerTimeUnixMs);
            Assert.Equal(HttpStatusCode.BadRequest, badTarget.StatusCode);

            using HttpResponseMessage created = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.NOTICES, operatorToken, active);
            using HttpResponseMessage replayed = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.NOTICES, operatorToken, active);
            using HttpResponseMessage createdScheduled = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.NOTICES, operatorToken, scheduled);
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            Assert.Equal(HttpStatusCode.OK, createdScheduled.StatusCode);
            string noticeId = (await AdminTestSupport.ReadAsync<OperationResult>(created)).ReceiptId;
            Assert.Equal(noticeId, (await AdminTestSupport.ReadAsync<OperationResult>(replayed)).ReceiptId);
            Assert.Equal(2L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM service_notices;"));

            ServiceStateResponse visible = await GetPublicAsync(client);
            Assert.Equal("Maintenance tonight", visible.Notice);
            Assert.Single(visible.Notices);
            Assert.Equal(noticeId, visible.Notices[0].NoticeId);
            Assert.Equal(ServiceNotice.TARGET_LAUNCHER, visible.Notices[0].Target);
            Assert.Equal(ServiceNotice.SEVERITY_WARNING, visible.Notices[0].Severity);
            Assert.Equal(ServiceNotice.CATEGORY_UPDATE, visible.Notices[0].Category);

            using HttpResponseMessage listed = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                AdminApiRoutes.NOTICES, viewer);
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            AdminNoticeSummary[] notices = (await AdminTestSupport.ReadAsync<AdminNoticeListResponse>(listed)).Notices;
            Assert.Equal(new[] { AdminNoticeSummary.STATUS_SCHEDULED, AdminNoticeSummary.STATUS_ACTIVE },
                notices.Select(notice => notice.Status));
            Assert.Equal(operatorId.ToString("D"), notices[1].CreatedBy);
            Assert.Equal(new[] { ServiceNotice.CATEGORY_NOTICE, ServiceNotice.CATEGORY_UPDATE },
                notices.Select(notice => notice.Category));

            string endRoute = string.Format(AdminApiRoutes.NOTICE_END_FORMAT, noticeId);
            using HttpResponseMessage ended = await AdminApiTestHost.SendAsync(client, HttpMethod.Post, endRoute,
                operatorToken, new AdminNoticeEndRequest(AdminApiTestHost.NewOperationId(), REASON));
            using HttpResponseMessage endedAgain = await AdminApiTestHost.SendAsync(client, HttpMethod.Post, endRoute,
                operatorToken, new AdminNoticeEndRequest(AdminApiTestHost.NewOperationId(), REASON));
            using HttpResponseMessage missing = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                string.Format(AdminApiRoutes.NOTICE_END_FORMAT, Guid.NewGuid().ToString("D")), operatorToken,
                new AdminNoticeEndRequest(AdminApiTestHost.NewOperationId(), REASON));
            Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
            Assert.Equal(AdminContractCodes.RejectReason.INVALID_STATE,
                (await AdminTestSupport.ReadAsync<OperationResult>(endedAgain)).ReasonCode);
            Assert.Equal(AdminContractCodes.RejectReason.NOT_FOUND,
                (await AdminTestSupport.ReadAsync<OperationResult>(missing)).ReasonCode);

            ServiceStateResponse afterEnd = await GetPublicAsync(client);
            Assert.Empty(afterEnd.Notices);
            Assert.Equal(string.Empty, afterEnd.Notice);
            Assert.Equal(3L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_audit_log WHERE target_kind = 'service_notice';"));
        }

        /// <summary> 확정된 공지를 노출 기간이 끝난 뒤 같은 operationId로 재전송해도 400이 아니라 처음 결과(같은 noticeId)를 받는다 </summary>
        [PostgresFact]
        public async Task Notices_ReplayAfterExpiry_ReturnsFirstResult()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, true);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] secret) = await host.CreateAdminAsync("notice.expiry", EAdminRole.Operator);
            string token = await AdminApiTestHost.LoginAsync(client, "notice.expiry", secret);
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            AdminNoticeCreateRequest shortNotice = new AdminNoticeCreateRequest(AdminApiTestHost.NewOperationId(),
                REASON, "short", string.Empty, ServiceNotice.TARGET_BOTH, ServiceNotice.SEVERITY_INFO,
                now - SHORT_NOTICE_MS, now + SHORT_NOTICE_MS);

            using HttpResponseMessage created = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.NOTICES, token, shortNotice);
            await Task.Delay(TimeSpan.FromMilliseconds(SHORT_NOTICE_MS + EXPIRY_MARGIN_MS));
            using HttpResponseMessage replayed = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.NOTICES, token, shortNotice);

            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            OperationResult first = await AdminTestSupport.ReadAsync<OperationResult>(created);
            OperationResult second = await AdminTestSupport.ReadAsync<OperationResult>(replayed);
            Assert.Equal(first.ReceiptId, second.ReceiptId);
            Assert.Equal(first.ServerTimeUnixMs, second.ServerTimeUnixMs);
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM service_notices;"));
        }

        private static AdminServiceStateUpdateRequest Update(bool? maintenance, string[] features, string[] builds,
            long revision)
        {
            return new AdminServiceStateUpdateRequest(AdminApiTestHost.NewOperationId(), REASON, maintenance, features,
                builds, revision);
        }

        private static async Task<ServiceStateResponse> GetPublicAsync(HttpClient client)
        {
            using HttpResponseMessage response = await client.GetAsync(ApiRoutes.SERVICE_STATE);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await AdminTestSupport.ReadAsync<ServiceStateResponse>(response);
        }

        // 빌드 판정은 코드 소비 전에 하므로 같은 초대를 여러 번 시험해도 사용 횟수가 줄지 않는다
        private static async Task<int> ExchangeRejectionAsync(HttpClient client, string invite, string buildId)
        {
            using HttpResponseMessage response = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE, AdminTestSupport.Json(
                new AuthExchangeRequest(invite, string.Empty, AdminApiTestHost.Build(buildId))));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            return (await AdminTestSupport.ReadAsync<ApiRejection>(response)).ReasonCode;
        }

        private static async Task<string> IssueRawInviteAsync(NpgsqlDataSource dataSource, AuthTokenOptions options)
        {
            Guid accountId = Guid.NewGuid();
            await using (NpgsqlConnection connection = await dataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync("INSERT INTO accounts (account_id) VALUES (@AccountId);",
                    new { AccountId = accountId });
            }

            return (await new PostgresAuthRepository(dataSource, options).IssueInviteAsync(accountId)).Token;
        }

        private static async Task<Guid?> ReadUpdatedByAsync(NpgsqlDataSource dataSource)
        {
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<Guid?>("SELECT updated_by FROM service_state WHERE id = 1;");
        }
    }
}
