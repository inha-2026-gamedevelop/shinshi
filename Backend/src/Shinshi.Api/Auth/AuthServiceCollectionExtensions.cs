// System
using System;
using System.Linq;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

// Project
using Shinshi.Backend.Accounts;
using Shinshi.Data.Auth;

namespace Shinshi.Backend
{
    /// <summary> 부모 composition root가 명시적으로 연결하는 인증 모듈 등록 </summary>
    public static class AuthServiceCollectionExtensions
    {
        /****************************************
        *                Methods
        ****************************************/

        public static IServiceCollection AddShinshiAuth(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            services.TryAddSingleton<TimeProvider>(TimeProvider.System);
            services.Configure<AuthTokenOptions>(configuration.GetSection("Auth"));
            services.Configure<AuthBuildOptions>(configuration.GetSection(AuthBuildOptions.SECTION_NAME));
            services.Configure<ServiceCredentialOptions>(configuration.GetSection(ServiceCredentialOptions.SECTION_NAME));
            services.TryAddSingleton<AuthBuildPolicy>();
            services.TryAddScoped<AuthInvalidModelStateFilter>();
            services.TryAddSingleton<PostgresAuthRepository>(provider => new PostgresAuthRepository(
                provider.GetRequiredService<NpgsqlDataSource>(),
                provider.GetRequiredService<IOptions<AuthTokenOptions>>().Value));
            services.TryAddSingleton<IAuthRepository>(provider => provider.GetRequiredService<PostgresAuthRepository>());
            services.TryAddSingleton<ServerCredentialIssuer>();
            services.AddShinshiAccounts(configuration);
            AddRateLimiting(services);
            return services;
        }

        /// <summary> UseRouting과 MapControllers 사이에 호출한다. 요청 한도를 먼저 적용하고 일반 엔드포인트는 접근 토큰을 요구한다 </summary>
        public static IApplicationBuilder UseShinshiAuthentication(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            app.UseRateLimiter();
            return app.UseMiddleware<BearerTokenMiddleware>();
        }

        // 같은 컬렉션에 두 번 등록하면 정책 이름이 겹쳐 시작이 실패하므로 한 번만 등록한다
        private static void AddRateLimiting(IServiceCollection services)
        {
            if (services.Any(descriptor => descriptor.ServiceType == typeof(AuthFailureLimiter)))
            {
                return;
            }

            services.AddSingleton<AuthFailureLimiter>();
            services.AddKeyedSingleton<AuthFailureLimiter>(AuthFailureLimiter.SERVICE_CREDENTIAL_KEY);
            services.TryAddSingleton<AccountRequestLimiter>();
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = (context, cancellationToken) => new ValueTask(
                    AuthFailureLimiter.WriteTooManyAsync(context.HttpContext,
                        context.HttpContext.RequestServices.GetRequiredService<TimeProvider>()));
                options.AddPolicy(AuthFailureLimiter.CREDENTIAL_POLICY, context =>
                    RateLimitPartition.GetFixedWindowLimiter(AuthFailureLimiter.KeyOf(context),
                        _ => AuthFailureLimiter.CreateWindow(AuthFailureLimiter.MAX_CREDENTIAL_REQUESTS_PER_WINDOW)));
                options.AddPolicy(AuthFailureLimiter.SERVER_LEASE_POLICY, context =>
                    RateLimitPartition.GetFixedWindowLimiter(AuthFailureLimiter.ServerLeaseKeyOf(context),
                        _ => AuthFailureLimiter.CreateWindow(AuthFailureLimiter.MAX_SERVER_LEASE_REQUESTS_PER_WINDOW)));
                options.AddPolicy(AccountSignupLimiter.AVAILABILITY_POLICY, context =>
                    RateLimitPartition.GetFixedWindowLimiter(AuthFailureLimiter.KeyOf(context),
                        _ => AuthFailureLimiter.CreateWindow(AccountSignupLimiter.MAX_AVAILABILITY_REQUESTS_PER_WINDOW)));
            });
        }
    }
}

