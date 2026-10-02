// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Backend.Sessions;
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data;
using Shinshi.Data.Admin;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 수동 복구가 자동 복구와 같은 생존 판정 보류를 따르는지, 2단계 실패에도 의도 감사 행이 남는지,
    /// alreadyApplied가 복구 작업 저장 여부로 정해지는지 검사한다 </summary>
    public sealed class AdminRecoveryLivenessTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long TICKETS        = 3;
        private const long POTIONS        = 4;
        private const int  PAGE_SIZE      = 100;
        private const int  RECOVERY_BATCH = 8;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 정비 서비스가 DB 단절을 겪으면 어드민 복구와 공유하는 보류가 다시 닫힌다 </summary>
        [Fact]
        public async Task Maintenance_DatabaseOutage_DefersSharedGate()
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            RecoveryLivenessGate gate = new RecoveryLivenessGate(clock);
            bool startup = gate.IsDeferred();
            clock.Now = clock.Now + ServerPoolTimings.StartupRecoveryGrace;
            bool afterGrace = gate.IsDeferred();
            GameSessionMaintenanceService service = new GameSessionMaintenanceService(new UnreachableWork(),
                NullLogger<GameSessionMaintenanceService>.Instance, clock, gate);

            await service.RunOnceAsync(CancellationToken.None);

            Assert.True(startup);
            Assert.False(afterGrace);
            Assert.True(gate.IsDeferred());
            Assert.Equal((int)ServerPoolTimings.StartupRecoveryGrace.TotalSeconds, gate.RetryAfterSeconds());
        }

        /// <summary> 보류 중 새 장애 취소와 강제 종료는 저장 없이 503(TransientRejection)이고 목록은 instanceLost를 내린다.
        /// 보류가 끝나면 같은 operationId로 다시 보내 취소된다 </summary>
        [PostgresFact]
        public async Task ServiceCancel_DuringLivenessDeferral_RejectsWithoutStoringAndMasksList()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            RecoveryTestHarness harness = await RecoveryTestHarness.CreateAsync(environment, livenessDeferred: true);
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.NORMAL, new[] { player }, 0);
            await environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '2 minutes' WHERE instance_id = @Id;",
                new { Id = session.InstanceId });
            Guid cancelId = Guid.NewGuid();
            Guid forceId = Guid.NewGuid();

            TransientRejectionException cancel = await Assert.ThrowsAsync<TransientRejectionException>(() =>
                harness.RecoverAsync(session.SessionId, cancelId, AdminSessionRecoveryCodes.SERVICE_CANCEL));
            TransientRejectionException force = await Assert.ThrowsAsync<TransientRejectionException>(() =>
                harness.RecoverAsync(session.SessionId, forceId, AdminSessionRecoveryCodes.FORCE_CLOSE));
            AdminRecoverySessionPage deferred = await harness.Recovery.ListAsync(null, null, PAGE_SIZE,
                livenessDeferred: harness.Service.IsLivenessDeferred);
            long storedWhileDeferred = await environment.ScalarAsync(
                "SELECT count(*) FROM operations WHERE operation_id = ANY(@Ids);",
                new { Ids = new[] { cancelId, forceId } });
            string stateWhileDeferred = await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId });

            harness.OpenLiveness();
            AdminRecoverySessionPage open = await harness.Recovery.ListAsync(null, null, PAGE_SIZE,
                livenessDeferred: harness.Service.IsLivenessDeferred);
            AdminSessionRecoveryOutcome cancelled = await harness.RecoverAsync(session.SessionId, cancelId,
                AdminSessionRecoveryCodes.SERVICE_CANCEL);

            Assert.Equal(ContractCodes.RejectReason.BACKEND_UNAVAILABLE, cancel.ReasonCode);
            Assert.True(cancel.RetryAfterSeconds > 0);
            Assert.Equal(ContractCodes.RejectReason.BACKEND_UNAVAILABLE, force.ReasonCode);
            Assert.Equal(0L, storedWhileDeferred);
            Assert.Equal(GameSessionStates.RUNNING, stateWhileDeferred);
            Assert.True(deferred.LivenessDeferred);
            AdminRecoverySession masked = Assert.Single(deferred.Sessions);
            Assert.False(masked.InstanceLost);
            Assert.False(masked.Instance.AssignedBootAlive);
            Assert.False(open.LivenessDeferred);
            Assert.True(Assert.Single(open.Sessions).InstanceLost);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, cancelled.Result.Status);
            Assert.Equal(GameSessionStates.CLOSED, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
        }

        /// <summary> 복구 작업은 확정됐는데 어드민 작업 확정(감사 쓰기)이 실패해도 의도 감사 행이 먼저 커밋돼 남는다.
        /// 같은 operationId 재전송은 이미 저장된 복구 작업이라 alreadyApplied로 확정된다 </summary>
        [PostgresFact]
        public async Task ServiceCancel_SecondPhaseFails_LeavesIntentAuditAndRetryIsAlreadyApplied()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            RecoveryTestHarness failing = await RecoveryTestHarness.CreateAsync(environment,
                operationAuditWriter: new FailingOperationAuditWriter());
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);

            // 보스 출정만 입장권을 소비하므로 장애 취소 환급 원장이 생긴다
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player }, 0);
            await environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '2 minutes' WHERE instance_id = @Id;",
                new { Id = session.InstanceId });
            Guid operationId = Guid.NewGuid();
            Guid cancelOperationId = PostgresSettlementRepository.ServiceCancelOperationId(session.SessionId);

            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.RecoverAsync(session.SessionId,
                operationId, AdminSessionRecoveryCodes.SERVICE_CANCEL));
            string stateAfterFailure = await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId });
            long adminOperations = await environment.ScalarAsync(
                "SELECT count(*) FROM operations WHERE actor_id = @AdminId AND operation_id = @Id;",
                new { failing.Admin.AdminId, Id = operationId });
            long operationAudits = await environment.ScalarAsync(
                "SELECT count(*) FROM admin_audit_log WHERE operation_id IS NOT NULL;");
            IntentAudit intent = await ReadIntentAsync(environment, session.SessionId);

            RecoveryTestHarness healthy = failing.WithOperationAuditWriter(environment, null);
            AdminSessionRecoveryOutcome retried = await healthy.RecoverAsync(session.SessionId, operationId,
                AdminSessionRecoveryCodes.SERVICE_CANCEL);
            AdminSessionRecoveryOutcome fresh = await healthy.RecoverAsync(session.SessionId, Guid.NewGuid(),
                AdminSessionRecoveryCodes.SERVICE_CANCEL);

            Assert.Equal(GameSessionStates.CLOSED, stateAfterFailure);
            Assert.Equal(0L, adminOperations);
            Assert.Equal(0L, operationAudits);
            Assert.Equal(failing.Admin.AdminId, intent.AdminId);
            Assert.Null(intent.OperationId);
            Assert.Equal(RecoveryTestHarness.REASON, intent.Reason);
            JObject intentAfter = JObject.Parse(intent.AfterState!);
            Assert.Equal(operationId.ToString("D"), intentAfter.Value<string>("operationId"));
            Assert.Equal(AdminSessionRecoveryCodes.SERVICE_CANCEL, intentAfter.Value<string>("action"));
            Assert.Equal(cancelOperationId.ToString("D"), intentAfter.Value<string>("recoveryOperationId"));
            Assert.Equal(GameSessionStates.RUNNING, JObject.Parse(intent.BeforeState!).Value<string>("state"));

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, retried.Result.Status);
            Assert.Equal(cancelOperationId.ToString("D"), retried.Result.ReceiptId);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, fresh.Result.Status);
            JObject retriedAudit = JObject.Parse(await environment.TextAsync(
                "SELECT after_state::text FROM admin_audit_log WHERE operation_id = @Id;", new { Id = operationId }));
            Assert.True(retriedAudit.Value<bool>("alreadyApplied"));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM operations WHERE actor_id = @Actor AND operation_id = @Id;",
                new { Actor = session.InstanceId, Id = cancelOperationId }));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE operation_id = @Id AND reason = @Refund;",
                new { Id = cancelOperationId, Refund = ContractCodes.EconomyReason.SERVICE_CANCEL_REFUND }));
        }

        /// <summary> 자동 복구가 먼저 취소한 세션의 수동 취소는 그 작업의 저장 결과로 확정되고 alreadyApplied다.
        /// 이번 호출이 실제로 취소하면 alreadyApplied가 아니다 </summary>
        [PostgresFact]
        public async Task ServiceCancel_AlreadyAppliedFollowsStoredRecoveryOperation()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(2);
            RecoveryTestHarness harness = await RecoveryTestHarness.CreateAsync(environment);
            TestPlayer first = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer second = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView automatic = await environment.StartAsync(GameSessionCodes.Mode.NORMAL, new[] { first }, 0);
            ReservedSessionView manual = await environment.StartAsync(GameSessionCodes.Mode.NORMAL, new[] { second }, 0);
            await environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '2 minutes' WHERE instance_id = @Id;",
                new { Id = automatic.InstanceId });
            SessionRecoveryResult recovered = await harness.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);
            await environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '2 minutes' WHERE instance_id = @Id;",
                new { Id = manual.InstanceId });
            Guid automaticAdminOperation = Guid.NewGuid();
            Guid manualAdminOperation = Guid.NewGuid();

            AdminSessionRecoveryOutcome afterAutomatic = await harness.RecoverAsync(automatic.SessionId,
                automaticAdminOperation, AdminSessionRecoveryCodes.SERVICE_CANCEL);
            AdminSessionRecoveryOutcome byOperator = await harness.RecoverAsync(manual.SessionId, manualAdminOperation,
                AdminSessionRecoveryCodes.SERVICE_CANCEL);

            Assert.Equal(1, recovered.Cancelled);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, afterAutomatic.Result.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, byOperator.Result.Status);
            Assert.True(await ReadAlreadyAppliedAsync(environment, automaticAdminOperation));
            Assert.False(await ReadAlreadyAppliedAsync(environment, manualAdminOperation));
            Assert.Equal(2L, await environment.ScalarAsync(
                "SELECT count(*) FROM admin_audit_log WHERE action = @Action;",
                new { Action = AdminRecoveryActions.RECOVERY_INTENT }));
        }

        private static async Task<bool> ReadAlreadyAppliedAsync(SessionTestEnvironment environment, Guid operationId)
        {
            JObject after = JObject.Parse(await environment.TextAsync(
                "SELECT after_state::text FROM admin_audit_log WHERE operation_id = @Id;", new { Id = operationId }));
            return after.Value<bool>("alreadyApplied");
        }

        private static async Task<IntentAudit> ReadIntentAsync(SessionTestEnvironment environment, Guid sessionId)
        {
            AdminAuditPage page = await new PostgresAdminAuditReader(environment.DataSource).ReadPageAsync(
                new AdminAuditQuery
                {
                    TargetKind = AdminRecoveryActions.TARGET_GAME_SESSION,
                    TargetId   = sessionId.ToString("D"),
                    Limit      = PAGE_SIZE
                });
            AdminAuditRecord record = Assert.Single(page.Entries,
                entry => entry.Action == AdminRecoveryActions.RECOVERY_INTENT);
            return new IntentAudit(record);
        }

        /// <summary> 모든 DB 작업이 연결 실패로 끝나는 정비 작업 대역 </summary>
        private sealed class UnreachableWork : IGameSessionMaintenanceWork
        {
            /****************************************
            *                Methods
            ****************************************/

            public Task<int> ReleaseExpiredAsync(int maxSessions, CancellationToken cancellationToken)
            {
                throw new NpgsqlException("Simulated database outage.");
            }

            public Task<SessionRecoveryResult> RecoverLostSessionsAsync(int maxSessions,
                CancellationToken cancellationToken)
            {
                throw new NpgsqlException("Simulated database outage.");
            }

            public Task<int> PurgeExpiredJoinTokensAsync(int maxRows, CancellationToken cancellationToken)
            {
                throw new NpgsqlException("Simulated database outage.");
            }
        }

        /// <summary> 감사 기록 한 줄에서 이 테스트가 보는 값 </summary>
        private sealed class IntentAudit
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid?   AdminId     { get; }
            public Guid?   OperationId { get; }
            public string  Reason      { get; }
            public string? BeforeState { get; }
            public string? AfterState  { get; }

            /****************************************
            *                Methods
            ****************************************/

            public IntentAudit(AdminAuditRecord record)
            {
                AdminId     = string.IsNullOrEmpty(record.AdminId) ? null : Guid.Parse(record.AdminId);
                OperationId = string.IsNullOrEmpty(record.OperationId) ? null : Guid.Parse(record.OperationId);
                Reason      = record.Reason;
                BeforeState = record.BeforeState?.ToString();
                AfterState  = record.AfterState?.ToString();
            }
        }
    }
}
