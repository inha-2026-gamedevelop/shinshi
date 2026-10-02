// System
using System;

// Third Party
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Project
using Shinshi.Data.Admin;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Admin
{
    /// <summary> 서비스 상태, 공지, 계정 조회, 초대 코드의 서비스 등록 자리. AddShinshiAdmin이 부른다 </summary>
    public static class AdminServiceStateServiceCollectionExtensions
    {
        /****************************************
        *                Fields
        ****************************************/

        // Program이 런타임 DB에 쓰는 연결 문자열 이름. 이 값이 있을 때만 서비스 상태를 DB에서 읽는다
        public const string RUNTIME_CONNECTION_NAME = "Postgres";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> ServiceStateStore를 등록하면 AuthBuildPolicy와 공개 ServiceStateController가 생성자 선택 매개변수로 이 캐시를 받는다 </summary>
        public static IServiceCollection AddShinshiAdminServiceState(this IServiceCollection services,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            services.TryAddSingleton<TimeProvider>(TimeProvider.System);
            services.AddOptions<ServiceStateOptions>();
            services.TryAddSingleton<PostgresServiceStateRepository>();
            services.TryAddSingleton<PostgresAdminAccountRepository>();
            services.TryAddSingleton<PostgresAdminInviteRepository>();

            // 연결 문자열은 해석 시점의 구성에서 읽는다. 데이터 소스만 바꿔 끼운 테스트 호스트는 설정 모드로 남는다
            services.TryAddSingleton(provider =>
            {
                string? connection = provider.GetRequiredService<IConfiguration>()
                    .GetConnectionString(RUNTIME_CONNECTION_NAME);
                return new ServiceStateStore(provider.GetRequiredService<IOptionsMonitor<ServiceStateOptions>>(),
                    provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<ServiceStateStore>>(),
                    string.IsNullOrWhiteSpace(connection)
                        ? null : provider.GetRequiredService<PostgresServiceStateRepository>());
            });
            services.TryAddSingleton<ServiceFeatureGate>();
            services.AddHostedService<ServiceStateStartupService>();

            // 초대 검색, 발급이 게임 인증과 같은 키와 용도 문자열로 HMAC한다. 키가 없으면 첫 사용 요청에서 실패한다
            services.TryAddSingleton(provider => new AuthTokenHasher(
                provider.GetRequiredService<IOptions<AuthTokenOptions>>().Value));
            return services;
        }
    }
}
