// System
using System;

// Third-party
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

// Project
using Shinshi.Backend.Ledger;
using Shinshi.Data;
using Shinshi.Data.Growth;
using Shinshi.Data.Ledger;

namespace Shinshi.Backend.Growth
{
    /// <summary> 성장, 초기 지급, 계정 진행 모듈과 일일 경제 대사 등록 </summary>
    public static class GrowthServiceCollectionExtensions
    {
        /****************************************
        *                Methods
        ****************************************/

        // NpgsqlDataSource, IdempotentOperationExecutor, IEconomyLedgerEntryWriter, PostgresItemHistoryWriter(AddShinshiLedger)는
        // Program이 등록한다. 저장소는 DB 경로를 처음 부를 때 만들어진다
        public static IServiceCollection AddShinshiGrowth(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            services.Configure<GrowthOptions>(configuration.GetSection(GrowthOptions.SECTION_NAME));
            services.TryAddSingleton(_ => GrowthContent.LoadEmbedded());
            services.TryAddSingleton(provider => new InitialGrantService(provider.GetRequiredService<GrowthContent>(),
                ResolveProfile(provider), provider.GetRequiredService<IEconomyLedgerEntryWriter>(),
                provider.GetRequiredService<PostgresItemHistoryWriter>()));
            services.TryAddSingleton<PostgresGrowthReader>();
            services.TryAddSingleton<GrowthOperationRunner>();
            services.TryAddSingleton<PostgresTalismanRepository>();
            services.TryAddSingleton<PostgresEquipmentRepository>();
            services.TryAddSingleton<PostgresTrainingRepository>();
            services.TryAddSingleton(provider => new PostgresAccountProgressRepository(
                provider.GetRequiredService<NpgsqlDataSource>(), provider.GetRequiredService<GrowthContent>(),
                ResolveProfile(provider), provider.GetRequiredService<IdempotentOperationExecutor>()));
            services.TryAddSingleton(provider => new SortieUnlockPolicy(provider.GetRequiredService<GrowthContent>(),
                ResolveProfile(provider), provider.GetRequiredService<IOptions<GrowthOptions>>().Value.RejectUnknownBattles));
            return services;
        }

        /// <summary> 일일 경제 대사 주기 작업. DB 연결이 있는 API 실행에만 등록한다(AddShinshiLedger가 대사기를 등록한다) </summary>
        public static IServiceCollection AddShinshiEconomyReconciliation(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton<PostgresReconciliationRunRepository>();
            services.AddHostedService<EconomyReconciliationService>();
            return services;
        }

        private static EGrowthContentProfile ResolveProfile(IServiceProvider provider)
        {
            return provider.GetRequiredService<IOptions<GrowthOptions>>().Value.ResolveProfile();
        }
    }
}
