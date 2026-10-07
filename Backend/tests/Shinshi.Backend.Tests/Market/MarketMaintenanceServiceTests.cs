// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Market;
using Shinshi.Backend.Sessions;
using Shinshi.Backend.Tests.Growth;
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Data.Market;
using Shinshi.Data.Sessions;
using Shinshi.Data.Trade;

namespace Shinshi.Backend.Tests.Market
{
    /// <summary> 만료 반환과 방치 정리 유지 작업의 보류 규칙(시작 직후, DB 단절 직후)과 실제 저장소 연결을 검사한다 </summary>
    public sealed class MarketMaintenanceServiceTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int BATCH = 50;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task RunOnce_DuringStartupGrace_DoesNotTouchTheDatabase()
        {
            ManualTimeProvider clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
            FakeWork work = new FakeWork();
            MarketMaintenanceService service = CreateService(work, clock);

            await service.RunListingExpiryOnceAsync(CancellationToken.None);
            await service.RunTradeAbandonOnceAsync(CancellationToken.None);
            Assert.Equal((0, 0), (work.ExpiryCalls, work.AbandonCalls));

            clock.Advance(ServerPoolTimings.StartupRecoveryGrace + TimeSpan.FromSeconds(1));
            await service.RunListingExpiryOnceAsync(CancellationToken.None);
            await service.RunTradeAbandonOnceAsync(CancellationToken.None);
            Assert.Equal((1, 1), (work.ExpiryCalls, work.AbandonCalls));
        }

        [Fact]
        public async Task RunOnce_ConnectionFailure_DefersAndOtherFailuresDoNot()
        {
            ManualTimeProvider clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
            RecoveryLivenessGate gate = new RecoveryLivenessGate(clock);
            clock.Advance(ServerPoolTimings.StartupRecoveryGrace + TimeSpan.FromSeconds(1));
            FakeWork work = new FakeWork { Failure = new InvalidOperationException("bug") };
            MarketMaintenanceService service = new MarketMaintenanceService(work,
                NullLogger<MarketMaintenanceService>.Instance, clock, gate);

            await service.RunListingExpiryOnceAsync(CancellationToken.None);
            Assert.False(gate.IsDeferred());

            work.Failure = new TimeoutException("down");
            await service.RunListingExpiryOnceAsync(CancellationToken.None);
            Assert.True(gate.IsDeferred());
            int calls = work.ExpiryCalls;
            await service.RunTradeAbandonOnceAsync(CancellationToken.None);
            Assert.Equal(calls, work.ExpiryCalls);
            Assert.Equal(0, work.AbandonCalls);

            clock.Advance(ServerPoolTimings.StartupRecoveryGrace + TimeSpan.FromSeconds(1));
            work.Failure = null;
            await service.RunTradeAbandonOnceAsync(CancellationToken.None);
            Assert.Equal(1, work.AbandonCalls);
        }

        [PostgresFact]
        public async Task RunOnce_WithRealRepositories_ReturnsExpiredListingsAndAbandonsStaleExecutions()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: new System.Collections.Generic.Dictionary<string, long>
            {
                [MarketTestEnvironment.HERB] = 5
            });
            GrowthPlayer buyer = await environment.CreatePlayerAsync(currency: 100);
            string listingId = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 5, 10));
            await environment.ExpireNowAsync(listingId);
            TradeOutcome prepared = await environment.PrepareCommittedAsync(
                MarketTestEnvironment.Offer(seller, 0), MarketTestEnvironment.Offer(buyer, 10));
            await environment.AgePreparedAsync(prepared.Execution!.ExecutionId, TradeCodes.TRADE_PREPARE_TTL_SECONDS + 5);
            ManualTimeProvider clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
            MarketMaintenanceService service = CreateService(new MarketMaintenanceWork(environment.Market, environment.Trades),
                clock);
            clock.Advance(ServerPoolTimings.StartupRecoveryGrace + TimeSpan.FromSeconds(1));

            await service.RunListingExpiryOnceAsync(CancellationToken.None);
            await service.RunTradeAbandonOnceAsync(CancellationToken.None);

            Assert.Equal(MarketCodes.ListingState.EXPIRED, (await environment.ListingAsync(listingId)).State);
            Assert.Equal(5, await environment.ItemQuantityAsync(seller, MarketTestEnvironment.HERB));
            Assert.Equal(TradeCodes.ExecutionState.ABANDONED, (await environment.ExecutionAsync(
                prepared.Execution.ExecutionId)).State);
            await environment.AssertReconciledAsync();
        }

        private static MarketMaintenanceService CreateService(IMarketMaintenanceWork work, TimeProvider clock)
        {
            return new MarketMaintenanceService(work, NullLogger<MarketMaintenanceService>.Instance, clock,
                new RecoveryLivenessGate(clock));
        }

        private sealed class FakeWork : IMarketMaintenanceWork
        {
            public int        ExpiryCalls  { get; private set; }
            public int        AbandonCalls { get; private set; }
            public Exception? Failure      { get; set; }

            public Task<MarketExpiryResult> ExpireListingsAsync(int maxListings, CancellationToken cancellationToken)
            {
                ++ExpiryCalls;
                if (Failure != null)
                {
                    throw Failure;
                }

                Assert.Equal(BATCH, maxListings);
                return Task.FromResult(new MarketExpiryResult(0, 0, 0));
            }

            public Task<int> AbandonTradeExecutionsAsync(int maxExecutions, CancellationToken cancellationToken)
            {
                ++AbandonCalls;
                if (Failure != null)
                {
                    throw Failure;
                }

                return Task.FromResult(0);
            }
        }
    }
}
