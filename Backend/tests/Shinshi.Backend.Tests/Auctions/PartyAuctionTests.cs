// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Auctions;
using Shinshi.Data.Ledger;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Auctions
{
    /// <summary> 보스 성공 정산 뒤 파티 경매의 예약, 낙찰, 분배, 소각, 단독 구매, 소유 검증, 복구를 실제 PostgreSQL에서 검사한다 </summary>
    public sealed class PartyAuctionTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long   TICKETS          = 3;
        private const long   POTIONS          = 2;
        private const long   POTIONS_RESERVED = 1;
        private const long   ESSENCE_GRANT    = 10;
        private const long   CURRENCY_GRANT   = 500;
        private const long   LOW_CURRENCY     = 100;
        private const string MATERIAL_ID      = "item_boss_material_rare";
        private const int    RECOVERY_BATCH   = 8;

        private const long   FIRST_BID        = 100;
        private const long   WINNING_BID      = 151;
        private const long   RAISED_BID       = 110;
        private const long   STALE_BID        = 300;

        // 151을 두 명에게 나누면 75씩, 1은 소각
        private const long   PAYOUT_EACH      = 75;
        private const long   BURNED           = 1;

        // 낙찰자 엽전 차감, 재료 지급, 분배 두 줄
        private const long   SOLD_LEDGER_LINES = 4;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 경쟁 입찰: 새 최고가가 이전 예약을 풀고, 낙찰자는 예약금을 소비해 재료를 받으며,
        /// 자격 ∩ 종료 접속 - 낙찰자에게 내림 분배하고 나머지는 소각한다. finish가 출정을 닫는다 </summary>
        [PostgresFact]
        public async Task Bidding_HighestWins_DistributesFloorAndBurnsRemainder()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            AuctionSession setup = await CreateAuctionSessionAsync(environment, 3, CURRENCY_GRANT);
            Guid[] ids = setup.ParticipantIds;
            Guid auction = Guid.NewGuid();

            // 테스트 환경의 원장은 별도 표에 쓰므로 실제 economy_ledger를 보려고 마감만 실제 원장 기록기로 한다
            PostgresPartyAuctionRepository realLedger = new PostgresPartyAuctionRepository(environment.DataSource,
                environment.Executor, new LedgerWriter(new PostgresEconomyLedgerWriter()));

            OperationResult opened = await OpenAsync(environment, setup, auction, ids);
            OperationResult first = await BidAsync(environment, setup, auction, ids[0], FIRST_BID, 0);
            OperationResult second = await BidAsync(environment, setup, auction, ids[1], WINNING_BID, 1);
            OperationResult tooLow = await BidAsync(environment, setup, auction, ids[2],
                WINNING_BID + PartyAuctionRules.MIN_INCREMENT - 1, 2);
            OperationResult stale = await BidAsync(environment, setup, auction, ids[2], STALE_BID, 1);
            long reservedBeforeClose = await ReservedAsync(environment, setup.Accounts[ids[1]]);
            long releasedFirst = await ReservedAsync(environment, setup.Accounts[ids[0]]);
            OperationResult closed = await CloseAsync(realLedger, setup, auction, new[] { ids[0], ids[2] });
            OperationResult finished = await environment.FinishAuctionAsync(setup.Session);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, opened.Status);
            Assert.Equal(1L, first.CommittedRevision);
            Assert.Equal(2L, second.CommittedRevision);
            Assert.Equal(ContractCodes.RejectReason.BID_TOO_LOW, tooLow.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.STALE_REVISION, stale.ReasonCode);
            Assert.Equal(WINNING_BID, reservedBeforeClose);
            Assert.Equal(0L, releasedFirst);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, closed.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, finished.Status);

            Assert.Equal(CURRENCY_GRANT - WINNING_BID, await CurrencyAsync(environment, setup.Accounts[ids[1]]));
            Assert.Equal(CURRENCY_GRANT + PAYOUT_EACH, await CurrencyAsync(environment, setup.Accounts[ids[0]]));
            Assert.Equal(CURRENCY_GRANT + PAYOUT_EACH, await CurrencyAsync(environment, setup.Accounts[ids[2]]));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT COALESCE(sum(currency_reserved), 0) FROM account_wallets;"));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT quantity FROM account_items WHERE account_id = @Id AND definition_id = @Item;",
                new { Id = setup.Accounts[ids[1]], Item = MATERIAL_ID }));
            Assert.Equal(BURNED, await environment.ScalarAsync(
                "SELECT burned FROM auctions WHERE auction_id = @Id;", new { Id = auction }));
            Assert.Equal(PartyAuctionRules.STATE_SOLD, await environment.TextAsync(
                "SELECT state FROM auctions WHERE auction_id = @Id;", new { Id = auction }));

            // 실제 원장 줄 수와, 엽전 줄의 잔액 기록이 실제 잔액과 같은지 본다
            Assert.Equal(SOLD_LEDGER_LINES, await environment.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE source_id = @Id;", new { Id = auction.ToString("D") }));
            Assert.Equal(0L, await environment.ScalarAsync(
                """
                SELECT count(*) FROM economy_ledger AS ledger JOIN account_wallets AS wallet USING (account_id)
                WHERE ledger.source_id = @Id AND ledger.asset_kind = 'Currency' AND ledger.balance_after <> wallet.currency;
                """, new { Id = auction.ToString("D") }));
            Assert.Equal(GameSessionStates.CLOSED, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = setup.Session.SessionId }));
            Assert.Equal(GameSessionStates.INSTANCE_DRAINING, await environment.TextAsync(
                "SELECT state FROM server_instances WHERE instance_id = @Id;", new { Id = setup.Session.InstanceId }));
        }

        /// <summary> 자격자 한 명은 단독 모드: 최저가 100 한 번만 받고, 분배 대상이 없어 전액 소각한다 </summary>
        [PostgresFact]
        public async Task Solo_OnlyMinimumOnce_BurnsWholePrice()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            AuctionSession setup = await CreateAuctionSessionAsync(environment, 1, CURRENCY_GRANT);
            Guid participant = setup.ParticipantIds[0];
            Guid auction = Guid.NewGuid();

            await OpenAsync(environment, setup, auction, setup.ParticipantIds);
            OperationResult above = await BidAsync(environment, setup, auction, participant,
                PartyAuctionRules.SOLO_PRICE + 1, 0);
            OperationResult bought = await BidAsync(environment, setup, auction, participant, PartyAuctionRules.SOLO_PRICE, 0);
            OperationResult again = await BidAsync(environment, setup, auction, participant, PartyAuctionRules.SOLO_PRICE, 1);
            OperationResult closed = await CloseAsync(environment.Auctions, setup, auction, setup.ParticipantIds);

            Assert.Equal(PartyAuctionRules.MODE_SOLO, await environment.TextAsync(
                "SELECT mode FROM auctions WHERE auction_id = @Id;", new { Id = auction }));
            Assert.Equal(ContractCodes.RejectReason.BID_TOO_LOW, above.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, bought.Status);
            Assert.Equal(ContractCodes.RejectReason.BID_TOO_LOW, again.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, closed.Status);
            Assert.Equal(CURRENCY_GRANT - PartyAuctionRules.SOLO_PRICE,
                await CurrencyAsync(environment, setup.Accounts[participant]));
            Assert.Equal(PartyAuctionRules.SOLO_PRICE, await environment.ScalarAsync(
                "SELECT burned FROM auctions WHERE auction_id = @Id;", new { Id = auction }));
        }

        /// <summary> 본인 최고가를 올릴 때도 가용 엽전(잔액 - 예약 + 본인 예약)으로 다시 검증하고, 같은 ID 재시도는 최초 결과다 </summary>
        [PostgresFact]
        public async Task Bid_InsufficientAvailable_RejectsAndRetryReplays()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            AuctionSession setup = await CreateAuctionSessionAsync(environment, 2, LOW_CURRENCY);
            Guid[] ids = setup.ParticipantIds;
            Guid auction = Guid.NewGuid();
            Guid operationId = Guid.NewGuid();
            long acceptedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // 재시도는 접수 시각까지 같은 본문이어야 최초 결과를 받는다
            await OpenAsync(environment, setup, auction, ids);
            OperationResult first = await BidAsync(environment, setup, auction, ids[0], FIRST_BID, 0, operationId, acceptedAt);
            OperationResult replay = await BidAsync(environment, setup, auction, ids[0], FIRST_BID, 0, operationId, acceptedAt);
            OperationResult conflict = await BidAsync(environment, setup, auction, ids[0], RAISED_BID, 0, operationId,
                acceptedAt);
            OperationResult raise = await BidAsync(environment, setup, auction, ids[0], RAISED_BID, 1);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Status);
            Assert.Equal(first.ReceiptId, replay.ReceiptId);
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, conflict.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.INSUFFICIENT_CURRENCY, raise.ReasonCode);
            Assert.Equal(FIRST_BID, await ReservedAsync(environment, setup.Accounts[ids[0]]));
        }

        /// <summary> 다른 던전이나 출정 밖 참가자는 남의 경매 예약을 바꾸지 못한다 </summary>
        [PostgresFact]
        public async Task Bid_FromOtherInstanceOrOutsider_Rejected()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            AuctionSession setup = await CreateAuctionSessionAsync(environment, 2, CURRENCY_GRANT);
            Guid[] ids = setup.ParticipantIds;
            Guid auction = Guid.NewGuid();

            await OpenAsync(environment, setup, auction, ids);
            OperationResult foreign = await environment.Auctions.BidAsync(Guid.NewGuid(), setup.BootId,
                Bid(setup, auction, ids[0], FIRST_BID, 0, Guid.NewGuid()));
            OperationResult outsider = await BidAsync(environment, setup, auction, Guid.NewGuid(), FIRST_BID, 0);

            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, foreign.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.NOT_ELIGIBLE, outsider.ReasonCode);
            Assert.Equal(0L, await environment.ScalarAsync("SELECT COALESCE(sum(currency_reserved), 0) FROM account_wallets;"));
        }

        /// <summary> 같은 순번의 동시 입찰은 하나만 승인되고 나머지는 StaleRevision이며 예약 합계는 승인된 금액뿐이다 </summary>
        [PostgresFact]
        public async Task Bid_ConcurrentSameSeq_OnlyOneWins()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            AuctionSession setup = await CreateAuctionSessionAsync(environment, PartyAuctionRules.MAX_ELIGIBLE,
                CURRENCY_GRANT);
            Guid auction = Guid.NewGuid();
            await OpenAsync(environment, setup, auction, setup.ParticipantIds);

            OperationResult[] results = await Task.WhenAll(setup.ParticipantIds.Select(id =>
                BidAsync(environment, setup, auction, id, FIRST_BID, 0)));

            Assert.Equal(1, results.Count(result => result.Status == ContractCodes.OperationStatus.COMMITTED));
            Assert.All(results.Where(result => result.Status != ContractCodes.OperationStatus.COMMITTED),
                result => Assert.Equal(ContractCodes.RejectReason.STALE_REVISION, result.ReasonCode));
            Assert.Equal(FIRST_BID, await environment.ScalarAsync(
                "SELECT COALESCE(sum(currency_reserved), 0) FROM account_wallets;"));
        }

        /// <summary> 열린 품목이 있으면 finish가 거절되고, 경매 단계 기한이 지나면 복구가 예약을 풀어 취소한 뒤 출정을 닫는다 </summary>
        [PostgresFact]
        public async Task Recover_OpenAuctionPastPhase_CancelsReleasesAndCloses()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            AuctionSession setup = await CreateAuctionSessionAsync(environment, 2, CURRENCY_GRANT);
            Guid auction = Guid.NewGuid();
            await OpenAsync(environment, setup, auction, setup.ParticipantIds);
            await BidAsync(environment, setup, auction, setup.ParticipantIds[0], FIRST_BID, 0);

            // 열린 품목이 있는 finish는 저장하지 않는 일시 거절이다. 품목이 끝나면 같은 operationId로 다시 닫을 수 있다
            TransientRejectionException blocked = await Assert.ThrowsAsync<TransientRejectionException>(() =>
                environment.FinishAuctionAsync(setup.Session));
            SessionRecoveryResult early = await environment.Auctions.RecoverAsync(RECOVERY_BATCH);
            await ExpirePhaseAsync(environment, setup);
            SessionRecoveryResult recovered = await environment.Auctions.RecoverAsync(RECOVERY_BATCH);

            Assert.Equal(ContractCodes.RejectReason.WRONG_PHASE, blocked.ReasonCode);
            Assert.Equal(0, early.Settled);
            Assert.Equal(1, recovered.Settled);
            Assert.Equal(1, recovered.Cancelled);
            Assert.Equal(PartyAuctionRules.STATE_CANCELLED, await environment.TextAsync(
                "SELECT state FROM auctions WHERE auction_id = @Id;", new { Id = auction }));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT COALESCE(sum(currency_reserved), 0) FROM account_wallets;"));
            Assert.Equal(CURRENCY_GRANT, await CurrencyAsync(environment, setup.Accounts[setup.ParticipantIds[0]]));
            Assert.Equal(GameSessionStates.CLOSED, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = setup.Session.SessionId }));
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM account_leases WHERE instance_id = @Id;", new { Id = setup.Session.InstanceId }));
        }

        /// <summary> 종료 기록(Closing)이 저장된 뒤 정산 전에 던전이 사라지면 복구가 저장된 접속 집합으로 정산한다 </summary>
        [PostgresFact]
        public async Task Recover_ClosingRecorded_SettlesFromStoredConnectedSet()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            AuctionSession setup = await CreateAuctionSessionAsync(environment, 3, CURRENCY_GRANT);
            Guid[] ids = setup.ParticipantIds;
            Guid auction = Guid.NewGuid();
            await OpenAsync(environment, setup, auction, ids);
            await BidAsync(environment, setup, auction, ids[1], WINNING_BID, 0);

            // close 작업만 저장되고 settle 전에 프로세스가 사라진 상태. 종료 접속자는 ids[0] 하나다
            await environment.ExecuteAsync(
                """
                UPDATE auctions SET state = 'Closing', connected_participant_ids = @Connected, ended_at = clock_timestamp(),
                    close_operation_id = @Op WHERE auction_id = @Id;
                """, new { Connected = new[] { ids[0] }, Op = Guid.NewGuid(), Id = auction });
            await ExpirePhaseAsync(environment, setup);
            SessionRecoveryResult recovered = await environment.Auctions.RecoverAsync(RECOVERY_BATCH);

            Assert.Equal(1, recovered.Settled);
            Assert.Equal(0, recovered.Cancelled);
            Assert.Equal(PartyAuctionRules.STATE_SOLD, await environment.TextAsync(
                "SELECT state FROM auctions WHERE auction_id = @Id;", new { Id = auction }));
            Assert.Equal(CURRENCY_GRANT - WINNING_BID, await CurrencyAsync(environment, setup.Accounts[ids[1]]));
            Assert.Equal(CURRENCY_GRANT + WINNING_BID, await CurrencyAsync(environment, setup.Accounts[ids[0]]));
            Assert.Equal(CURRENCY_GRANT, await CurrencyAsync(environment, setup.Accounts[ids[2]]));
        }

        /// <summary> 다른 던전이 같은 operationId로 먼저 불러도 행위자의 멱등 기록이 남지 않아, 배정 던전의 같은 요청은 정상 처리된다 </summary>
        [PostgresFact]
        public async Task Bid_ForeignCallerFirst_DoesNotPoisonOperation()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            AuctionSession setup = await CreateAuctionSessionAsync(environment, 2, CURRENCY_GRANT);
            Guid auction = Guid.NewGuid();
            Guid operationId = Guid.NewGuid();
            AuctionBidInput input = Bid(setup, auction, setup.ParticipantIds[0], FIRST_BID, 0, operationId);
            await OpenAsync(environment, setup, auction, setup.ParticipantIds);

            OperationResult foreign = await environment.Auctions.BidAsync(Guid.NewGuid(), setup.BootId, input);
            long stored = await environment.ScalarAsync("SELECT count(*) FROM operations WHERE operation_id = @Id;",
                new { Id = operationId });
            OperationResult genuine = await environment.Auctions.BidAsync(setup.Session.InstanceId, setup.BootId, input);

            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, foreign.ReasonCode);
            Assert.Equal(0L, stored);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, genuine.Status);
        }

        /// <summary> 품목은 경매 단계 복구 전에 120초 상한을 다 쓸 수 있을 때만 열고, close 응답은 요청 operationId를 담는다 </summary>
        [PostgresFact]
        public async Task Open_LateInPhase_ClosedAndCloseEchoesOperation()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            AuctionSession setup = await CreateAuctionSessionAsync(environment, 2, CURRENCY_GRANT);
            Guid early = Guid.NewGuid();
            Guid closeId = Guid.NewGuid();

            await OpenAsync(environment, setup, early, setup.ParticipantIds);
            OperationResult closed = await setup.CloseWith(environment.Auctions, early, closeId, setup.ParticipantIds);

            // 남은 경매 단계가 품목 상한보다 짧으면 품목 수 규칙보다 먼저 기한으로 거절한다
            await environment.ExecuteAsync(
                "UPDATE game_sessions SET auction_ends_at = clock_timestamp() + make_interval(secs => @Left) WHERE session_id = @Id;",
                new { Id = setup.Session.SessionId, Left = PartyAuctionRules.ITEM_HARD_LIMIT_SECONDS - 1 });
            OperationResult late = await OpenAsync(environment, setup, Guid.NewGuid(), setup.ParticipantIds);

            Assert.Equal(closeId.ToString("D"), closed.OperationId);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, closed.Status);
            Assert.Equal(ContractCodes.RejectReason.CLOSED, late.ReasonCode);
        }

        /// <summary> 일반전은 경매 단계 없이 바로 닫힌다 </summary>
        [PostgresFact]
        public async Task Settle_NormalMode_ClosesWithoutAuction()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.NORMAL, new[] { player }, 0);
            Guid settlementId = Guid.NewGuid();
            await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Ended(1, settlementId,
                    new[] { SessionEventTests.Grant(session.Participants[0].ParticipantId, ESSENCE_GRANT) })
            });

            OperationResult settled = await environment.Settlement.SettleAsync(session.InstanceId, session.SessionId,
                settlementId);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, settled.Status);
            Assert.Equal(GameSessionStates.CLOSED, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
        }

        private static async Task<AuctionSession> CreateAuctionSessionAsync(SessionTestEnvironment environment, int players,
            long currency)
        {
            List<TestPlayer> created = new List<TestPlayer>();
            for (int index = 0; index < players; ++index)
            {
                created.Add(await environment.CreatePlayerAsync(TICKETS, POTIONS));
            }

            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, created, POTIONS_RESERVED);
            Guid settlementId = Guid.NewGuid();
            List<SettlementGrantInput> grants = session.Participants.Select(row =>
            {
                SettlementGrantInput grant = SessionEventTests.Grant(row.ParticipantId, ESSENCE_GRANT);
                grant.Currency = currency;
                return grant;
            }).ToList();
            OperationResult appended = await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Ended(1, settlementId, grants)
            });
            OperationResult settled = await environment.Settlement.SettleAsync(session.InstanceId, session.SessionId,
                settlementId);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, appended.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, settled.Status);
            Assert.Equal(GameSessionStates.AUCTION, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));

            Guid? boot;
            await using (NpgsqlConnection connection = await environment.DataSource.OpenConnectionAsync())
            {
                boot = await connection.QuerySingleOrDefaultAsync<Guid?>(
                    "SELECT instance_boot_id FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId });
            }

            return new AuctionSession(session, boot,
                session.Participants.ToDictionary(row => row.ParticipantId, row => row.AccountId));
        }

        private static Task<OperationResult> OpenAsync(SessionTestEnvironment environment, AuctionSession setup, Guid auction,
            IReadOnlyList<Guid> eligible)
        {
            return environment.Auctions.OpenAsync(setup.Session.InstanceId, setup.BootId, new AuctionOpenInput
            {
                SessionId = setup.Session.SessionId, OperationId = Guid.NewGuid(), AuctionId = auction,
                DefinitionId = MATERIAL_ID, EligibleParticipantIds = eligible
            });
        }

        private static Task<OperationResult> BidAsync(SessionTestEnvironment environment, AuctionSession setup, Guid auction,
            Guid participant, long amount, long expectedSeq, Guid? operationId = null, long? acceptedAtUnixMs = null)
        {
            return environment.Auctions.BidAsync(setup.Session.InstanceId, setup.BootId,
                Bid(setup, auction, participant, amount, expectedSeq, operationId ?? Guid.NewGuid(), acceptedAtUnixMs));
        }

        private static AuctionBidInput Bid(AuctionSession setup, Guid auction, Guid participant, long amount, long expectedSeq,
            Guid operationId, long? acceptedAtUnixMs = null)
        {
            return new AuctionBidInput
            {
                SessionId = setup.Session.SessionId, AuctionId = auction, OperationId = operationId,
                ParticipantId = participant, Amount = amount, ExpectedBidSeq = expectedSeq,
                AcceptedAtUnixMs = acceptedAtUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        private static Task<OperationResult> CloseAsync(PostgresPartyAuctionRepository auctions, AuctionSession setup,
            Guid auction, IReadOnlyList<Guid> connected)
        {
            return auctions.CloseAsync(setup.Session.InstanceId, setup.BootId, new AuctionCloseInput
            {
                SessionId = setup.Session.SessionId, AuctionId = auction, OperationId = Guid.NewGuid(),
                ConnectedParticipantIds = connected, EndedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }

        private static Task ExpirePhaseAsync(SessionTestEnvironment environment, AuctionSession setup)
        {
            return environment.ExecuteAsync(
                "UPDATE game_sessions SET auction_ends_at = clock_timestamp() - make_interval(secs => @Past) WHERE session_id = @Id;",
                new { Id = setup.Session.SessionId, Past = PartyAuctionRules.PHASE_RECOVERY_GRACE_SECONDS + 1 });
        }

        private static Task<long> CurrencyAsync(SessionTestEnvironment environment, Guid accountId)
        {
            return environment.ScalarAsync("SELECT currency FROM account_wallets WHERE account_id = @Id;", new { Id = accountId });
        }

        private static Task<long> ReservedAsync(SessionTestEnvironment environment, Guid accountId)
        {
            return environment.ScalarAsync("SELECT currency_reserved FROM account_wallets WHERE account_id = @Id;",
                new { Id = accountId });
        }

        private sealed class AuctionSession
        {
            public Task<OperationResult> CloseWith(PostgresPartyAuctionRepository auctions, Guid auction, Guid operationId,
                IReadOnlyList<Guid> connected)
            {
                return auctions.CloseAsync(Session.InstanceId, BootId, new AuctionCloseInput
                {
                    SessionId = Session.SessionId, AuctionId = auction, OperationId = operationId,
                    ConnectedParticipantIds = connected, EndedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }

            public ReservedSessionView    Session        { get; }
            public Guid?                  BootId         { get; }
            public Dictionary<Guid, Guid> Accounts       { get; }
            public Guid[]                 ParticipantIds { get; }

            public AuctionSession(ReservedSessionView session, Guid? bootId, Dictionary<Guid, Guid> accounts)
            {
                Session        = session;
                BootId         = bootId;
                Accounts       = accounts;
                ParticipantIds = session.Participants.Select(row => row.ParticipantId).ToArray();
            }
        }
    }
}
