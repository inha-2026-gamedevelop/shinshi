// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Data.Market;
using Shinshi.Data.Trade;

namespace Shinshi.Backend.Market
{
    /// <summary> 유지 작업 한 주기가 부르는 DB 작업. 시험에서 DB 단절을 흉내 낼 수 있게 서비스와 분리한다 </summary>
    public interface IMarketMaintenanceWork
    {
        /****************************************
        *                Methods
        ****************************************/

        Task<MarketExpiryResult> ExpireListingsAsync(int maxListings, CancellationToken cancellationToken);
        Task<int> AbandonTradeExecutionsAsync(int maxExecutions, CancellationToken cancellationToken);
    }

    /// <summary> 경매장, 거래 저장소에 그대로 넘기는 기본 구현 </summary>
    public sealed class MarketMaintenanceWork : IMarketMaintenanceWork
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresMarketRepository _market;
        private readonly PostgresTradeRepository _trades;

        /****************************************
        *                Methods
        ****************************************/

        public MarketMaintenanceWork(PostgresMarketRepository market, PostgresTradeRepository trades)
        {
            ArgumentNullException.ThrowIfNull(market);
            ArgumentNullException.ThrowIfNull(trades);
            _market = market;
            _trades = trades;
        }

        public Task<MarketExpiryResult> ExpireListingsAsync(int maxListings, CancellationToken cancellationToken)
        {
            return _market.ExpireDueAsync(maxListings, cancellationToken);
        }

        public Task<int> AbandonTradeExecutionsAsync(int maxExecutions, CancellationToken cancellationToken)
        {
            return _trades.AbandonExpiredAsync(maxExecutions, cancellationToken);
        }
    }
}
