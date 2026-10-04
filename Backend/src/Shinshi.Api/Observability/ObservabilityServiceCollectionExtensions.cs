// System
using System;

// Third-party
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Shinshi.Backend.Observability
{
    /// <summary> 공통 JSON 로그 포매터, 요청 로그, 요청 시간 지표와 60초 지표 보고 등록 </summary>
    public static class ObservabilityServiceCollectionExtensions
    {
        /****************************************
        *                Methods
        ****************************************/

        // 포매터는 등록만 하고 고르지 않는다. 배포가 Logging__Console__FormatterName=shinshi-json으로 고르고, 로컬 개발은 기본 포맷을 쓴다.
        // TimeProvider는 Program이 등록한다
        public static IServiceCollection AddShinshiObservability(this IServiceCollection services,
            IConfiguration configuration, bool databaseAvailable)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            services.AddLogging(logging => logging.AddConsole()
                .AddConsoleFormatter<ShinshiJsonConsoleFormatter, ShinshiJsonFormatterOptions>());
            services.Configure<ShinshiJsonFormatterOptions>(
                configuration.GetSection(ShinshiJsonFormatterOptions.SECTION_NAME));
            services.TryAddSingleton<ApiMetrics>();
            services.AddHostedService(provider => new MetricsReporterService(provider.GetRequiredService<ApiMetrics>(),
                provider.GetRequiredService<ILogger<MetricsReporterService>>(),
                provider.GetRequiredService<TimeProvider>(), provider, databaseAvailable));
            return services;
        }

        /// <summary> 요청 로그 미들웨어. 전달 헤더 처리 바로 뒤, 라우팅과 인증보다 앞에 둔다 </summary>
        public static IApplicationBuilder UseShinshiRequestLogging(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            return app.UseMiddleware<RequestLoggingMiddleware>();
        }
    }
}
