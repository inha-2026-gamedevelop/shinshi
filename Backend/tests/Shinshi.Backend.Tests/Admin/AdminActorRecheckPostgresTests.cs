// System
using System;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 인증한 뒤 처리 중에 운영자가 비활성화되거나 역할이 낮아져도 그 쓰기가 확정되지 않는지 실제 PostgreSQL로 검사한다.
    /// 비활성화와 역할 하향은 직접 SQL로 흉내 낸다 </summary>
    public sealed class AdminActorRecheckPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TEST_KIND   = "admin.test.recheck";
        private const string TEST_REASON = "actor recheck test";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 인증 뒤 비활성화된 Owner와 Admin으로 낮아진 Owner의 운영자 생성은 FORBIDDEN_ROLE로 저장되고 아무도 만들지 않는다 </summary>
        [PostgresFact]
        public async Task CreateOperator_ActorDisabledOrLoweredAfterAuthentication_StoresForbiddenWithoutCreating()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            AdminPostgresTests.Fixture fixture = new AdminPostgresTests.Fixture(database.DataSource);
            AdminOperationExecutor executor = fixture.CreateExecutor();
            AdminNewOperator owner = await fixture.BootstrapAsync(AdminTotp.CreateSecret());
            AdminNewOperator lowered = await fixture.InsertAsync("owner.lowered", EAdminRole.Owner);
            AdminIdentity disabledActor = new AdminIdentity(owner.AdminId, EAdminRole.Owner, Guid.NewGuid());
            AdminIdentity loweredActor = new AdminIdentity(lowered.AdminId, EAdminRole.Owner, Guid.NewGuid());
            await ExecuteAsync(database.DataSource,
                "UPDATE admin_users SET disabled_at = clock_timestamp() WHERE admin_id = @AdminId;",
                new { owner.AdminId });
            await LowerToAdminAsync(database.DataSource, lowered.AdminId);
            string operationId = Guid.NewGuid().ToString("D");

            OperationResult byDisabled = await CreateOperatorAsync(fixture, executor, disabledActor, "made.by.disabled",
                operationId);
            OperationResult replay = await CreateOperatorAsync(fixture, executor, disabledActor, "made.by.disabled",
                operationId);
            OperationResult byLowered = await CreateOperatorAsync(fixture, executor, loweredActor, "made.by.lowered",
                Guid.NewGuid().ToString("D"));

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, byDisabled.Status);
            Assert.Equal(AdminContractCodes.RejectReason.FORBIDDEN_ROLE, byDisabled.ReasonCode);
            Assert.Equal(byDisabled.ServerTimeUnixMs, replay.ServerTimeUnixMs);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, byLowered.Status);
            Assert.Equal(AdminContractCodes.RejectReason.FORBIDDEN_ROLE, byLowered.ReasonCode);
            Assert.Equal(2L, await AdminTestSupport.CountAsync(database.DataSource, "SELECT count(*) FROM admin_users;"));
            Assert.Equal(2L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM operations WHERE operation_kind = @Kind AND status = 'Rejected';",
                new { Kind = AdminOperatorsController.CREATE_OPERATION_KIND }));
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_audit_log WHERE action = @Action;",
                new { Action = AdminOperatorsController.CREATE_OPERATION_KIND }));
        }

        /// <summary> 행위자 확인을 지난 쓰기가 진행 중이면 비활성화는 그 쓰기의 커밋을 기다리고, 비활성화 뒤 같은 인증으로 온 쓰기는
        /// 효과 없이 FORBIDDEN_ROLE이다 </summary>
        [PostgresFact]
        public async Task Write_DisabledWhileInFlight_DisableWaitsAndLaterWriteIsForbidden()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            AdminPostgresTests.Fixture fixture = new AdminPostgresTests.Fixture(database.DataSource);
            AdminOperationExecutor executor = fixture.CreateExecutor();
            AdminNewOperator owner = await fixture.BootstrapAsync(AdminTotp.CreateSecret());
            AdminIdentity actor = new AdminIdentity(owner.AdminId, EAdminRole.Owner, Guid.NewGuid());
            TaskCompletionSource entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            Task<OperationResult> inFlight = executor.ExecuteAsync(actor, null, Request(), TEST_KIND, "target",
                async (context, token) =>
                {
                    entered.SetResult();
                    await release.Task;
                    await context.AuditAsync("test_target", "target", null, new { Value = 1 }, token);
                    return OperationDecision.Committed(0, "receipt");
                });
            await entered.Task;

            await using NpgsqlConnection disabler = await database.DataSource.OpenConnectionAsync();
            await using NpgsqlTransaction transaction = await disabler.BeginTransactionAsync();
            await disabler.ExecuteAsync("SET LOCAL lock_timeout = '10s';", transaction: transaction);
            int disablerPid = await disabler.QuerySingleAsync<int>("SELECT pg_backend_pid();", transaction: transaction);
            Task<int> disable = disabler.ExecuteAsync(
                "UPDATE admin_users SET disabled_at = clock_timestamp() WHERE admin_id = @AdminId;",
                new { owner.AdminId }, transaction);
            await AdminTestSupport.WaitUntilWaitingAsync(database.DataSource, disablerPid);
            bool disabledBeforeCommit = disable.IsCompleted;
            release.SetResult();
            OperationResult committed = await inFlight;
            Assert.Equal(1, await disable);
            await transaction.CommitAsync();

            OperationResult afterDisable = await executor.ExecuteAsync(actor, null, Request(), TEST_KIND, "target",
                (context, token) => throw new InvalidOperationException("A disabled actor must not run the effect."));

            Assert.False(disabledBeforeCommit);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, committed.Status);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, afterDisable.Status);
            Assert.Equal(AdminContractCodes.RejectReason.FORBIDDEN_ROLE, afterDisable.ReasonCode);
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_audit_log WHERE action = @Action;", new { Action = TEST_KIND }));
        }

        /// <summary> 운영자 비활성화는 실행기의 행위자 잠금 없이 ID 순서로 잠그므로 역할 하향을 그 잠금 안에서 거절한다 </summary>
        [PostgresFact]
        public async Task Disable_ActorLoweredAfterAuthentication_IsForbidden()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            AdminPostgresTests.Fixture fixture = new AdminPostgresTests.Fixture(database.DataSource);
            AdminOperationExecutor executor = fixture.CreateExecutor();
            AdminNewOperator owner = await fixture.BootstrapAsync(AdminTotp.CreateSecret());
            AdminNewOperator target = await fixture.InsertAsync("owner.target", EAdminRole.Owner);
            await LowerToAdminAsync(database.DataSource, owner.AdminId);

            OperationResult result = await AdminPostgresTests.DisableAsync(fixture.Users, executor, owner.AdminId,
                target.AdminId);

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, result.Status);
            Assert.Equal(AdminContractCodes.RejectReason.FORBIDDEN_ROLE, result.ReasonCode);
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_users WHERE disabled_at IS NOT NULL;"));
        }

        private static Task<OperationResult> CreateOperatorAsync(AdminPostgresTests.Fixture fixture,
            AdminOperationExecutor executor, AdminIdentity actor, string loginName, string operationId)
        {
            AdminNewOperator next = AdminTestSupport.CreateOperator(fixture.Protector, loginName, EAdminRole.Operator,
                AdminTotp.CreateSecret(), actor.AdminId);
            return executor.ExecuteAsync(actor, null,
                new AdminOperatorCreateRequest(operationId, TEST_REASON, loginName, (int)EAdminRole.Operator),
                AdminOperatorsController.CREATE_OPERATION_KIND, string.Empty,
                async (context, token) => await fixture.Users.CreateAsync(context, next, token)
                    ? OperationDecision.Committed(0, next.AdminId.ToString("D"))
                    : OperationDecision.Rejected(AdminContractCodes.RejectReason.LOGIN_NAME_TAKEN));
        }

        private static AdminOperatorDisableRequest Request()
        {
            return new AdminOperatorDisableRequest(Guid.NewGuid().ToString("D"), TEST_REASON);
        }

        private static Task LowerToAdminAsync(NpgsqlDataSource dataSource, Guid adminId)
        {
            return ExecuteAsync(dataSource, "UPDATE admin_users SET role = @Role WHERE admin_id = @AdminId;",
                new { Role = (int)EAdminRole.Admin, AdminId = adminId });
        }

        private static async Task ExecuteAsync(NpgsqlDataSource dataSource, string sql, object parameters)
        {
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(sql, parameters);
        }
    }
}
