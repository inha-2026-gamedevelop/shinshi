// System
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

namespace Shinshi.Data.Ledger
{
    /// <summary> 일일 경제 대사의 하루 한 번 선점. 날짜는 DB의 UTC 날짜이며 여러 API 인스턴스 중 먼저 넣은 쪽만 그날 대사를 돈다 </summary>
    public sealed class PostgresReconciliationRunRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string DATE_FORMAT = "yyyy-MM-dd";

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresReconciliationRunRepository(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> 오늘 실행을 선점하면 그 날짜, 이미 끝났거나 다른 작업자가 진행 중이면 null이다.
        /// staleAfter보다 오래 끝나지 않은 선점은 작업자가 죽은 것으로 보고 이어받는다 </summary>
        public async Task<DateOnly?> TryClaimAsync(Guid workerId, TimeSpan staleAfter, CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            string? claimed = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
                """
                INSERT INTO economy_reconciliation_runs AS run (run_date, claimed_by)
                VALUES ((clock_timestamp() AT TIME ZONE 'UTC')::date, @Worker)
                ON CONFLICT (run_date) DO UPDATE SET claimed_by = EXCLUDED.claimed_by, started_at = clock_timestamp()
                    WHERE run.finished_at IS NULL AND run.started_at < clock_timestamp() - @StaleAfter
                RETURNING run.run_date::text;
                """, new { Worker = workerId, StaleAfter = staleAfter }, cancellationToken: cancellationToken));
            return claimed == null ? null : DateOnly.ParseExact(claimed, DATE_FORMAT, CultureInfo.InvariantCulture);
        }

        /// <summary> 진행 중인 선점의 시작 시각을 지금으로 갱신한다(heartbeat). 오래 걸리는 대사가 stale로 보여 다른 작업자가
        /// 이어받아 이중으로 돌지 않게 한다. 다른 작업자가 이미 이어받았거나 끝났으면 false다 </summary>
        public async Task<bool> HeartbeatAsync(DateOnly runDate, Guid workerId, CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            int updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE economy_reconciliation_runs SET started_at = clock_timestamp()
                WHERE run_date = CAST(@RunDate AS date) AND claimed_by = @Worker AND finished_at IS NULL;
                """, new
                {
                    RunDate = runDate.ToString(DATE_FORMAT, CultureInfo.InvariantCulture),
                    Worker = workerId
                }, cancellationToken: cancellationToken));
            return updated == 1;
        }

        /// <summary> 선점한 작업자만 그날 실행을 끝낸다. 다른 작업자가 이어받았으면 false다 </summary>
        public async Task<bool> FinishAsync(DateOnly runDate, Guid workerId, long mismatchCount,
            CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            int updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE economy_reconciliation_runs SET finished_at = clock_timestamp(), mismatch_count = @Count
                WHERE run_date = CAST(@RunDate AS date) AND claimed_by = @Worker AND finished_at IS NULL;
                """, new
                {
                    RunDate = runDate.ToString(DATE_FORMAT, CultureInfo.InvariantCulture),
                    Worker = workerId,
                    Count = mismatchCount
                }, cancellationToken: cancellationToken));
            return updated == 1;
        }
    }
}
