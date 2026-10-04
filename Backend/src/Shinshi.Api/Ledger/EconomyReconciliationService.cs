// System
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

// Project
using Shinshi.Data;
using Shinshi.Data.Ledger;

namespace Shinshi.Backend.Ledger
{
    /// <summary> 하루 한 번 자산별 원장 합계와 현재 잔액을 대조한다. 여러 API 인스턴스가 돌아도 선점 행으로 한 번만 실행하며
    /// 불일치는 금액 없이 ID만 ERROR로 남긴다 </summary>
    public sealed class EconomyReconciliationService : BackgroundService
    {
        /****************************************
        *                Fields
        ****************************************/

        // 선점 확인 주기. 날짜가 바뀐 뒤 이 시간 안에 그날 대사가 시작된다
        private const int CHECK_INTERVAL_MINUTES = 15;

        // 끝나지 않은 선점을 죽은 작업자의 것으로 보는 시간
        private const int STALE_CLAIM_HOURS = 2;

        private const int BATCH_SIZE = 500;

        private readonly EconomyLedgerReconciler _reconciler;
        private readonly PostgresReconciliationRunRepository _runs;
        private readonly ILogger<EconomyReconciliationService> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly Guid _workerId = Guid.NewGuid();

        /****************************************
        *                Methods
        ****************************************/

        public EconomyReconciliationService(EconomyLedgerReconciler reconciler, PostgresReconciliationRunRepository runs,
            ILogger<EconomyReconciliationService> logger, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(reconciler);
            ArgumentNullException.ThrowIfNull(runs);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(timeProvider);
            _reconciler   = reconciler;
            _runs         = runs;
            _logger       = logger;
            _timeProvider = timeProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await RunOnceAsync(stoppingToken);
            using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromMinutes(CHECK_INTERVAL_MINUTES), _timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }

        /// <summary> 오늘 실행을 선점했으면 전체를 훑고 불일치 수를 돌려준다. 선점하지 못했거나 실패하면 null이다 </summary>
        public async Task<long?> RunOnceAsync(CancellationToken cancellationToken)
        {
            try
            {
                DateOnly? runDate = await _runs.TryClaimAsync(_workerId, TimeSpan.FromHours(STALE_CLAIM_HOURS),
                    cancellationToken);
                if (!runDate.HasValue)
                {
                    return null;
                }

                // 계정 창마다 선점 시작 시각을 갱신해, 오래 걸리는 대사를 다른 작업자가 stale로 보고 이중 실행하지 않게 한다
                bool claimLost = false;
                async Task<bool> HeartbeatAsync(CancellationToken token)
                {
                    claimLost = !await _runs.HeartbeatAsync(runDate.Value, _workerId, token);
                    return !claimLost;
                }

                long mismatches = 0;
                EconomyReconciliationCursor? cursor = null;
                while (true)
                {
                    IReadOnlyList<EconomyReconciliationMismatch> page = await _reconciler.FindMismatchesAsync(BATCH_SIZE,
                        cursor, HeartbeatAsync, cancellationToken);
                    foreach (EconomyReconciliationMismatch mismatch in page)
                    {
                        ++mismatches;
                        _logger.LogError("Economy ledger mismatch on {RunDate}: account {AccountId}, character {CharacterId}, "
                            + "asset {AssetKind} {AssetId} {BindingKind}.", runDate.Value, mismatch.AccountId,
                            mismatch.CharacterId, mismatch.AssetKind, mismatch.AssetId, mismatch.BindingKind);
                    }
                    if (claimLost)
                    {
                        _logger.LogWarning("Economy reconciliation for {RunDate} stopped: the claim moved to another worker.",
                            runDate.Value);
                        return null;
                    }
                    if (page.Count < BATCH_SIZE)
                    {
                        break;
                    }
                    if (!await HeartbeatAsync(cancellationToken))
                    {
                        _logger.LogWarning("Economy reconciliation for {RunDate} stopped: the claim moved to another worker.",
                            runDate.Value);
                        return null;
                    }
                    cursor = page[page.Count - 1].Cursor;
                }

                await _runs.FinishAsync(runDate.Value, _workerId, mismatches, cancellationToken);
                _logger.LogInformation("Economy reconciliation for {RunDate} finished with {Mismatches} mismatches.",
                    runDate.Value, mismatches);
                return mismatches;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // DB 예외 메시지에 연결 정보가 섞일 수 있어 종류와 SQLSTATE만 남긴다. 선점은 오래된 뒤 다시 잡힌다
                string sqlState = (exception as PostgresException)?.SqlState ?? string.Empty;
                _logger.LogError("Economy reconciliation failed ({ExceptionType}, {SqlState}, {Transient}).",
                    exception.GetType().Name, sqlState, PostgresTransactionPolicy.IsConnectionFailure(exception));
                return null;
            }
        }
    }
}
