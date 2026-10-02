// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Auth;
using Shinshi.Data.Ledger;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Sessions
{
    /// <summary> 세션 경제 효과와 실제 공용 원장 연결, 발급 거절 결과를 PostgreSQL에서 검사한다 </summary>
    public sealed class PostgresAdapterIntegrationTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long INITIAL_TICKETS = 2;
        private const long INITIAL_POTIONS = 5;
        private const long RESERVED_POTIONS = 3;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task StartAsync_RealLedgerAdapter_CommitsTicketAndLedgerExactlyOnce()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(INITIAL_TICKETS, INITIAL_POTIONS);
            ReservedSessionView session = await environment.ReserveAsync(GameSessionCodes.Mode.BOSS,
                new[] { player }, RESERVED_POTIONS);
            PostgresGameSessionRepository repository = new PostgresGameSessionRepository(environment.DataSource,
                environment.Executor, new LedgerWriter(new PostgresEconomyLedgerWriter()));
            Guid operationId = Guid.NewGuid();
            Guid[] participants = session.Participants.Select(participant => participant.ParticipantId).ToArray();

            OperationResult result = await repository.StartAsync(session.InstanceId, session.SessionId, operationId, participants);
            OperationResult replay = await repository.StartAsync(session.InstanceId, session.SessionId, operationId, participants);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
            Assert.Equal(result.ReceiptId, replay.ReceiptId);
            Assert.Equal(INITIAL_TICKETS - 1, (await environment.ReadWalletAsync(player.AccountId)).Tickets);
            Assert.Equal(1L, await environment.ScalarAsync("SELECT count(*) FROM economy_ledger;"));
            Assert.Equal(-1L, await environment.ScalarAsync("SELECT delta FROM economy_ledger;"));
            Assert.Equal((long)ContractCodes.EconomyReason.SESSION_START_TICKET,
                await environment.ScalarAsync("SELECT reason FROM economy_ledger;"));
            Assert.Equal(EconomyAsset.TICKETS, await environment.TextAsync("SELECT asset_kind FROM economy_ledger;"));
            Assert.Equal(operationId.ToString("D"), await environment.TextAsync("SELECT operation_id::text FROM economy_ledger;"));
        }

        [PostgresFact]
        public async Task IssueJoinTicketsAsync_OneIssuerRejects_ReturnsReasonAndNoPartialTickets()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer[] players = new[]
            {
                await environment.CreatePlayerAsync(INITIAL_TICKETS, INITIAL_POTIONS),
                await environment.CreatePlayerAsync(INITIAL_TICKETS, INITIAL_POTIONS)
            };
            ReservedSessionView session = await environment.ReserveAsync(GameSessionCodes.Mode.BOSS,
                players, RESERVED_POTIONS);
            PartialRejectingIssuer issuer = new PartialRejectingIssuer();
            PostgresReservationRepository repository = new PostgresReservationRepository(environment.DataSource,
                environment.Executor, new PostgresAccountLeaseRepository(environment.DataSource), issuer);

            JoinTicketsOutcome outcome = await repository.IssueJoinTicketsAsync(environment.TownId, session.SessionId);

            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED, outcome.ReasonCode);
            Assert.Empty(outcome.Tickets);
            Assert.Equal(2, issuer.Calls);
            Assert.Equal("Reserved", await environment.TextAsync("SELECT state FROM game_sessions;"));
        }

        private sealed class PartialRejectingIssuer : IJoinTokenIssuer
        {
            private readonly FakeJoinTokenIssuer _success = new FakeJoinTokenIssuer();
            public int Calls { get; private set; }

            public Task<JoinTicket> IssueDungeonTicketAsync(DungeonJoinGrant grant, CancellationToken cancellationToken)
            {
                ++Calls;
                if (Calls > 1)
                {
                    throw new JoinTokenIssuanceException(ContractCodes.RejectReason.TOKEN_REVOKED);
                }
                return _success.IssueDungeonTicketAsync(grant, cancellationToken);
            }
        }
    }
}