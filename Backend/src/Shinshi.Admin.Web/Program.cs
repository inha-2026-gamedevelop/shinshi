// System
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

// Project
using Shinshi.Admin.Web.Components;
using Shinshi.Admin.Web.Infrastructure;

namespace Shinshi.Admin.Web
{
    /// <summary> 운영 웹의 독립 진입점. 게임 API 또는 데이터 저장소를 참조하지 않는다 </summary>
    public sealed class AdminWebProgram
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int FORWARD_LIMIT = 1;
        private const int MAX_FORM_VALUE_LENGTH = 4096;
        private const int MAX_FORM_VALUES = 16;

        /****************************************
        *                Methods
        ****************************************/

        public static async Task Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            // Production 환경의 소스 실행도 SDK 자산 매니페스트로 패키지 스크립트를 찾는다
            builder.WebHost.UseStaticWebAssets();
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole();
            builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
            // 최종 호스트 구성이 적용된 뒤 허용 목록을 읽는다. 테스트 호스트도 같은 시작 검증을 받는다
            builder.Services.AddSingleton<AdminWebNetworkPolicy>(services =>
            {
                IConfiguration configuration = services.GetRequiredService<IConfiguration>();
                return AdminWebNetworkPolicy.Parse(
                    configuration.GetSection(AdminWebNetworkPolicy.NETWORKS_SECTION).Get<string[]>(),
                    ReadKnownProxies(configuration));
            });
            builder.Services.AddSingleton<AdminWebSessionStore>();
            builder.Services.AddSingleton<AdminWebLoginLimiter>();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<AdminWebContext>();
            builder.Services.AddScoped<ToastService>();
            builder.Services.AddHttpClient<AdminWebApiClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                });
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents(options => options.DetailedErrors = false);
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            // 시작 시 키 관리 서비스도 메모리를 사용해 프로세스 세션과 키 수명을 일치시킨다
            builder.Services.Configure<KeyManagementOptions>(options => options.XmlRepository = new AdminWebMemoryKeyRepository());
            builder.Services.AddAntiforgery(options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            });
            builder.Services.Configure<FormOptions>(options =>
            {
                options.ValueLengthLimit = MAX_FORM_VALUE_LENGTH;
                options.ValueCountLimit = MAX_FORM_VALUES;
            });

            await using WebApplication app = builder.Build();
            IPAddress[] proxies = ReadKnownProxies(app.Configuration);
            AdminWebApiClient.NormalizeBaseUrl(app.Configuration[AdminWebApiClient.API_BASE_KEY]
                ?? AdminWebApiClient.DEFAULT_API_BASE);
            if (proxies.Length > 0)
            {
                ForwardedHeadersOptions forwarding = new()
                {
                    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                    ForwardLimit = FORWARD_LIMIT
                };
                forwarding.KnownIPNetworks.Clear();
                forwarding.KnownProxies.Clear();
                foreach (IPAddress proxy in proxies)
                {
                    forwarding.KnownProxies.Add(proxy);
                }
                app.UseForwardedHeaders(forwarding);
            }
            app.UseMiddleware<AdminWebSecurityHeadersMiddleware>();
            app.UseMiddleware<AdminWebNetworkMiddleware>();
            app.UseExceptionHandler(handler => handler.Run(async context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync("요청을 처리하지 못했습니다. 잠시 후 다시 시도해 주세요.",
                    context.RequestAborted);
            }));
            app.UseRouting();
            app.UseMiddleware<AdminWebSessionMiddleware>();
            app.UseAntiforgery();
            app.MapStaticAssets().WithMetadata(new AllowAnonymousAttribute());
            app.MapAdminWebAuthentication();
            app.MapGet("/healthz", () => Results.Text("ok"));
            app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
            await app.RunAsync();
        }

        private static IPAddress[] ReadKnownProxies(IConfiguration configuration)
        {
            string[] values = configuration.GetSection(AdminWebNetworkPolicy.PROXIES_SECTION).Get<string[]>()
                ?? Array.Empty<string>();
            IPAddress[] proxies = new IPAddress[values.Length];
            for (int index = 0; index < values.Length; ++index)
            {
                if (!IPAddress.TryParse(values[index], out IPAddress? address))
                {
                    throw new InvalidOperationException("ForwardedHeaders:KnownProxies must contain IP addresses.");
                }
                proxies[index] = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            }
            return proxies;
        }
    }
}
