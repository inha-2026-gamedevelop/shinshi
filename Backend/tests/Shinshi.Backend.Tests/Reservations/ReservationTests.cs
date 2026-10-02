// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Data.Reservations;

namespace Shinshi.Backend.Tests.Reservations
{
    /// <summary> 출정 예약의 전원 원자성, 멱등, 잠금 순서, 유휴 던전 배정을 실제 PostgreSQL에서 검사한다 </summary>
    public sealed class ReservationTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long TICKETS          = 2;
        private const long POTIONS          = 5;
        private const long POTIONS_RESERVED = 3;
        private const int  TIMEOUT_SECONDS  = 60;
        private const int  OVERLAP_ROUNDS   = 12;
        private const int  REPLAY_CALLS     = 4;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 보스전 예약은 티켓 1장과 지참 물약을 예약량으로만 잡고 총량과 원장은 바꾸지 않는다 </summary>
        [PostgresFact]
        public async Task Reserve_FundedBossParty_ReservesWithoutChangingTotals()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer first = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer second = await environment.CreatePlayerAsync(TICKETS, POTIONS);

            ReservedSessionView session = await environment.ReserveAsync(GameSessionCodes.Mode.BOSS,
                new[] { first, second }, POTIONS_RESERVED);

            Assert.Equal(environment.DungeonIds[0], session.InstanceId);
            Assert.Equal(2, session.Participants.Count);
            foreach (TestPlayer player in new[] { first, second })
            {
                WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
                Assert.Equal(TICKETS, wallet.Tickets);
                Assert.Equal(1L, wallet.TicketsReserved);
                Assert.Equal(POTIONS, wallet.Potions);
                Assert.Equal(POTIONS_RESERVED, wallet.PotionsReserved);
            }
            Assert.Equal("Reserved", await environment.TextAsync(
                "SELECT state FROM server_instances WHERE instance_id = @Id;", new { Id = session.InstanceId }));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM test_economy_ledger;"));
        }

        /// <summary> 한 명이라도 티켓이 없으면 아무도 예약하지 않고 던전도 배정하지 않는다 </summary>
        [PostgresFact]
        public async Task Reserve_OneMemberWithoutTicket_ReservesNothing()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer[] players =
            {
                await environment.CreatePlayerAsync(TICKETS, POTIONS),
                await environment.CreatePlayerAsync(0, POTIONS),
                await environment.CreatePlayerAsync(TICKETS, POTIONS)
            };

            ReservationOutcome outcome = await environment.Reservations.ReserveAsync(environment.CreateReservation(
                Guid.NewGuid(), GameSessionCodes.Mode.BOSS, players, POTIONS_RESERVED));

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, outcome.Result.Status);
            Assert.Equal(ContractCodes.RejectReason.INSUFFICIENT_TICKET, outcome.Result.ReasonCode);
            Assert.Null(outcome.Session);
            await AssertNothingReservedAsync(environment, players);
        }

        /// <summary> 티켓이 충분해도 한 명의 물약이 부족하면 티켓 예약까지 모두 되돌린다 </summary>
        [PostgresFact]
        public async Task Reserve_OneMemberShortOfPotions_ReservesNoTickets()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer[] players =
            {
                await environment.CreatePlayerAsync(TICKETS, POTIONS),
                await environment.CreatePlayerAsync(TICKETS, POTIONS_RESERVED - 1)
            };

            ReservationOutcome outcome = await environment.Reservations.ReserveAsync(environment.CreateReservation(
                Guid.NewGuid(), GameSessionCodes.Mode.BOSS, players, POTIONS_RESERVED));

            Assert.Equal(ContractCodes.RejectReason.INSUFFICIENT_ITEM, outcome.Result.ReasonCode);
            await AssertNothingReservedAsync(environment, players);
        }

        /// <summary> 일반전은 티켓 없이도 예약되고 티켓 예약량을 바꾸지 않는다 </summary>
        [PostgresFact]
        public async Task Reserve_NormalModeWithoutTickets_ReservesPotionsOnly()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(0, POTIONS);

            await environment.ReserveAsync(GameSessionCodes.Mode.NORMAL, new[] { player }, POTIONS_RESERVED);

            WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
            Assert.Equal(0L, wallet.TicketsReserved);
            Assert.Equal(POTIONS_RESERVED, wallet.PotionsReserved);
        }

        /// <summary> 같은 operationId 재전송은 같은 세션을 돌려주고, 같은 ID의 다른 본문은 IdempotencyConflict다 </summary>
        [PostgresFact]
        public async Task Reserve_ReplayAndChangedBody_AppliesOnce()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            Guid operationId = Guid.NewGuid();
            ReservationInput input = environment.CreateReservation(operationId, GameSessionCodes.Mode.BOSS,
                new[] { player }, POTIONS_RESERVED);

            ReservationOutcome[] outcomes = await Task.WhenAll(Enumerable.Range(0, REPLAY_CALLS)
                .Select(_ => environment.Reservations.ReserveAsync(input)));
            ReservationOutcome changed = await environment.Reservations.ReserveAsync(environment.CreateReservation(
                operationId, GameSessionCodes.Mode.BOSS, new[] { player }, POTIONS_RESERVED - 1));

            Assert.All(outcomes, outcome =>
            {
                Assert.Equal(ContractCodes.OperationStatus.COMMITTED, outcome.Result.Status);
                Assert.Equal(outcomes[0].Result.ReceiptId, outcome.Result.ReceiptId);
            });
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, changed.Result.ReasonCode);
            Assert.Equal(1L, await environment.ScalarAsync("SELECT count(*) FROM game_sessions;"));
            WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
            Assert.Equal(1L, wallet.TicketsReserved);
            Assert.Equal(POTIONS_RESERVED, wallet.PotionsReserved);
        }

        /// <summary> 참가자가 겹치는 두 파티를 동시에 예약해도 데드락 없이 계정마다 한 출정만 잡힌다 </summary>
        [PostgresFact]
        public async Task Reserve_ConcurrentOverlappingParties_NoDeadlockAndOneWinner()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(OVERLAP_ROUNDS);
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));

            for (int round = 0; round < OVERLAP_ROUNDS; ++round)
            {
                TestPlayer left = await environment.CreatePlayerAsync(TICKETS, POTIONS);
                TestPlayer shared = await environment.CreatePlayerAsync(TICKETS, POTIONS);
                TestPlayer right = await environment.CreatePlayerAsync(TICKETS, POTIONS);
                TaskCompletionSource<bool> start = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                // 요청 순서를 서로 반대로 둔다. 잠금을 요청 순서로 잡으면 여기서 데드락이 난다
                async Task<ReservationOutcome> ReserveAfterStartAsync(TestPlayer[] party)
                {
                    await start.Task.WaitAsync(timeout.Token);
                    return await environment.Reservations.ReserveAsync(environment.CreateReservation(Guid.NewGuid(),
                        GameSessionCodes.Mode.BOSS, party, POTIONS_RESERVED), timeout.Token);
                }

                Task<ReservationOutcome> first = ReserveAfterStartAsync(new[] { left, shared });
                Task<ReservationOutcome> second = ReserveAfterStartAsync(new[] { right, shared });
                start.SetResult(true);
                ReservationOutcome[] outcomes = await Task.WhenAll(first, second).WaitAsync(timeout.Token);

                Assert.Single(outcomes, outcome => outcome.Result.Status == ContractCodes.OperationStatus.COMMITTED);
                Assert.Single(outcomes, outcome => outcome.Result.ReasonCode == ContractCodes.RejectReason.RESERVATION_CONFLICT);
                WalletSnapshot sharedWallet = await environment.ReadWalletAsync(shared.AccountId);
                Assert.Equal(1L, sharedWallet.TicketsReserved);
                Assert.Equal(POTIONS_RESERVED, sharedWallet.PotionsReserved);
            }

            Assert.Equal(0, environment.Store.Deadlocks);
        }

        /// <summary> 유휴 던전이 하나면 동시에 출정한 두 파티 중 하나만 배정받고 다른 하나는 아무것도 예약하지 않는다 </summary>
        [PostgresFact]
        public async Task Reserve_TwoPartiesOneIdleDungeon_OneGetsNoCapacity()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer first = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer second = await environment.CreatePlayerAsync(TICKETS, POTIONS);

            ReservationOutcome[] outcomes = await Task.WhenAll(
                environment.Reservations.ReserveAsync(environment.CreateReservation(Guid.NewGuid(),
                    GameSessionCodes.Mode.BOSS, new[] { first }, POTIONS_RESERVED)),
                environment.Reservations.ReserveAsync(environment.CreateReservation(Guid.NewGuid(),
                    GameSessionCodes.Mode.BOSS, new[] { second }, POTIONS_RESERVED)));

            Assert.Single(outcomes, outcome => outcome.Result.Status == ContractCodes.OperationStatus.COMMITTED);
            Assert.Single(outcomes, outcome => outcome.Result.ReasonCode == ContractCodes.RejectReason.NO_CAPACITY);
            long reservedTickets = (await environment.ReadWalletAsync(first.AccountId)).TicketsReserved
                + (await environment.ReadWalletAsync(second.AccountId)).TicketsReserved;
            Assert.Equal(1L, reservedTickets);
        }

        /// <summary> 마을 서버에 접속 임대가 없는 계정은 그 마을이 예약할 수 없다 </summary>
        [PostgresFact]
        public async Task Reserve_AccountNotLeasedToCallingTown_RejectsSessionMismatch()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            await environment.ExecuteAsync("DELETE FROM account_leases WHERE account_id = @AccountId;",
                new { player.AccountId });

            ReservationOutcome outcome = await environment.Reservations.ReserveAsync(environment.CreateReservation(
                Guid.NewGuid(), GameSessionCodes.Mode.BOSS, new[] { player }, POTIONS_RESERVED));

            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, outcome.Result.ReasonCode);
            await AssertNothingReservedAsync(environment, new[] { player });
        }

        /// <summary> 예약한 마을은 참가자 전원의 입장권을 받고, 던전은 토큰 교환 뒤 임대를 이관한다 </summary>
        [PostgresFact]
        public async Task JoinTicketsAndLeaseTransfer_ReservedSession_IssuesPerParticipantAndMovesLease()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.ReserveAsync(GameSessionCodes.Mode.BOSS,
                new[] { player }, POTIONS_RESERVED);
            Guid participantId = session.Participants[0].ParticipantId;
            Guid newLeaseId = Guid.NewGuid();

            JoinTicketsOutcome tickets = await environment.Reservations.IssueJoinTicketsAsync(environment.TownId,
                session.SessionId);
            JoinTicketsOutcome otherTown = await environment.Reservations.IssueJoinTicketsAsync(Guid.NewGuid(),
                session.SessionId);
            LeaseTransferOutcome moved = await environment.Reservations.TransferParticipantLeaseAsync(
                session.InstanceId, session.SessionId, participantId, newLeaseId);
            LeaseTransferOutcome retried = await environment.Reservations.TransferParticipantLeaseAsync(
                session.InstanceId, session.SessionId, participantId, newLeaseId);

            Assert.Equal(ContractCodes.RejectReason.NONE, tickets.ReasonCode);
            JoinTicket ticket = Assert.Single(tickets.Tickets);
            Assert.Equal(participantId.ToString("D"), ticket.ParticipantId);
            Assert.Equal(session.InstanceId.ToString("D"), ticket.InstanceId);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, otherTown.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.NONE, moved.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.NONE, retried.ReasonCode);
            Assert.Equal(session.InstanceId.ToString("D"), await environment.TextAsync(
                "SELECT instance_id::text FROM account_leases WHERE account_id = @AccountId;", new { player.AccountId }));
        }

        private static async Task AssertNothingReservedAsync(SessionTestEnvironment environment, TestPlayer[] players)
        {
            foreach (TestPlayer player in players)
            {
                WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
                Assert.Equal(0L, wallet.TicketsReserved);
                Assert.Equal(0L, wallet.PotionsReserved);
            }
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM game_sessions;"));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM reservations;"));
            Assert.Equal("Idle", await environment.TextAsync(
                "SELECT state FROM server_instances WHERE instance_id = @Id;", new { Id = environment.DungeonIds[0] }));
        }
    }
}
