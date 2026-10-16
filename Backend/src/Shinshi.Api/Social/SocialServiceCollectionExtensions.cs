// System
using System;

// Third-party
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Project
using Shinshi.Data.Social;

namespace Shinshi.Backend.Social
{
    /// <summary> 소셜 저장소, 캐릭터별 한도, 만료 정리 등록 </summary>
    public static class SocialServiceCollectionExtensions
    {
        /****************************************
        *                Methods
        ****************************************/

        // NpgsqlDataSource는 먼저 등록돼 있어야 한다. 저장소는 DB 경로를 처음 부를 때 만들어진다
        public static IServiceCollection AddShinshiSocial(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton<PostgresSocialRepository>();
            services.TryAddSingleton<SocialRequestLimiter>();
            return services;
        }

        /// <summary> 만료 정리(60초) 주기 작업. DB 연결이 있는 API 실행에만 등록한다 </summary>
        public static IServiceCollection AddShinshiSocialMaintenance(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton<ISocialMaintenanceWork, SocialMaintenanceWork>();
            services.AddHostedService<SocialMaintenanceService>();
            return services;
        }
    }
}
