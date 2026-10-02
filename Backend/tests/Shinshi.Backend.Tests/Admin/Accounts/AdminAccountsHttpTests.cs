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

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 실제 PostgreSQL과 Program 파이프라인에서 초대 발급, 교환, 검색, 상세 감사, 강제 종료, 초대 폐기를 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class AdminAccountsHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string BUILD          = "accounts-build";
        private const string REASON         = "support ticket 42";
        private const int    MAX_USES       = 2;
        private const int    EXPIRES_HOURS  = 48;
        private const long   CURRENCY       = 1200;
        private const long   ESSENCE        = 35;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task InviteSearchDetailKick_FullFlow_RevokesTokensAndAudits()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, false, options =>
            {
                options.Maintenance     = false;
                options.AllowedBuildIds = new[] { BUILD };
            });
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] viewerSecret) = await host.CreateAdminAsync("acct.viewer", EAdminRole.Viewer);
            (Guid operatorId, byte[] operatorSecret) = await host.CreateAdminAsync("acct.operator", EAdminRole.Operator);
            string viewer = await AdminApiTestHost.LoginAsync(client, "acct.viewer", viewerSecret);
            string operatorToken = await AdminApiTestHost.LoginAsync(client, "acct.operator", operatorSecret);

            // 새 계정 초대: 원문은 확정 응답에만, 재전송은 같은 inviteId에 원문 null
            AdminInviteIssueRequest issue = new AdminInviteIssueRequest(AdminApiTestHost.NewOperationId(), REASON, null,
                MAX_USES, EXPIRES_HOURS, null);
            using HttpResponseMessage byViewer = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.INVITES, viewer, issue);
            using HttpResponseMessage issued = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.INVITES, operatorToken, issue);
            using HttpResponseMessage replayed = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.INVITES, operatorToken, issue);
            using HttpResponseMessage badUses = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.INVITES, operatorToken, new AdminInviteIssueRequest(AdminApiTestHost.NewOperationId(),
                    REASON, null, 11, EXPIRES_HOURS, null));
            using HttpResponseMessage missingAccount = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.INVITES, operatorToken, new AdminInviteIssueRequest(AdminApiTestHost.NewOperationId(),
                    REASON, Guid.NewGuid().ToString("D"), 1, 1, null));
            Assert.Equal(HttpStatusCode.Forbidden, byViewer.StatusCode);
            Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
            Assert.Equal("no-store", issued.Headers.CacheControl!.ToString());
            Assert.Equal(HttpStatusCode.BadRequest, badUses.StatusCode);
            Assert.Equal(AdminContractCodes.RejectReason.NOT_FOUND,
                (await AdminTestSupport.ReadAsync<OperationResult>(missingAccount)).ReasonCode);
            AdminInviteIssueResponse first = await AdminTestSupport.ReadAsync<AdminInviteIssueResponse>(issued);
            AdminInviteIssueResponse second = await AdminTestSupport.ReadAsync<AdminInviteIssueResponse>(replayed);
            Assert.NotNull(first.InviteCode);
            Assert.Null(second.InviteCode);
            Assert.Equal(first.InviteId, second.InviteId);
            Assert.Equal(first.AccountId, second.AccountId);
            Assert.Equal(MAX_USES, second.MaxUses);
            Assert.InRange(first.ExpiresAtUnixMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                (long)TimeSpan.FromHours(EXPIRES_HOURS - 1).TotalMilliseconds,
                (long)TimeSpan.FromHours(EXPIRES_HOURS).TotalMilliseconds);
            Guid accountId = Guid.Parse(first.AccountId);

            AuthExchangeResponse login = await ExchangeAsync(client, first.InviteCode);
            Assert.Equal(first.AccountId, login.AccountId);
            Guid characterId = await SeedAccountAsync(database.DataSource, accountId);

            // 검색 세 방식: 계정 ID, 캐릭터 ID, 초대 원문. 형식이 아닌 문자열은 빈 목록
            AdminAccountSummary byAccount = Assert.Single(await SearchAsync(client, viewer, first.AccountId));
            AdminAccountSummary byCharacter = Assert.Single(await SearchAsync(client, viewer, characterId.ToString("D")));
            AdminAccountSummary byInvite = Assert.Single(await SearchAsync(client, viewer, first.InviteCode));
            Assert.Equal(AdminAccountQuery.MATCH_ACCOUNT_ID, byAccount.MatchedBy);
            Assert.Equal(1, byAccount.CharacterCount);
            Assert.False(byAccount.Online);
            Assert.Equal(first.AccountId, byCharacter.AccountId);
            Assert.Equal(AdminAccountQuery.MATCH_CHARACTER_ID, byCharacter.MatchedBy);
            Assert.Equal(AdminAccountQuery.MATCH_INVITE_CODE, byInvite.MatchedBy);
            Assert.Equal(first.InviteId, byInvite.MatchedId);
            Assert.Empty(await SearchAsync(client, viewer, "not-an-invite"));
            Assert.Empty(await SearchAsync(client, viewer, Guid.NewGuid().ToString("D")));

            // GET 조회 문자열은 요청 로그에 남으므로 UUID만 받는다. 초대 원문과 빈 값은 400이다
            using HttpResponseMessage getById = await GetSearchAsync(client, viewer, characterId.ToString("D"));
            using HttpResponseMessage getByInvite = await GetSearchAsync(client, viewer, first.InviteCode);
            using HttpResponseMessage getEmpty = await GetSearchAsync(client, viewer, string.Empty);
            using HttpResponseMessage postEmpty = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.ACCOUNTS_SEARCH, viewer, new AdminAccountSearchRequest("  "));
            Assert.Equal(HttpStatusCode.OK, getById.StatusCode);
            Assert.Equal(first.AccountId,
                Assert.Single((await AdminTestSupport.ReadAsync<AdminAccountSearchResponse>(getById)).Accounts).AccountId);
            Assert.Equal(HttpStatusCode.BadRequest, getByInvite.StatusCode);
            Assert.Equal(AdminContractCodes.RejectReason.INVALID_REQUEST,
                (await AdminTestSupport.ReadAsync<ApiRejection>(getByInvite)).ReasonCode);
            Assert.Equal(HttpStatusCode.BadRequest, getEmpty.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, postEmpty.StatusCode);

            // 상세는 사유가 필수이고 데이터를 주기 전에 감사 행을 남긴다
            string detailRoute = string.Format(AdminApiRoutes.ACCOUNT_FORMAT, first.AccountId);
            using HttpResponseMessage noReason = await AdminApiTestHost.SendAsync(client, HttpMethod.Get, detailRoute,
                viewer);
            using HttpResponseMessage unknown = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                string.Format(AdminApiRoutes.ACCOUNT_FORMAT, Guid.NewGuid().ToString("D")) + ReasonQuery(), viewer);
            using HttpResponseMessage detailed = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                detailRoute + ReasonQuery(), viewer);
            Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.OK, detailed.StatusCode);
            AdminAccountDetailResponse detail = await AdminTestSupport.ReadAsync<AdminAccountDetailResponse>(detailed);
            Assert.Equal(CURRENCY, detail.Wallet.Currency);
            Assert.Equal(ESSENCE, Assert.Single(detail.Characters).Essence);
            Assert.Equal(1, detail.ActiveTokenFamilies);
            Assert.Null(detail.Lease);
            Assert.Empty(detail.RecentSessions);
            AdminInviteView inviteView = Assert.Single(detail.Invites);
            Assert.Equal(1, inviteView.UsedCount);
            Assert.Null(inviteView.RevokedAtUnixMs);
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                """
                SELECT count(*) FROM admin_audit_log
                WHERE action = @Action AND target_id = @TargetId AND reason = @Reason AND operation_id IS NULL
                    AND request_ip_hash IS NOT NULL;
                """, new { Action = AdminAccountAuditActions.ACCOUNT_VIEW, TargetId = first.AccountId, Reason = REASON }));

            // 강제 종료 뒤 기존 접근 토큰과 갱신 토큰은 모두 거절된다
            string playerRoute = string.Format(ApiRoutes.REJOIN_TICKET_FORMAT, Guid.NewGuid().ToString("D"));
            using HttpResponseMessage beforeKick = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                playerRoute, login.AccessToken);
            using HttpResponseMessage gameOnAdmin = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                AdminApiRoutes.SERVICE_STATE, login.AccessToken);
            Assert.NotEqual(HttpStatusCode.Unauthorized, beforeKick.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, gameOnAdmin.StatusCode);

            string kickRoute = string.Format(AdminApiRoutes.ACCOUNT_KICK_FORMAT, first.AccountId);
            AdminAccountKickRequest kick = new AdminAccountKickRequest(AdminApiTestHost.NewOperationId(), REASON, false);
            using HttpResponseMessage kickByViewer = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                kickRoute, viewer, kick);
            using HttpResponseMessage kicked = await AdminApiTestHost.SendAsync(client, HttpMethod.Post, kickRoute,
                operatorToken, kick);
            using HttpResponseMessage kickMissing = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                string.Format(AdminApiRoutes.ACCOUNT_KICK_FORMAT, Guid.NewGuid().ToString("D")), operatorToken,
                new AdminAccountKickRequest(AdminApiTestHost.NewOperationId(), REASON));
            Assert.Equal(HttpStatusCode.Forbidden, kickByViewer.StatusCode);
            Assert.Equal(HttpStatusCode.OK, kicked.StatusCode);
            AdminAccountKickResponse kickResponse = await AdminTestSupport.ReadAsync<AdminAccountKickResponse>(kicked);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, kickResponse.Operation.Status);
            Assert.Equal(first.AccountId, kickResponse.AccountId);
            Assert.False(kickResponse.InvitesRevoked);
            Assert.True(kickResponse.CurrentSessionKept);
            Assert.Equal(AdminContractCodes.RejectReason.NOT_FOUND,
                (await AdminTestSupport.ReadAsync<OperationResult>(kickMissing)).ReasonCode);

            using HttpResponseMessage afterKick = await AdminApiTestHost.SendAsync(client, HttpMethod.Post, playerRoute,
                login.AccessToken);
            using HttpResponseMessage refresh = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE, AdminTestSupport.Json(
                new AuthExchangeRequest(string.Empty, login.RefreshToken, AdminApiTestHost.Build(BUILD))));
            Assert.Equal(HttpStatusCode.Unauthorized, afterKick.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await AdminTestSupport.ReadAsync<ApiRejection>(refresh)).ReasonCode);
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                """
                SELECT count(*) FROM admin_audit_log
                WHERE action = @Action AND admin_id = @AdminId AND target_id = @TargetId
                    AND (after_state -> 'revoked' ->> 'families')::integer = 1
                    AND (after_state -> 'revoked' ->> 'accessTokens')::integer = 1;
                """, new { Action = AdminAccountAuditActions.ACCOUNT_KICK, AdminId = operatorId,
                    TargetId = first.AccountId }));

            // revokeInvites=false로 강제 종료했으므로 같은 초대의 남은 1회는 계속 쓸 수 있다. 폐기 뒤에는 기존 거절 코드다
            AuthExchangeResponse relogin = await ExchangeAsync(client, first.InviteCode);
            Assert.Equal(first.AccountId, relogin.AccountId);
            string revokeRoute = string.Format(AdminApiRoutes.INVITE_REVOKE_FORMAT, first.InviteId);
            using HttpResponseMessage revoked = await AdminApiTestHost.SendAsync(client, HttpMethod.Post, revokeRoute,
                operatorToken, new AdminInviteRevokeRequest(AdminApiTestHost.NewOperationId(), REASON));
            using HttpResponseMessage revokedAgain = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                revokeRoute, operatorToken, new AdminInviteRevokeRequest(AdminApiTestHost.NewOperationId(), REASON));
            Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
            Assert.Equal(AdminContractCodes.RejectReason.INVALID_STATE,
                (await AdminTestSupport.ReadAsync<OperationResult>(revokedAgain)).ReasonCode);

            // 기기 변경 재발급: 기존 계정에 새 초대를 주면 기존 로그인과 이전 초대를 함께 폐기한다
            using HttpResponseMessage reissued = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.INVITES, operatorToken, new AdminInviteIssueRequest(AdminApiTestHost.NewOperationId(),
                    REASON, first.AccountId, 1, 1, true));
            AdminInviteIssueResponse device = await AdminTestSupport.ReadAsync<AdminInviteIssueResponse>(reissued);
            Assert.Equal(first.AccountId, device.AccountId);
            using HttpResponseMessage oldSession = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                playerRoute, relogin.AccessToken);
            Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
            Assert.Equal(first.AccountId, (await ExchangeAsync(client, device.InviteCode)).AccountId);
            using HttpResponseMessage spent = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE, AdminTestSupport.Json(
                new AuthExchangeRequest(device.InviteCode, string.Empty, AdminApiTestHost.Build(BUILD))));
            using HttpResponseMessage revokedCode = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE, AdminTestSupport.Json(
                new AuthExchangeRequest(first.InviteCode, string.Empty, AdminApiTestHost.Build(BUILD))));
            Assert.Equal(ContractCodes.RejectReason.INVITE_CODE_USED,
                (await AdminTestSupport.ReadAsync<ApiRejection>(spent)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.INVALID_INVITE_CODE,
                (await AdminTestSupport.ReadAsync<ApiRejection>(revokedCode)).ReasonCode);

            // 초대 원문은 감사 로그 어디에도 남지 않는다
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                """
                SELECT count(*) FROM admin_audit_log
                WHERE COALESCE(before_state::text, '') LIKE @First OR COALESCE(after_state::text, '') LIKE @First
                    OR COALESCE(after_state::text, '') LIKE @Device OR reason LIKE @First;
                """, new { First = "%" + first.InviteCode + "%", Device = "%" + device.InviteCode + "%" }));
            Assert.Equal(3L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_audit_log WHERE action IN ('admin.invite.issue', 'admin.invite.revoke');"));
        }

        /// <summary> revokeInvites를 생략한 강제 종료는 미폐기 초대를 함께 폐기해 남은 사용 횟수로 다시 로그인하지 못한다.
        /// 응답은 현재 접속 유지를 알리고, 계정 접속 임대는 그대로 남는다 </summary>
        [PostgresFact]
        public async Task Kick_DefaultRevokesInvites_RemainingUseIsRejected()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, false, options =>
            {
                options.Maintenance     = false;
                options.AllowedBuildIds = new[] { BUILD };
            });
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] operatorSecret) = await host.CreateAdminAsync("kick.operator", EAdminRole.Operator);
            string operatorToken = await AdminApiTestHost.LoginAsync(client, "kick.operator", operatorSecret);
            using HttpResponseMessage issued = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.INVITES, operatorToken, new AdminInviteIssueRequest(AdminApiTestHost.NewOperationId(),
                    REASON, null, MAX_USES, EXPIRES_HOURS, null));
            AdminInviteIssueResponse invite = await AdminTestSupport.ReadAsync<AdminInviteIssueResponse>(issued);
            AuthExchangeResponse login = await ExchangeAsync(client, invite.InviteCode);

            using HttpResponseMessage kicked = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                string.Format(AdminApiRoutes.ACCOUNT_KICK_FORMAT, login.AccountId), operatorToken,
                new AdminAccountKickRequest(AdminApiTestHost.NewOperationId(), REASON));
            using HttpResponseMessage reused = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE, AdminTestSupport.Json(
                new AuthExchangeRequest(invite.InviteCode, string.Empty, AdminApiTestHost.Build(BUILD))));

            Assert.Equal(HttpStatusCode.OK, kicked.StatusCode);
            AdminAccountKickResponse response = await AdminTestSupport.ReadAsync<AdminAccountKickResponse>(kicked);
            Assert.True(response.InvitesRevoked);
            Assert.True(response.CurrentSessionKept);
            Assert.NotEqual(HttpStatusCode.OK, reused.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.INVALID_INVITE_CODE,
                (await AdminTestSupport.ReadAsync<ApiRejection>(reused)).ReasonCode);
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM auth_invites WHERE invite_id = @Id AND revoked_at IS NOT NULL AND used_count = 1;",
                new { Id = Guid.Parse(invite.InviteId) }));
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                """
                SELECT count(*) FROM admin_audit_log
                WHERE action = @Action AND (after_state -> 'revoked' ->> 'invites')::integer = 1
                    AND (after_state ->> 'revokeInvites')::boolean AND (after_state ->> 'currentSessionKept')::boolean;
                """, new { Action = AdminAccountAuditActions.ACCOUNT_KICK }));
        }

        private static string ReasonQuery()
        {
            return "?" + AdminAccountQuery.REASON_PARAMETER + "=" + Uri.EscapeDataString(REASON);
        }

        private static async Task<AdminAccountSummary[]> SearchAsync(HttpClient client, string token, string query)
        {
            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.ACCOUNTS_SEARCH, token, new AdminAccountSearchRequest(query));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await AdminTestSupport.ReadAsync<AdminAccountSearchResponse>(response)).Accounts;
        }

        private static Task<HttpResponseMessage> GetSearchAsync(HttpClient client, string token, string query)
        {
            return AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                AdminApiRoutes.ACCOUNTS + "?" + AdminApiRoutes.QUERY_PARAMETER + "=" + Uri.EscapeDataString(query), token);
        }

        private static async Task<AuthExchangeResponse> ExchangeAsync(HttpClient client, string inviteCode)
        {
            using HttpResponseMessage response = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE, AdminTestSupport.Json(
                new AuthExchangeRequest(inviteCode, string.Empty, AdminApiTestHost.Build(BUILD))));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await AdminTestSupport.ReadAsync<AuthExchangeResponse>(response);
        }

        private static async Task<Guid> SeedAccountAsync(NpgsqlDataSource dataSource, Guid accountId)
        {
            Guid characterId = Guid.NewGuid();
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO account_wallets (account_id, currency) VALUES (@AccountId, @Currency);
                INSERT INTO characters (character_id, account_id, class_id, essence) VALUES (@CharacterId, @AccountId, 0, @Essence);
                """, new { AccountId = accountId, CharacterId = characterId, Currency = CURRENCY, Essence = ESSENCE });
            return characterId;
        }
    }
}
