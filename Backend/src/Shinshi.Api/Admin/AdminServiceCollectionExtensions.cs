// System
using System;
using System.Linq;

// Third Party
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

// Project
using Shinshi.Data.Admin;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Admin
{
    /// <summary> 어드민 모듈 등록. 컨트롤러는 MapControllers가 같은 어셈블리에서 찾으므로 여기서 등록하지 않는다.
    /// Phase 2 기능의 데이터 서비스는 각자의 확장 파일(AddShinshiAdminGrants, AddShinshiAdminServiceState, AddShinshiAdminLogs)에 넣는다 </summary>
    public static class AdminServiceCollectionExtensions
    {
        /****************************************
        *                Fields
        ****************************************/

        // 어드민 로그인과 어드민 bearer 실패만 세는 AuthFailureLimiter 인스턴스의 DI 키. 게임 인증 실패 한도와 섞지 않는다
        public const string FAILURE_LIMITER_KEY = "shinshi-admin";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> AddShinshiAuth 뒤에 호출한다. 비밀키가 필요한 서비스는 첫 어드민 요청에서 만들어지므로 어드민 설정 없이도 게임 API는 뜬다 </summary>
        public static IServiceCollection AddShinshiAdmin(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            services.TryAddSingleton<TimeProvider>(TimeProvider.System);
            services.Configure<AdminOptions>(configuration.GetSection(AdminOptions.SECTION_NAME));
            services.TryAddSingleton(provider => AdminNetworkPolicy.Parse(
                provider.GetRequiredService<IOptions<AdminOptions>>().Value.AllowedNetworks,
                ForwardedHeadersConfiguration.ReadKnownProxies(provider.GetRequiredService<IConfiguration>())));
            services.TryAddSingleton(provider => provider.GetRequiredService<IOptions<AdminOptions>>().Value
                .CreateSecuritySettings());
            services.TryAddSingleton(provider => provider.GetRequiredService<IOptions<AdminOptions>>().Value
                .CreateSecretProtector());
            services.TryAddSingleton(provider => new AdminTokenHasher(
                provider.GetRequiredService<IOptions<AuthTokenOptions>>().Value));
            services.TryAddSingleton(provider => new AdminPasswordHasher(
                provider.GetRequiredService<AdminSecuritySettings>().PasswordIterations));
            services.TryAddSingleton<AdminCredentialFactory>();
            services.TryAddSingleton<IAdminAuditWriter>(provider => new PostgresAdminAuditWriter(
                provider.GetRequiredService<NpgsqlDataSource>()));
            services.TryAddSingleton<PostgresAdminUserRepository>();
            services.TryAddSingleton<PostgresAdminSessionRepository>();
            services.TryAddSingleton<PostgresAdminOperationClaimRepository>();
            services.TryAddSingleton<IAdminSessionValidator>(provider =>
                provider.GetRequiredService<PostgresAdminSessionRepository>());
            services.TryAddSingleton<AdminOperationExecutor>();
            services.TryAddScoped<AdminInvalidModelStateFilter>();
            services.TryAddScoped<AdminExceptionFilter>();
            if (!services.Any(descriptor => (descriptor.ServiceType == typeof(AuthFailureLimiter))
                && Equals(descriptor.ServiceKey, FAILURE_LIMITER_KEY)))
            {
                services.AddKeyedSingleton<AuthFailureLimiter>(FAILURE_LIMITER_KEY);
            }

            services.AddShinshiAdminGrants(configuration);
            services.AddShinshiAdminServiceState(configuration);
            services.AddShinshiAdminLogs(configuration);
            return services;
        }

        /// <summary> UseRouting 뒤, UseShinshiAuthentication 앞에 호출한다. 허용 네트워크 설정 오류는 여기서 시작을 실패시킨다 </summary>
        public static IApplicationBuilder UseShinshiAdminAuthentication(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            app.ApplicationServices.GetRequiredService<AdminNetworkPolicy>();
            return app.UseMiddleware<AdminAuthenticationMiddleware>();
        }
    }
}
