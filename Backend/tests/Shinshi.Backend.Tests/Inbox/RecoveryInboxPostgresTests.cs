// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Data.Auctions;
using Shinshi.Data.Inbox;
using Shinshi.Data.Ledger;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Inbox
{
    /// <summary> 실제 PostgreSQL에서 30일 보상 회수함: 정산과 경매 낙찰의 보관 상한 초과분 이동, 재시도 1행, 부분 수령, 반복 수령 금지,
    /// 만료와 원장 대사를 검사한다 </summary>
    public sealed class RecoveryInboxPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MATERIAL_ID      = "item_boss_material_common";
        private const string TALISMAN_ID      = "T01-1A";
        private const int    CONCURRENT_CALLS = 4;

        // 상한 바로 아래의 시드 보유량
        private const long   SEED_TICKETS     = RewardStorageLimits.MATERIAL_STACK - 1;
        private const long   SEED_SHARDS      = RewardStorageLimits.MATERIAL_STACK - 9;
        private const long   SEED_MATERIAL    = RewardStorageLimits.MATERIAL_STACK - 1;

        // 정산안: 넘친 몫은 티켓 2, 정련편 11, 재료 4다. 정기와 부적은 상한 아래라 전부 들어간다
        private const long   GRANT_ESSENCE    = 10;
        private const long   GRANT_SHARDS     = 20;
        private const long   GRANT_TICKETS    = 3;
        private const long   GRANT_MATERIAL   = 5;
        private const long   GRANT_TALISMAN   = 2;
        private const long   OVER_TICKETS     = 2;
        private const long   OVER_SHARDS      = 11;
        private const long   OVER_MATERIAL    = 4;
        private const int    RECOVERY_LINES   = 3;

        // 부분 수령: 재료 공간 2칸만 비운다
        private const long   FREED_MATERIAL   = 2;

        private const long   CURRENCY_GRANT   = 500;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 정산을 동시에 여러 번 불러도 상한까지만 지급하고 넘친 몫은 회수함 한 행(원래 획득 시각 + 30일)으로 옮긴다.
        /// 지급한 몫의 원장 합계가 잔액 증가와 같다 </summary>
        [PostgresFact]
        public async Task Settle_OverStorageLimit_MovesOnlyOverflowOnceToRecoveryInbox()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            (TestPlayer player, ReservedSessionView session) = await SettleOverLimitAsync(environment);

            Assert.Equal(RewardStorageLimits.MATERIAL_STACK, await TicketsAsync(environment, player));
            Assert.Equal(RewardStorageLimits.MATERIAL_STACK, await ShardsAsync(environment, player));
            Assert.Equal(RewardStorageLimits.MATERIAL_STACK, await ItemAsync(environment, player, MATERIAL_ID));
            Assert.Equal(GRANT_TALISMAN, await ItemAsync(environment, player, TALISMAN_ID));
            Assert.Equal(GRANT_ESSENCE, await environment.ScalarAsync(
                "SELECT essence FROM characters WHERE character_id = @CharacterId;", new { player.CharacterId }));

            Assert.Equal(1L, await environment.ScalarAsync(
                """
                SELECT count(*) FROM reward_inbox WHERE account_id = @AccountId AND source_kind = 'SessionReward'
                    AND source_ref = @Ref AND grant_id IS NULL AND expires_at = acquired_at + interval '30 days'
                    AND claimed_at IS NULL;
                """, new { player.AccountId, Ref = session.SessionId.ToString("D") }));
            Assert.Equal(OVER_TICKETS, await LineAsync(environment, player, EconomyAsset.TICKETS, string.Empty));
            Assert.Equal(OVER_SHARDS, await LineAsync(environment, player, EconomyAsset.SHARDS, string.Empty));
            Assert.Equal(OVER_MATERIAL, await LineAsync(environment, player, EconomyAsset.ACCOUNT_ITEM, MATERIAL_ID));
            Assert.Equal(player.CharacterId.ToString("D"), await environment.TextAsync(
                "SELECT character_id::text FROM reward_inbox_lines WHERE asset_kind = 'Shards';"));

            // 지급한 몫만 원장에 있다(회수함으로 옮긴 몫은 수령 때 기록한다). 원장 합계 = 시드 이후 잔액 증가
            Assert.Equal(RewardStorageLimits.MATERIAL_STACK - SEED_TICKETS,
                await LedgerSumAsync(environment, player, EconomyAsset.TICKETS, string.Empty));
            Assert.Equal(RewardStorageLimits.MATERIAL_STACK - SEED_SHARDS,
                await LedgerSumAsync(environment, player, EconomyAsset.SHARDS, string.Empty));
            Assert.Equal(RewardStorageLimits.MATERIAL_STACK - SEED_MATERIAL,
                await LedgerSumAsync(environment, player, EconomyAsset.ACCOUNT_ITEM, MATERIAL_ID));

            PostgresInboxRepository inbox = CreateInbox(environment);
            InboxRecord entry = Assert.Single((await inbox.ListAsync(player.AccountId)).Entries);
            Assert.Equal(RecoveryInbox.SOURCE_SESSION_REWARD, entry.SourceKind);
            Assert.False(entry.RequiresCharacter);
            Assert.Equal(RECOVERY_LINES, entry.Rewards.Count);
        }

        /// <summary> 수령은 상한까지 남은 공간만 옮기고 같은 operationId 재시도는 다시 지급하지 않는다. 공간이 없으면 효과 없이
        /// INVENTORY_FULL, 나머지를 모두 받으면 수령 완료로 닫혀 다시 받을 수 없다 </summary>
        [PostgresFact]
        public async Task Claim_PartialThenRest_MovesOnlyFreeSpaceAndNeverTwice()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            (TestPlayer player, ReservedSessionView _) = await SettleOverLimitAsync(environment);
            PostgresInboxRepository inbox = CreateInbox(environment);
            Guid inboxId = Guid.Parse(await environment.TextAsync(
                "SELECT inbox_id::text FROM reward_inbox WHERE account_id = @AccountId;", new { player.AccountId }));
            await environment.ExecuteAsync(
                """
                UPDATE account_items SET quantity = quantity - @Freed
                WHERE account_id = @AccountId AND definition_id = @Material;
                """, new { player.AccountId, Material = MATERIAL_ID, Freed = FREED_MATERIAL });

            Guid partial = Guid.NewGuid();
            OperationResult first = await inbox.ClaimAsync(player.AccountId, partial, inboxId, null);
            OperationResult replay = await inbox.ClaimAsync(player.AccountId, partial, inboxId, null);
            OperationResult conflict = await inbox.ClaimAsync(player.AccountId, partial, inboxId, player.CharacterId);
            OperationResult full = await inbox.ClaimAsync(player.AccountId, Guid.NewGuid(), inboxId, null);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Status);
            Assert.Equal(inboxId.ToString("D"), first.ReceiptId);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, replay.Status);
            Assert.Equal(first.ServerTimeUnixMs, replay.ServerTimeUnixMs);
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, conflict.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.INVENTORY_FULL, full.ReasonCode);
            Assert.Equal(RewardStorageLimits.MATERIAL_STACK, await ItemAsync(environment, player, MATERIAL_ID));
            Assert.Equal(OVER_MATERIAL - FREED_MATERIAL,
                await LineAsync(environment, player, EconomyAsset.ACCOUNT_ITEM, MATERIAL_ID));
            Assert.Equal(OVER_TICKETS, await LineAsync(environment, player, EconomyAsset.TICKETS, string.Empty));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE operation_id = @Id AND reason = @Reason;",
                new { Id = partial, Reason = ContractCodes.EconomyReason.INBOX_CLAIM }));

            // 공간을 비우면 나머지 전부가 들어가고 우편은 수령 완료로 닫힌다
            await environment.ExecuteAsync(
                """
                UPDATE account_wallets SET tickets = 0 WHERE account_id = @AccountId;
                UPDATE characters SET shards = 0 WHERE character_id = @CharacterId;
                UPDATE account_items SET quantity = 0 WHERE account_id = @AccountId AND definition_id = @Material;
                """, new { player.AccountId, player.CharacterId, Material = MATERIAL_ID });
            Guid rest = Guid.NewGuid();
            OperationResult second = await inbox.ClaimAsync(player.AccountId, rest, inboxId, null);
            OperationResult again = await inbox.ClaimAsync(player.AccountId, Guid.NewGuid(), inboxId, null);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, second.Status);
            Assert.Equal(ContractCodes.RejectReason.INBOX_ALREADY_CLAIMED, again.ReasonCode);
            Assert.Equal(OVER_TICKETS, await TicketsAsync(environment, player));
            Assert.Equal(OVER_SHARDS, await ShardsAsync(environment, player));
            Assert.Equal(OVER_MATERIAL - FREED_MATERIAL, await ItemAsync(environment, player, MATERIAL_ID));
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT COALESCE(sum(remaining_qty), 0)::bigint FROM reward_inbox_lines WHERE inbox_id = @Id;",
                new { Id = inboxId }));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM reward_inbox WHERE inbox_id = @Id AND claimed_at IS NOT NULL AND claim_operation_id = @Op;",
                new { Id = inboxId, Op = rest }));
            Assert.Equal(OVER_TICKETS + OVER_SHARDS + OVER_MATERIAL, await environment.ScalarAsync(
                "SELECT COALESCE(sum(delta), 0)::bigint FROM economy_ledger WHERE reason = @Reason;",
                new { Reason = ContractCodes.EconomyReason.INBOX_CLAIM }));
            Assert.Empty((await inbox.ListAsync(player.AccountId)).Entries);
        }

        /// <summary> 정련편 줄은 획득 캐릭터 보유량으로만 판정한다. 그 캐릭터가 상한이면 다른 캐릭터로 받아도 INVENTORY_FULL이고,
        /// 상한 아래로 내려가면 남은 수량 전부가 그 캐릭터에 들어간다 </summary>
        [PostgresFact]
        public async Task Claim_ShardLine_UsesAcquiringCharacterSpaceNotClaimingCharacter()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            (TestPlayer player, ReservedSessionView _) = await SettleOverLimitAsync(environment);
            PostgresInboxRepository inbox = CreateInbox(environment);
            Guid other = await AddCharacterAsync(environment, player);
            Guid inboxId = Guid.Parse(await environment.TextAsync(
                "SELECT inbox_id::text FROM reward_inbox WHERE account_id = @AccountId;", new { player.AccountId }));

            // 티켓과 재료 줄을 먼저 모두 받아 정련편 줄만 남긴다. 정련편 획득 캐릭터는 아직 상한이다
            await environment.ExecuteAsync(
                """
                UPDATE account_wallets SET tickets = 0 WHERE account_id = @AccountId;
                UPDATE account_items SET quantity = 0 WHERE account_id = @AccountId AND definition_id = @Material;
                """, new { player.AccountId, Material = MATERIAL_ID });
            OperationResult others = await inbox.ClaimAsync(player.AccountId, Guid.NewGuid(), inboxId, other);
            OperationResult full = await inbox.ClaimAsync(player.AccountId, Guid.NewGuid(), inboxId, other);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, others.Status);
            Assert.Equal(ContractCodes.RejectReason.INVENTORY_FULL, full.ReasonCode);
            Assert.Equal(OVER_SHARDS, await LineAsync(environment, player, EconomyAsset.SHARDS, string.Empty));
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT shards FROM characters WHERE character_id = @Other;", new { Other = other }));

            await environment.ExecuteAsync("UPDATE characters SET shards = @Shards WHERE character_id = @CharacterId;",
                new { player.CharacterId, Shards = RewardStorageLimits.MATERIAL_STACK - OVER_SHARDS });
            OperationResult rest = await inbox.ClaimAsync(player.AccountId, Guid.NewGuid(), inboxId, other);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, rest.Status);
            Assert.Equal(RewardStorageLimits.MATERIAL_STACK, await ShardsAsync(environment, player));
            Assert.Equal(0L, await LineAsync(environment, player, EconomyAsset.SHARDS, string.Empty));
            Assert.Empty((await inbox.ListAsync(player.AccountId)).Entries);
        }

        /// <summary> 남은 줄이 삭제된 획득 캐릭터의 정련편뿐이면 공간 문제가 아니므로 INVENTORY_FULL이 아니라 CHARACTER_NOT_FOUND이고
        /// 효과(잔액, 남은 수량, 원장)가 없다 </summary>
        [PostgresFact]
        public async Task Claim_OnlyDeletedCharacterLinesLeft_RejectsCharacterNotFoundWithoutEffect()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            (TestPlayer player, ReservedSessionView _) = await SettleOverLimitAsync(environment);
            PostgresInboxRepository inbox = CreateInbox(environment);
            Guid inboxId = Guid.Parse(await environment.TextAsync(
                "SELECT inbox_id::text FROM reward_inbox WHERE account_id = @AccountId;", new { player.AccountId }));
            await environment.ExecuteAsync(
                """
                UPDATE account_wallets SET tickets = 0 WHERE account_id = @AccountId;
                UPDATE account_items SET quantity = 0 WHERE account_id = @AccountId AND definition_id = @Material;
                UPDATE characters SET shards = 0, deleted_at = clock_timestamp(), delete_operation_id = gen_random_uuid()
                WHERE character_id = @CharacterId;
                """, new { player.AccountId, player.CharacterId, Material = MATERIAL_ID });

            OperationResult others = await inbox.ClaimAsync(player.AccountId, Guid.NewGuid(), inboxId, null);
            Guid blockedId = Guid.NewGuid();
            OperationResult blocked = await inbox.ClaimAsync(player.AccountId, blockedId, inboxId, null);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, others.Status);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, blocked.Status);
            Assert.Equal(ContractCodes.RejectReason.CHARACTER_NOT_FOUND, blocked.ReasonCode);
            Assert.Equal(OVER_SHARDS, await LineAsync(environment, player, EconomyAsset.SHARDS, string.Empty));
            Assert.Equal(0L, await ShardsAsync(environment, player));
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE operation_id = @Id;", new { Id = blockedId }));
        }

        /// <summary> 원래 획득 시각에서 30일이 지난 회수함은 목록에서 빠지고 수령은 INBOX_EXPIRED로 효과가 없다 </summary>
        [PostgresFact]
        public async Task Claim_AfterThirtyDays_IsExpiredAndHidden()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            (TestPlayer player, ReservedSessionView _) = await SettleOverLimitAsync(environment);
            PostgresInboxRepository inbox = CreateInbox(environment);
            await environment.ExecuteAsync(
                """
                UPDATE reward_inbox SET created_at = clock_timestamp() - interval '31 days',
                    acquired_at = clock_timestamp() - interval '31 days',
                    expires_at = clock_timestamp() - interval '1 day'
                WHERE account_id = @AccountId;
                UPDATE account_wallets SET tickets = 0 WHERE account_id = @AccountId;
                """, new { player.AccountId });
            Guid inboxId = Guid.Parse(await environment.TextAsync(
                "SELECT inbox_id::text FROM reward_inbox WHERE account_id = @AccountId;", new { player.AccountId }));

            OperationResult expired = await inbox.ClaimAsync(player.AccountId, Guid.NewGuid(), inboxId, null);

            Assert.Equal(ContractCodes.RejectReason.INBOX_EXPIRED, expired.ReasonCode);
            Assert.Equal(0L, await TicketsAsync(environment, player));
            Assert.Empty((await inbox.ListAsync(player.AccountId)).Entries);
        }

        /// <summary> 낙찰자의 재료가 상한이면 낙찰은 취소하지 않고 재료만 경매 회수함 행으로 옮긴다. 엽전 정산은 그대로다 </summary>
        [PostgresFact]
        public async Task AuctionSold_WinnerAtLimit_MovesItemToRecoveryInbox()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer winner = await environment.CreatePlayerAsync(1, 1);
            TestPlayer other = await environment.CreatePlayerAsync(1, 1);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS,
                new[] { winner, other }, 0);
            Guid settlementId = Guid.NewGuid();
            List<SettlementGrantInput> grants = session.Participants.Select(row =>
            {
                SettlementGrantInput grant = SessionEventTests.Grant(row.ParticipantId, GRANT_ESSENCE);
                grant.Currency = CURRENCY_GRANT;
                return grant;
            }).ToList();
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await SessionEventTests.AppendAsync(environment,
                session, new[] { SessionEventTests.Ended(1, settlementId, grants) })).Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await environment.Settlement.SettleAsync(
                session.InstanceId, session.SessionId, settlementId)).Status);
            await environment.ExecuteAsync(
                """
                INSERT INTO account_items (account_id, definition_id, binding_kind, quantity)
                VALUES (@AccountId, @Material, @Binding, @Limit);
                """, new { winner.AccountId, Material = MATERIAL_ID, Binding = SessionTestEnvironment.BINDING,
                    Limit = RewardStorageLimits.MATERIAL_STACK });
            Guid? boot = Guid.Parse(await environment.TextAsync(
                "SELECT instance_boot_id::text FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
            Guid winnerParticipant = session.Participants.Single(row => row.AccountId == winner.AccountId).ParticipantId;
            Guid[] eligible = session.Participants.Select(row => row.ParticipantId).ToArray();
            Guid auctionId = Guid.NewGuid();

            OperationResult opened = await environment.Auctions.OpenAsync(session.InstanceId, boot, new AuctionOpenInput
            {
                SessionId = session.SessionId, OperationId = Guid.NewGuid(), AuctionId = auctionId,
                DefinitionId = MATERIAL_ID, EligibleParticipantIds = eligible
            });
            OperationResult bid = await environment.Auctions.BidAsync(session.InstanceId, boot, new AuctionBidInput
            {
                SessionId = session.SessionId, AuctionId = auctionId, OperationId = Guid.NewGuid(),
                ParticipantId = winnerParticipant, Amount = PartyAuctionRules.MIN_BID, ExpectedBidSeq = 0,
                AcceptedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            OperationResult closed = await environment.Auctions.CloseAsync(session.InstanceId, boot, new AuctionCloseInput
            {
                SessionId = session.SessionId, AuctionId = auctionId, OperationId = Guid.NewGuid(),
                ConnectedParticipantIds = eligible, EndedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, opened.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, bid.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, closed.Status);
            Assert.Equal(PartyAuctionRules.STATE_SOLD, await environment.TextAsync(
                "SELECT state FROM auctions WHERE auction_id = @Id;", new { Id = auctionId }));
            Assert.Equal(CURRENCY_GRANT - PartyAuctionRules.MIN_BID, await environment.ScalarAsync(
                "SELECT currency FROM account_wallets WHERE account_id = @AccountId;", new { winner.AccountId }));
            Assert.Equal(RewardStorageLimits.MATERIAL_STACK, await ItemAsync(environment, winner, MATERIAL_ID));
            Assert.Equal(1L, await environment.ScalarAsync(
                """
                SELECT count(*) FROM reward_inbox AS inbox JOIN reward_inbox_lines AS line ON line.inbox_id = inbox.inbox_id
                WHERE inbox.account_id = @AccountId AND inbox.source_kind = 'AuctionReward' AND inbox.source_ref = @Ref
                    AND line.definition_id = @Material AND line.remaining_qty = 1;
                """, new { winner.AccountId, Ref = auctionId.ToString("D"), Material = MATERIAL_ID }));
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM reward_inbox WHERE account_id = @AccountId;", new { other.AccountId }));
        }

        // 상한 바로 아래로 시드한 일반전 참가자 한 명의 정산을 같은 operationId로 동시에 여러 번 부른다
        private static async Task<(TestPlayer Player, ReservedSessionView Session)> SettleOverLimitAsync(
            SessionTestEnvironment environment)
        {
            TestPlayer player = await environment.CreatePlayerAsync(0, 1);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.NORMAL, new[] { player }, 0);
            await environment.ExecuteAsync(
                """
                UPDATE account_wallets SET tickets = @Tickets WHERE account_id = @AccountId;
                UPDATE characters SET shards = @Shards WHERE character_id = @CharacterId;
                INSERT INTO account_items (account_id, definition_id, binding_kind, quantity)
                VALUES (@AccountId, @Material, @Binding, @MaterialSeed);
                """, new
                {
                    player.AccountId, player.CharacterId, Tickets = SEED_TICKETS, Shards = SEED_SHARDS,
                    Material = MATERIAL_ID, Binding = SessionTestEnvironment.BINDING, MaterialSeed = SEED_MATERIAL
                });

            Guid settlementId = Guid.NewGuid();
            SettlementGrantInput grant = SessionEventTests.Grant(session.Participants[0].ParticipantId, GRANT_ESSENCE,
                new SettlementItemInput
                {
                    DefinitionId = MATERIAL_ID, BindingKind = SessionTestEnvironment.BINDING, Quantity = GRANT_MATERIAL
                });
            grant.RefineShards = GRANT_SHARDS;
            grant.Tickets = GRANT_TICKETS;
            grant.Talismans.Add(SettlementWalletValidatorTests.Talisman(TALISMAN_ID, GRANT_TALISMAN));
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await SessionEventTests.AppendAsync(environment,
                session, new[] { SessionEventTests.Ended(1, settlementId, new[] { grant }) })).Status);

            PostgresSettlementRepository settlement = new PostgresSettlementRepository(environment.DataSource,
                environment.Executor, new LedgerWriter(new PostgresEconomyLedgerWriter()));
            OperationResult[] results = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_CALLS).Select(_ =>
                settlement.SettleAsync(session.InstanceId, session.SessionId, settlementId)));
            Assert.All(results, result =>
            {
                Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
                Assert.Equal(results[0].ReceiptId, result.ReceiptId);
            });
            return (player, session);
        }

        // 같은 계정의 두 번째 캐릭터(정련편 0)
        private static async Task<Guid> AddCharacterAsync(SessionTestEnvironment environment, TestPlayer player)
        {
            Guid characterId = Guid.NewGuid();
            await environment.ExecuteAsync(
                "INSERT INTO characters (character_id, account_id, class_id) VALUES (@CharacterId, @AccountId, 0);",
                new { CharacterId = characterId, player.AccountId });
            return characterId;
        }

        private static PostgresInboxRepository CreateInbox(SessionTestEnvironment environment)
        {
            return new PostgresInboxRepository(environment.DataSource, environment.Executor,
                new PostgresEconomyLedgerWriter());
        }

        private static Task<long> TicketsAsync(SessionTestEnvironment environment, TestPlayer player)
        {
            return environment.ScalarAsync("SELECT tickets FROM account_wallets WHERE account_id = @AccountId;",
                new { player.AccountId });
        }

        private static Task<long> ShardsAsync(SessionTestEnvironment environment, TestPlayer player)
        {
            return environment.ScalarAsync("SELECT shards FROM characters WHERE character_id = @CharacterId;",
                new { player.CharacterId });
        }

        private static Task<long> ItemAsync(SessionTestEnvironment environment, TestPlayer player, string definitionId)
        {
            return environment.ScalarAsync(
                """
                SELECT COALESCE(sum(quantity), 0)::bigint FROM account_items
                WHERE account_id = @AccountId AND definition_id = @Id;
                """, new { player.AccountId, Id = definitionId });
        }

        private static Task<long> LineAsync(SessionTestEnvironment environment, TestPlayer player, string assetKind,
            string definitionId)
        {
            return environment.ScalarAsync(
                """
                SELECT COALESCE(sum(remaining_qty), 0)::bigint FROM reward_inbox_lines
                WHERE account_id = @AccountId AND asset_kind = @Kind AND definition_id = @Id;
                """, new { player.AccountId, Kind = assetKind, Id = definitionId });
        }

        private static Task<long> LedgerSumAsync(SessionTestEnvironment environment, TestPlayer player, string assetKind,
            string assetId)
        {
            return environment.ScalarAsync(
                """
                SELECT COALESCE(sum(delta), 0)::bigint FROM economy_ledger
                WHERE account_id = @AccountId AND asset_kind = @Kind AND asset_id = @Id;
                """, new { player.AccountId, Kind = assetKind, Id = assetId });
        }
    }
}
