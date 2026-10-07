// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Market
{
    /// <summary> 만료 유지 작업 한 번의 결과 </summary>
    public sealed class MarketExpiryResult
    {
        /****************************************
        *              Properties
        ****************************************/

        public int Expired { get; }

        // 다른 작업이 등록 행을 잡고 있어 건너뛴 수. 다음 주기에 다시 처리한다
        public int Skipped { get; }

        // DB 연결 실패가 아닌 오류로 처리하지 못한 수. 다음 주기에 다시 처리한다
        public int Failed  { get; }

        // 이번 호출에서 처리하지 못한 등록 ID. 호출자가 로그에 남긴다. 같은 인스턴스는 일정 시간 후보에서 뺀다
        public IReadOnlyList<Guid> FailedListingIds { get; }

        /****************************************
        *                Methods
        ****************************************/

        public MarketExpiryResult(int expired, int skipped, int failed)
            : this(expired, skipped, failed, Array.Empty<Guid>())
        {
        }

        public MarketExpiryResult(int expired, int skipped, int failed, IReadOnlyList<Guid> failedListingIds)
        {
            Expired = expired;
            Skipped = skipped;
            Failed  = failed;
            FailedListingIds = failedListingIds ?? Array.Empty<Guid>();
        }
    }

    public sealed partial class PostgresMarketRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        // 처리에 실패한 등록을 후보에서 빼 두는 시간과 보관 상한. 영구 실패 행이 배치를 독점(starvation)하지 않게 한다
        private static readonly TimeSpan FAILED_COOLDOWN = TimeSpan.FromMinutes(10);
        private const int MAX_TRACKED_FAILURES = 256;

        // 인스턴스 안에서만 쓰는 값이다. 다른 인스턴스는 자기 목록을 따로 가지며 정확성은 멱등 작업 ID가 지킨다
        private readonly object _failedLock = new object();
        private readonly Dictionary<Guid, long> _failedUntilTicks = new Dictionary<Guid, long>();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 만료 시각이 지난 활성 등록을 batchSize건까지 닫고 미판매분을 돌려준다. 등록마다 별도 트랜잭션이고 operationId는
        /// 등록 ID에서 만든 고정 값(actor System)이라 어느 API 인스턴스가 몇 번 돌려도 반환은 한 번이다.
        /// 구매나 취소가 잡고 있는 행은 건너뛴다(SKIP LOCKED). DB 연결 실패는 호출자에게 던진다 </summary>
        public async Task<MarketExpiryResult> ExpireDueAsync(int batchSize, CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            }

            Guid[] excluded = SnapshotFailedListings();
            IReadOnlyList<Guid> due;
            await using (DbConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken))
            {
                due = (await connection.QueryAsync<Guid>(new CommandDefinition(
                    """
                    SELECT listing_id FROM market_listings
                    WHERE state = 'Active' AND expires_at <= clock_timestamp()
                        AND listing_id <> ALL(@Failed)
                    ORDER BY expires_at, listing_id
                    LIMIT @Batch;
                    """, new { Batch = batchSize, Failed = excluded }, cancellationToken: cancellationToken))).ToList();
            }

            int expired = 0;
            int skipped = 0;
            int failed = 0;
            List<Guid> failedIds = new List<Guid>();
            foreach (Guid listingId in due)
            {
                try
                {
                    if (await ExpireOneAsync(listingId, cancellationToken))
                    {
                        ++expired;
                    }
                }
                catch (MarketListingBusyException)
                {
                    ++skipped;
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                    && !PostgresTransactionPolicy.IsConnectionFailure(exception))
                {
                    ++failed;
                    failedIds.Add(listingId);
                }
            }

            RememberFailedListings(failedIds);
            return new MarketExpiryResult(expired, skipped, failed, failedIds);
        }

        // 쿨다운이 지난 항목은 정리하고 아직 유효한 실패 ID만 돌려준다
        private Guid[] SnapshotFailedListings()
        {
            long now = DateTime.UtcNow.Ticks;
            lock (_failedLock)
            {
                foreach (Guid id in _failedUntilTicks.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToList())
                {
                    _failedUntilTicks.Remove(id);
                }

                return _failedUntilTicks.Keys.ToArray();
            }
        }

        private void RememberFailedListings(IReadOnlyList<Guid> failedIds)
        {
            if (failedIds.Count == 0)
            {
                return;
            }

            long until = DateTime.UtcNow.Ticks + FAILED_COOLDOWN.Ticks;
            lock (_failedLock)
            {
                foreach (Guid id in failedIds)
                {
                    _failedUntilTicks[id] = until;
                }

                // 상한을 넘으면 만료가 가장 이른 것부터 버린다. 버려진 행은 다음 주기에 다시 후보가 된다
                while (_failedUntilTicks.Count > MAX_TRACKED_FAILURES)
                {
                    Guid oldest = _failedUntilTicks.OrderBy(pair => pair.Value).First().Key;
                    _failedUntilTicks.Remove(oldest);
                }
            }
        }

        // 이 호출이 실제로 반환을 적용했으면 true다. 다른 인스턴스가 먼저 확정해 저장된 결과를 받았으면 효과가 실행되지 않아 false다
        private async Task<bool> ExpireOneAsync(Guid listingId, CancellationToken cancellationToken)
        {
            Guid systemActor = MarketOperationKinds.SystemActorId();
            Guid operationId = MarketOperationKinds.ExpireOperationId(listingId);
            OperationRequest request = new OperationRequest(systemActor, operationId, MarketOperationKinds.EXPIRE,
                SessionDb.ToRequestJson(new { listingId = listingId.ToString("D") }));
            bool applied = false;
            OperationResult result = await _executor.ExecuteAsync(request, async (context, token) =>
            {
                applied = false;
                OperationDecision decision = await ExpireEffectAsync(systemActor, operationId, listingId, context, token);
                applied = decision.Status == ContractCodes.OperationStatus.COMMITTED;
                return decision;
            }, cancellationToken);
            return applied && (result.Status == ContractCodes.OperationStatus.COMMITTED);
        }

        private async Task<OperationDecision> ExpireEffectAsync(Guid systemActor, Guid operationId, Guid listingId,
            OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            MarketListingRow? listing = await LockListingAsync(connection, transaction, listingId, true, cancellationToken);
            if (listing == null)
            {
                // 잠겨 있어 건너뛴 것인지 이미 닫힌 것인지 가른다. 잠긴 것은 저장 없이 되돌려 다음 주기에 다시 처리한다
                string? state = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
                    "SELECT state FROM market_listings WHERE listing_id = @ListingId;", new { ListingId = listingId },
                    transaction, cancellationToken: cancellationToken));
                if (state == MarketCodes.ListingState.ACTIVE)
                {
                    throw new MarketListingBusyException();
                }

                return OperationDecision.Rejected(state == null
                    ? ContractCodes.RejectReason.INVALID_TARGET
                    : ClosedReason(state));
            }
            if (listing.State != MarketCodes.ListingState.ACTIVE)
            {
                return OperationDecision.Rejected(ClosedReason(listing.State), listing.Revision);
            }
            if (!await IsExpiredAsync(connection, transaction, listingId, cancellationToken))
            {
                // 만료 시각은 바뀌지 않으므로 후보 조회 뒤에는 항상 지나 있다. 아니면 저장하지 않고 되돌린다
                throw new MarketListingBusyException();
            }

            await LockAccountsAsync(connection, transaction, new[] { listing.SellerAccountId }, cancellationToken);
            long revision = await CloseListingAsync(connection, transaction, listingId, MarketCodes.ListingState.EXPIRED,
                false, cancellationToken);
            EconomyMover mover = CreateMover(context, MarketOperationKinds.ACTOR_SYSTEM, systemActor, operationId, listingId);
            await MarketEscrow.ReturnUnsoldAsync(context, connection, transaction, mover, _history, listing, systemActor,
                operationId, cancellationToken);
            return OperationDecision.Committed(revision, listingId.ToString("D"));
        }

        /// <summary> 만료 처리를 지금 할 수 없어 저장 없이 되돌린다 </summary>
        private sealed class MarketListingBusyException : Exception
        {
            public MarketListingBusyException()
                : base("The listing is locked or not yet due; it will be retried by the next maintenance run.")
            {
            }
        }
    }
}
