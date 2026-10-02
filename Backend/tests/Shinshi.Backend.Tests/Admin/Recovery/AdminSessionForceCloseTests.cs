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
    /// <summary> 정산이 반복 실패해 격리된 Settling 세션을 운영자가 강제 종료하는 경로를 실제 PostgreSQL에서 검사한다.
    /// 장애 취소와 같은 지급, 참가자와 임대 정리, 운영자 원장과 같은 트랜잭션의 감사, 재전송, 늦은 정산과의 경합을 본다 </summary>
    public sealed class AdminSessionForceCloseTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long TICKETS          = 3;
        private const long POTIONS          = 4;
        private const long POTIONS_RESERVED = 2;
        private const long EXTRA_ACCRUED    = 101;
        private const long CANCEL_ESSENCE   = 50;
        private const long SETTLED_ESSENCE  = 70;
        private const int  MISMATCH_BATCH   = 1000;
        private const int  LEASE_MINUTES    = 15;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 격리된 Settling은 장애 취소 규칙으로 닫히고 원장이 잔액과 맞는다. 격리 안 된 Settling과 Running은 INVALID_STATE,
        /// 같은 operationId 재전송은 처음 결과, 늦게 온 /settlement는 ALREADY_SETTLED다 </summary>
        [PostgresFact]
        public async Task ForceClose_QuarantinedSettling_RefundsOnceReleasesAndAudits()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(3);
            RecoveryTestHarness harness = await RecoveryTestHarness.CreateAsync(environment);
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            await AdminSessionRecoveryPostgresTests.SeedOpeningLedgerAsync(environment, player);
            (ReservedSessionView session, Guid settlementId) = await StartEndedAsync(environment, player);
            await QuarantineAsync(environment, session.SessionId);
            await environment.ExecuteAsync(
                """
                INSERT INTO account_leases (account_id, lease_id, instance_id, role, expires_at)
                VALUES (@AccountId, @LeaseId, @InstanceId, @Role, clock_timestamp() + make_interval(mins => @Minutes))
                ON CONFLICT (account_id) DO UPDATE SET instance_id = EXCLUDED.instance_id, role = EXCLUDED.role,
                    expires_at = EXCLUDED.expires_at;
                """, new
                {
                    player.AccountId, LeaseId = Guid.NewGuid(), session.InstanceId,
                    Role = ContractCodes.ServerRole.DUNGEON, Minutes = LEASE_MINUTES
                });
            TestPlayer other = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer runner = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            await AdminSessionRecoveryPostgresTests.SeedOpeningLedgerAsync(environment, other);
            await AdminSessionRecoveryPostgresTests.SeedOpeningLedgerAsync(environment, runner);
            (ReservedSessionView unquarantined, Guid _) = await StartEndedAsync(environment, other);
            ReservedSessionView running = await environment.StartAsync(GameSessionCodes.Mode.NORMAL, new[] { runner }, 0);

            AdminSessionRecoveryOutcome notQuarantined = await harness.RecoverAsync(unquarantined.SessionId,
                Guid.NewGuid(), AdminSessionRecoveryCodes.FORCE_CLOSE);
            AdminSessionRecoveryOutcome notSettling = await harness.RecoverAsync(running.SessionId, Guid.NewGuid(),
                AdminSessionRecoveryCodes.FORCE_CLOSE);
            AdminSessionRecoveryOutcome missing = await harness.RecoverAsync(Guid.NewGuid(), Guid.NewGuid(),
                AdminSessionRecoveryCodes.FORCE_CLOSE);
            Guid operationId = Guid.NewGuid();
            AdminSessionRecoveryOutcome closed = await harness.RecoverAsync(session.SessionId, operationId,
                AdminSessionRecoveryCodes.FORCE_CLOSE);
            AdminSessionRecoveryOutcome replay = await harness.RecoverAsync(session.SessionId, operationId,
                AdminSessionRecoveryCodes.FORCE_CLOSE);
            AdminSessionRecoveryOutcome again = await harness.RecoverAsync(session.SessionId, Guid.NewGuid(),
                AdminSessionRecoveryCodes.FORCE_CLOSE);
            AdminSessionRecoveryOutcome retry = await harness.RecoverAsync(session.SessionId, Guid.NewGuid(),
                AdminSessionRecoveryCodes.RETRY_SETTLEMENT);
            OperationResult late = await harness.Settlement.SettleAsync(session.InstanceId, session.SessionId,
                settlementId);

            AdminSessionRecoveryPostgresTests.AssertRejected(notQuarantined, AdminContractCodes.RejectReason.INVALID_STATE);
            AdminSessionRecoveryPostgresTests.AssertRejected(notSettling, AdminContractCodes.RejectReason.INVALID_STATE);
            AdminSessionRecoveryPostgresTests.AssertRejected(missing, AdminContractCodes.RejectReason.NOT_FOUND);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, closed.Result.Status);
            Assert.Equal(operationId.ToString("D"), closed.Result.ReceiptId);
            Assert.Equal(harness.Admin.AdminId, closed.RecoveryActorId);
            Assert.Equal(closed.Result.ServerTimeUnixMs, replay.Result.ServerTimeUnixMs);
            Assert.Equal(closed.Result.ReceiptId, replay.Result.ReceiptId);
            AdminSessionRecoveryPostgresTests.AssertRejected(again, AdminContractCodes.RejectReason.INVALID_STATE);
            AdminSessionRecoveryPostgresTests.AssertRejected(retry, AdminContractCodes.RejectReason.INVALID_STATE);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, late.Status);
            Assert.Equal(ContractCodes.RejectReason.ALREADY_SETTLED, late.ReasonCode);

            Assert.Equal(GameSessionStates.CLOSED, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
            Assert.Equal(GameSessionStates.RESULT_SERVICE_CANCELLED, await environment.TextAsync(
                "SELECT result FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
            Assert.Equal(GameSessionStates.SETTLING, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = unquarantined.SessionId }));
            WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
            Assert.Equal(TICKETS, wallet.Tickets);
            Assert.Equal(0L, wallet.TicketsReserved);
            Assert.Equal(POTIONS, wallet.Potions);
            Assert.Equal(0L, wallet.PotionsReserved);
            Assert.Equal(CANCEL_ESSENCE, wallet.Essence);
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM session_participants WHERE session_id = @Id AND state = 'Active';",
                new { Id = session.SessionId }));
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM account_leases WHERE account_id = @AccountId;", new { player.AccountId }));
            Assert.Empty(await new EconomyLedgerReconciler(environment.DataSource).FindMismatchesAsync(MISMATCH_BATCH));

            // 원장은 운영자 actor와 어드민 operationId로, 사유는 기존 장애 취소 값으로 남고 감사는 같은 작업에 묶인다
            AdminOperationDetailResponse? detail = await new PostgresAdminTraceReader(environment.DataSource)
                .ReadOperationAsync(harness.Admin.AdminId, operationId);
            Assert.NotNull(detail);
            Assert.Equal(AdminRecoveryActions.FORCE_CLOSE, detail.Operation.OperationKind);
            Assert.All(detail.Ledger, entry => Assert.Equal(EconomyLedgerCodes.ACTOR_ADMIN, entry.ActorKind));
            Assert.Contains(detail.Ledger, entry => (entry.Reason == ContractCodes.EconomyReason.SERVICE_CANCEL_REFUND)
                && (entry.AssetKind == EconomyAsset.TICKETS) && (entry.Delta == 1));
            Assert.Contains(detail.Ledger, entry => (entry.Reason == ContractCodes.EconomyReason.SERVICE_CANCEL_ESSENCE)
                && (entry.Delta == CANCEL_ESSENCE));
            AdminAuditRecord audit = Assert.Single(detail.Audit);
            Assert.Equal(AdminRecoveryActions.FORCE_CLOSE, audit.Action);
            Assert.Equal(session.SessionId.ToString("D"), audit.TargetId);
            Assert.Equal(GameSessionStates.SETTLING, JObject.Parse(audit.BeforeState.ToString()).Value<string>("state"));
            JObject after = JObject.Parse(audit.AfterState.ToString());
            Assert.Equal(GameSessionStates.CLOSED, after["session"]!.Value<string>("state"));
            Assert.False(after.Value<bool>("alreadyApplied"));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM admin_audit_log WHERE action = @Action;",
                new { Action = AdminRecoveryActions.FORCE_CLOSE }));
        }

        /// <summary> 강제 종료와 늦게 온 /settlement가 동시에 와도 한쪽만 적용되고 원장이 잔액과 맞는다 </summary>
        [PostgresFact]
        public async Task ForceClose_RacingLateSettlement_AppliesExactlyOne()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            RecoveryTestHarness harness = await RecoveryTestHarness.CreateAsync(environment);
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            await AdminSessionRecoveryPostgresTests.SeedOpeningLedgerAsync(environment, player);
            (ReservedSessionView session, Guid settlementId) = await StartEndedAsync(environment, player);
            await QuarantineAsync(environment, session.SessionId);

            Task<AdminSessionRecoveryOutcome> force = Task.Run(() => harness.RecoverAsync(session.SessionId,
                Guid.NewGuid(), AdminSessionRecoveryCodes.FORCE_CLOSE));
            Task<OperationResult> settle = Task.Run(() => harness.Settlement.SettleAsync(session.InstanceId,
                session.SessionId, settlementId));
            await Task.WhenAll(force, settle);

            // 보스 성공 정산은 닫지 않고 경매 단계로 넘긴다. 강제 종료가 이기면 장애 취소로 닫히고 입장권이 환급된다
            bool forced = force.Result.Result.Status == ContractCodes.OperationStatus.COMMITTED;
            bool settled = settle.Result.Status == ContractCodes.OperationStatus.COMMITTED;
            Assert.True(forced ^ settled);
            Assert.Equal(forced ? GameSessionStates.CLOSED : GameSessionStates.AUCTION, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
            WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
            Assert.Equal(forced ? CANCEL_ESSENCE : SETTLED_ESSENCE, wallet.Essence);
            Assert.Equal(forced ? TICKETS : TICKETS - 1, wallet.Tickets);
            Assert.Empty(await new EconomyLedgerReconciler(environment.DataSource).FindMismatchesAsync(MISMATCH_BATCH));
        }

        // 실제 원장 쓰기의 출정 저장소로 보스 출정을 시작해 입장권 소비를 원장에 남기고, 추가 정기 적립과 종료 이벤트까지 저장한다
        private static async Task<(ReservedSessionView Session, Guid SettlementId)> StartEndedAsync(
            SessionTestEnvironment environment, TestPlayer player)
        {
            PostgresGameSessionRepository sessions = new PostgresGameSessionRepository(environment.DataSource,
                environment.Executor, new LedgerWriter(new PostgresEconomyLedgerWriter()));
            ReservedSessionView session = await environment.ReserveAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid participant = session.Participants[0].ParticipantId;
            OperationResult started = await sessions.StartAsync(session.InstanceId, session.SessionId, Guid.NewGuid(),
                new[] { participant });
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, started.Status);
            Guid settlementId = Guid.NewGuid();
            await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Essence(1, participant, EXTRA_ACCRUED),
                SessionEventTests.Ended(2, settlementId,
                    new List<SettlementGrantInput> { SessionEventTests.Grant(participant, SETTLED_ESSENCE) })
            });
            Assert.Equal(GameSessionStates.SETTLING, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
            return (session, settlementId);
        }

        private static Task QuarantineAsync(SessionTestEnvironment environment, Guid sessionId)
        {
            return environment.ExecuteAsync("UPDATE game_sessions SET recovery_failures = @Failures WHERE session_id = @Id;",
                new { Id = sessionId, Failures = PostgresSettlementRepository.MAX_RECOVERY_FAILURES });
        }
    }
}
