// System
using System;

// Third Party
using Microsoft.Extensions.DependencyInjection;

namespace Shinshi.Data.Ledger
{
    public static class LedgerServiceCollectionExtensions
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 공유 NpgsqlDataSource를 composition root에서 먼저 등록한다 </summary>
        public static IServiceCollection AddShinshiLedger(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddSingleton<PostgresEconomyLedgerWriter>();
            services.AddSingleton<PostgresItemHistoryWriter>();
            services.AddSingleton<PostgresOutboxWriter>();
            services.AddSingleton<PostgresOutboxDispatcher>();
            services.AddSingleton<EconomyLedgerReconciler>();
            return services;
        }
    }
}
