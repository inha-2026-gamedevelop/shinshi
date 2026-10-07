// System
using System;

// Third-party
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Project
using Shinshi.Data.Market;
using Shinshi.Data.Trade;

namespace Shinshi.Backend.Market
{
    /// <summary> 유저 경매장과 1:1 거래 저장소 등록 </summary>
    public static class MarketServiceCollectionExtensions
    {
        /****************************************
        *                Methods
        ****************************************/

        // NpgsqlDataSource, IdempotentOperationExecutor, IEconomyLedgerEntryWriter, PostgresItemHistoryWriter(AddShinshiLedger)와
        // GrowthContent(AddShinshiGrowth)는 먼저 등록돼 있어야 한다. 저장소는 DB 경로를 처음 부를 때 만들어진다
        public static IServiceCollection AddShinshiMarket(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton<PostgresMarketRepository>();
            services.TryAddSingleton<PostgresMarketQueries>();
            services.TryAddSingleton<PostgresTradeRepository>();
            return services;
        }

        /// <summary> 등록 만료(1분)와 거래 실행 방치 정리(30초) 주기 작업. DB 연결이 있는 API 실행에만 등록한다.
        /// 생존 판정 보류(RecoveryLivenessGate)는 AddShinshiGameSessions가 등록한다 </summary>
        public static IServiceCollection AddShinshiMarketMaintenance(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton<IMarketMaintenanceWork, MarketMaintenanceWork>();
            services.AddHostedService<MarketMaintenanceService>();
            return services;
        }
    }
}
