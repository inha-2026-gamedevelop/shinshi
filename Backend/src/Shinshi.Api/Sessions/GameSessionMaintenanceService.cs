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
        private readonly RecoveryLivenessGate _liveness;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이 서비스만 쓰는 보류로 만든다. 운영자 수동 복구와 보류를 나누려면 DI가 쓰는 공유 게이트 생성자를 쓴다 </summary>
        public GameSessionMaintenanceService(IGameSessionMaintenanceWork work, ILogger<GameSessionMaintenanceService> logger,
            TimeProvider timeProvider)
            : this(work, logger, timeProvider, new RecoveryLivenessGate(timeProvider))
        {
        }

        public GameSessionMaintenanceService(IGameSessionMaintenanceWork work, ILogger<GameSessionMaintenanceService> logger,
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
                // 만료 예약 해제는 생존 판정이 아니므로 보류 중에도 계속한다
                int released = await _work.ReleaseExpiredAsync(MAX_RELEASES_PER_RUN, cancellationToken);
                SessionRecoveryResult recovered = _liveness.IsDeferred()
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
                    _liveness.Defer();
                    _logger.LogWarning("Game session maintenance could not reach the database ({ExceptionType}, {SqlState}); "
                        + "lost-session recovery is deferred until {RecoveryAllowedAt}.",
                        exception.GetType().Name, sqlState, _liveness.AllowedAt);
                    return;
                }

                _logger.LogError("Game session maintenance failed ({ExceptionType}, {SqlState}).",
                    exception.GetType().Name, sqlState);
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
