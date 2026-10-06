// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Sessions
{
    /// <summary> 지참 물약 사용 되돌림(POTION_USE_REVERTED)과 보스전 참가자별 사용 상한을 실제 PostgreSQL에서 검사한다 </summary>
    public sealed class PotionRevertTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long TICKETS          = 2;
        private const long POTIONS          = 12;
        private const long POTIONS_RESERVED = 11;
        private const long ESSENCE_GRANT    = 100;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 저장된 사용을 되돌리면 consumed가 1 줄고 재고와 예약량이 1씩 돌아오며 원장 합계가 다시 0이다 </summary>
        [PostgresFact]
        public async Task Revert_StoredPotionUse_RestoresStockReservationAndLedger()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            (ReservedSessionView session, TestPlayer player, Guid participant) = await StartAsync(environment,
                GameSessionCodes.Mode.NORMAL);
            SessionEventInput used = SessionEventTests.Potion(1, participant);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                (await SessionEventTests.AppendAsync(environment, session, new[] { used })).Status);

            OperationResult reverted = await SessionEventTests.AppendAsync(environment, session,
                new[] { Revert(2, participant, used.EventId) });

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, reverted.Status);
            WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
            Assert.Equal(POTIONS, wallet.Potions);
            Assert.Equal(POTIONS_RESERVED, wallet.PotionsReserved);
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT consumed FROM reservations WHERE session_id = @Id AND asset_kind = 'Item';",
                new { Id = session.SessionId }));
            Assert.Equal(0L, await environment.LedgerSumAsync(player.AccountId, EconomyLedgerCodes.ASSET_ITEM));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM test_economy_ledger WHERE account_id = @Id AND reason = @Reason AND delta = 1;",
                new { Id = player.AccountId, Reason = EconomyLedgerCodes.REASON_POTION_RETURN }));
        }

        /// <summary> 같은 배치의 사용과 되돌림도 받는다. 원장은 사용과 되돌림 두 줄로 남는다 </summary>
        [PostgresFact]
        public async Task Revert_InSameBatchAfterUse_NetsToZero()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            (ReservedSessionView session, TestPlayer player, Guid participant) = await StartAsync(environment,
                GameSessionCodes.Mode.NORMAL);
            SessionEventInput used = SessionEventTests.Potion(1, participant);

            OperationResult result = await SessionEventTests.AppendAsync(environment, session,
                new[] { used, Revert(2, participant, used.EventId) });

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
            Assert.Equal(POTIONS, (await environment.ReadWalletAsync(player.AccountId)).Potions);
            Assert.Equal(2L, await environment.ScalarAsync(
                "SELECT count(*) FROM test_economy_ledger WHERE account_id = @Id;", new { Id = player.AccountId }));
        }

        /// <summary> 던전이 출정 종료 직전에 미적용 사용을 되돌리는 순서: [사용, 되돌림, 종료]를 한 배치로 받고 정산은 그 물약을
        /// 예약에서 풀어 재고로 남긴다. 종료 뒤에 온 배치는 SESSION_TERMINAL이라 던전은 종료 뒤 물약 이벤트를 보내면 안 된다 </summary>
        [PostgresFact]
        public async Task Revert_BeforeSessionEndedInSameBatch_ReturnsPotionAtSettlement()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            (ReservedSessionView session, TestPlayer player, Guid participant) = await StartAsync(environment,
                GameSessionCodes.Mode.BOSS);
            SessionEventInput used = SessionEventTests.Potion(1, participant);
            Guid settlementId = Guid.NewGuid();

            OperationResult ended = await SessionEventTests.AppendAsync(environment, session, new[]
            {
                used,
                Revert(2, participant, used.EventId),
                SessionEventTests.Ended(3, settlementId, new[] { SessionEventTests.Grant(participant, ESSENCE_GRANT) })
            });
            OperationResult late = await SessionEventTests.AppendAsync(environment, session,
                new[] { Revert(4, participant, used.EventId) });
            OperationResult settled = await environment.Settlement.SettleAsync(session.InstanceId, session.SessionId,
                settlementId);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, ended.Status);
            Assert.Equal(ContractCodes.RejectReason.SESSION_TERMINAL, late.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, settled.Status);
            WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
            Assert.Equal(POTIONS, wallet.Potions);
            Assert.Equal(0L, wallet.PotionsReserved);
            Assert.Equal(0L, await environment.LedgerSumAsync(player.AccountId, EconomyLedgerCodes.ASSET_ITEM));
        }

        /// <summary> 없는 원 이벤트, 다른 참가자의 사용, 이미 되돌린 사용은 배치 전체를 거절하고 아무것도 바꾸지 않는다 </summary>
        [PostgresFact]
        public async Task Revert_MissingForeignOrDuplicate_RejectsWholeBatch()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer first = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer second = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.NORMAL,
                new[] { first, second }, POTIONS_RESERVED);
            Guid firstParticipant = session.Participants.Single(row => row.AccountId == first.AccountId).ParticipantId;
            Guid secondParticipant = session.Participants.Single(row => row.AccountId == second.AccountId).ParticipantId;
            SessionEventInput used = SessionEventTests.Potion(1, firstParticipant);
            await SessionEventTests.AppendAsync(environment, session, new[] { used });
            await SessionEventTests.AppendAsync(environment, session, new[] { Revert(2, firstParticipant, used.EventId) });
            WalletSnapshot before = await environment.ReadWalletAsync(first.AccountId);

            OperationResult missing = await SessionEventTests.AppendAsync(environment, session,
                new[] { SessionEventTests.Loot(3), Revert(4, firstParticipant, Guid.NewGuid()) });
            OperationResult foreign = await SessionEventTests.AppendAsync(environment, session,
                new[] { Revert(3, secondParticipant, used.EventId) });
            OperationResult duplicate = await SessionEventTests.AppendAsync(environment, session,
                new[] { Revert(3, firstParticipant, used.EventId) });

            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, missing.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, foreign.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, duplicate.ReasonCode);
            Assert.Equal(2L, missing.CommittedRevision);
            Assert.Equal(2L, await environment.ScalarAsync("SELECT count(*) FROM session_events;"));
            WalletSnapshot after = await environment.ReadWalletAsync(first.AccountId);
            Assert.Equal(before.Potions, after.Potions);
            Assert.Equal(before.PotionsReserved, after.PotionsReserved);
            Assert.Equal(POTIONS, (await environment.ReadWalletAsync(second.AccountId)).Potions);
        }

        /// <summary> 되돌림만 있는 계정도 배치 선행 잠금에 들어간다. 작은 ID 계정(되돌림만)이 다른 트랜잭션에 잠겨 기다리는 동안
        /// 큰 ID 계정(사용)은 아직 잠그지 않았어야 한다(계정 ID 오름차순 잠금). 예전에는 큰 ID를 먼저 잡은 채 작은 ID를 기다렸다 </summary>
        [PostgresFact]
        public async Task Batch_RevertOnlyAccount_IsLockedInAscendingOrderBeforeOthers()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer first = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer second = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.NORMAL,
                new[] { first, second }, POTIONS_RESERVED);
            Guid small = (await environment.TextAsync(
                "SELECT account_id::text FROM accounts WHERE account_id = ANY(@Ids) ORDER BY account_id LIMIT 1;",
                new { Ids = new[] { first.AccountId, second.AccountId } })) == first.AccountId.ToString("D")
                ? first.AccountId : second.AccountId;
            Guid big = small == first.AccountId ? second.AccountId : first.AccountId;
            Guid smallParticipant = session.Participants.Single(row => row.AccountId == small).ParticipantId;
            Guid bigParticipant = session.Participants.Single(row => row.AccountId == big).ParticipantId;
            SessionEventInput used = SessionEventTests.Potion(1, smallParticipant);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                (await SessionEventTests.AppendAsync(environment, session, new[] { used })).Status);

            bool bigFree;
            Task<OperationResult> batch;
            await using (Npgsql.NpgsqlConnection holder = await environment.DataSource.OpenConnectionAsync())
            {
                await using Npgsql.NpgsqlTransaction holding = await holder.BeginTransactionAsync();
                await Dapper.SqlMapper.ExecuteAsync(holder, "SELECT 1 FROM accounts WHERE account_id = @Id FOR UPDATE;",
                    new { Id = small }, holding);
                batch = SessionEventTests.AppendAsync(environment, session,
                    new[] { SessionEventTests.Potion(2, bigParticipant), Revert(3, smallParticipant, used.EventId) });
                await WaitForLockWaiterAsync(environment);
                bigFree = await TryLockNowaitAsync(environment, big);
                await holding.RollbackAsync();
            }

            Assert.True(bigFree);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await batch).Status);
        }

        // 다른 연결이 행 잠금을 기다리기 시작할 때까지 짧게 본다. 정책 lock_timeout(2초) 안에 끝나야 한다
        private static async Task WaitForLockWaiterAsync(SessionTestEnvironment environment)
        {
            const int POLLS = 60;
            const int POLL_MS = 20;
            for (int i = 0; i < POLLS; ++i)
            {
                if (await environment.ScalarAsync("SELECT count(*) FROM pg_locks WHERE NOT granted;") > 0)
                {
                    return;
                }

                await Task.Delay(POLL_MS);
            }

            Assert.Fail("Event batch did not start waiting for the held account lock.");
        }

        private static async Task<bool> TryLockNowaitAsync(SessionTestEnvironment environment, Guid accountId)
        {
            await using Npgsql.NpgsqlConnection connection = await environment.DataSource.OpenConnectionAsync();
            await using Npgsql.NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            try
            {
                await Dapper.SqlMapper.ExecuteAsync(connection,
                    "SELECT 1 FROM accounts WHERE account_id = @Id FOR UPDATE NOWAIT;", new { Id = accountId }, transaction);
                return true;
            }
            catch (Npgsql.PostgresException exception) when (exception.SqlState == "55P03")
            {
                return false;
            }
            finally
            {
                await transaction.RollbackAsync();
            }
        }

        /// <summary> 한 배치 안에서 같은 원 이벤트를 두 번 되돌리거나 되돌림을 되돌리는 것은 형식 오류다 </summary>
        [Fact]
        public void Validator_DuplicateOrNonPotionTarget_IsSessionMismatch()
        {
            Guid participant = Guid.NewGuid();
            SessionEventInput used = SessionEventTests.Potion(1, participant);
            SessionEventInput loot = SessionEventTests.Loot(1);

            int duplicate = SessionEventValidator.GetRejectReason(new[]
            {
                used, Revert(2, participant, used.EventId), Revert(3, participant, used.EventId)
            });
            int notPotion = SessionEventValidator.GetRejectReason(new[] { loot, Revert(2, participant, loot.EventId) });
            int noTarget = SessionEventValidator.GetRejectReason(new[] { Revert(1, participant, Guid.Empty) });

            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, duplicate);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, notPotion);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, noTarget);
        }

        /// <summary> 보스전 참가자는 9번까지 쓰고 10번째는 POTION_LIMIT로 배치 전체를 거절한다. 되돌린 사용은 세지 않는다 </summary>
        [PostgresFact]
        public async Task BossPotion_TenthUse_RejectsUntilOneIsReverted()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            (ReservedSessionView session, TestPlayer player, Guid participant) = await StartAsync(environment,
                GameSessionCodes.Mode.BOSS);
            List<SessionEventInput> nine = Enumerable.Range(1, (int)PostgresGameSessionRepository.BOSS_POTION_USE_LIMIT)
                .Select(seq => SessionEventTests.Potion(seq, participant)).ToList();
            long next = PostgresGameSessionRepository.BOSS_POTION_USE_LIMIT + 1;

            OperationResult allowed = await SessionEventTests.AppendAsync(environment, session, nine);
            OperationResult tenth = await SessionEventTests.AppendAsync(environment, session,
                new[] { SessionEventTests.Potion(next, participant) });
            WalletSnapshot afterLimit = await environment.ReadWalletAsync(player.AccountId);
            OperationResult reverted = await SessionEventTests.AppendAsync(environment, session,
                new[] { Revert(next, participant, nine[0].EventId) });
            OperationResult reused = await SessionEventTests.AppendAsync(environment, session,
                new[] { SessionEventTests.Potion(next + 1, participant) });

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, allowed.Status);
            Assert.Equal(ContractCodes.RejectReason.POTION_LIMIT, tenth.ReasonCode);
            Assert.Equal(POTIONS - PostgresGameSessionRepository.BOSS_POTION_USE_LIMIT, afterLimit.Potions);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, reverted.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, reused.Status);
            Assert.Equal(POTIONS - PostgresGameSessionRepository.BOSS_POTION_USE_LIMIT,
                (await environment.ReadWalletAsync(player.AccountId)).Potions);
        }

        /// <summary> 일반전에는 보스 상한을 적용하지 않는다(지참 예약량만 상한이다) </summary>
        [PostgresFact]
        public async Task NormalPotion_TenthUse_IsAllowedWithinReservation()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            (ReservedSessionView session, TestPlayer _, Guid participant) = await StartAsync(environment,
                GameSessionCodes.Mode.NORMAL);
            List<SessionEventInput> ten = Enumerable.Range(1, (int)PostgresGameSessionRepository.BOSS_POTION_USE_LIMIT + 1)
                .Select(seq => SessionEventTests.Potion(seq, participant)).ToList();

            OperationResult result = await SessionEventTests.AppendAsync(environment, session, ten);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
        }

        private static async Task<(ReservedSessionView Session, TestPlayer Player, Guid Participant)> StartAsync(
            SessionTestEnvironment environment, int mode)
        {
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(mode, new[] { player }, POTIONS_RESERVED);
            return (session, player, session.Participants[0].ParticipantId);
        }

        private static SessionEventInput Revert(long seq, Guid participantId, Guid revertsEventId)
        {
            return new SessionEventInput
            {
                EventSeq = seq,
                EventId = Guid.NewGuid(),
                EventKind = GameSessionCodes.EventKind.POTION_USE_REVERTED,
                ParticipantId = participantId,
                RevertsEventId = revertsEventId
            };
        }
    }
}
