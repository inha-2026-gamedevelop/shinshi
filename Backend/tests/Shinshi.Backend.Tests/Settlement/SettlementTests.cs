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
    /// <summary> SessionEnded 정산의 1회 적용, 장애 취소 보상, 소실 프로세스 복구를 실제 PostgreSQL에서 검사한다 </summary>
    public sealed class SettlementTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long   TICKETS          = 3;
        private const long   POTIONS          = 6;
        private const long   POTIONS_RESERVED = 4;
        private const long   ESSENCE_GRANT    = 100;
        private const long   LOOT_QUANTITY    = 2;
        private const string LOOT_ID          = "item_boss_material_common";
        private const long   ODD_ACCRUED      = 101;
        private const long   EVEN_ACCRUED     = 40;

        // 장애 취소는 추가 정기 전액이라 홀수 적립도 내림 없이 그대로 지급한다
        private const long   ODD_PAID         = ODD_ACCRUED;
        private const long   EVEN_PAID        = EVEN_ACCRUED;
        private const int    CONCURRENT_CALLS = 4;
        private const int    RECOVERY_BATCH   = 8;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 같은 operationId 정산을 동시에 여러 번 불러도 정산안은 한 번만 적용되고 미사용 물약 예약이 풀린다 </summary>
        [PostgresFact]
        public async Task Settle_SameOperationRepeated_AppliesOnce()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid participant = session.Participants[0].ParticipantId;
            Guid settlementId = Guid.NewGuid();
            await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Potion(1, participant),
                SessionEventTests.Ended(2, settlementId, new[]
                {
                    SessionEventTests.Grant(participant, ESSENCE_GRANT, new SettlementItemInput
                    {
                        DefinitionId = LOOT_ID, BindingKind = SessionTestEnvironment.BINDING, Quantity = LOOT_QUANTITY
                    })
                })
            });

            OperationResult[] results = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_CALLS).Select(_ =>
                environment.Settlement.SettleAsync(session.InstanceId, session.SessionId, settlementId)));
            OperationResult other = await environment.Settlement.SettleAsync(session.InstanceId, session.SessionId,
                Guid.NewGuid());

            Assert.All(results, result =>
            {
                Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
                Assert.Equal(results[0].ReceiptId, result.ReceiptId);
            });
            Assert.Equal(ContractCodes.RejectReason.ALREADY_SETTLED, other.ReasonCode);
            WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
            Assert.Equal(ESSENCE_GRANT, wallet.Essence);
            Assert.Equal(POTIONS - 1, wallet.Potions);
            Assert.Equal(0L, wallet.PotionsReserved);
            Assert.Equal(LOOT_QUANTITY, await environment.ScalarAsync(
                "SELECT quantity FROM account_items WHERE account_id = @AccountId AND definition_id = @LootId;",
                new { player.AccountId, LootId = LOOT_ID }));
            await AssertLedgerMatchesBalancesAsync(environment, player, TICKETS, POTIONS);

            // 보스 성공은 정산 뒤 경매 단계라 서버가 Active로 남고, 경매 finish가 Draining으로 돌려보낸다
            Assert.Equal(GameSessionStates.INSTANCE_ACTIVE, await environment.TextAsync(
                "SELECT state FROM server_instances WHERE instance_id = @Id;", new { Id = session.InstanceId }));
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                (await environment.FinishAuctionAsync(session)).Status);
            Assert.Equal(GameSessionStates.INSTANCE_DRAINING, await environment.TextAsync(
                "SELECT state FROM server_instances WHERE instance_id = @Id;", new { Id = session.InstanceId }));
        }

        /// <summary> 종료 이벤트에 기록되지 않은 operationId로는 정산할 수 없다 </summary>
        [PostgresFact]
        public async Task Settle_UnrecordedOperationBeforeSettlement_RejectsSessionMismatch()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid participant = session.Participants[0].ParticipantId;
            await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Ended(1, Guid.NewGuid(), new[] { SessionEventTests.Grant(participant, ESSENCE_GRANT) })
            });

            OperationResult result = await environment.Settlement.SettleAsync(session.InstanceId, session.SessionId,
                Guid.NewGuid());

            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, result.ReasonCode);
            Assert.Equal(0L, (await environment.ReadWalletAsync(player.AccountId)).Essence);
        }

        /// <summary> 종료 이벤트 없이 프로세스가 사라지면 티켓과 사용 물약을 복구하고 쌓인 추가 정기를 전액 한 번만 지급한다 </summary>
        [PostgresFact]
        public async Task ServiceCancel_LostInstance_RefundsAndGrantsFullEssenceOnce()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer odd = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer even = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { odd, even },
                POTIONS_RESERVED);
            Guid oddParticipant = session.Participants.Single(row => row.AccountId == odd.AccountId).ParticipantId;
            Guid evenParticipant = session.Participants.Single(row => row.AccountId == even.AccountId).ParticipantId;
            await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Essence(1, oddParticipant, ODD_ACCRUED),
                SessionEventTests.Essence(2, evenParticipant, EVEN_ACCRUED),
                SessionEventTests.Potion(3, oddParticipant),
                SessionEventTests.Potion(4, oddParticipant)
            });

            OperationResult alive = (await environment.Settlement.ServiceCancelAsync(session.SessionId))!;
            await environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '2 minutes' WHERE instance_id = @Id;",
                new { Id = session.InstanceId });
            SessionRecoveryResult recovered = await environment.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);
            OperationResult replay = (await environment.Settlement.ServiceCancelAsync(session.SessionId))!;
            SessionRecoveryResult again = await environment.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);

            Assert.Equal(ContractCodes.RejectReason.WRONG_PHASE, alive.ReasonCode);
            Assert.Equal(1, recovered.Cancelled);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, replay.Status);
            Assert.Equal(0, again.Cancelled + again.Settled);
            WalletSnapshot oddWallet = await environment.ReadWalletAsync(odd.AccountId);
            WalletSnapshot evenWallet = await environment.ReadWalletAsync(even.AccountId);
            Assert.Equal(ODD_PAID, oddWallet.Essence);
            Assert.Equal(EVEN_PAID, evenWallet.Essence);
            foreach (WalletSnapshot wallet in new[] { oddWallet, evenWallet })
            {
                Assert.Equal(TICKETS, wallet.Tickets);
                Assert.Equal(0L, wallet.TicketsReserved);
                Assert.Equal(POTIONS, wallet.Potions);
                Assert.Equal(0L, wallet.PotionsReserved);
            }
            await AssertLedgerMatchesBalancesAsync(environment, odd, TICKETS, POTIONS);
            await AssertLedgerMatchesBalancesAsync(environment, even, TICKETS, POTIONS);
            Assert.Equal("ServiceCancelled", await environment.TextAsync(
                "SELECT result FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
        }

        /// <summary> 종료 이벤트 저장 뒤 프로세스가 사라지면 복구 작업이 같은 operationId로 정산하고, 늦게 온 던전 재시도도 같은 영수증을 받는다 </summary>
        [PostgresFact]
        public async Task Recover_SettlingSessionOfLostInstance_SettlesWithStoredOperation()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid participant = session.Participants[0].ParticipantId;
            Guid settlementId = Guid.NewGuid();
            await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Essence(1, participant, ODD_ACCRUED),
                SessionEventTests.Ended(2, settlementId, new[] { SessionEventTests.Grant(participant, ESSENCE_GRANT) })
            });
            await environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '2 minutes' WHERE instance_id = @Id;",
                new { Id = session.InstanceId });

            SessionRecoveryResult recovered = await environment.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);
            OperationResult late = await environment.Settlement.SettleAsync(session.InstanceId, session.SessionId,
                settlementId);
            OperationResult cancel = (await environment.Settlement.ServiceCancelAsync(session.SessionId))!;

            Assert.Equal(1, recovered.Settled);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, late.Status);
            Assert.Equal(ContractCodes.RejectReason.ALREADY_SETTLED, cancel.ReasonCode);
            Assert.Equal(ESSENCE_GRANT, (await environment.ReadWalletAsync(player.AccountId)).Essence);
            await AssertLedgerMatchesBalancesAsync(environment, player, TICKETS, POTIONS);
        }

        private static async Task AssertLedgerMatchesBalancesAsync(SessionTestEnvironment environment, TestPlayer player,
            long initialTickets, long initialPotions)
        {
            // 시드 잔액은 원장 밖에서 넣었으므로 시드 이후 변화량과 원장 합계를 비교한다
            WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
            Assert.Equal(wallet.Tickets - initialTickets,
                await environment.LedgerSumAsync(player.AccountId, EconomyLedgerCodes.ASSET_TICKET));
            Assert.Equal(wallet.Essence, await environment.LedgerSumAsync(player.AccountId, EconomyLedgerCodes.ASSET_ESSENCE));
            long items = await environment.ScalarAsync(
                "SELECT COALESCE(sum(quantity), 0)::bigint FROM account_items WHERE account_id = @AccountId;",
                new { player.AccountId });
            Assert.Equal(items - initialPotions,
                await environment.LedgerSumAsync(player.AccountId, EconomyLedgerCodes.ASSET_ITEM));
        }
    }
}
