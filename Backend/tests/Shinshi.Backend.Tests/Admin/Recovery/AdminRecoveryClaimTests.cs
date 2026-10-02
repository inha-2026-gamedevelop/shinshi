// System
using System;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data;
using Shinshi.Data.Admin;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 수동 복구의 운영자 작업 키 결속. 같은 operationId로 다른 세션을 복구하지 못하고(동시, 어드민 단계 실패 뒤),
    /// 인증 뒤 비활성화된 운영자는 서버 복구를 일으키지 못하는지 실제 PostgreSQL로 검사한다 </summary>
    public sealed class AdminRecoveryClaimTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long TICKETS                = 3;
        private const long POTIONS                = 2;
        private const int  DUNGEONS               = 2;
        private const int  IN_FLIGHT_WAIT_SECONDS = 1;
        private const string OTHER_OPERATION = "admin.test.other_write";

        private const string CANCELLED_COUNT_SQL =
            """
            SELECT count(*) FROM game_sessions WHERE session_id IN (@First, @Second) AND result = @Cancelled;
            """;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 같은 운영자, 같은 operationId로 서로 다른 세션의 장애 취소를 동시에 보내면 한 세션만 취소되고,
        /// 다른 요청은 서버 복구와 의도 감사 없이 IDEMPOTENCY_CONFLICT다 </summary>
        [PostgresFact]
        public async Task ServiceCancel_SameOperationIdOnTwoSessionsConcurrently_CancelsOnlyOne()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(DUNGEONS);
            RecoveryTestHarness harness = await RecoveryTestHarness.CreateAsync(environment);
            ReservedSessionView first = await StartLostAsync(environment);
            ReservedSessionView second = await StartLostAsync(environment);
            Guid operationId = Guid.NewGuid();

            AdminSessionRecoveryOutcome[] outcomes = await Task.WhenAll(
                harness.RecoverAsync(first.SessionId, operationId, AdminSessionRecoveryCodes.SERVICE_CANCEL),
                harness.RecoverAsync(second.SessionId, operationId, AdminSessionRecoveryCodes.SERVICE_CANCEL));

            Assert.Single(outcomes, outcome => outcome.Result.Status == ContractCodes.OperationStatus.COMMITTED);
            AdminSessionRecoveryOutcome conflict = Assert.Single(outcomes,
                outcome => outcome.Result.Status == ContractCodes.OperationStatus.REJECTED);
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, conflict.Result.ReasonCode);
            Assert.Equal(1L, await CountCancelledAsync(environment, first, second));
            Assert.Equal(1L, await CountIntentsAsync(environment, operationId));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM admin_operation_claims WHERE operation_id = @OperationId;",
                new { OperationId = operationId }));
        }

        /// <summary> 서버 복구는 확정됐는데 어드민 단계가 실패한 operationId로 다른 세션을 복구하면 그 세션은 그대로이고
        /// IDEMPOTENCY_CONFLICT다. 처음 세션으로의 재전송은 이미 적용된 복구로 감사하고 확정하며, 그 뒤 재전송은 처음 결과다 </summary>
        [PostgresFact]
        public async Task ServiceCancel_AfterAdminStepFailure_KeepsOperationIdBoundToFirstSession()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(DUNGEONS);
            RecoveryTestHarness harness = await RecoveryTestHarness.CreateAsync(environment);
            ReservedSessionView first = await StartLostAsync(environment);
            ReservedSessionView second = await StartLostAsync(environment);
            Guid operationId = Guid.NewGuid();
            RecoveryTestHarness failing = harness.WithOperationAuditWriter(environment, new FailingOperationAuditWriter());

            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.RecoverAsync(first.SessionId, operationId,
                AdminSessionRecoveryCodes.SERVICE_CANCEL));
            AdminSessionRecoveryOutcome other = await harness.RecoverAsync(second.SessionId, operationId,
                AdminSessionRecoveryCodes.SERVICE_CANCEL);
            string secondState = await environment.TextAsync("SELECT state FROM game_sessions WHERE session_id = @Id;",
                new { Id = second.SessionId });
            AdminSessionRecoveryOutcome retried = await harness.RecoverAsync(first.SessionId, operationId,
                AdminSessionRecoveryCodes.SERVICE_CANCEL);
            AdminSessionRecoveryOutcome replay = await harness.RecoverAsync(first.SessionId, operationId,
                AdminSessionRecoveryCodes.SERVICE_CANCEL);

            AdminSessionRecoveryPostgresTests.AssertRejected(other, ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT);
            Assert.Equal(GameSessionStates.RUNNING, secondState);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, retried.Result.Status);
            Assert.Equal(retried.Result.ServerTimeUnixMs, replay.Result.ServerTimeUnixMs);
            Assert.Equal(1L, await CountCancelledAsync(environment, first, second));
            Assert.Equal(1L, await environment.ScalarAsync(
                """
                SELECT count(*) FROM admin_audit_log
                WHERE operation_id = @OperationId AND action = @Action AND target_id = @Target
                    AND after_state->>'alreadyApplied' = 'true';
                """, new
                {
                    OperationId = operationId, Action = AdminRecoveryActions.SERVICE_CANCEL,
                    Target = first.SessionId.ToString("D")
                }));
        }

        /// <summary> 인증 뒤 비활성화된 운영자의 장애 취소는 서버 복구와 의도 감사 없이 FORBIDDEN_ROLE로 저장된다 </summary>
        [PostgresFact]
        public async Task ServiceCancel_ActorDisabledAfterAuthentication_DoesNotRecover()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            RecoveryTestHarness harness = await RecoveryTestHarness.CreateAsync(environment);
            ReservedSessionView session = await StartLostAsync(environment);
            Guid operationId = Guid.NewGuid();
            await environment.ExecuteAsync(
                "UPDATE admin_users SET disabled_at = clock_timestamp() WHERE admin_id = @AdminId;",
                new { harness.Admin.AdminId });

            AdminSessionRecoveryOutcome outcome = await harness.RecoverAsync(session.SessionId, operationId,
                AdminSessionRecoveryCodes.SERVICE_CANCEL);

            AdminSessionRecoveryPostgresTests.AssertRejected(outcome, AdminContractCodes.RejectReason.FORBIDDEN_ROLE);
            Assert.Equal(GameSessionStates.RUNNING, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
            Assert.Equal(0L, await CountIntentsAsync(environment, operationId));
        }

        /// <summary> 일반 쓰기가 아직 효과 트랜잭션 안에 있어도 다른 본문의 같은 키로 온 수동 복구는 서버를 취소하지 않는다 </summary>
        [PostgresFact]
        public async Task ServiceCancel_WhileOtherWriteIsInFlight_RejectsWithoutRecovering()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            RecoveryTestHarness harness = await RecoveryTestHarness.CreateAsync(environment);
            ReservedSessionView session = await StartLostAsync(environment);
            Guid operationId = Guid.NewGuid();
            AdminOperationExecutor operations = CreateOperations(environment);
            TaskCompletionSource entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<OperationResult> write = operations.ExecuteAsync(harness.Admin, null,
                new AdminOperatorDisableRequest(operationId.ToString("D"), RecoveryTestHarness.REASON),
                OTHER_OPERATION, "other_target", async (context, token) =>
                {
                    entered.SetResult();
                    await release.Task;
                    await context.AuditAsync("test_target", "other_target", null, new { Value = 1 }, token);
                    return OperationDecision.Committed(0, "other_receipt");
                });
            await entered.Task;
            Task<AdminSessionRecoveryOutcome> recovering = harness.RecoverAsync(session.SessionId, operationId,
                AdminSessionRecoveryCodes.SERVICE_CANCEL);
            try
            {
                // 올바른 거절은 진행 중인 일반 효과의 완료를 기다릴 필요가 없다. 결함 경로는 서버 취소 후 operations에서 기다린다
                await Task.WhenAny(recovering, Task.Delay(TimeSpan.FromSeconds(IN_FLIGHT_WAIT_SECONDS)));
            }
            finally
            {
                release.SetResult();
            }

            OperationResult written = await write;
            AdminSessionRecoveryOutcome recovery = await recovering;
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, written.Status);
            AdminSessionRecoveryPostgresTests.AssertRejected(recovery, ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT);
            Assert.Equal(GameSessionStates.RUNNING, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
            Assert.Equal(0L, await CountIntentsAsync(environment, operationId));
        }

        /// <summary> 수동 복구가 먼저 결속한 키에 다른 쓰기가 오면 거절 결과로 operations를 점유하지 않고,
        /// 원래 복구와 재전송이 정상 확정된다 </summary>
        [PostgresFact]
        public async Task ServiceCancel_ClaimedBeforeOtherWrite_DoesNotLetConflictOccupyResult()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            RecoveryTestHarness harness = await RecoveryTestHarness.CreateAsync(environment);
            ReservedSessionView session = await StartLostAsync(environment);
            Guid operationId = Guid.NewGuid();
            AdminOperationExecutor operations = CreateOperations(environment);
            AdminSessionRecoveryRequest request = new AdminSessionRecoveryRequest(operationId.ToString("D"),
                RecoveryTestHarness.REASON, AdminSessionRecoveryCodes.SERVICE_CANCEL);
            string target = session.SessionId.ToString("D");
            OperationRequest operation = operations.CreateRequest(harness.Admin, request,
                AdminRecoveryActions.SERVICE_CANCEL, target);
            await using (AdminOperationClaim claim = await harness.Recovery.ClaimAsync(harness.Admin, operation, target))
            {
                Assert.True(claim.Matches);
                OperationResult conflict = await operations.ExecuteAsync(harness.Admin, null,
                    new AdminOperatorDisableRequest(operationId.ToString("D"), RecoveryTestHarness.REASON),
                    OTHER_OPERATION, "other_target", (context, token) =>
                        throw new InvalidOperationException("A conflicting write must not run its effect."));
                Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, conflict.ReasonCode);
                Assert.Equal(0L, await environment.ScalarAsync(
                    "SELECT count(*) FROM operations WHERE actor_id = @AdminId AND operation_id = @OperationId;",
                    new { harness.Admin.AdminId, OperationId = operationId }));
            }

            AdminSessionRecoveryOutcome recovered = await harness.RecoverAsync(session.SessionId, operationId,
                AdminSessionRecoveryCodes.SERVICE_CANCEL);
            AdminSessionRecoveryOutcome replay = await harness.RecoverAsync(session.SessionId, operationId,
                AdminSessionRecoveryCodes.SERVICE_CANCEL);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, recovered.Result.Status);
            Assert.Equal(recovered.Result.ServerTimeUnixMs, replay.Result.ServerTimeUnixMs);
            Assert.Equal(1L, await CountIntentsAsync(environment, operationId));
        }

        /// <summary> 결속 도입 전에 다른 본문으로 저장된 작업도 수동 복구보다 먼저 검사하므로 서버 취소를 일으키지 않는다 </summary>
        [PostgresFact]
        public async Task ServiceCancel_PreexistingOperationWithoutClaim_RejectsWithoutRecovering()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            RecoveryTestHarness harness = await RecoveryTestHarness.CreateAsync(environment);
            ReservedSessionView session = await StartLostAsync(environment);
            Guid operationId = Guid.NewGuid();
            await environment.Executor.ExecuteAsync(new OperationRequest(harness.Admin.AdminId, operationId,
                OTHER_OPERATION, "{}"), (context, token) => Task.FromResult(OperationDecision.Committed(0, "old_receipt")));

            AdminSessionRecoveryOutcome outcome = await harness.RecoverAsync(session.SessionId, operationId,
                AdminSessionRecoveryCodes.SERVICE_CANCEL);

            AdminSessionRecoveryPostgresTests.AssertRejected(outcome, ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT);
            Assert.Equal(GameSessionStates.RUNNING, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
            Assert.Equal(0L, await CountIntentsAsync(environment, operationId));
        }

        private static AdminOperationExecutor CreateOperations(SessionTestEnvironment environment)
        {
            return new AdminOperationExecutor(environment.Executor, new PostgresAdminAuditWriter(environment.DataSource),
                new PostgresAdminOperationClaimRepository(environment.DataSource));
        }

        // 배정 던전의 heartbeat를 장애 취소 기준보다 오래 끊어 서버 소실로 보이게 한다
        private static async Task<ReservedSessionView> StartLostAsync(SessionTestEnvironment environment)
        {
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player }, 0);
            await environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '2 minutes' WHERE instance_id = @Id;",
                new { Id = session.InstanceId });
            return session;
        }

        private static Task<long> CountCancelledAsync(SessionTestEnvironment environment, ReservedSessionView first,
            ReservedSessionView second)
        {
            return environment.ScalarAsync(CANCELLED_COUNT_SQL, new
            {
                First = first.SessionId, Second = second.SessionId,
                Cancelled = GameSessionStates.RESULT_SERVICE_CANCELLED
            });
        }

        private static Task<long> CountIntentsAsync(SessionTestEnvironment environment, Guid operationId)
        {
            return environment.ScalarAsync(
                """
                SELECT count(*) FROM admin_audit_log
                WHERE action = @Action AND after_state->>'operationId' = @OperationId;
                """, new { Action = AdminRecoveryActions.RECOVERY_INTENT, OperationId = operationId.ToString("D") });
        }
    }
}
