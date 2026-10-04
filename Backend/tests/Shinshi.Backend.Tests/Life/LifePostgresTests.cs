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
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Ledger;
using Shinshi.Data.Life;

namespace Shinshi.Backend.Tests.Life
{
    /// <summary> 채집과 물약 제작의 멱등, 전체 거절, 행동력 회복과 임대 소유를 실제 PostgreSQL에서 검사한다 </summary>
    public sealed class LifePostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ZONE_ID      = "life_zone_1";
        private const string NODE_ID      = "node-03";
        private const int    RECONCILE_BATCH = 100;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 같은 operationId를 동시에, 다시 보내도 약초는 2개, 행동력은 2만 줄고 원장은 한 줄이다 </summary>
        [PostgresFact]
        public async Task Gather_SameOperationConcurrentAndRepeated_AppliesOnce()
        {
            await using LifeFixture fixture = await LifeFixture.CreateAsync();
            (Guid account, Guid lease) = await fixture.CreateAccountAsync(fixture.TownId);
            LifeGatherInput input = fixture.Gather(account, lease, Guid.NewGuid());

            OperationResult[] results = await Task.WhenAll(Enumerable.Range(0, 4)
                .Select(_ => fixture.Life.GatherAsync(input)));
            OperationResult replay = await fixture.Life.GatherAsync(input);

            Assert.All(results, result => Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status));
            Assert.Single(results.Select(result => result.ReceiptId).Append(replay.ReceiptId).Distinct());
            Assert.Equal(LifeContent.HERBS_PER_GATHER, await fixture.ItemAsync(account, LifeContent.HERB_ID));
            Assert.Equal(LifeContent.ACTION_MAX - LifeContent.ACTION_COST_PER_GATHER, await fixture.ActionValueAsync(account));
            Assert.Equal(1L, await fixture.ScalarAsync(
                """
                SELECT count(*) FROM economy_ledger
                WHERE account_id = @Account AND reason = @Reason AND source_type = @Source AND source_id = @SourceId
                    AND actor_kind = 'Player' AND actor_id = @Account;
                """, new
                {
                    Account = account, Reason = ContractCodes.EconomyReason.GATHERING,
                    Source = PostgresLifeRepository.GATHER_SOURCE_TYPE, SourceId = ZONE_ID + ":" + NODE_ID
                }));
            Assert.Empty(await fixture.ReconcileAsync());
        }

        /// <summary> 멱등 actor는 계정이다. 재접속으로 다른 마을, 다른 임대로 옮긴 뒤 같은 operationId를 다시 보내도 저장 결과이고
        /// 채집, 제작 효과는 한 번뿐이다 </summary>
        [PostgresFact]
        public async Task GatherAndCraft_ResentFromOtherTownAfterReconnect_ReturnStoredResult()
        {
            await using LifeFixture fixture = await LifeFixture.CreateAsync();
            (Guid account, Guid lease) = await fixture.CreateAccountAsync(fixture.TownId);
            Guid gatherId = Guid.NewGuid();
            Guid craftId = Guid.NewGuid();
            string potionId = LifeContent.PotionIds.First();
            LifeContent.TryGetPotionCost(potionId, out long herbCost);
            await fixture.SetItemAsync(account, LifeContent.HERB_ID, herbCost);

            OperationResult gathered = await fixture.Life.GatherAsync(fixture.Gather(account, lease, gatherId));
            OperationResult crafted = await fixture.Life.CraftPotionAsync(fixture.Craft(account, lease, craftId, potionId, 1));
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, gathered.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, crafted.Status);
            long ledgerRows = await fixture.LedgerCountAsync(account);
            long herbs = await fixture.ItemAsync(account, LifeContent.HERB_ID);

            // 재접속: 같은 계정의 임대가 다른 마을의 새 임대로 바뀐다
            Guid movedLease = await fixture.MoveLeaseAsync(account, fixture.OtherTownId);
            LifeGatherInput gatherAgain = fixture.Gather(account, movedLease, gatherId);
            gatherAgain.TownInstanceId = fixture.OtherTownId;
            LifePotionCraftInput craftAgain = fixture.Craft(account, movedLease, craftId, potionId, 1);
            craftAgain.TownInstanceId = fixture.OtherTownId;

            OperationResult gatherReplay = await fixture.Life.GatherAsync(gatherAgain);
            OperationResult craftReplay = await fixture.Life.CraftPotionAsync(craftAgain);
            OperationResult? storedGather = await fixture.Life.FindStoredGatherAsync(gatherAgain);

            Assert.Equal(gathered.ReceiptId, gatherReplay.ReceiptId);
            Assert.Equal(gathered.ServerTimeUnixMs, gatherReplay.ServerTimeUnixMs);
            Assert.Equal(crafted.ReceiptId, craftReplay.ReceiptId);
            Assert.Equal(gathered.ReceiptId, storedGather?.ReceiptId);
            Assert.Equal(ledgerRows, await fixture.LedgerCountAsync(account));
            Assert.Equal(herbs, await fixture.ItemAsync(account, LifeContent.HERB_ID));
            Assert.Equal(1L, await fixture.ItemAsync(account, potionId) / LifeContent.POTION_OUTPUT);
        }

        [PostgresFact]
        public async Task Gather_ActionOne_RejectsInsufficientActionWithoutEffect()
        {
            await using LifeFixture fixture = await LifeFixture.CreateAsync();
            (Guid account, Guid lease) = await fixture.CreateAccountAsync(fixture.TownId);
            await fixture.SetActionAsync(account, 1, 0);

            OperationResult result = await fixture.Life.GatherAsync(fixture.Gather(account, lease, Guid.NewGuid()));

            Assert.Equal(ContractCodes.RejectReason.INSUFFICIENT_ACTION, result.ReasonCode);
            Assert.Equal(1L, await fixture.ActionValueAsync(account));
            Assert.Equal(0L, await fixture.ItemAsync(account, LifeContent.HERB_ID));
            Assert.Equal(0L, await fixture.LedgerCountAsync(account));
        }

        [PostgresFact]
        public async Task Gather_HerbsAt9998_RejectsInventoryFullWithoutSpendingAction()
        {
            await using LifeFixture fixture = await LifeFixture.CreateAsync();
            (Guid account, Guid lease) = await fixture.CreateAccountAsync(fixture.TownId);
            await fixture.SetItemAsync(account, LifeContent.HERB_ID, LifeContent.HERB_STACK - 1);

            OperationResult result = await fixture.Life.GatherAsync(fixture.Gather(account, lease, Guid.NewGuid()));

            Assert.Equal(ContractCodes.RejectReason.INVENTORY_FULL, result.ReasonCode);
            Assert.Equal(LifeContent.HERB_STACK - 1, await fixture.ItemAsync(account, LifeContent.HERB_ID));

            // 거절은 효과 전체를 되돌리므로 처음 만들던 지갑 행도 남지 않는다(행동력은 그대로 상한)
            Assert.Equal(0L, await fixture.ScalarAsync(
                "SELECT count(*) FROM account_wallets WHERE account_id = @Id;", new { Id = account }));
            Assert.Equal(0L, await fixture.LedgerCountAsync(account));
        }

        /// <summary> 1에서 299초 지나면 아직 1이라 거절, 300초 지나면 2가 돼 채집되고 0이 된다 </summary>
        [PostgresFact]
        public async Task Gather_RecoveryBoundary_UsesDatabaseTime()
        {
            await using LifeFixture fixture = await LifeFixture.CreateAsync();
            (Guid early, Guid earlyLease) = await fixture.CreateAccountAsync(fixture.TownId);
            (Guid ready, Guid readyLease) = await fixture.CreateAccountAsync(fixture.TownId);
            await fixture.SetActionAsync(early, 1, LifeContent.ACTION_RECOVER_INTERVAL_SECONDS - 1);
            await fixture.SetActionAsync(ready, 1, LifeContent.ACTION_RECOVER_INTERVAL_SECONDS);

            OperationResult rejected = await fixture.Life.GatherAsync(fixture.Gather(early, earlyLease, Guid.NewGuid()));
            OperationResult committed = await fixture.Life.GatherAsync(fixture.Gather(ready, readyLease, Guid.NewGuid()));

            Assert.Equal(ContractCodes.RejectReason.INSUFFICIENT_ACTION, rejected.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, committed.Status);
            Assert.Equal(0L, await fixture.ActionValueAsync(ready));
        }

        /// <summary> 상한에서 쓰면 기준 시각이 지금으로, 상한 아래에서는 회복에 쓴 구간(300초)만큼만 앞으로 간다 </summary>
        [PostgresFact]
        public async Task Gather_AnchorMovement_ResetsAtCapAndAdvancesBelowCap()
        {
            await using LifeFixture fixture = await LifeFixture.CreateAsync();
            (Guid full, Guid fullLease) = await fixture.CreateAccountAsync(fixture.TownId);
            (Guid partial, Guid partialLease) = await fixture.CreateAccountAsync(fixture.TownId);
            await fixture.SetActionAsync(full, LifeContent.ACTION_MAX, 10000);
            await fixture.SetActionAsync(partial, 50, 450);
            DateTime partialAnchor = await fixture.AnchorAsync(partial);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                (await fixture.Life.GatherAsync(fixture.Gather(full, fullLease, Guid.NewGuid()))).Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                (await fixture.Life.GatherAsync(fixture.Gather(partial, partialLease, Guid.NewGuid()))).Status);

            Assert.Equal(LifeContent.ACTION_MAX - LifeContent.ACTION_COST_PER_GATHER, await fixture.ActionValueAsync(full));
            Assert.True(await fixture.ScalarAsync(
                "SELECT (action_anchor > clock_timestamp() - interval '30 seconds')::int FROM account_wallets WHERE account_id = @Id;",
                new { Id = full }) == 1);
            Assert.Equal(49L, await fixture.ActionValueAsync(partial));
            Assert.Equal(partialAnchor.AddSeconds(LifeContent.ACTION_RECOVER_INTERVAL_SECONDS),
                await fixture.AnchorAsync(partial));
        }

        [PostgresFact]
        public async Task Gather_LeaseOnOtherTown_RejectsSessionMismatch()
        {
            await using LifeFixture fixture = await LifeFixture.CreateAsync();
            (Guid account, Guid lease) = await fixture.CreateAccountAsync(fixture.OtherTownId);

            OperationResult result = await fixture.Life.GatherAsync(fixture.Gather(account, lease, Guid.NewGuid()));
            LifeSnapshotOutcome snapshot = await fixture.Life.ReadSnapshotAsync(fixture.TownId, account, lease);

            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, result.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, snapshot.ReasonCode);
            Assert.Null(snapshot.Response);
            Assert.Equal(0L, await fixture.ItemAsync(account, LifeContent.HERB_ID));
            Assert.Equal(0L, await fixture.ScalarAsync(
                "SELECT count(*) FROM account_wallets WHERE account_id = @Id;", new { Id = account }));
        }

        /// <summary> 약초 3번 채집(6개) 뒤 소형 1개를 만들면 약초 1, 물약 1이고 원장 대사 불일치가 없다. 같은 ID 다른 본문은 충돌이다 </summary>
        [PostgresFact]
        public async Task Craft_AfterGathering_DebitsHerbsOnceAndRejectsConflictingBody()
        {
            await using LifeFixture fixture = await LifeFixture.CreateAsync();
            (Guid account, Guid lease) = await fixture.CreateAccountAsync(fixture.TownId);
            for (int index = 0; index < 3; ++index)
            {
                await fixture.Life.GatherAsync(fixture.Gather(account, lease, Guid.NewGuid()));
            }

            Guid operationId = Guid.NewGuid();
            LifePotionCraftInput craft = fixture.Craft(account, lease, operationId, LifeContent.POTION_SMALL_ID, 1);
            OperationResult first = await fixture.Life.CraftPotionAsync(craft);
            OperationResult replay = await fixture.Life.CraftPotionAsync(craft);
            OperationResult conflict = await fixture.Life.CraftPotionAsync(
                fixture.Craft(account, lease, operationId, LifeContent.POTION_LARGE_ID, 1));

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Status);
            Assert.Equal(first.ReceiptId, replay.ReceiptId);
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, conflict.ReasonCode);
            Assert.Equal(3 * LifeContent.HERBS_PER_GATHER - LifeContent.POTION_SMALL_COST,
                await fixture.ItemAsync(account, LifeContent.HERB_ID));
            Assert.Equal(LifeContent.POTION_OUTPUT, await fixture.ItemAsync(account, LifeContent.POTION_SMALL_ID));
            Assert.Equal(2L, await fixture.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE account_id = @Id AND reason = @Reason;",
                new { Id = account, Reason = ContractCodes.EconomyReason.CRAFT }));
            Assert.Empty(await fixture.ReconcileAsync());
        }

        /// <summary> 예약분을 뺀 약초가 모자라면 INSUFFICIENT_ITEM, 완제품 999를 넘으면 INVENTORY_FULL이고 아무것도 바뀌지 않는다 </summary>
        [PostgresFact]
        public async Task Craft_InsufficientHerbsOrFullStack_RejectsWithoutEffect()
        {
            await using LifeFixture fixture = await LifeFixture.CreateAsync();
            (Guid poor, Guid poorLease) = await fixture.CreateAccountAsync(fixture.TownId);
            (Guid full, Guid fullLease) = await fixture.CreateAccountAsync(fixture.TownId);
            await fixture.SetItemAsync(poor, LifeContent.HERB_ID, 2 * LifeContent.POTION_LARGE_COST, reserved: 1);
            await fixture.SetItemAsync(full, LifeContent.HERB_ID, LifeContent.POTION_SMALL_COST * 2);
            await fixture.SetItemAsync(full, LifeContent.POTION_SMALL_ID, LifeContent.POTION_STACK - 1);

            OperationResult insufficient = await fixture.Life.CraftPotionAsync(
                fixture.Craft(poor, poorLease, Guid.NewGuid(), LifeContent.POTION_LARGE_ID, 2));
            OperationResult overflow = await fixture.Life.CraftPotionAsync(
                fixture.Craft(full, fullLease, Guid.NewGuid(), LifeContent.POTION_SMALL_ID, 2));

            Assert.Equal(ContractCodes.RejectReason.INSUFFICIENT_ITEM, insufficient.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.INVENTORY_FULL, overflow.ReasonCode);
            Assert.Equal(2 * LifeContent.POTION_LARGE_COST, await fixture.ItemAsync(poor, LifeContent.HERB_ID));
            Assert.Equal(0L, await fixture.ItemAsync(poor, LifeContent.POTION_LARGE_ID));
            Assert.Equal(LifeContent.POTION_SMALL_COST * 2, await fixture.ItemAsync(full, LifeContent.HERB_ID));
            Assert.Equal(LifeContent.POTION_STACK - 1, await fixture.ItemAsync(full, LifeContent.POTION_SMALL_ID));
            Assert.Equal(0L, await fixture.LedgerCountAsync(poor) + await fixture.LedgerCountAsync(full));
        }

        [PostgresFact]
        public async Task Snapshot_NoWalletRow_ShowsCapAndEmptyStocks()
        {
            await using LifeFixture fixture = await LifeFixture.CreateAsync();
            (Guid account, Guid lease) = await fixture.CreateAccountAsync(fixture.TownId);
            await fixture.SetItemAsync(account, LifeContent.POTION_LARGE_ID, 3, reserved: 1);

            LifeSnapshotOutcome outcome = await fixture.Life.ReadSnapshotAsync(fixture.TownId, account, lease);

            LifeSnapshotResponse snapshot = Assert.IsType<LifeSnapshotResponse>(outcome.Response);
            Assert.Equal(LifeContent.ACTION_MAX, snapshot.ActionValue);
            Assert.Equal(0L, snapshot.NextRecoverAtUnixMs);
            Assert.Equal(0L, snapshot.Herb.Quantity);
            Assert.Equal(LifeContent.HERB_STACK, snapshot.Herb.Cap);
            Assert.Equal(new[] { LifeContent.POTION_SMALL_ID, LifeContent.POTION_LARGE_ID },
                snapshot.Potions.Select(potion => potion.DefinitionId));
            Assert.Equal(3L, snapshot.Potions[1].Quantity);
            Assert.Equal(1L, snapshot.Potions[1].Reserved);
            Assert.True(snapshot.ServerTimeUnixMs > 0);
        }

        /// <summary> 마을 두 곳, 계정과 계정 임대, 실제 원장 기록기를 쓰는 생활 저장소 </summary>
        private sealed class LifeFixture : IAsyncDisposable
        {
            /****************************************
            *                Fields
            ****************************************/

            private const int    TOWN_PORT     = 7777;
            private const string CONTENT       = "life-test";
            private const int    LEASE_MINUTES = 15;

            private readonly PostgresTestDatabase _database;

            /****************************************
            *              Properties
            ****************************************/

            public PostgresLifeRepository Life        { get; }
            public Guid                   TownId      { get; } = Guid.NewGuid();
            public Guid                   OtherTownId { get; } = Guid.NewGuid();

            /****************************************
            *                Methods
            ****************************************/

            private LifeFixture(PostgresTestDatabase database)
            {
                _database = database;
                Life = new PostgresLifeRepository(database.DataSource,
                    new IdempotentOperationExecutor(new PostgresOperationStore(database.DataSource)),
                    new PostgresEconomyLedgerWriter());
            }

            public static async Task<LifeFixture> CreateAsync()
            {
                LifeFixture fixture = new LifeFixture(await PostgresTestDatabase.CreateAsync());
                await fixture.ExecuteAsync(
                    """
                    INSERT INTO server_instances(instance_id, role, address, port, build_id, content_version, state, boot_id)
                    VALUES (@TownId, @Town, '127.0.0.1', @Port, @Content, @Content, 'Idle', @BootA),
                        (@OtherTownId, @Town, '127.0.0.1', @Port, @Content, @Content, 'Idle', @BootB);
                    """, new
                    {
                        fixture.TownId, fixture.OtherTownId, Town = ContractCodes.ServerRole.TOWN, Port = TOWN_PORT,
                        Content = CONTENT, BootA = Guid.NewGuid(), BootB = Guid.NewGuid()
                    });
                return fixture;
            }

            public ValueTask DisposeAsync()
            {
                return _database.DisposeAsync();
            }

            public async Task<(Guid AccountId, Guid LeaseId)> CreateAccountAsync(Guid townId)
            {
                Guid account = Guid.NewGuid();
                Guid lease = Guid.NewGuid();
                await ExecuteAsync(
                    """
                    INSERT INTO accounts(account_id) VALUES (@Account);
                    INSERT INTO account_leases(account_id, lease_id, instance_id, role, expires_at)
                    VALUES (@Account, @Lease, @TownId, @Town, clock_timestamp() + make_interval(mins => @Minutes));
                    """, new
                    {
                        Account = account, Lease = lease, TownId = townId, Town = ContractCodes.ServerRole.TOWN,
                        Minutes = LEASE_MINUTES
                    });
                return (account, lease);
            }

            /// <summary> 계정 임대를 다른 마을의 새 임대 ID로 옮긴다(재접속) </summary>
            public async Task<Guid> MoveLeaseAsync(Guid account, Guid townId)
            {
                Guid lease = Guid.NewGuid();
                await ExecuteAsync(
                    """
                    UPDATE account_leases SET lease_id = @Lease, instance_id = @TownId
                    WHERE account_id = @Account;
                    """, new { Account = account, Lease = lease, TownId = townId });
                return lease;
            }

            public LifeGatherInput Gather(Guid account, Guid lease, Guid operationId)
            {
                return new LifeGatherInput
                {
                    TownInstanceId = TownId, OperationId = operationId, AccountId = account, AccountLeaseId = lease,
                    ZoneId = ZONE_ID, NodeId = NODE_ID
                };
            }

            public LifePotionCraftInput Craft(Guid account, Guid lease, Guid operationId, string potionId, int count)
            {
                return new LifePotionCraftInput
                {
                    TownInstanceId = TownId, OperationId = operationId, AccountId = account, AccountLeaseId = lease,
                    PotionDefinitionId = potionId, Count = count
                };
            }

            // 행동력과 그 기준 시각(DB 시각 기준 secondsAgo 전)을 정한다
            public Task SetActionAsync(Guid account, long value, int secondsAgo)
            {
                return ExecuteAsync(
                    """
                    INSERT INTO account_wallets(account_id, action_value, action_anchor)
                    VALUES (@Account, @Value, clock_timestamp() - make_interval(secs => @Seconds))
                    ON CONFLICT (account_id) DO UPDATE
                        SET action_value = EXCLUDED.action_value, action_anchor = EXCLUDED.action_anchor;
                    """, new { Account = account, Value = value, Seconds = secondsAgo });
            }

            public Task SetItemAsync(Guid account, string definitionId, long quantity, long reserved = 0)
            {
                return ExecuteAsync(
                    """
                    INSERT INTO account_items(account_id, definition_id, binding_kind, quantity, reserved)
                    VALUES (@Account, @Definition, @Binding, @Quantity, @Reserved);
                    """, new
                    {
                        Account = account, Definition = definitionId, Binding = LifeContent.BINDING_KIND,
                        Quantity = quantity, Reserved = reserved
                    });
            }

            public Task<long> ItemAsync(Guid account, string definitionId)
            {
                return ScalarAsync(
                    """
                    SELECT COALESCE((SELECT quantity FROM account_items
                        WHERE account_id = @Account AND definition_id = @Definition AND binding_kind = @Binding), 0);
                    """, new { Account = account, Definition = definitionId, Binding = LifeContent.BINDING_KIND });
            }

            public Task<long> ActionValueAsync(Guid account)
            {
                return ScalarAsync("SELECT action_value FROM account_wallets WHERE account_id = @Id;", new { Id = account });
            }

            public async Task<DateTime> AnchorAsync(Guid account)
            {
                await using NpgsqlConnection connection = await _database.DataSource.OpenConnectionAsync();
                return await connection.QuerySingleAsync<DateTime>(
                    "SELECT action_anchor FROM account_wallets WHERE account_id = @Id;", new { Id = account });
            }

            public Task<long> LedgerCountAsync(Guid account)
            {
                return ScalarAsync("SELECT count(*) FROM economy_ledger WHERE account_id = @Id;", new { Id = account });
            }

            public Task<IReadOnlyList<EconomyReconciliationMismatch>> ReconcileAsync()
            {
                return new EconomyLedgerReconciler(_database.DataSource).FindMismatchesAsync(RECONCILE_BATCH);
            }

            public async Task<long> ScalarAsync(string sql, object? parameters = null)
            {
                await using NpgsqlConnection connection = await _database.DataSource.OpenConnectionAsync();
                return await connection.QuerySingleAsync<long>(sql, parameters);
            }

            private async Task ExecuteAsync(string sql, object parameters)
            {
                await using NpgsqlConnection connection = await _database.DataSource.OpenConnectionAsync();
                await connection.ExecuteAsync(sql, parameters);
            }
        }
    }
}
