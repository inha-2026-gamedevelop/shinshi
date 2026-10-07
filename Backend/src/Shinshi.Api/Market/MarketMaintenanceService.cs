// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

// Project
using Shinshi.Backend.Sessions;
using Shinshi.Data;
using Shinshi.Data.Market;

namespace Shinshi.Backend.Market
{
    /// <summary> 만료된 경매장 등록의 반환(1분 간격)과 방치된 거래 실행의 종결(30초 간격)을 주기적으로 실행한다. 여러 API 인스턴스가 함께
    /// 돌아도 등록은 고정 operationId와 SKIP LOCKED로, 거래 실행은 행 잠금과 조건부 갱신으로 한 번만 적용된다.
    /// 시작 직후와 DB 단절 직후에는 출정 복구와 같은 보류 규칙을 따른다 </summary>
    public sealed class MarketMaintenanceService : BackgroundService
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int LISTING_INTERVAL_SECONDS = 60;
        private const int TRADE_INTERVAL_SECONDS   = 30;
        private const int MAX_LISTINGS_PER_RUN     = 50;
        private const int MAX_EXECUTIONS_PER_RUN   = 50;

        private readonly IMarketMaintenanceWork _work;
        private readonly ILogger<MarketMaintenanceService> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly RecoveryLivenessGate _liveness;

        /****************************************
        *                Methods
        ****************************************/

        public MarketMaintenanceService(IMarketMaintenanceWork work, ILogger<MarketMaintenanceService> logger,
            TimeProvider timeProvider, RecoveryLivenessGate liveness)
        {
            ArgumentNullException.ThrowIfNull(work);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(timeProvider);
            ArgumentNullException.ThrowIfNull(liveness);
            _work         = work;
            _logger       = logger;
            _timeProvider = timeProvider;
            _liveness     = liveness;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.WhenAll(
                RunLoopAsync(LISTING_INTERVAL_SECONDS, RunListingExpiryOnceAsync, stoppingToken),
                RunLoopAsync(TRADE_INTERVAL_SECONDS, RunTradeAbandonOnceAsync, stoppingToken));
        }

        /// <summary> 한 주기 실행. 실패해도 다음 주기에 같은 대상을 다시 처리한다 </summary>
        public async Task RunListingExpiryOnceAsync(CancellationToken cancellationToken)
        {
            if (_liveness.IsDeferred())
            {
                return;
            }

            try
            {
                MarketExpiryResult result = await _work.ExpireListingsAsync(MAX_LISTINGS_PER_RUN, cancellationToken);
                if ((result.Expired > 0) || (result.Failed > 0))
                {
                    _logger.LogInformation("Market expiry returned {Expired}, skipped {Skipped}, failed {Failed}.",
                        result.Expired, result.Skipped, result.Failed);
                }

                if (result.FailedListingIds.Count > 0)
                {
                    _logger.LogWarning("Market expiry failed for listings {FailedListingIds}.",
                        string.Join(",", result.FailedListingIds));
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogFailure("Market listing expiry", exception);
            }
        }

        /// <summary> 한 주기 실행. 실패해도 다음 주기에 같은 대상을 다시 처리한다 </summary>
        public async Task RunTradeAbandonOnceAsync(CancellationToken cancellationToken)
        {
            if (_liveness.IsDeferred())
            {
                return;
            }

            try
            {
                int finished = await _work.AbandonTradeExecutionsAsync(MAX_EXECUTIONS_PER_RUN, cancellationToken);
                if (finished > 0)
                {
                    _logger.LogInformation("Trade execution cleanup finished {Finished}.", finished);
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogFailure("Trade execution cleanup", exception);
            }
        }

        private async Task RunLoopAsync(int intervalSeconds, Func<CancellationToken, Task> runOnce,
            CancellationToken stoppingToken)
        {
            using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds), _timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await runOnce(stoppingToken);
            }
        }

        private void LogFailure(string name, Exception exception)
        {
            // DB 예외 메시지에 연결 정보가 섞일 수 있어 종류와 SQLSTATE만 남긴다
            string sqlState = (exception as PostgresException)?.SqlState ?? string.Empty;
            if (PostgresTransactionPolicy.IsConnectionFailure(exception))
            {
                _liveness.Defer();
                _logger.LogWarning("{Work} could not reach the database ({ExceptionType}, {SqlState}); deferred until {AllowedAt}.",
                    name, exception.GetType().Name, sqlState, _liveness.AllowedAt);
                return;
            }

            _logger.LogError("{Work} failed ({ExceptionType}, {SqlState}).", name, exception.GetType().Name, sqlState);
        }
    }
}
