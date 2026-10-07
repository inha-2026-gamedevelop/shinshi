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
    /// <summary> 다중 후보 선택 유지, 조회 대상 변경 시 늦은 응답 폐기, query 재진입 </summary>
    public sealed class AdminWebStateRaceTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ACCOUNT_A = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
        private const string ACCOUNT_B = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task Choose_ThenEnsureAgain_KeepsChosenAccountWithoutNewLookup()
        {
            int lookups = 0;
            ResolvedAccount a = new(ACCOUNT_A, null, "여우", "CharacterName", false, false, 1);
            ResolvedAccount b = new(ACCOUNT_B, null, "여우", "CharacterName", true, false, 1);
            AccountResolution resolution = new((text, deleted, invite) =>
            {
                ++lookups;
                return Task.FromResult(new AccountLookupResult(new[] { a, b }, string.Empty));
            });

            Assert.Null(await resolution.EnsureAsync("여우", true, false));
            Assert.True(resolution.Choose(b));
            ResolvedAccount? again = await resolution.EnsureAsync("여우", true, false);

            Assert.Same(b, again);
            Assert.Equal(1, lookups);
        }

        [Fact]
        public async Task Choose_ThenInputChanges_DropsChoice()
        {
            ResolvedAccount a = new(ACCOUNT_A, null, "여우", "CharacterName", false, false, 1);
            ResolvedAccount b = new(ACCOUNT_B, null, "여우", "CharacterName", false, false, 1);
            AccountResolution resolution = new((text, deleted, invite) => Task.FromResult(
                new AccountLookupResult(text == "여우" ? new[] { a, b } : Array.Empty<ResolvedAccount>(), string.Empty)));
            await resolution.EnsureAsync("여우", false, false);
            resolution.Choose(a);

            Assert.Null(await resolution.EnsureAsync("다른이름", false, false));
            Assert.Null(resolution.Selected);
        }

        [Fact]
        public async Task EnsureOutcomeAsync_DiscardedLookupCompletes_IsMarkedStaleAndLatestSelectionSurvives()
        {
            TaskCompletionSource<AccountLookupResult> slow = new(TaskCreationOptions.RunContinuationsAsynchronously);
            ResolvedAccount fast = new(ACCOUNT_B, null, "빠름", "CharacterName", false, false, 1);
            AccountResolution resolution = new((text, deleted, invite) => text.Trim() == "느림"
                ? slow.Task : Task.FromResult(new AccountLookupResult(new[] { fast }, string.Empty)));

            Task<ResolutionOutcome> old = resolution.EnsureOutcomeAsync("느림", false, false);
            ResolutionOutcome latest = await resolution.EnsureOutcomeAsync("빠름", false, false);
            slow.SetResult(new AccountLookupResult(new[] { new ResolvedAccount(ACCOUNT_A, null, "느림", "CharacterName", false, false, 1) }, string.Empty));
            ResolutionOutcome stale = await old;

            Assert.False(latest.Stale);
            Assert.Same(fast, latest.Account);
            Assert.True(stale.Stale);
            Assert.Null(stale.Account);
            Assert.Same(fast, resolution.Selected);
        }

        [Fact]
        public async Task EnsureOutcomeAsync_SameNameOptionChangedAndBack_OldCompletionIsStale()
        {
            TaskCompletionSource<AccountLookupResult> first = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            ResolvedAccount account = new(ACCOUNT_A, null, "여우", "CharacterName", false, false, 1);
            AccountResolution resolution = new((text, deleted, invite) =>
                ++calls == 1 ? first.Task : Task.FromResult(new AccountLookupResult(new[] { account }, string.Empty)));

            Task<ResolutionOutcome> old = resolution.EnsureOutcomeAsync("여우", false, false);
            ResolutionOutcome latest = await resolution.EnsureOutcomeAsync("여우", true, false);
            first.SetResult(new AccountLookupResult(Array.Empty<ResolvedAccount>(), string.Empty));

            Assert.True((await old).Stale);
            Assert.Same(account, latest.Account);
            Assert.Same(account, resolution.Selected);
            Assert.Equal(string.Empty, resolution.Error);
        }

        [Fact]
        public void QueryParameterTracker_ReenteringSameLinkAfterEmptyQuery_IsTakenAgain()
        {
            QueryParameterTracker tracker = new();

            Assert.True(tracker.TryTake(ACCOUNT_A, out string first));
            Assert.Equal(ACCOUNT_A, first);
            Assert.False(tracker.TryTake(ACCOUNT_A, out _));
            Assert.False(tracker.TryTake(null, out _));
            Assert.True(tracker.TryTake(ACCOUNT_A, out string again));
            Assert.Equal(ACCOUNT_A, again);
            Assert.False(tracker.TryTake("not-a-uuid", out _));
            Assert.True(tracker.TryTake(ACCOUNT_B, out string other));
            Assert.Equal(ACCOUNT_B, other);
        }

        [Fact]
        public async Task ReadAsync_TargetChangesWhileReading_LateResponseIsDiscardedAndNewReadRuns()
        {
            RaceHandler handler = new();
            Harness harness = Harness.Create(handler);
            await using (harness)
            {
                Task? first = null;
                await harness.Renderer.Dispatcher.InvokeAsync(async () =>
                {
                    await harness.Renderer.RenderComponentAsync<ReadProbe>();
                    first = harness.Capture.Page!.ReadAsync(AdminApiRoutes.ACCOUNTS + "?query=a");
                });
                await handler.WaitForRequestsAsync(1);

                Task? second = null;
                await harness.Renderer.Dispatcher.InvokeAsync(() =>
                {
                    harness.Capture.Page!.Invalidate();
                    second = harness.Capture.Page.ReadAsync(AdminApiRoutes.ACCOUNTS + "?query=b");
                });
                await handler.WaitForRequestsAsync(2);
                handler.Respond(1, new { marker = "B" });
                await second!;
                handler.Respond(0, new { marker = "A" });
                await first!;

                Assert.Equal("B", harness.Capture.Page!.Marker);
                Assert.False(harness.Capture.Page.IsBusy);
                Assert.Equal(1, harness.Capture.Page.ReadResponses);
            }
        }

        public sealed class Capture
        {
            public ReadProbe? Page { get; set; }
        }

        public sealed class ReadProbe : AdminPageBase
        {
            /****************************************
            *              Properties
            ****************************************/

            [Inject] public Capture Holder { get; set; } = default!;
            public string? Marker => _data?["marker"]?.ToString();
            public bool IsBusy => _busy;
            public int ReadResponses { get; private set; }

            /****************************************
            *                Methods
            ****************************************/

            protected override void OnInitialized()
            {
                base.OnInitialized();
                Holder.Page = this;
            }

            public new Task ReadAsync(string path) { return base.ReadAsync(path); }
            public void Invalidate() { InvalidateRead(); }
            protected override void OnReadResponse(JToken? value) { ++ReadResponses; }
        }

        private sealed class RaceHandler : HttpMessageHandler
        {
            /****************************************
            *                Fields
            ****************************************/

            private readonly List<TaskCompletionSource<HttpResponseMessage>> _pending = new();
            private readonly object _lock = new();

            /****************************************
            *                Methods
            ****************************************/

            public async Task WaitForRequestsAsync(int count)
            {
                for (int attempt = 0; attempt < 200; ++attempt)
                {
                    lock (_lock)
                    {
                        if (_pending.Count >= count)
                        {
                            return;
                        }
                    }
                    await Task.Delay(10);
                }
                throw new TimeoutException("요청이 도착하지 않았습니다.");
            }

            public void Respond(int index, object body)
            {
                lock (_lock)
                {
                    _pending[index].SetResult(AdminWebTestHandler.Json(body));
                }
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                TaskCompletionSource<HttpResponseMessage> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_lock)
                {
                    _pending.Add(source);
                }
                return source.Task;
            }
        }

        private sealed class Harness : IAsyncDisposable
        {
            public ServiceProvider Services { get; }
            public HtmlRenderer Renderer { get; }
            public Capture Capture { get; }

            private Harness(ServiceProvider services, HtmlRenderer renderer, Capture capture)
            {
                Services = services;
                Renderer = renderer;
                Capture = capture;
            }

            public static Harness Create(HttpMessageHandler handler)
            {
                ManualTimeProvider clock = new();
                AdminWebSessionStore store = new(clock);
                string sessionId = store.Create(new AdminLoginResponse(AdminWebTestHandler.TOKEN,
                    clock.Now.AddMinutes(30).ToUnixTimeMilliseconds(), Guid.NewGuid().ToString("D"),
                    AdminContractCodes.Role.OWNER), "test-owner");
                DefaultHttpContext httpContext = new();
                httpContext.Request.Headers.Cookie = AdminWebSessionStore.COOKIE_NAME + "=" + sessionId;
                AdminWebContext context = new(store, new HttpContextAccessor { HttpContext = httpContext });
                HttpClient http = new(handler);
                IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?> { [AdminWebApiClient.API_BASE_KEY] = "http://fake-api.test" }).Build();
                AdminWebApiClient api = new(http, context, configuration);
                Capture capture = new();
                ServiceProvider services = new ServiceCollection().AddLogging().AddSingleton(context)
                    .AddSingleton(api).AddSingleton(capture).BuildServiceProvider();
                return new Harness(services, new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>()), capture);
            }

            public async ValueTask DisposeAsync()
            {
                await Renderer.DisposeAsync();
                await Services.DisposeAsync();
            }
        }
    }
}
