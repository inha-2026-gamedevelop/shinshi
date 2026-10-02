// System
using System;
using System.Linq;
using System.Threading;
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
    /// <summary> /start의 티켓 소비, 만료 해제, 취소가 Reserved 조건부 갱신으로 한쪽만 이기는지 검사한다 </summary>
    public sealed class SessionStartTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long   TICKETS          = 3;
        private const long   POTIONS          = 5;
        private const long   POTIONS_RESERVED = 2;
        private const int    RACE_ROUNDS      = 12;
        private const int    RACE_DELAY_MS    = 40;
        private const double MS_PER_SECOND    = 1000.0;
        private const int    RELEASE_BATCH    = 8;
        private const int    TIMEOUT_SECONDS  = 60;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 시작은 전원 티켓을 한 번 소비하고 원장 합계가 잔액 변화와 같다. 재전송은 같은 영수증, 새 ID는 거절이다 </summary>
        [PostgresFact]
        public async Task Start_AllReady_ConsumesTicketsOnceWithLedger()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer[] players =
            {
                await environment.CreatePlayerAsync(TICKETS, POTIONS),
                await environment.CreatePlayerAsync(TICKETS, POTIONS)
            };
            ReservedSessionView session = await environment.ReserveAsync(GameSessionCodes.Mode.BOSS, players,
                POTIONS_RESERVED);
            Guid[] ready = session.Participants.Select(participant => participant.ParticipantId).ToArray();
            Guid operationId = Guid.NewGuid();

            OperationResult[] starts = await Task.WhenAll(Enumerable.Range(0, players.Length + 1).Select(_ =>
                environment.Sessions.StartAsync(session.InstanceId, session.SessionId, operationId, ready)));
            OperationResult other = await environment.Sessions.StartAsync(session.InstanceId, session.SessionId,
                Guid.NewGuid(), ready);

            Assert.All(starts, start =>
            {
                Assert.Equal(ContractCodes.OperationStatus.COMMITTED, start.Status);
                Assert.Equal(starts[0].ReceiptId, start.ReceiptId);
            });
            Assert.Equal(ContractCodes.RejectReason.WRONG_PHASE, other.ReasonCode);
            foreach (TestPlayer player in players)
            {
                WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
                Assert.Equal(TICKETS - 1, wallet.Tickets);
                Assert.Equal(0L, wallet.TicketsReserved);
                Assert.Equal(POTIONS_RESERVED, wallet.PotionsReserved);
                Assert.Equal(-1L, await environment.LedgerSumAsync(player.AccountId, EconomyLedgerCodes.ASSET_TICKET));
            }
            Assert.Equal("Active", await environment.TextAsync(
                "SELECT state FROM server_instances WHERE instance_id = @Id;", new { Id = session.InstanceId }));
        }

        /// <summary> 준비 증거에 참가자가 빠지면 시작하지 않고 아무것도 소비하지 않는다 </summary>
        [PostgresFact]
        public async Task Start_MissingParticipant_RejectsWithoutConsuming()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer[] players =
            {
                await environment.CreatePlayerAsync(TICKETS, POTIONS),
                await environment.CreatePlayerAsync(TICKETS, POTIONS)
            };
            ReservedSessionView session = await environment.ReserveAsync(GameSessionCodes.Mode.BOSS, players,
                POTIONS_RESERVED);

            OperationResult result = await environment.Sessions.StartAsync(session.InstanceId, session.SessionId,
                Guid.NewGuid(), new[] { session.Participants[0].ParticipantId });

            Assert.Equal(ContractCodes.RejectReason.NOT_ALL_READY, result.ReasonCode);
            Assert.Equal("Reserved", await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
            foreach (TestPlayer player in players)
            {
                Assert.Equal(TICKETS, (await environment.ReadWalletAsync(player.AccountId)).Tickets);
            }
        }

        /// <summary> 만료 뒤 /start는 거절되고 만료 해제가 티켓, 물약 예약과 던전을 한 번 돌려준다 </summary>
        [PostgresFact]
        public async Task Start_AfterExpiry_RejectedAndReleaseFreesEverything()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.ReserveAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            await ExpireAsync(environment, session.SessionId);

            OperationResult start = await environment.Sessions.StartAsync(session.InstanceId, session.SessionId,
                Guid.NewGuid(), new[] { session.Participants[0].ParticipantId });
            int released = await environment.Reservations.ReleaseExpiredAsync(RELEASE_BATCH);
            int releasedAgain = await environment.Reservations.ReleaseExpiredAsync(RELEASE_BATCH);
            OperationResult late = await environment.Sessions.StartAsync(session.InstanceId, session.SessionId,
                Guid.NewGuid(), new[] { session.Participants[0].ParticipantId });

            Assert.Equal(ContractCodes.RejectReason.RESERVATION_CONFLICT, start.ReasonCode);
            Assert.Equal(1, released);
            Assert.Equal(0, releasedAgain);
            Assert.Equal(ContractCodes.RejectReason.RESERVATION_CONFLICT, late.ReasonCode);
            WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
            Assert.Equal(TICKETS, wallet.Tickets);
            Assert.Equal(0L, wallet.TicketsReserved);
            Assert.Equal(0L, wallet.PotionsReserved);
            // 예약을 맡았던 boot는 다시 배정하지 않는다. 던전은 Draining을 보고 새 bootId로 다시 등록한다
            Assert.Equal(GameSessionStates.INSTANCE_DRAINING, await environment.TextAsync(
                "SELECT state FROM server_instances WHERE instance_id = @Id;", new { Id = session.InstanceId }));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM test_economy_ledger;"));
            await environment.RebootDungeonAsync(session.InstanceId);

            // 해제된 계정은 바로 다시 출정할 수 있다
            await environment.ReserveAsync(GameSessionCodes.Mode.BOSS, new[] { player }, POTIONS_RESERVED);
        }

        /// <summary> 만료 시각 경계에서 /start와 만료 해제를 동시에 실행해도 둘 중 하나만 이기고 잔액이 어긋나지 않는다 </summary>
        [PostgresFact]
        public async Task StartAndExpiryRelease_Concurrent_OnlyOneWins()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(RACE_ROUNDS);
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));

            for (int round = 0; round < RACE_ROUNDS; ++round)
            {
                TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
                ReservedSessionView session = await environment.ReserveAsync(GameSessionCodes.Mode.BOSS,
                    new[] { player }, POTIONS_RESERVED);
                await environment.ExecuteAsync(
                    "UPDATE game_sessions SET expires_at = clock_timestamp() + make_interval(secs => @Seconds) WHERE session_id = @Id;",
                    new { Id = session.SessionId, Seconds = RACE_DELAY_MS / MS_PER_SECOND });
                await Task.Delay(RACE_DELAY_MS, timeout.Token);

                Task<OperationResult> start = environment.Sessions.StartAsync(session.InstanceId, session.SessionId,
                    Guid.NewGuid(), new[] { session.Participants[0].ParticipantId }, timeout.Token);
                Task<int> release = environment.Reservations.ReleaseExpiredAsync(RELEASE_BATCH, timeout.Token);
                await Task.WhenAll(start, release).WaitAsync(timeout.Token);
                bool started = (await start).Status == ContractCodes.OperationStatus.COMMITTED;
                int releasedCount = await release;

                // 경계 직전이면 둘 다 아무것도 못 할 수 있다. 그러면 다음 해제가 끝낸다. 둘 다 이기는 경우만 없어야 한다
                Assert.False(started && (releasedCount > 0));
                if (!started)
                {
                    await environment.Reservations.ReleaseExpiredAsync(RELEASE_BATCH, timeout.Token);
                }

                string state = await environment.TextAsync("SELECT state FROM game_sessions WHERE session_id = @Id;",
                    new { Id = session.SessionId });
                WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
                Assert.Equal(started ? "Running" : "Closed", state);
                Assert.Equal(started ? TICKETS - 1 : TICKETS, wallet.Tickets);
                Assert.Equal(0L, wallet.TicketsReserved);
                Assert.Equal(started ? POTIONS_RESERVED : 0L, wallet.PotionsReserved);
                Assert.Equal(wallet.Tickets - TICKETS,
                    await environment.LedgerSumAsync(player.AccountId, EconomyLedgerCodes.ASSET_TICKET));
            }

            Assert.Equal(0, environment.Store.Deadlocks);
        }

        /// <summary> 시작한 출정은 만료 해제 대상이 아니고 마을의 취소도 단계 오류다 </summary>
        [PostgresFact]
        public async Task ReleaseAndCancel_AfterStart_DoNothing()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            await environment.ExecuteAsync(
                "UPDATE game_sessions SET expires_at = clock_timestamp() - interval '1 second' WHERE session_id = @Id;",
                new { Id = session.SessionId });

            int released = await environment.Reservations.ReleaseExpiredAsync(RELEASE_BATCH);
            OperationResult cancel = await environment.Reservations.CancelAsync(environment.TownId, session.SessionId,
                Guid.NewGuid());

            Assert.Equal(0, released);
            Assert.Equal(ContractCodes.RejectReason.WRONG_PHASE, cancel.ReasonCode);
            Assert.Equal(TICKETS - 1, (await environment.ReadWalletAsync(player.AccountId)).Tickets);
        }

        /// <summary> 예약한 마을만 취소할 수 있고 같은 ID 재전송은 같은 결과다 </summary>
        [PostgresFact]
        public async Task Cancel_ReservedByTown_ReleasesOnce()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.ReserveAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid operationId = Guid.NewGuid();

            OperationResult stranger = await environment.Reservations.CancelAsync(Guid.NewGuid(), session.SessionId,
                Guid.NewGuid());
            OperationResult cancel = await environment.Reservations.CancelAsync(environment.TownId, session.SessionId,
                operationId);
            OperationResult replay = await environment.Reservations.CancelAsync(environment.TownId, session.SessionId,
                operationId);

            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, stranger.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, cancel.Status);
            Assert.Equal(cancel.ReceiptId, replay.ReceiptId);
            WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
            Assert.Equal(0L, wallet.TicketsReserved);
            Assert.Equal(0L, wallet.PotionsReserved);
        }

        private static Task ExpireAsync(SessionTestEnvironment environment, Guid sessionId)
        {
            return environment.ExecuteAsync(
                "UPDATE game_sessions SET expires_at = clock_timestamp() - interval '1 second' WHERE session_id = @Id;",
                new { Id = sessionId });
        }
    }
}
