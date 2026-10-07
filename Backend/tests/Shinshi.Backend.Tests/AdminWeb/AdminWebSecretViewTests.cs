// System
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Admin.Web.Components.Shared;

namespace Shinshi.Backend.Tests.AdminWeb
{
    public sealed class AdminWebSecretViewTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SECRET = "one-time-initial-password";
        private const string SAFE_VALUE = "safe-operation-result";
        private const string MISSING_WARNING = "비밀값을 잃었으면 비활성화하고 새로 만드세요.";

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task OneTimeSecretView_ShowThenClear_UpdatesRenderedContentAndRemovesSecret()
        {
            SecretCapture capture = new SecretCapture();
            await using ServiceProvider services = new ServiceCollection().AddLogging().AddSingleton(capture)
                .BuildServiceProvider();
            await using HtmlRenderer renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                HtmlRootComponent rendered = await renderer.RenderComponentAsync<SecretHost>();
                Assert.DoesNotContain(SECRET, rendered.ToHtmlString());

                capture.View!.Show(new Dictionary<string, string> { ["초기 비밀번호"] = SECRET }, MISSING_WARNING);
                Assert.Contains(SECRET, rendered.ToHtmlString());
                capture.View.Clear();
                Assert.DoesNotContain(SECRET, rendered.ToHtmlString());

                capture.View.Show(new Dictionary<string, string>(), MISSING_WARNING);
                Assert.Contains(MISSING_WARNING, System.Net.WebUtility.HtmlDecode(rendered.ToHtmlString()));
                Assert.DoesNotContain(SECRET, rendered.ToHtmlString());
            });
        }

        [Fact]
        public async Task JsonDataView_NestedSecretFields_ExcludesSecretsAndKeepsSafeResults()
        {
            await using ServiceProvider services = new ServiceCollection().AddLogging().BuildServiceProvider();
            await using HtmlRenderer renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            JObject response = new JObject
            {
                ["adminId"] = SAFE_VALUE,
                ["initialPassword"] = SECRET,
                ["nested"] = new JArray(new JObject
                {
                    ["inviteCode"] = SECRET, ["totpSecretBase32"] = SECRET, ["accessToken"] = SECRET,
                    ["receiptId"] = SAFE_VALUE
                })
            };

            string html = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                HtmlRootComponent rendered = await renderer.RenderComponentAsync<JsonDataView>(ParameterView.FromDictionary(
                    new Dictionary<string, object?> { [nameof(JsonDataView.Value)] = response }));
                return rendered.ToHtmlString();
            });

            Assert.Contains(SAFE_VALUE, html);
            Assert.DoesNotContain(SECRET, html);
            Assert.DoesNotContain("initialPassword", html);
            Assert.DoesNotContain("inviteCode", html);
            Assert.DoesNotContain("totpSecretBase32", html);
            Assert.DoesNotContain("accessToken", html);
        }

        public sealed class SecretCapture
        {
            public OneTimeSecretView? View { get; set; }
        }

        public sealed class SecretHost : ComponentBase
        {
            /****************************************
            *              Properties
            ****************************************/

            [Inject] public SecretCapture Capture { get; set; } = default!;

            /****************************************
            *                Methods
            ****************************************/

            protected override void BuildRenderTree(RenderTreeBuilder builder)
            {
                builder.OpenComponent<OneTimeSecretView>(0);
                builder.AddComponentReferenceCapture(1, component => Capture.View = (OneTimeSecretView)component);
                builder.CloseComponent();
            }
        }
    }
}