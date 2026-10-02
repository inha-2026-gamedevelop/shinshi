// System
using System;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Settlement
{
    /// <summary> 던전이 살아 있어도 정체된 Settling의 정산, 반복 실패 세션의 격리와 정상 세션의 계속 처리를 검사한다 </summary>
    public sealed class SettlementRecoveryTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long TICKETS          = 3;
        private const long POTIONS          = 4;
        private const long POTIONS_RESERVED = 2;
        private const long ESSENCE_GRANT    = 70;
        private const int  RECOVERY_BATCH   = 8;
        private const int  SINGLE_BATCH     = 1;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 종료 이벤트 저장 뒤 60초가 지나도 /settlement가 없으면 살아 있는 던전의 출정도 같은 operationId로 정산한다 </summary>
        [PostgresFact]
        public async Task Recover_SettlingPastDelayWithLiveInstance_SettlesOnceWithStoredOperation()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid settlementId = Guid.NewGuid();
            await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Ended(1, settlementId,
                    new[] { SessionEventTests.Grant(session.Participants[0].ParticipantId, ESSENCE_GRANT) })
            });

            SessionRecoveryResult early = await environment.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);
            await environment.ExecuteAsync(
                "UPDATE game_sessions SET settling_at = clock_timestamp() - make_interval(secs => @Delay) WHERE session_id = @Id;",
                new { Id = session.SessionId, Delay = ServerPoolTimings.SETTLING_RECOVERY_SECONDS + 1 });
            SessionRecoveryResult stalled = await environment.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);
            OperationResult late = await environment.Settlement.SettleAsync(session.InstanceId, session.SessionId,
                settlementId);

            Assert.Equal(0, early.Settled);
            Assert.Equal(1, stalled.Settled);
            Assert.Empty(stalled.Failures);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, late.Status);
            Assert.Equal(ESSENCE_GRANT, (await environment.ReadWalletAsync(player.AccountId)).Essence);
            // 보스 성공 정산은 경매 단계로 넘어간다. 출정 종료는 경매 finish(또는 경매 복구)가 한다
            Assert.Equal(GameSessionStates.AUCTION, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
        }

        /// <summary> 정산안이 깨진 세션이 반복 실패해도 다음 정상 세션은 복구되고, 실패가 한도에 닿으면 자동 복구에서 빠진다 </summary>
        [PostgresFact]
        public async Task Recover_PoisonSession_IsIsolatedAndQuarantinedWhileOthersRecover()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(2);
            TestPlayer poisonPlayer = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer healthyPlayer = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView poison = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { poisonPlayer },
                POTIONS_RESERVED);
            ReservedSessionView healthy = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { healthyPlayer },
                POTIONS_RESERVED);
            await CorruptIntoSettlingAsync(environment, poison.SessionId);

            // 독성 세션이 먼저 실패한 뒤 정상 세션이 소실되면, 한 건씩 처리해도 실패 횟수가 적은 정상 세션이 앞선다
            SessionRecoveryResult first = await environment.Settlement.RecoverLostSessionsAsync(SINGLE_BATCH);
            await environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '2 minutes' WHERE instance_id = @Id;",
                new { Id = healthy.InstanceId });
            SessionRecoveryResult second = await environment.Settlement.RecoverLostSessionsAsync(SINGLE_BATCH);
            SessionRecoveryResult last = first;
            for (int attempt = 1; attempt < PostgresSettlementRepository.MAX_RECOVERY_FAILURES; ++attempt)
            {
                // 실패마다 늘어나는 백오프를 시험에서는 바로 지나가게 한다
                await environment.ExecuteAsync("UPDATE game_sessions SET next_recovery_at = NULL;");
                last = await environment.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);
            }
            await environment.ExecuteAsync("UPDATE game_sessions SET next_recovery_at = NULL;");
            SessionRecoveryResult quiet = await environment.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);

            Assert.Equal(poison.SessionId, Assert.Single(first.Failures).SessionId);
            Assert.Equal(1, second.Cancelled);
            Assert.Empty(second.Failures);
            Assert.True(Assert.Single(last.Failures).Quarantined);
            Assert.Empty(quiet.Failures);
            Assert.Equal(0, quiet.Settled + quiet.Cancelled);
            Assert.Equal((long)PostgresSettlementRepository.MAX_RECOVERY_FAILURES, await environment.ScalarAsync(
                "SELECT recovery_failures FROM game_sessions WHERE session_id = @Id;", new { Id = poison.SessionId }));
            Assert.Equal(GameSessionStates.SETTLING, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = poison.SessionId }));
            Assert.Equal(GameSessionStates.RESULT_SERVICE_CANCELLED, await environment.TextAsync(
                "SELECT result FROM game_sessions WHERE session_id = @Id;", new { Id = healthy.SessionId }));
        }

        /// <summary> 배치마다 상한 안이어도 출정 누적 추가 정기가 상한을 넘는 배치는 물약 소비까지 포함해 전부 거절한다 </summary>
        [PostgresFact]
        public async Task Append_AccrualPastSessionCap_RejectsWholeBatchWithoutEffects()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid participant = session.Participants[0].ParticipantId;

            OperationResult first = await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Essence(1, participant, SettlementSanityLimits.MAX_SESSION_ESSENCE)
            });
            OperationResult over = await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Potion(2, participant),
                SessionEventTests.Essence(3, participant, 1)
            });

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Status);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, over.Status);
            Assert.Equal(SettlementSanityLimits.REJECT_REASON, over.ReasonCode);
            Assert.Equal(SettlementSanityLimits.MAX_SESSION_ESSENCE, await environment.ScalarAsync(
                "SELECT essence_accrued FROM session_participants WHERE participant_id = @Id;", new { Id = participant }));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT last_event_seq FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
            WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
            Assert.Equal(POTIONS, wallet.Potions);
            Assert.Equal(POTIONS_RESERVED, wallet.PotionsReserved);
        }

        // 저장된 종료 정산안이 출정에 없는 참가자를 가리키게 만든다. 정산 효과가 매번 예외로 끝나는 세션이다
        private static async Task CorruptIntoSettlingAsync(SessionTestEnvironment environment, Guid sessionId)
        {
            Guid settlementId = Guid.NewGuid();
            string payload = "{\"endReason\":0,\"settlementOperationId\":\"" + settlementId.ToString("D")
                + "\",\"grants\":[{\"participantId\":\"" + Guid.NewGuid().ToString("D") + "\",\"essence\":1,\"items\":[]}]}";
            await environment.ExecuteAsync(
                """
                INSERT INTO session_events (session_id, event_seq, event_id, event_kind, payload, operation_id)
                VALUES (@SessionId, 1, gen_random_uuid(), @Ended, CAST(@Payload AS jsonb), gen_random_uuid());
                UPDATE game_sessions SET state = 'Settling', last_event_seq = 1, ended_event_seq = 1,
                    settlement_operation_id = @SettlementId, end_reason = 0,
                    settling_at = clock_timestamp() - interval '2 minutes'
                WHERE session_id = @SessionId;
                """, new { SessionId = sessionId, Ended = GameSessionCodes.EventKind.SESSION_ENDED, Payload = payload,
                    SettlementId = settlementId });
        }
    }
}
