// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

// Project
using Shinshi.Data;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Sessions
{
    /// <summary> 만료 예약 해제, 소실 프로세스의 출정 종결, 만료 입장 토큰 정리를 주기적으로 실행한다. 여러 API 인스턴스가 함께 돌아도 한 번만 적용된다 </summary>
    public sealed class GameSessionMaintenanceService : BackgroundService
    {
        /****************************************
        *                Fields
        ****************************************/

        // heartbeat 주기와 같다. 만료 판정 자체는 DB 시각이라 주기는 해제 지연만 정한다
        private const int INTERVAL_SECONDS     = 5;
        private const int MAX_RELEASES_PER_RUN = 32;
        private const int MAX_RECOVERY_PER_RUN = 16;
        private const int MAX_PURGES_PER_RUN   = 500;

        private readonly IGameSessionMaintenanceWork _work;
        private readonly ILogger<GameSessionMaintenanceService> _logger;
        private readonly TimeProvider _timeProvider;
        private DateTimeOffset _recoveryAllowedAt;

        /****************************************
        *                Methods
        ****************************************/

        public GameSessionMaintenanceService(IGameSessionMaintenanceWork work, ILogger<GameSessionMaintenanceService> logger,
            TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(work);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(timeProvider);
            _work               = work;
            _logger             = logger;
            _timeProvider       = timeProvider;
            _recoveryAllowedAt  = timeProvider.GetUtcNow() + ServerPoolTimings.StartupRecoveryGrace;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(INTERVAL_SECONDS), _timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }

        /// <summary> 한 주기 실행. 실패해도 다음 주기에 같은 대상을 다시 처리한다 </summary>
        public async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            try
            {
                int released = await _work.ReleaseExpiredAsync(MAX_RELEASES_PER_RUN, cancellationToken);
                SessionRecoveryResult recovered = IsRecoveryDeferred()
                    ? new SessionRecoveryResult(0, 0)
                    : await _work.RecoverLostSessionsAsync(MAX_RECOVERY_PER_RUN, cancellationToken);
                int purged = await _work.PurgeExpiredJoinTokensAsync(MAX_PURGES_PER_RUN, cancellationToken);
                if ((released > 0) || (recovered.Settled > 0) || (recovered.Cancelled > 0) || (purged > 0))
                {
                    _logger.LogInformation(
                        "Game session maintenance released {Released}, settled {Settled}, cancelled {Cancelled}, purged {Purged}.",
                        released, recovered.Settled, recovered.Cancelled, purged);
                }
                LogFailures(recovered);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // DB 예외 메시지에 연결 정보가 섞일 수 있어 종류와 SQLSTATE만 남긴다
                string sqlState = (exception as PostgresException)?.SqlState ?? string.Empty;
                if (PostgresTransactionPolicy.IsConnectionFailure(exception))
                {
                    DeferRecovery();
                    _logger.LogWarning("Game session maintenance could not reach the database ({ExceptionType}, {SqlState}); "
                        + "lost-session recovery is deferred until {RecoveryAllowedAt}.",
                        exception.GetType().Name, sqlState, _recoveryAllowedAt);
                    return;
                }

                _logger.LogError("Game session maintenance failed ({ExceptionType}, {SqlState}).",
                    exception.GetType().Name, sqlState);
            }
        }

        // API가 막 시작했거나 DB에 닿지 못했던 동안 서버들의 heartbeat도 저장되지 못했다. 그 직후의 오래된 heartbeat를
        // 서버 소실로 보면 살아 있는 출정을 장애 취소(환급)하므로, 서버들이 heartbeat를 다시 보낼 시간만큼 복구를 미룬다.
        // 만료 예약 해제는 생존 판정이 아니라 계속한다
        private bool IsRecoveryDeferred()
        {
            return _timeProvider.GetUtcNow() < _recoveryAllowedAt;
        }

        private void DeferRecovery()
        {
            DateTimeOffset allowedAt = _timeProvider.GetUtcNow() + ServerPoolTimings.StartupRecoveryGrace;
            if (allowedAt > _recoveryAllowedAt)
            {
                _recoveryAllowedAt = allowedAt;
            }
        }

        private void LogFailures(SessionRecoveryResult recovered)
        {
            foreach (SessionRecoveryFailure failure in recovered.Failures)
            {
                if (failure.Retryable)
                {
                    _logger.LogInformation("Game session recovery postponed for {SessionId} ({ReasonKind}).",
                        failure.SessionId, failure.Kind);
                    continue;
                }
                if (failure.Quarantined)
                {
                    _logger.LogError("Game session recovery quarantined {SessionId} after {Failures} failures ({ReasonKind}).",
                        failure.SessionId, failure.Failures, failure.Kind);
                    continue;
                }

                _logger.LogWarning("Game session recovery failed for {SessionId} ({Failures}, {ReasonKind}).",
                    failure.SessionId, failure.Failures, failure.Kind);
            }
        }
    }
}
