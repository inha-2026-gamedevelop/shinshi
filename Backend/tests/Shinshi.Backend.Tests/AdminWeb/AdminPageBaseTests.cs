// System
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Admin.Web.Infrastructure;
using Shinshi.Admin.Web.Models;
using Shinshi.Backend.Tests.Admin;
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Tests.AdminWeb
{
    public sealed class AdminPageBaseTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SECRET = "late-one-time-password-must-be-dropped";
        private const int LIFETIME_MINUTES = 30;

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ConfirmAsync_NavigationOrSessionRevocationBeforeResponse_DropsDataAndOneTimeSecrets(bool revoke)
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            AdminWebSessionStore store = new AdminWebSessionStore(clock);
            string sessionId = store.Create(new AdminLoginResponse(AdminWebTestHandler.TOKEN,
                clock.Now.AddMinutes(LIFETIME_MINUTES).ToUnixTimeMilliseconds(), Guid.NewGuid().ToString("D"),
                AdminContractCodes.Role.OWNER), "test-owner");
            DefaultHttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Headers.Cookie = AdminWebSessionStore.COOKIE_NAME + "=" + sessionId;
            AdminWebContext context = new AdminWebContext(store, new HttpContextAccessor { HttpContext = httpContext });
            DelayedHandler handler = new DelayedHandler();
            using HttpClient http = new HttpClient(handler);
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { [AdminWebApiClient.API_BASE_KEY] = "http://fake-api.test" }).Build();
            AdminWebApiClient api = new AdminWebApiClient(http, context, configuration);
            PageCapture capture = new PageCapture();
            await using ServiceProvider services = new ServiceCollection().AddLogging().AddSingleton(context)
                .AddSingleton(api).AddSingleton(capture).BuildServiceProvider();
            await using HtmlRenderer renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            Task? confirmation = null;
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                await renderer.RenderComponentAsync<ProbePage>();
                capture.Page!.PrepareWrite();
                confirmation = capture.Page.ConfirmWriteAsync();
            });
            await handler.Started.Task;
            await renderer.Dispatcher.InvokeAsync(() =>
            {
                if (revoke)
                {
                    context.Revoke();
                }
                else
                {
                    capture.Page!.Dispose();
                }
            });
            handler.Response.SetResult(AdminWebTestHandler.Json(new { initialPassword = SECRET }));
            await confirmation!;

            Assert.Null(capture.Page!.Data);
            Assert.Equal(0, capture.Page.WriteResponses);
            Assert.True(capture.Page.Clears > 0);
            Assert.False(capture.Page.Prepared);
        }

        public sealed class PageCapture
        {
            public ProbePage? Page { get; set; }
        }

        public sealed class ProbePage : AdminPageBase
        {
            /****************************************
            *              Properties
            ****************************************/

            [Inject] public PageCapture Capture { get; set; } = default!;
            public JToken? Data => _data;
            public int WriteResponses { get; private set; }
            public int Clears { get; private set; }
            public bool Prepared => HasPrepared;

            /****************************************
            *                Methods
            ****************************************/

            protected override void OnInitialized()
            {
                base.OnInitialized();
                Capture.Page = this;
            }

            public void PrepareWrite()
            {
                Prepare("운영자 생성", "new-operator", "운영 확인", AdminContractCodes.Role.OWNER,
                    AdminApiRoutes.OPERATORS,
                    operationId => new { operationId, reason = "운영 확인", loginName = "new-operator" });
            }

            public Task ConfirmWriteAsync() { return ConfirmAsync(); }
            protected override void OnWriteResponse(JToken? value) { ++WriteResponses; }
            protected override void ClearSensitiveState() { ++Clears; }
        }

        private sealed class DelayedHandler : HttpMessageHandler
        {
            /****************************************
            *              Properties
            ****************************************/

            public TaskCompletionSource<bool> Started { get; } = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<HttpResponseMessage> Response { get; } = new TaskCompletionSource<HttpResponseMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            /****************************************
            *                Methods
            ****************************************/

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Started.TrySetResult(true);
                return Response.Task;
            }
        }
    }
}