// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Admin;
using Shinshi.Data.Ledger;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 로그 조회와 수동 복구 시험이 함께 쓰는 원장 작성, 운영자 준비 도구 </summary>
    internal static class AdminLogTestSupport
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string OPERATION_KIND = "test.admin-logs";
        public const string SYSTEM_ACTOR   = "System";
        public const string OWNER_NAME     = "owner.logs";

        private const string REQUEST_JSON = "{\"test\":\"admin-logs\"}";
        private const long   NO_REVISION  = 0;

        private static readonly PostgresEconomyLedgerWriter G_LedgerWriter = new PostgresEconomyLedgerWriter();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 원장 한 줄만 쓰는 작업 하나를 확정하고 그 operationId를 돌려준다 </summary>
        public static Task<Guid> WriteLedgerAsync(IdempotentOperationExecutor executor, Guid actorId, EconomyAsset asset,
            long delta, long balanceAfter, int reason, string sourceType, string sourceId)
        {
            return RunAsync(executor, actorId, async (operationId, context, token) =>
            {
                EconomyLedgerEntry entry = new EconomyLedgerEntry(Guid.NewGuid(), asset.Kind, asset, delta, balanceAfter,
                    reason, sourceType, sourceId, SYSTEM_ACTOR, actorId, operationId);
                Assert.True(await G_LedgerWriter.AppendAsync(context, entry, token));
            });
        }

        /// <summary> 효과 콜백을 시험용 작업 하나로 확정한다. 효과는 작업 트랜잭션만 쓴다 </summary>
        public static async Task<Guid> RunAsync(IdempotentOperationExecutor executor, Guid actorId,
            Func<Guid, OperationExecutionContext, CancellationToken, Task> effect)
        {
            OperationRequest request = new OperationRequest(actorId, Guid.NewGuid(), OPERATION_KIND, REQUEST_JSON);
            OperationResult result = await executor.ExecuteAsync(request, async (context, token) =>
            {
                await effect(request.OperationId, context, token);
                return OperationDecision.Committed(NO_REVISION, request.OperationId.ToString("D"));
            });
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
            return request.OperationId;
        }

        /// <summary> 빈 운영자 표에 Owner를 부트스트랩하고 그 운영자로 확인된 신원을 돌려준다 </summary>
        public static async Task<AdminIdentity> BootstrapOwnerAsync(Npgsql.NpgsqlDataSource dataSource)
        {
            PostgresAdminUserRepository users = new PostgresAdminUserRepository(dataSource,
                new PostgresAdminAuditWriter(dataSource));
            AdminNewOperator owner = AdminTestSupport.CreateOperator(
                AdminTestSupport.CreateOptions().CreateSecretProtector(), OWNER_NAME, EAdminRole.Owner,
                AdminTotp.CreateSecret(), null);
            Assert.True(await users.BootstrapOwnerAsync(owner));
            return new AdminIdentity(owner.AdminId, EAdminRole.Owner, Guid.NewGuid());
        }
    }
}
