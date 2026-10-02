using System;
using System.Threading;
using System.Threading.Tasks;

using Dapper;
using Npgsql;

using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Ledger;

namespace Shinshi.Backend.Tests
{
    internal static class LedgerTestSupport
    {
        /****************************************
        *                Fields
        ****************************************/

        // 사유 코드 도입 전의 독립 테스트 데이터에만 사용하는 값
        public const int TEST_REASON_CODE     = 1;
        public const long GRANT_AMOUNT       = 9007199254740993L;
        public const long COMMITTED_REVISION = 1L;
        public const string OPERATION_KIND   = "test-ledger-grant";
        public const string REQUEST_JSON     = "{\"grant\":\"initial\"}";
        public const string CONSUMER_KEY     = "test-ledger-consumer";
        public const string EVENT_KIND       = "EconomyCommitted";
        public const string PAYLOAD_JSON     = "{\"asset\":\"currency\"}";
        public const string RECEIPT_ID       = "test-ledger-receipt";
        public const string ITEM_DEFINITION  = "test-herb";
        public const string UNBOUND          = "Unbound";
        public const string ACCOUNT_BOUND    = "Account";

        /****************************************
        *                Methods
        ****************************************/

        public static OperationRequest CreateRequest(Guid actorId)
        {
            return new OperationRequest(actorId, Guid.NewGuid(), OPERATION_KIND, REQUEST_JSON);
        }

        public static EconomyLedgerEntry CreateEntry(OperationRequest request, EconomyAsset asset,
            long delta = GRANT_AMOUNT, long balanceAfter = GRANT_AMOUNT, string entryKey = "currency")
        {
            return new EconomyLedgerEntry(Guid.NewGuid(), entryKey, asset, delta, balanceAfter,
                TEST_REASON_CODE, "InitialGrant", "test-grant", "System",
                request.ActorId, request.OperationId);
        }

        public static OutboxMessage CreateMessage(OperationRequest request, Guid eventId,
            string consumerKey = CONSUMER_KEY, string payloadJson = PAYLOAD_JSON)
        {
            return new OutboxMessage(eventId, request.ActorId, consumerKey, EVENT_KIND, payloadJson,
                request.ActorId, request.OperationId);
        }

        public static async Task SeedAccountAsync(PostgresTestDatabase database, Guid accountId,
            Guid? characterId = null)
        {
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO accounts (account_id) VALUES (@AccountId);
                INSERT INTO account_wallets (account_id) VALUES (@AccountId);
                """, new { AccountId = accountId });
            if (characterId != null)
            {
                await connection.ExecuteAsync(
                    """
                    INSERT INTO characters (character_id, account_id, class_id)
                    VALUES (@CharacterId, @AccountId, @ClassId);
                    """, new { CharacterId = characterId.Value, AccountId = accountId, ClassId = ContractCodes.PlayerClass.WARRIOR });
            }
        }

        public static async Task<long> AddCurrencyAsync(OperationExecutionContext context, Guid accountId,
            long amount, CancellationToken cancellationToken)
        {
            if ((context.Connection == null) || (context.Transaction == null))
            {
                throw new InvalidOperationException("The test effect requires the operation transaction.");
            }
            return await context.Connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                UPDATE account_wallets SET currency = currency + @Amount, revision = revision + 1
                WHERE account_id = @AccountId RETURNING currency;
                """, new { AccountId = accountId, Amount = amount }, context.Transaction,
                cancellationToken: cancellationToken));
        }

        public static async Task CreateConsumerEffectsAsync(PostgresTestDatabase database)
        {
            await using NpgsqlCommand command = database.DataSource.CreateCommand(
                """
                CREATE TABLE test_outbox_effects
                (
                    consumer_key text NOT NULL,
                    event_id uuid NOT NULL,
                    PRIMARY KEY (consumer_key, event_id)
                );
                """);
            await command.ExecuteNonQueryAsync();
        }

        public static async Task InsertConsumerEffectAsync(OperationExecutionContext context,
            OutboxDelivery delivery, CancellationToken cancellationToken)
        {
            if ((context.Connection == null) || (context.Transaction == null))
            {
                throw new InvalidOperationException("The consumer must receive the dispatcher transaction.");
            }
            await context.Connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO test_outbox_effects (consumer_key, event_id) VALUES (@ConsumerKey, @EventId);",
                new { delivery.ConsumerKey, delivery.EventId }, context.Transaction,
                cancellationToken: cancellationToken));
        }

        public static async Task<T> QueryAsync<T>(PostgresTestDatabase database, string sql, object? parameters = null)
        {
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<T>(sql, parameters);
        }
    }
}


