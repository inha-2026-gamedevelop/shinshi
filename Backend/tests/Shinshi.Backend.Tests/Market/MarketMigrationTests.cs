// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Tests.Growth;
using Shinshi.Data.Inbox;
using Shinshi.Data.Ledger;

namespace Shinshi.Backend.Tests.Market
{
    /// <summary> 0015의 적용 결과(검증된 CHECK, 인덱스)와 되돌리기 뒤 재적용을 실제 PostgreSQL에서 검사한다 </summary>
    public sealed class MarketMigrationTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MIGRATION_0015 = "0015_user_market_trade";

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Migration0015_Applied_AllNewConstraintsAreValidatedAndIndexesExist()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();

            long unvalidated = await connection.QuerySingleAsync<long>(
                """
                SELECT count(*) FROM pg_constraint AS constraint_row
                JOIN pg_class AS table_row ON table_row.oid = constraint_row.conrelid
                JOIN pg_namespace AS schema_row ON schema_row.oid = table_row.relnamespace
                WHERE schema_row.nspname = current_schema() AND NOT constraint_row.convalidated;
                """);
            string[] indexes = (await connection.QueryAsync<string>(
                """
                SELECT indexname FROM pg_indexes WHERE schemaname = current_schema() AND indexname = ANY(@Names)
                ORDER BY indexname;
                """, new
                {
                    Names = new[]
                    {
                        "ux_market_listings_item_active", "ix_market_listings_search", "ix_market_listings_seller",
                        "ix_market_listings_expiry", "ix_market_trades_buyer", "ix_market_trades_seller",
                        "ix_market_trades_definition", "ux_trade_executions_prepared_a", "ux_trade_executions_prepared_b",
                        "ux_reward_inbox_recovery_source"
                    }
                })).ToArray();
            string recoveryIndex = await connection.QuerySingleAsync<string>(
                "SELECT indexdef FROM pg_indexes WHERE schemaname = current_schema() AND indexname = 'ux_reward_inbox_recovery_source';");

            Assert.Equal(0L, unvalidated);
            Assert.Equal(10, indexes.Length);
            Assert.Contains("MarketReturn", recoveryIndex, StringComparison.Ordinal);
        }

        /// <summary> down은 경매장, 거래 테이블과 보관 표시, MarketReturn 회수함 행을 지우고 이전 CHECK를 복원한다. 다시 up하면 모두 돌아온다 </summary>
        [PostgresFact]
        public async Task Migration0015_DownThenUp_RemovesMarketStateAndRestoresOnReapply()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync();
            Guid item = await environment.Growth.InsertEquipmentAsync(seller, MarketTestEnvironment.WEAPON, 0);
            MarketTestEnvironment.Listed(await environment.ListEquipmentAsync(seller, item, 10));
            await using NpgsqlConnection connection = await environment.DataSource.OpenConnectionAsync();
            await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync())
            {
                await RecoveryInbox.InsertAsync(connection, transaction, RecoveryInbox.SOURCE_MARKET_RETURN,
                    Guid.NewGuid().ToString("D"), new[]
                    {
                        new RecoveryLine(seller.AccountId, null, EconomyAsset.ACCOUNT_ITEM, MarketTestEnvironment.HERB, "Account", 3)
                    }, CancellationToken.None);
                await transaction.CommitAsync();
            }
            (long version, string name) = MigrationTestResources.All.Single(entry => entry.Name == MIGRATION_0015);

            await MigrationTestResources.RollBackAboveAsync(connection, version - 1);

            Assert.Equal(0L, await connection.QuerySingleAsync<long>(
                """
                SELECT count(*) FROM information_schema.tables
                WHERE table_schema = current_schema()
                    AND table_name IN ('market_listings', 'market_trades', 'player_trade_executions');
                """));
            Assert.Equal(0L, await connection.QuerySingleAsync<long>(
                """
                SELECT count(*) FROM information_schema.columns
                WHERE table_schema = current_schema() AND column_name IN ('escrow_kind', 'escrow_ref');
                """));
            Assert.Equal(0L, await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM reward_inbox WHERE source_kind = 'MarketReturn';"));
            Assert.Equal(1L, await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM equipment_instances WHERE item_id = @Item;", new { Item = item }));
            // 복원한 CHECK는 새 출처를 막는다
            await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                """
                INSERT INTO reward_inbox (inbox_id, account_id, source_kind, grant_id, source_ref, rewards, expires_at,
                    acquired_at)
                VALUES (gen_random_uuid(), @AccountId, 'MarketReturn', NULL, 'x', '[{"assetKind":"Currency"}]'::jsonb,
                    clock_timestamp() + interval '1 day', clock_timestamp());
                """, new { seller.AccountId }));

            await connection.ExecuteAsync(await MigrationTestResources.ReadUpAsync(name));

            Assert.Equal(3L, await connection.QuerySingleAsync<long>(
                """
                SELECT count(*) FROM information_schema.tables
                WHERE table_schema = current_schema()
                    AND table_name IN ('market_listings', 'market_trades', 'player_trade_executions');
                """));
            Assert.Equal(0L, await connection.QuerySingleAsync<long>(
                """
                SELECT count(*) FROM pg_constraint AS constraint_row
                JOIN pg_class AS table_row ON table_row.oid = constraint_row.conrelid
                JOIN pg_namespace AS schema_row ON schema_row.oid = table_row.relnamespace
                WHERE schema_row.nspname = current_schema() AND NOT constraint_row.convalidated;
                """));
        }
    }
}
