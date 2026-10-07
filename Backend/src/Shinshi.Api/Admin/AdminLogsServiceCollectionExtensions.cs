// System
using System;

// Third Party
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Project
using Shinshi.Backend.Sessions;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 원장, 아이템 이력, 세션 이벤트, 작업, 감사 로그 조회와 수동 복구의 서비스 등록. AddShinshiAdmin이 부른다.
    /// 수동 복구는 AddShinshiGameSessions가 등록한 PostgresSettlementRepository를 쓴다 </summary>
    public static class AdminLogsServiceCollectionExtensions
    {
        /****************************************
        *                Methods
        ****************************************/

        public static IServiceCollection AddShinshiAdminLogs(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            services.TryAddSingleton<IAdminCharacterRepository, PostgresAdminCharacterRepository>();
            services.TryAddSingleton<PostgresAdminLedgerReader>();
            services.TryAddSingleton<PostgresAdminTraceReader>();
            services.TryAddSingleton<PostgresAdminAuditReader>();
            services.TryAddSingleton<PostgresAdminRecoveryRepository>();
            // 자동 복구(GameSessionMaintenanceService)와 같은 인스턴스를 써야 기동, DB 단절 보류를 함께 따른다
            services.TryAddSingleton(provider => new RecoveryLivenessGate(provider.GetRequiredService<TimeProvider>()));
            services.TryAddSingleton<AdminSessionRecoveryService>();
            services.TryAddSingleton<PostgresAdminAuctionReader>();
            return services;
        }
    }
}
