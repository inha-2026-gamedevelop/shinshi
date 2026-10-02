// System
using System;
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

namespace Shinshi.Backend.Tests.Admin.Grants
{
    /// <summary> 대상 2명 이상의 일괄 지급 실행은 Admin 이상만, 1명 지급은 Operator도 실행하는지 실제 PostgreSQL과 Program 파이프라인에서 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class AdminGrantRoleHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string OPERATOR_NAME = "grant.operator";
        private const string ADMIN_NAME    = "grant.admin";
        private const long   CURRENCY      = 30;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Execute_BatchGrantByOperator_Returns403UntilAdminExecutes()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, false);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] operatorSecret) = await host.CreateAdminAsync(OPERATOR_NAME, EAdminRole.Operator);
            (Guid _, byte[] adminSecret) = await host.CreateAdminAsync(ADMIN_NAME, EAdminRole.Admin);
            string operatorToken = await AdminApiTestHost.LoginAsync(client, OPERATOR_NAME, operatorSecret);
            string adminToken = await AdminApiTestHost.LoginAsync(client, ADMIN_NAME, adminSecret);
            Guid first = await CreateAccountAsync(database.DataSource);
            Guid second = await CreateAccountAsync(database.DataSource);
            string single = await CreateGrantAsync(client, operatorToken, first);
            string batch = await CreateGrantAsync(client, operatorToken, first, second);

            AdminGrantActionRequest batchByOperator = new AdminGrantActionRequest(AdminApiTestHost.NewOperationId(),
                GrantTestSupport.REASON);
            using HttpResponseMessage refused = await ExecuteAsync(client, operatorToken, batch, batchByOperator);
            using HttpResponseMessage singleByOperator = await ExecuteAsync(client, operatorToken, single,
                new AdminGrantActionRequest(AdminApiTestHost.NewOperationId(), GrantTestSupport.REASON));

            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(AdminContractCodes.RejectReason.FORBIDDEN_ROLE,
                (await AdminTestSupport.ReadAsync<ApiRejection>(refused)).ReasonCode);
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM reward_inbox WHERE grant_id = @GrantId;", new { GrantId = Guid.Parse(batch) }));
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM operations WHERE operation_id = @OperationId;",
                new { OperationId = Guid.Parse(batchByOperator.OperationId) }));
            Assert.Equal(HttpStatusCode.OK, singleByOperator.StatusCode);

            using HttpResponseMessage executed = await ExecuteAsync(client, adminToken, batch,
                new AdminGrantActionRequest(AdminApiTestHost.NewOperationId(), GrantTestSupport.REASON));
            Assert.Equal(HttpStatusCode.OK, executed.StatusCode);
            AdminGrantActionResponse detail = await AdminTestSupport.ReadAsync<AdminGrantActionResponse>(executed);
            Assert.Equal(AdminGrantCodes.STATE_EXECUTED, detail.Grant.State);
            Assert.Equal(2, detail.Grant.OpenCount);
        }

        /// <summary> 대상이 1명이어도 복구 보정 지급의 실행은 Admin 이상이다. Operator는 작업 행 없이 403이다 </summary>
        [PostgresFact]
        public async Task Execute_RecoveryCompensationByOperator_Returns403UntilAdminExecutes()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, false);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] operatorSecret) = await host.CreateAdminAsync(OPERATOR_NAME, EAdminRole.Operator);
            (Guid _, byte[] adminSecret) = await host.CreateAdminAsync(ADMIN_NAME, EAdminRole.Admin);
            string operatorToken = await AdminApiTestHost.LoginAsync(client, OPERATOR_NAME, operatorSecret);
            string adminToken = await AdminApiTestHost.LoginAsync(client, ADMIN_NAME, adminSecret);
            Guid account = await CreateAccountAsync(database.DataSource);
            using HttpResponseMessage created = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.GRANTS, operatorToken, new AdminGrantCreateRequest(AdminApiTestHost.NewOperationId(),
                    GrantTestSupport.REASON, new[] { account.ToString("D") },
                    new[] { GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY) }, null,
                    AdminGrantCodes.SOURCE_RECOVERY_COMPENSATION, Guid.NewGuid().ToString("D")));
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            string grantId = (await AdminTestSupport.ReadAsync<AdminGrantCreateResponse>(created)).GrantId;

            AdminGrantActionRequest byOperator = new AdminGrantActionRequest(AdminApiTestHost.NewOperationId(),
                GrantTestSupport.REASON);
            using HttpResponseMessage refused = await ExecuteAsync(client, operatorToken, grantId, byOperator);
            using HttpResponseMessage executed = await ExecuteAsync(client, adminToken, grantId,
                new AdminGrantActionRequest(AdminApiTestHost.NewOperationId(), GrantTestSupport.REASON));

            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(AdminContractCodes.RejectReason.FORBIDDEN_ROLE,
                (await AdminTestSupport.ReadAsync<ApiRejection>(refused)).ReasonCode);
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM operations WHERE operation_id = @OperationId;",
                new { OperationId = Guid.Parse(byOperator.OperationId) }));
            Assert.Equal(HttpStatusCode.OK, executed.StatusCode);
            Assert.Equal(1, (await AdminTestSupport.ReadAsync<AdminGrantActionResponse>(executed)).Grant.OpenCount);
        }

        private static async Task<string> CreateGrantAsync(HttpClient client, string token, params Guid[] accounts)
        {
            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.GRANTS, token, GrantTestSupport.CreateRequest(AdminApiTestHost.NewOperationId(), accounts,
                    GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY)));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await AdminTestSupport.ReadAsync<AdminGrantCreateResponse>(response)).GrantId;
        }

        private static Task<HttpResponseMessage> ExecuteAsync(HttpClient client, string token, string grantId,
            AdminGrantActionRequest request)
        {
            return AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                string.Format(AdminApiRoutes.GRANT_EXECUTE_FORMAT, grantId), token, request);
        }

        private static async Task<Guid> CreateAccountAsync(NpgsqlDataSource dataSource)
        {
            Guid accountId = Guid.NewGuid();
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("INSERT INTO accounts (account_id) VALUES (@AccountId);",
                new { AccountId = accountId });
            return accountId;
        }
    }
}
