// System
using System;

// Third-party
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Project
using Shinshi.Data.Life;

namespace Shinshi.Backend.Life
{
    /// <summary> 생활(행동력, 채집, 물약 제작) 저장소 등록 </summary>
    public static class LifeServiceCollectionExtensions
    {
        /****************************************
        *                Methods
        ****************************************/

        // NpgsqlDataSource, IdempotentOperationExecutor, IEconomyLedgerEntryWriter는 Program이 등록한다.
        // 서버 호출 확인(IServerCallerAuthenticator)과 서버 풀 저장소(AddShinshiGameSessions)도 먼저 등록돼 있어야 한다
        public static IServiceCollection AddShinshiLife(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton<PostgresLifeRepository>();
            return services;
        }
    }
}
