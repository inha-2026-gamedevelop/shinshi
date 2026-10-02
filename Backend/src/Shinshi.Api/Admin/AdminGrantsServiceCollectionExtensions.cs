// System
using System;

// Third Party
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Project
using Shinshi.Backend.Inbox;
using Shinshi.Data.Admin.Grants;
using Shinshi.Data.Inbox;

namespace Shinshi.Backend.Admin
{
    /// <summary> 운영자 지급, 우편함, 개인 재화 조정의 서비스 등록 자리. AddShinshiAdmin이 부른다.
    /// 플레이어 우편함 저장소도 같은 지급 데이터를 읽고 쓰므로 여기서 함께 등록한다 </summary>
    public static class AdminGrantsServiceCollectionExtensions
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> NpgsqlDataSource, IdempotentOperationExecutor, IEconomyLedgerEntryWriter는 Program이 등록한다.
        /// 모두 첫 요청에서 만들어지므로 DB 연결 문자열 없이도 API는 뜬다 </summary>
        public static IServiceCollection AddShinshiAdminGrants(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            services.TryAddSingleton<PostgresAdminGrantRepository>();
            services.TryAddSingleton<PostgresAdminAdjustmentRepository>();
            services.TryAddSingleton<PostgresInboxRepository>();
            services.TryAddSingleton<InboxRequestLimiter>();
            return services;
        }
    }
}
