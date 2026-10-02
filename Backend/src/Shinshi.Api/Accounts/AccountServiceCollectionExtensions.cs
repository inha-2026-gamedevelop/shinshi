// System
using System;

// Third Party
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

// Project
using Shinshi.Data;
using Shinshi.Data.Accounts;
using Shinshi.Data.Auth;
using Shinshi.Data.Characters;

namespace Shinshi.Backend.Accounts
{
    /// <summary> 가입, 비밀번호 로그인, 캐릭터 생성 모듈 등록. AddShinshiAuth가 부르므로 Program.cs는 따로 부르지 않는다.
    /// 저장소는 DB 연결이 필요하므로 그 경로를 처음 부를 때 만들어진다 </summary>
    public static class AccountServiceCollectionExtensions
    {
        /****************************************
        *                Methods
        ****************************************/

        public static IServiceCollection AddShinshiAccounts(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            services.TryAddSingleton<TimeProvider>(TimeProvider.System);
            services.Configure<AccountOptions>(configuration.GetSection(AccountOptions.SECTION_NAME));
            services.TryAddSingleton<IAccountPasswordHasher>(provider => new AccountPasswordHasher(
                provider.GetRequiredService<IOptions<AccountOptions>>().Value.PasswordIterations));
            services.TryAddSingleton(_ => new PasswordHashGate());
            services.TryAddSingleton(provider => new PasswordLoginLimiter(provider.GetRequiredService<TimeProvider>()));
            services.TryAddSingleton<PasswordCredentialVerifier>();
            services.TryAddSingleton<AccountSignupLimiter>();
            services.TryAddScoped<AccountInvalidModelStateFilter>();
            services.TryAddSingleton<IAccountRepository>(provider => provider.GetRequiredService<PostgresAuthRepository>());
            services.TryAddSingleton<ICharacterRepository>(provider => new PostgresCharacterRepository(
                provider.GetRequiredService<NpgsqlDataSource>(), provider.GetRequiredService<IdempotentOperationExecutor>()));
            return services;
        }
    }
}
