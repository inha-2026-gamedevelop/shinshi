// System
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Admin.Web.Components.Layout;
using Shinshi.Admin.Web.Infrastructure;
using Shinshi.Admin.Web.Models;
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Tests.AdminWeb
{
    public sealed class AdminWebSessionMonitoringTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SECRET = "sensitive-idle-page-value";
        private const int EXPIRY_SECONDS = 10;
        private const int CHECK_SECONDS = 15;

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MainLayout_IdleExpiryOrRevocation_ClearsPageAndNavigatesToLogin(bool revoke)
        {
            AdminWebSessionTimeProvider clock = new();
            using AdminWebSessionStore store = new(clock);
            string sessionId = store.Create(new AdminLoginResponse("server-only-api-token",
                clock.Now.AddSeconds(revoke ? CHECK_SECONDS * 2 : EXPIRY_SECONDS).ToUnixTimeMilliseconds(),
                Guid.NewGuid().ToString("D"), AdminContractCodes.Role.OWNER), "test-owner");
            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers.Cookie = AdminWebSessionStore.COOKIE_NAME + "=" + sessionId;
            using AdminWebContext context = new(store, new HttpContextAccessor { HttpContext = httpContext });
            NavigationProbe navigation = new();
            ComponentCapture capture = new();
            using HttpClient http = new();
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { [AdminWebApiClient.API_BASE_KEY] = "http://fake-api.test" }).Build();
            AdminWebApiClient api = new(http, context, configuration);
            await using ServiceProvider services = new ServiceCollection().AddLogging().AddSingleton(context)
                .AddSingleton(api).AddSingleton(capture).AddSingleton<NavigationManager>(navigation)
                .AddSingleton<AntiforgeryStateProvider, AntiforgeryProbe>().BuildServiceProvider();
            await using HtmlRenderer renderer = new(services, services.GetRequiredService<ILoggerFactory>());
            RenderFragment body = builder =>
            {
                builder.OpenComponent<PageProbe>(0);
                builder.CloseComponent();
            };
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                await renderer.RenderComponentAsync<LayoutProbe>(ParameterView.FromDictionary(
                    new Dictionary<string, object?> { [nameof(LayoutProbe.Body)] = body }));
                capture.Page!.SeedSensitiveState();
                // HtmlRenderer는 SSR만 실행하므로 interactive 첫 렌더 콜백을 명시적으로 진행한다
                capture.Layout!.CompleteInteractiveRender();
            });
            Assert.Equal(2, clock.ActiveTimerCount);
            Assert.NotNull(capture.Page!.Data);
            Assert.True(capture.Page.Prepared);
            if (revoke)
            {
                store.Revoke(sessionId);
            }

            await renderer.Dispatcher.InvokeAsync(() => clock.Advance(
                TimeSpan.FromSeconds(revoke ? CHECK_SECONDS : EXPIRY_SECONDS)));

            Assert.Null(capture.Page.Data);
            Assert.Equal(string.Empty, capture.Page.Secret);
            Assert.False(capture.Page.Prepared);
            Assert.Equal(1, capture.Page.Clears);
            Assert.Equal("/login?error=expired", navigation.LastTarget);
            Assert.True(navigation.ForceLoad);
            Assert.False(context.IsAuthenticated);
            Assert.Equal(1, clock.ActiveTimerCount);
        }

        public sealed class ComponentCapture
        {
            public LayoutProbe? Layout { get; set; }
            public PageProbe? Page { get; set; }
        }

        public sealed class LayoutProbe : MainLayout
        {
            [Inject] public ComponentCapture Capture { get; set; } = default!;

            protected override void OnInitialized()
            {
                base.OnInitialized();
                Capture.Layout = this;
            }

            public void CompleteInteractiveRender() { base.OnAfterRender(firstRender: true); }
        }

        public sealed class PageProbe : AdminPageBase
        {
            /****************************************
            *              Properties
            ****************************************/

            [Inject] public ComponentCapture Capture { get; set; } = default!;
            public JToken? Data => _data;
            public bool Prepared => HasPrepared;
            public string Secret { get; private set; } = string.Empty;
            public int Clears { get; private set; }

            /****************************************
            *                Methods
            ****************************************/

            protected override void OnInitialized()
            {
                base.OnInitialized();
                Capture.Page = this;
            }

            public void SeedSensitiveState()
            {
                _data = new JObject { ["accountSecret"] = SECRET };
                Secret = SECRET;
                Prepare("운영자 생성", "new-operator", "운영 확인", AdminContractCodes.Role.OWNER,
                    AdminApiRoutes.OPERATORS,
                    operationId => new { operationId, reason = "운영 확인", loginName = "new-operator" });
            }

            protected override void ClearSensitiveState()
            {
                Secret = string.Empty;
                ++Clears;
            }
        }

        private sealed class AntiforgeryProbe : AntiforgeryStateProvider
        {
            public override AntiforgeryRequestToken? GetAntiforgeryToken()
            {
                return new AntiforgeryRequestToken("test-antiforgery-value", "__RequestVerificationToken");
            }
        }

        private sealed class NavigationProbe : NavigationManager
        {
            public string? LastTarget { get; private set; }
            public bool ForceLoad { get; private set; }

            public NavigationProbe() { Initialize("http://admin.test/", "http://admin.test/"); }

            protected override void NavigateToCore(string uri, bool forceLoad)
            {
                LastTarget = uri;
                ForceLoad = forceLoad;
                Uri = ToAbsoluteUri(uri).AbsoluteUri;
            }
        }
    }
}
