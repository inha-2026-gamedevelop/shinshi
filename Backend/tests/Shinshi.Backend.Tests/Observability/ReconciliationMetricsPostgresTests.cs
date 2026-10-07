// System
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Observability;

namespace Shinshi.Backend.Tests.Observability
{
    /// <summary> 실제 DB 완료 기록, 복제본 간 대사 관측과 잠금 지연 시 스크레이프 제한을 검사한다 </summary>
    public sealed class ReconciliationMetricsPostgresTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task ReadAsync_NoCompletedRuns_ObservedOutboxZeroAndUnknownMismatchCount()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using ServiceProvider services = new ServiceCollection().AddSingleton(database.DataSource).BuildServiceProvider();

            DatabaseMetricsSnapshot result = await Reader(services).ReadAsync(CancellationToken.None);

            Assert.Equal(0, result.OutboxPending);
            Assert.Equal(0, result.LastRunTimestampSeconds);
            Assert.Null(result.LastMismatchCount);
        }

        [PostgresFact]
        public async Task ReadAsync_PreviousDayRunFinishesLast_UsesFinishTimeAndReplicasMatch()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            DateTimeOffset latestFinish = DateTimeOffset.Parse("2026-10-06T04:00:00Z",
                System.Globalization.CultureInfo.InvariantCulture);
            await connection.ExecuteAsync(
                """
                INSERT INTO economy_reconciliation_runs (run_date, claimed_by, finished_at, mismatch_count)
                VALUES ('2026-10-04', @Worker, @LatestFinish, 4),
                       ('2026-10-05', @Worker, @EarlierFinish, 1),
                       ('2026-10-06', @Worker, NULL, NULL);
                """, new { Worker = Guid.NewGuid(), LatestFinish = latestFinish, EarlierFinish = latestFinish.AddHours(-1) });
            using ServiceProvider first = new ServiceCollection().AddSingleton(database.DataSource).BuildServiceProvider();
            using ServiceProvider second = new ServiceCollection().AddSingleton(database.DataSource).BuildServiceProvider();

            DatabaseMetricsSnapshot firstResult = await Reader(first).ReadAsync(CancellationToken.None);
            DatabaseMetricsSnapshot secondResult = await Reader(second).ReadAsync(CancellationToken.None);

            Assert.Equal(latestFinish.ToUnixTimeSeconds(), firstResult.LastRunTimestampSeconds);
            Assert.Equal(4, firstResult.LastMismatchCount);
            Assert.Equal(firstResult.LastRunTimestampSeconds, secondResult.LastRunTimestampSeconds);
            Assert.Equal(firstResult.LastMismatchCount, secondResult.LastMismatchCount);
        }

        [PostgresFact]
        public async Task ReadAsync_MixedDeliveryStates_CountsOnlyUndeliveredRows()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO operations (actor_id, operation_id, operation_kind, request_hash, status)
                VALUES (@Actor, @Operation, 'metrics-test', repeat('F', 64), 'Pending');
                INSERT INTO outbox (event_id, aggregate_id, consumer_key, event_kind, payload, actor_id, operation_id, delivered_at)
                VALUES (@First, @Aggregate, 'metrics-test', 'test', '{}', @Actor, @Operation, NULL),
                       (@Second, @Aggregate, 'metrics-test', 'test', '{}', @Actor, @Operation, NULL),
                       (@Third, @Aggregate, 'metrics-test', 'test', '{}', @Actor, @Operation, clock_timestamp());
                """, new
                {
                    Actor = Guid.NewGuid(), Operation = Guid.NewGuid(), Aggregate = Guid.NewGuid(),
                    First = Guid.NewGuid(), Second = Guid.NewGuid(), Third = Guid.NewGuid()
                });
            using ServiceProvider services = new ServiceCollection().AddSingleton(database.DataSource).BuildServiceProvider();

            DatabaseMetricsSnapshot result = await Reader(services).ReadAsync(CancellationToken.None);

            Assert.Equal(2, result.OutboxPending);
        }

        [PostgresFact]
        public async Task ReadAsync_OutboxLocked_ReturnsUnknownWithinDeadline()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection blocker = await database.DataSource.OpenConnectionAsync();
            await using NpgsqlTransaction transaction = await blocker.BeginTransactionAsync();
            await blocker.ExecuteAsync("LOCK TABLE outbox IN ACCESS EXCLUSIVE MODE;", transaction: transaction);
            using ServiceProvider services = new ServiceCollection().AddSingleton(database.DataSource).BuildServiceProvider();
            Stopwatch timer = Stopwatch.StartNew();

            DatabaseMetricsSnapshot result = await Reader(services).ReadAsync(CancellationToken.None);

            Assert.InRange(timer.Elapsed.TotalSeconds, 1.8, 5);
            Assert.Null(result.OutboxPending);
            Assert.Null(result.LastRunTimestampSeconds);
            Assert.Null(result.LastMismatchCount);
            await transaction.RollbackAsync();
        }

        private static PostgresMetricsDatabaseReader Reader(IServiceProvider services)
        {
            return new PostgresMetricsDatabaseReader(services, true, NullLogger<PostgresMetricsDatabaseReader>.Instance);
        }
    }
}
