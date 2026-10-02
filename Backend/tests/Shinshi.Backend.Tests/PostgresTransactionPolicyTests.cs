// System
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Backend.Tests
{
    /// <summary> 트랜잭션 제한의 적용 범위, 재시도 소진과 사용자 취소를 구분한다 </summary>
    public sealed class PostgresTransactionPolicyTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_ATTEMPTS             = 2;
        private const int SINGLE_ATTEMPT           = 1;
        private const int SLOW_STATEMENT_SECONDS   = 6;
        private const int COMMAND_TIMEOUT_SECONDS = 10;
        private const string OPERATION_KIND       = "test-transaction-timeout";
        private const string REQUEST_BODY         = "{}";
        private const string LOCK_TIMEOUT         = "2s";
        private const string STATEMENT_TIMEOUT    = "5s";
        private const string SETTINGS_SQL         = "SELECT current_setting('lock_timeout') AS \"LockTimeout\", "
            + "current_setting('statement_timeout') AS \"StatementTimeout\";";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 직렬화 실패, 데드락, 잠금 제한과 문장 제한만 PostgreSQL 재시도 대상으로 분류한다 </summary>
        [Theory]
        [InlineData(PostgresErrorCodes.SerializationFailure, true)]
        [InlineData(PostgresErrorCodes.DeadlockDetected, true)]
        [InlineData(PostgresErrorCodes.LockNotAvailable, true)]
        [InlineData(PostgresErrorCodes.QueryCanceled, true)]
        [InlineData(PostgresErrorCodes.CheckViolation, false)]
        [InlineData(PostgresErrorCodes.UniqueViolation, false)]
        public void IsRetryable_SqlState_ClassifiesExpectedDatabaseFailures(string sqlState, bool expected)
        {
            Assert.Equal(expected, PostgresTransactionPolicy.IsRetryable(CreateException(sqlState)));
        }

        /// <summary> 경합 재시도와 타임아웃 종료 사유를 서로 구분한다 </summary>
        [Theory]
        [InlineData(PostgresErrorCodes.LockNotAvailable, true)]
        [InlineData(PostgresErrorCodes.QueryCanceled, true)]
        [InlineData(PostgresErrorCodes.SerializationFailure, false)]
        [InlineData(PostgresErrorCodes.DeadlockDetected, false)]
        [InlineData(PostgresErrorCodes.CheckViolation, false)]
        public void IsTimeout_SqlState_IdentifiesOnlyTimeoutStates(string sqlState, bool expected)
        {
            Assert.Equal(expected, PostgresTransactionPolicy.IsTimeout(CreateException(sqlState)));
        }

        /// <summary> 드라이버 타임아웃은 재시도하고 사용자 취소는 재시도하지 않는다 </summary>
        [Fact]
        public void IsRetryable_DriverTimeoutAndCancellation_KeepsDifferentOutcomes()
        {
            NpgsqlException timeout = new NpgsqlException("Driver timeout.", new TimeoutException());
            OperationCanceledException cancellation = new OperationCanceledException();

            Assert.True(PostgresTransactionPolicy.IsTimeout(timeout));
            Assert.True(PostgresTransactionPolicy.IsRetryable(timeout));
            Assert.False(PostgresTransactionPolicy.IsTimeout(cancellation));
            Assert.False(PostgresTransactionPolicy.IsRetryable(cancellation));
        }

        /// <summary> 재시도 한도에서 타임아웃을 재시도 가능한 타입으로 노출하고 원래 예외와 작업 ID를 보존한다 </summary>
        [Theory]
        [InlineData(PostgresErrorCodes.LockNotAvailable)]
        [InlineData(PostgresErrorCodes.QueryCanceled)]
        public async Task ExecuteAsync_TimeoutRetryLimitReached_ThrowsTypedFailureWithOriginalException(string sqlState)
        {
            PostgresException original = CreateException(sqlState);
            FailingOperationStore store = new FailingOperationStore(original);
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(store, MAX_ATTEMPTS);
            OperationRequest request = CreateRequest();

            RetryableDatabaseException failure = await Assert.ThrowsAsync<RetryableDatabaseException>(() =>
                executor.ExecuteAsync(request, UnexpectedEffect));

            Assert.True(failure.IsRetryable);
            Assert.Same(original, failure.InnerException);
            Assert.Equal(MAX_ATTEMPTS, store.Requests.Count);
            Assert.All(store.Requests, attempted => Assert.Same(request, attempted));
        }

        /// <summary> 취소와 DB 취소 상태가 함께 도착하면 사용자 취소를 반환하고 재시도하지 않는다 </summary>
        [Fact]
        public async Task ExecuteAsync_CallerCancelledDuringDatabaseTimeout_PreservesCancellation()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            FailingOperationStore store = new FailingOperationStore(CreateException(PostgresErrorCodes.QueryCanceled),
                cancellation.Cancel);
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(store, MAX_ATTEMPTS);

            OperationCanceledException failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                executor.ExecuteAsync(CreateRequest(), UnexpectedEffect, cancellation.Token));

            Assert.Equal(cancellation.Token, failure.CancellationToken);
            Assert.Single(store.Requests);
        }

        /// <summary> SET LOCAL은 실제 트랜잭션에서만 적용되고 커밋과 롤백 뒤에는 원래 세션 설정으로 돌아간다 </summary>
        [PostgresFact]
        public async Task ApplyAsync_CommitAndRollback_RestoresOriginalSessionTimeouts()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            TimeoutSettings original = await ReadSettingsAsync(connection);

            await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync())
            {
                await PostgresTransactionPolicy.ApplyAsync(connection, transaction);
                AssertPolicy(await ReadSettingsAsync(connection, transaction));
                await transaction.CommitAsync();
            }
            AssertSettings(original, await ReadSettingsAsync(connection));

            await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync())
            {
                await PostgresTransactionPolicy.ApplyAsync(connection, transaction);
                AssertPolicy(await ReadSettingsAsync(connection, transaction));
                await transaction.RollbackAsync();
            }
            AssertSettings(original, await ReadSettingsAsync(connection));
        }

        /// <summary> 저장소를 통한 경제 작업 콜백도 잠금, 문장 제한을 상속한다 </summary>
        [PostgresFact]
        public async Task ExecuteAsync_OperationCallback_UsesConfiguredTransactionTimeouts()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(new PostgresOperationStore(database.DataSource));

            OperationResult result = await executor.ExecuteAsync(CreateRequest(), async (context, cancellationToken) =>
            {
                NpgsqlConnection operationConnection = Assert.IsType<NpgsqlConnection>(context.Connection);
                NpgsqlTransaction operationTransaction = Assert.IsType<NpgsqlTransaction>(context.Transaction);
                AssertPolicy(await ReadSettingsAsync(operationConnection, operationTransaction, cancellationToken));
                return OperationDecision.Rejected(ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            });

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, result.Status);
        }

        /// <summary> 실제 문장 제한으로 종료된 효과와 Pending 행을 롤백하고 재시도 가능한 장애를 반환한다 </summary>
        [PostgresFact]
        public async Task ExecuteAsync_StatementTimeout_RollsBackEffectsAndThrowsTypedFailure()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("CREATE TABLE test_timeout_effects (operation_id uuid PRIMARY KEY);");
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(
                new PostgresOperationStore(database.DataSource), SINGLE_ATTEMPT);
            OperationRequest request = CreateRequest();

            RetryableDatabaseException failure = await Assert.ThrowsAsync<RetryableDatabaseException>(() =>
                executor.ExecuteAsync(request, async (context, cancellationToken) =>
                {
                    NpgsqlConnection operationConnection = Assert.IsType<NpgsqlConnection>(context.Connection);
                    NpgsqlTransaction operationTransaction = Assert.IsType<NpgsqlTransaction>(context.Transaction);
                    await operationConnection.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO test_timeout_effects (operation_id) VALUES (@OperationId);", request,
                        operationTransaction, cancellationToken: cancellationToken));
                    await operationConnection.ExecuteAsync(new CommandDefinition("SELECT pg_sleep(@Delay);",
                        new { Delay = SLOW_STATEMENT_SECONDS }, operationTransaction,
                        commandTimeout: COMMAND_TIMEOUT_SECONDS, cancellationToken: cancellationToken));
                    return OperationDecision.Committed(0, "unreachable-timeout-receipt");
                }));

            Assert.True(failure.IsRetryable);
            Assert.Equal(PostgresErrorCodes.QueryCanceled, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
            Assert.Equal(0L, await connection.QuerySingleAsync<long>("SELECT count(*) FROM test_timeout_effects;"));
            Assert.Equal(0L, await connection.QuerySingleAsync<long>("SELECT count(*) FROM operations;"));
        }

        /// <summary> 첫 마이그레이션부터 이력 INSERT와 적용 SQL이 같은 제한된 트랜잭션을 사용한다 </summary>
        [PostgresFact]
        public async Task ApplyAsync_MigrationHistoryInsert_UsesConfiguredTransactionTimeouts()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(applyMigrations: false);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                CREATE TABLE schema_migrations
                (
                    version bigint PRIMARY KEY CHECK (version > 0),
                    name text NOT NULL,
                    checksum text NOT NULL CHECK (checksum ~ '^[0-9A-F]{64}$'),
                    applied_at timestamptz NOT NULL DEFAULT clock_timestamp()
                );
                CREATE TABLE migration_timeout_observations (lock_timeout text NOT NULL, statement_timeout text NOT NULL);
                CREATE FUNCTION observe_migration_timeouts() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    INSERT INTO migration_timeout_observations (lock_timeout, statement_timeout)
                    VALUES (current_setting('lock_timeout'), current_setting('statement_timeout'));
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER observe_migration_timeouts BEFORE INSERT ON schema_migrations
                FOR EACH ROW EXECUTE FUNCTION observe_migration_timeouts();
                """);

            int applied = await database.Migrations.ApplyAsync();
            TimeoutSettings[] observations = (await connection.QueryAsync<TimeoutSettings>(
                "SELECT lock_timeout AS \"LockTimeout\", statement_timeout AS \"StatementTimeout\" FROM migration_timeout_observations;"))
                .AsList().ToArray();

            Assert.True(applied > 0);
            Assert.Equal(applied, observations.Length);
            Assert.All(observations, AssertPolicy);
        }

        private static OperationRequest CreateRequest()
        {
            return new OperationRequest(Guid.NewGuid(), Guid.NewGuid(), OPERATION_KIND, REQUEST_BODY);
        }

        private static PostgresException CreateException(string sqlState)
        {
            return new PostgresException("Simulated database failure.", "ERROR", "ERROR", sqlState);
        }

        private static Task<OperationDecision> UnexpectedEffect(OperationExecutionContext context,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("An effect cannot run when its transaction cannot begin.");
        }

        private static async Task<TimeoutSettings> ReadSettingsAsync(NpgsqlConnection connection,
            NpgsqlTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            return await connection.QuerySingleAsync<TimeoutSettings>(new CommandDefinition(SETTINGS_SQL,
                transaction: transaction, cancellationToken: cancellationToken));
        }

        private static void AssertPolicy(TimeoutSettings settings)
        {
            Assert.Equal(LOCK_TIMEOUT, settings.LockTimeout);
            Assert.Equal(STATEMENT_TIMEOUT, settings.StatementTimeout);
        }

        private static void AssertSettings(TimeoutSettings expected, TimeoutSettings actual)
        {
            Assert.Equal(expected.LockTimeout, actual.LockTimeout);
            Assert.Equal(expected.StatementTimeout, actual.StatementTimeout);
        }

        private sealed class TimeoutSettings
        {
            /****************************************
            *              Properties
            ****************************************/

            public string LockTimeout { get; set; } = string.Empty;
            public string StatementTimeout { get; set; } = string.Empty;
        }

        private sealed class FailingOperationStore : IOperationStore
        {
            /****************************************
            *                Fields
            ****************************************/

            private readonly Exception _exception;
            private readonly Action? _beforeFailure;

            /****************************************
            *              Properties
            ****************************************/

            public List<OperationRequest> Requests { get; } = new List<OperationRequest>();

            /****************************************
            *                Methods
            ****************************************/

            public FailingOperationStore(Exception exception, Action? beforeFailure = null)
            {
                _exception     = exception;
                _beforeFailure = beforeFailure;
            }

            public Task<IOperationScope> BeginAsync(OperationRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                _beforeFailure?.Invoke();
                return Task.FromException<IOperationScope>(_exception);
            }

            public bool IsRetryable(Exception exception)
            {
                return PostgresTransactionPolicy.IsRetryable(exception);
            }
        }
    }
}
