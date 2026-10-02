// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data;
using Shinshi.Data.Admin;
using Shinshi.Data.Ledger;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 실제 PostgreSQL에서 복구 대상 분류, 운영자 정산 재시도와 서비스 장애 취소의 상태 검사, 재전송,
    /// 감사 행, 원장과 잔액 정합을 검사한다 </summary>
    public sealed class AdminSessionRecoveryPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long   TICKETS          = 3;
        private const long   POTIONS          = 4;
        private const long   POTIONS_RESERVED = 2;
        private const long   ESSENCE_GRANT    = 70;
        private const long   EXTRA_ACCRUED    = 101;
        private const long   CANCEL_ESSENCE   = 50;
        private const int    PAGE_SIZE        = 100;
        private const int    MISMATCH_BATCH   = 1000;
        private const int    DUNGEONS         = 4;
        private const string REASON           = "stuck after dungeon crash";
        private const string SEED_SOURCE      = "Seed";
        private const string SEED_ID          = "initial";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 기본 목록은 실패 기록, 오래된 Settling, 서버 소실 Running만 고르고 state 필터는 그 상태 전체를 고른다 </summary>
        [PostgresFact]
        public async Task List_DefaultAndStateFilter_ClassifiesRecoveryTargets()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(DUNGEONS);
            List<ReservedSessionView> sessions = new List<ReservedSessionView>();
            for (int index = 0; index < DUNGEONS; ++index)
            {
                TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
                sessions.Add(await environment.StartAsync(GameSessionCodes.Mode.NORMAL, new[] { player }, 0));
            }
            ReservedSessionView healthy = sessions[0];
            ReservedSessionView quarantined = sessions[1];
            ReservedSessionView stale = sessions[2];
            ReservedSessionView lost = sessions[3];
            await environment.ExecuteAsync("UPDATE game_sessions SET recovery_failures = @Failures WHERE session_id = @Id;",
                new { Id = quarantined.SessionId, Failures = PostgresSettlementRepository.MAX_RECOVERY_FAILURES });
            await SessionEventTests.AppendAsync(environment, stale, new[]
            {
                SessionEventTests.Ended(1, Guid.NewGuid(),
                    new[] { SessionEventTests.Grant(stale.Participants[0].ParticipantId, ESSENCE_GRANT) })
            });
            await environment.ExecuteAsync(
                "UPDATE game_sessions SET settling_at = clock_timestamp() - make_interval(secs => @Delay) WHERE session_id = @Id;",
                new { Id = stale.SessionId, Delay = ServerPoolTimings.SETTLING_RECOVERY_SECONDS + 1 });
            await environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '2 minutes' WHERE instance_id = @Id;",
                new { Id = lost.InstanceId });
            PostgresAdminRecoveryRepository repository = new PostgresAdminRecoveryRepository(environment.DataSource);

            AdminRecoverySessionPage candidates = await repository.ListAsync(null, null, PAGE_SIZE);
            AdminRecoverySessionPage running = await repository.ListAsync(GameSessionStates.RUNNING, null, PAGE_SIZE);
            List<string> paged = new List<string>();
            AdminLogCursor? cursor = null;
            do
            {
                AdminRecoverySessionPage page = await repository.ListAsync(null, cursor, 1);
                paged.AddRange(page.Sessions.Select(row => row.Session.SessionId));
                cursor = null;
                if (page.NextCursor != null)
                {
                    Assert.True(AdminLogCursor.TryDecode(page.NextCursor, out cursor));
                }
            }
            while (cursor != null);

            Dictionary<string, AdminRecoverySession> byId = candidates.Sessions.ToDictionary(row => row.Session.SessionId);
            Assert.Equal(3, byId.Count);
            Assert.DoesNotContain(healthy.SessionId.ToString("D"), byId.Keys);
            AdminRecoverySession quarantinedRow = byId[quarantined.SessionId.ToString("D")];
            Assert.True(quarantinedRow.Quarantined);
            Assert.Equal(PostgresSettlementRepository.MAX_RECOVERY_FAILURES, quarantinedRow.Session.RecoveryFailures);
            Assert.True(quarantinedRow.Instance.AssignedBootAlive);
            AdminRecoverySession staleRow = byId[stale.SessionId.ToString("D")];
            Assert.True(staleRow.SettlingStale);
            Assert.Equal(GameSessionStates.SETTLING, staleRow.Session.State);
            Assert.NotNull(staleRow.Session.SettlingAtUnixMs);
            AdminRecoverySession lostRow = byId[lost.SessionId.ToString("D")];
            Assert.True(lostRow.InstanceLost);
            Assert.False(lostRow.Instance.AssignedBootAlive);
            Assert.Equal(lost.InstanceId.ToString("D"), lostRow.Instance.InstanceId);
            Assert.Equal(lostRow.Session.InstanceBootId, lostRow.Instance.BootId);
            Assert.Equal(3, running.Sessions.Length);
            Assert.All(running.Sessions, row => Assert.Equal(GameSessionStates.RUNNING, row.Session.State));
            Assert.Equal(candidates.Sessions.Select(row => row.Session.SessionId), paged);
            await Assert.ThrowsAsync<ArgumentException>(() => repository.ListAsync("Bogus", null, PAGE_SIZE));
        }

        /// <summary> Settling만 정산 재시도할 수 있고, 성공은 자동 복구와 같은 정산 작업을 한 번 적용해 감사 행을 남기며,
        /// 같은 operationId 재전송은 처음 결과를 그대로 준다 </summary>
        [PostgresFact]
        public async Task RetrySettlement_SettlingSession_SettlesOnceAuditsAndReplays()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(2);
            RecoveryTestHarness harness = await RecoveryTestHarness.CreateAsync(environment);
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer runner = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.NORMAL, new[] { player }, 0);
            ReservedSessionView running = await environment.StartAsync(GameSessionCodes.Mode.NORMAL, new[] { runner }, 0);
            Guid settlementId = Guid.NewGuid();
            await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Ended(1, settlementId,
                    new[] { SessionEventTests.Grant(session.Participants[0].ParticipantId, ESSENCE_GRANT) })
            });

            AdminSessionRecoveryOutcome notEnded = await harness.RecoverAsync(running.SessionId, Guid.NewGuid(),
                AdminSessionRecoveryCodes.RETRY_SETTLEMENT);
            AdminSessionRecoveryOutcome notRunning = await harness.RecoverAsync(session.SessionId, Guid.NewGuid(),
                AdminSessionRecoveryCodes.SERVICE_CANCEL);
            AdminSessionRecoveryOutcome missing = await harness.RecoverAsync(Guid.NewGuid(), Guid.NewGuid(),
                AdminSessionRecoveryCodes.RETRY_SETTLEMENT);
            Guid operationId = Guid.NewGuid();
            AdminSessionRecoveryOutcome first = await harness.RecoverAsync(session.SessionId, operationId,
                AdminSessionRecoveryCodes.RETRY_SETTLEMENT);
            AdminSessionRecoveryOutcome replay = await harness.RecoverAsync(session.SessionId, operationId,
                AdminSessionRecoveryCodes.RETRY_SETTLEMENT);
            AdminSessionRecoveryOutcome conflict = await harness.RecoverAsync(session.SessionId, operationId,
                AdminSessionRecoveryCodes.SERVICE_CANCEL);
            AdminSessionRecoveryOutcome again = await harness.RecoverAsync(session.SessionId, Guid.NewGuid(),
                AdminSessionRecoveryCodes.RETRY_SETTLEMENT);
            OperationResult late = await harness.Settlement.SettleAsync(session.InstanceId, session.SessionId,
                settlementId);

            AssertRejected(notEnded, AdminContractCodes.RejectReason.INVALID_STATE);
            AssertRejected(notRunning, AdminContractCodes.RejectReason.INVALID_STATE);
            AssertRejected(missing, AdminContractCodes.RejectReason.NOT_FOUND);
            Assert.Null(missing.RecoveryActorId);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Result.Status);
            Assert.Equal(settlementId.ToString("D"), first.Result.ReceiptId);
            Assert.Equal(session.InstanceId, first.RecoveryActorId);
            Assert.Equal(first.Result.Status, replay.Result.Status);
            Assert.Equal(first.Result.ReceiptId, replay.Result.ReceiptId);
            Assert.Equal(first.Result.ServerTimeUnixMs, replay.Result.ServerTimeUnixMs);
            Assert.Equal(first.Result.CommittedRevision, replay.Result.CommittedRevision);
            AssertRejected(conflict, ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT);
            // 이미 정산된 세션은 정산 작업의 저장 결과로 확정된다(어드민 작업 실패 뒤 재전송이 감사 없이 남지 않게)
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, again.Result.Status);
            Assert.Equal(settlementId.ToString("D"), again.Result.ReceiptId);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, late.Status);

            Assert.Equal(GameSessionStates.CLOSED, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
            Assert.Equal(GameSessionStates.RUNNING, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = running.SessionId }));
            Assert.Equal(ESSENCE_GRANT, (await environment.ReadWalletAsync(player.AccountId)).Essence);
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE operation_id = @Id;", new { Id = settlementId }));

            // 감사 행은 확정된 두 요청뿐이다(실제 정산, 이미 적용됨). 거절된 요청은 감사 없이 결과만 저장된다
            Assert.Equal(2L, await environment.ScalarAsync(
                "SELECT count(*) FROM admin_audit_log WHERE admin_id = @AdminId AND operation_id IS NOT NULL;",
                new { harness.Admin.AdminId }));
            Assert.Equal(1L, await environment.ScalarAsync(
                """
                SELECT count(*) FROM admin_audit_log
                WHERE admin_id = @AdminId AND (after_state ->> 'alreadyApplied')::boolean;
                """, new { harness.Admin.AdminId }));
            AdminOperationDetailResponse? detail = await new PostgresAdminTraceReader(environment.DataSource)
                .ReadOperationAsync(harness.Admin.AdminId, operationId);
            Assert.NotNull(detail);
            Assert.Equal(AdminRecoveryActions.RETRY_SETTLEMENT, detail.Operation.OperationKind);
            AdminAuditRecord audit = Assert.Single(detail.Audit);
            Assert.Equal(AdminRecoveryActions.RETRY_SETTLEMENT, audit.Action);
            Assert.Equal(AdminRecoveryActions.TARGET_GAME_SESSION, audit.TargetKind);
            Assert.Equal(session.SessionId.ToString("D"), audit.TargetId);
            Assert.Equal(REASON, audit.Reason);
            Assert.Equal(GameSessionStates.SETTLING, JObject.Parse(audit.BeforeState.ToString()).Value<string>("state"));
            JObject after = JObject.Parse(audit.AfterState.ToString());
            Assert.Equal(GameSessionStates.CLOSED, after["session"]!.Value<string>("state"));
            Assert.False(after.Value<bool>("alreadyApplied"));
            Assert.Equal(settlementId.ToString("D"), after.Value<string>("recoveryOperationId"));
            Assert.Equal(session.InstanceId.ToString("D"), after.Value<string>("recoveryActorId"));
        }

        /// <summary> 살아 있는 서버의 Running은 장애 취소를 거절하고, 저장된 거절의 재전송은 서버가 소실된 뒤에도 취소를 일으키지 않는다.
        /// 새 요청은 기존 장애 취소 경로로 환급과 정기 보상을 한 번 적용하고 원장과 잔액이 맞는다 </summary>
        [PostgresFact]
        public async Task ServiceCancel_LostRunningSession_RefundsOnceAndReconciles()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            RecoveryTestHarness harness = await RecoveryTestHarness.CreateAsync(environment);
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            await SeedOpeningLedgerAsync(environment, player);

            // 시작 티켓 소비도 실제 원장에 남겨야 대사가 맞으므로 실제 원장 쓰기의 출정 저장소로 시작한다
            PostgresGameSessionRepository sessions = new PostgresGameSessionRepository(environment.DataSource,
                environment.Executor, new LedgerWriter(new PostgresEconomyLedgerWriter()));
            ReservedSessionView session = await environment.ReserveAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid participant = session.Participants[0].ParticipantId;
            OperationResult started = await sessions.StartAsync(session.InstanceId, session.SessionId, Guid.NewGuid(),
                new[] { participant });
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, started.Status);
            await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Essence(1, participant, EXTRA_ACCRUED)
            });

            Guid refusedId = Guid.NewGuid();
            AdminSessionRecoveryOutcome alive = await harness.RecoverAsync(session.SessionId, refusedId,
                AdminSessionRecoveryCodes.SERVICE_CANCEL);
            await environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '2 minutes' WHERE instance_id = @Id;",
                new { Id = session.InstanceId });
            AdminSessionRecoveryOutcome refusedReplay = await harness.RecoverAsync(session.SessionId, refusedId,
                AdminSessionRecoveryCodes.SERVICE_CANCEL);
            string stateAfterReplay = await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId });
            Guid operationId = Guid.NewGuid();
            AdminSessionRecoveryOutcome cancelled = await harness.RecoverAsync(session.SessionId, operationId,
                AdminSessionRecoveryCodes.SERVICE_CANCEL);
            AdminSessionRecoveryOutcome replay = await harness.RecoverAsync(session.SessionId, operationId,
                AdminSessionRecoveryCodes.SERVICE_CANCEL);
            AdminSessionRecoveryOutcome closed = await harness.RecoverAsync(session.SessionId, Guid.NewGuid(),
                AdminSessionRecoveryCodes.SERVICE_CANCEL);
            AdminSessionRecoveryOutcome wrongPath = await harness.RecoverAsync(session.SessionId, Guid.NewGuid(),
                AdminSessionRecoveryCodes.RETRY_SETTLEMENT);

            AssertRejected(alive, AdminContractCodes.RejectReason.INVALID_STATE);
            AssertRejected(refusedReplay, AdminContractCodes.RejectReason.INVALID_STATE);
            Assert.Equal(alive.Result.ServerTimeUnixMs, refusedReplay.Result.ServerTimeUnixMs);
            Assert.Equal(GameSessionStates.RUNNING, stateAfterReplay);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, cancelled.Result.Status);
            Assert.Equal(cancelled.Result.ReceiptId, replay.Result.ReceiptId);
            Assert.Equal(cancelled.Result.ServerTimeUnixMs, replay.Result.ServerTimeUnixMs);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, closed.Result.Status);
            Assert.Equal(cancelled.Result.ReceiptId, closed.Result.ReceiptId);
            AssertRejected(wrongPath, AdminContractCodes.RejectReason.INVALID_STATE);
            Assert.Equal(GameSessionStates.RESULT_SERVICE_CANCELLED, await environment.TextAsync(
                "SELECT result FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));

            WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
            Assert.Equal(TICKETS, wallet.Tickets);
            Assert.Equal(0L, wallet.TicketsReserved);
            Assert.Equal(POTIONS, wallet.Potions);
            Assert.Equal(0L, wallet.PotionsReserved);
            Assert.Equal(CANCEL_ESSENCE, wallet.Essence);
            IReadOnlyList<EconomyReconciliationMismatch> mismatches = await new EconomyLedgerReconciler(
                environment.DataSource).FindMismatchesAsync(MISMATCH_BATCH);
            Assert.Empty(mismatches);

            // 장애 취소 작업은 배정 서버 actor로 남고, 어드민 작업 영수증이 그 operationId다
            AdminOperationDetailResponse? cancel = await new PostgresAdminTraceReader(environment.DataSource)
                .ReadOperationAsync(session.InstanceId, Guid.Parse(cancelled.Result.ReceiptId));
            Assert.NotNull(cancel);
            Assert.Equal(PostgresSettlementRepository.SERVICE_CANCEL_KIND, cancel.Operation.OperationKind);
            Assert.Contains(cancel.Ledger, entry => (entry.Reason == ContractCodes.EconomyReason.SERVICE_CANCEL_REFUND)
                && (entry.AssetKind == EconomyAsset.TICKETS) && (entry.Delta == 1));
            Assert.Contains(cancel.Ledger, entry => (entry.Reason == ContractCodes.EconomyReason.SERVICE_CANCEL_ESSENCE)
                && (entry.Delta == CANCEL_ESSENCE));
            Assert.Empty(cancel.Audit);
            Assert.Equal(1L, await environment.ScalarAsync(
                """
                SELECT count(*) FROM admin_audit_log
                WHERE admin_id = @AdminId AND operation_id = @OperationId AND action = @Action AND target_id = @Target;
                """, new
                {
                    harness.Admin.AdminId, OperationId = operationId, Action = AdminRecoveryActions.SERVICE_CANCEL,
                    Target = session.SessionId.ToString("D")
                }));
            // 실제 취소와 이미 적용된 취소 확인 둘. 같은 취소 작업의 원장은 한 번만 있다
            Assert.Equal(2L, await environment.ScalarAsync(
                "SELECT count(*) FROM admin_audit_log WHERE operation_id IS NOT NULL;"));
            Assert.Equal(cancel.Ledger.Length, (int)await environment.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason IN (@Refund, @Essence);",
                new
                {
                    Refund = ContractCodes.EconomyReason.SERVICE_CANCEL_REFUND,
                    Essence = ContractCodes.EconomyReason.SERVICE_CANCEL_ESSENCE
                }));
        }

        internal static void AssertRejected(AdminSessionRecoveryOutcome outcome, int reasonCode)
        {
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, outcome.Result.Status);
            Assert.Equal(reasonCode, outcome.Result.ReasonCode);
        }

        // 시험 계정의 시작 잔액(티켓, 물약)을 실제 원장에도 남겨 대사의 기준을 맞춘다
        internal static async Task SeedOpeningLedgerAsync(SessionTestEnvironment environment, TestPlayer player)
        {
            Guid actor = Guid.NewGuid();
            await AdminLogTestSupport.WriteLedgerAsync(environment.Executor, actor, EconomyAsset.Tickets(player.AccountId),
                TICKETS, TICKETS, ContractCodes.EconomyReason.INITIAL_GRANT, SEED_SOURCE, SEED_ID);
            await AdminLogTestSupport.WriteLedgerAsync(environment.Executor, actor,
                EconomyAsset.AccountItem(player.AccountId, SessionTestEnvironment.POTION_ID, SessionTestEnvironment.BINDING),
                POTIONS, POTIONS, ContractCodes.EconomyReason.INITIAL_GRANT, SEED_SOURCE, SEED_ID);
        }
    }
}
