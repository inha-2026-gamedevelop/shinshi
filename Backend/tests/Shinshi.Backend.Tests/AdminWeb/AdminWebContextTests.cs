// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Xunit;

// Project
using Shinshi.Admin.Web.Infrastructure;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Tests.AdminWeb
{
    public sealed class AdminWebContextTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TOKEN = "server-only-api-token";
        private const string LOGIN_NAME = "test-owner";
        private const int LIFETIME_MINUTES = 30;
        private const int CHECK_INTERVAL_SECONDS = 15;
        private const int CONCURRENT_REQUESTS = 64;

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData(8)]
        [InlineData(17)]
        [InlineData(31)]
        public void StartExpiryMonitoring_IdleCircuit_NotifiesOnceAtExactExpiryAndStopsTimer(int lifetimeSeconds)
        {
            using Fixture fixture = new Fixture(TimeSpan.FromSeconds(lifetimeSeconds));
            int notifications = 0;
            fixture.Context.SessionExpired += () => ++notifications;
            fixture.Context.StartExpiryMonitoring(DispatchImmediately);
            Assert.Equal(2, fixture.Clock.ActiveTimerCount);

            fixture.Clock.Advance(TimeSpan.FromSeconds(lifetimeSeconds).Subtract(TimeSpan.FromMilliseconds(1)));
            Assert.Equal(0, notifications);
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));

            Assert.Equal(1, notifications);
            Assert.Equal(1, fixture.Clock.ActiveTimerCount);
            Assert.False(fixture.Context.IsAuthenticated);
            Assert.Equal(string.Empty, fixture.Context.AdminId);
            Assert.Equal(string.Empty, fixture.Context.LoginName);
            Assert.Equal(-1, fixture.Context.Role);
            Assert.Equal(0, fixture.Context.ExpiresAtUnixMs);
            Assert.False(fixture.Context.HasRole(AdminContractCodes.Role.VIEWER));
            fixture.Clock.Advance(TimeSpan.FromMinutes(LIFETIME_MINUTES));
            fixture.Context.Revoke();
            Assert.Equal(1, notifications);
        }

        [Fact]
        public void StartExpiryMonitoring_FractionalMillisecondClock_UsesExactApiExpiryTimestamp()
        {
            using Fixture fixture = new Fixture(TimeSpan.FromSeconds(CHECK_INTERVAL_SECONDS));
            fixture.Clock.Now = fixture.Clock.Now.AddTicks(1);
            int notifications = 0;
            fixture.Context.SessionExpired += () => ++notifications;
            fixture.Context.StartExpiryMonitoring(DispatchImmediately);
            DateTimeOffset expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(fixture.Response.ExpiresAtUnixMs);

            fixture.Clock.Advance(expiresAt - fixture.Clock.Now);

            Assert.Equal(1, notifications);
        }

        [Fact]
        public void StartExpiryMonitoring_RevokedByAnotherRequest_NotifiesWithinFifteenSeconds()
        {
            using Fixture fixture = new Fixture();
            int notifications = 0;
            fixture.Context.SessionExpired += () => ++notifications;
            fixture.Context.StartExpiryMonitoring(DispatchImmediately);
            fixture.Store.Revoke(fixture.SessionId);

            fixture.Clock.Advance(TimeSpan.FromSeconds(CHECK_INTERVAL_SECONDS).Subtract(TimeSpan.FromMilliseconds(1)));
            Assert.Equal(0, notifications);
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));

            Assert.Equal(1, notifications);
            Assert.False(fixture.Context.IsAuthenticated);
            Assert.Equal(1, fixture.Clock.ActiveTimerCount);
        }

        [Fact]
        public void StartExpiryMonitoring_EvictedByLoginLimit_NotifiesIdleCircuit()
        {
            using Fixture fixture = new Fixture();
            int notifications = 0;
            fixture.Context.SessionExpired += () => ++notifications;
            fixture.Context.StartExpiryMonitoring(DispatchImmediately);
            for (int index = 0; index < AdminWebSessionStore.MAX_SESSIONS_PER_ADMIN; ++index)
            {
                fixture.Store.Create(fixture.Response, LOGIN_NAME);
            }

            fixture.Clock.Advance(TimeSpan.FromSeconds(CHECK_INTERVAL_SECONDS));

            Assert.Equal(1, notifications);
            Assert.False(fixture.Context.IsAuthenticated);
        }

        [Fact]
        public void StartExpiryMonitoring_DuplicateCalls_UsesOnlyOneTimerAndFirstDispatcher()
        {
            using Fixture fixture = new Fixture();
            int firstChecks = 0;
            int secondChecks = 0;
            fixture.Context.StartExpiryMonitoring(action =>
            {
                ++firstChecks;
                return DispatchImmediately(action);
            });
            fixture.Context.StartExpiryMonitoring(action =>
            {
                ++secondChecks;
                return DispatchImmediately(action);
            });

            fixture.Clock.Advance(TimeSpan.FromSeconds(CHECK_INTERVAL_SECONDS));

            Assert.Equal(1, firstChecks);
            Assert.Equal(0, secondChecks);
            Assert.Equal(2, fixture.Clock.ActiveTimerCount);
        }

        [Fact]
        public async Task StopExpiryMonitoring_AlreadyQueuedCheck_DoesNotNotifyOrRestartTimer()
        {
            using Fixture fixture = new Fixture();
            Action? queued = null;
            TaskCompletionSource<bool> completion = new();
            int notifications = 0;
            fixture.Context.SessionExpired += () => ++notifications;
            fixture.Context.StartExpiryMonitoring(action =>
            {
                queued = action;
                return completion.Task;
            });
            fixture.Clock.Advance(TimeSpan.FromSeconds(CHECK_INTERVAL_SECONDS));
            Assert.NotNull(queued);
            fixture.Store.Revoke(fixture.SessionId);

            fixture.Context.StopExpiryMonitoring();
            queued!();
            completion.SetResult(true);
            await completion.Task;
            fixture.Clock.Advance(TimeSpan.FromSeconds(CHECK_INTERVAL_SECONDS));

            Assert.Equal(0, notifications);
            Assert.Equal(1, fixture.Clock.ActiveTimerCount);
        }

        [Fact]
        public void StartExpiryMonitoring_AfterStop_RestartsAndStillDetectsRevocation()
        {
            using Fixture fixture = new Fixture();
            int notifications = 0;
            fixture.Context.SessionExpired += () => ++notifications;
            fixture.Context.StartExpiryMonitoring(DispatchImmediately);
            fixture.Context.StopExpiryMonitoring();
            Assert.Equal(1, fixture.Clock.ActiveTimerCount);

            fixture.Context.StartExpiryMonitoring(DispatchImmediately);
            fixture.Store.Revoke(fixture.SessionId);
            fixture.Clock.Advance(TimeSpan.FromSeconds(CHECK_INTERVAL_SECONDS));

            Assert.Equal(1, notifications);
            Assert.Equal(1, fixture.Clock.ActiveTimerCount);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void StartExpiryMonitoring_DispatcherDisposedOrCancelled_StopsWithoutFaultingTimer(bool cancelled)
        {
            using Fixture fixture = new Fixture();
            fixture.Context.StartExpiryMonitoring(_ => cancelled
                ? Task.FromCanceled(new CancellationToken(canceled: true))
                : Task.FromException(new ObjectDisposedException("renderer")));

            fixture.Clock.Advance(TimeSpan.FromSeconds(CHECK_INTERVAL_SECONDS));

            Assert.Equal(1, fixture.Clock.ActiveTimerCount);
            Assert.True(fixture.Context.IsAuthenticated);
        }

        [Fact]
        public void IsAuthenticated_ExpiredSession_NotifiesOnceAndNeverRevivesAfterClockMovesBack()
        {
            using Fixture fixture = new Fixture();
            DateTimeOffset original = fixture.Clock.Now;
            int notifications = 0;
            fixture.Context.SessionExpired += () => ++notifications;
            fixture.Clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(fixture.Response.ExpiresAtUnixMs);

            Assert.False(fixture.Context.IsAuthenticated);
            Assert.Equal(1, notifications);
            fixture.Clock.Now = original;

            Assert.False(fixture.Context.IsAuthenticated);
            Assert.Equal(string.Empty, fixture.Context.AdminId);
            fixture.Context.Revoke();
            Assert.Equal(1, notifications);
        }

        [Fact]
        public async Task Revoke_ConcurrentCalls_NotifiesExactlyOnceAndClearsIdentity()
        {
            using Fixture fixture = new Fixture();
            int notifications = 0;
            fixture.Context.SessionExpired += () => Interlocked.Increment(ref notifications);
            fixture.Context.StartExpiryMonitoring(DispatchImmediately);

            await Task.WhenAll(Enumerable.Range(0, CONCURRENT_REQUESTS)
                .Select(_ => Task.Run(fixture.Context.Revoke)));

            Assert.Equal(1, notifications);
            Assert.False(fixture.Context.IsAuthenticated);
            Assert.False(fixture.Store.IsActive(fixture.SessionId));
            Assert.Equal(1, fixture.Clock.ActiveTimerCount);
        }

        [Fact]
        public void Initialize_HttpContextArrivesLate_CapturesCookieOnceForCircuit()
        {
            AdminWebSessionTimeProvider clock = new AdminWebSessionTimeProvider();
            using AdminWebSessionStore store = new AdminWebSessionStore(clock);
            string adminId = Guid.NewGuid().ToString("D");
            string sessionId = store.Create(new AdminLoginResponse(TOKEN,
                clock.Now.AddMinutes(LIFETIME_MINUTES).ToUnixTimeMilliseconds(), adminId,
                AdminContractCodes.Role.OWNER), LOGIN_NAME);
            HttpContextAccessor accessor = new();
            using AdminWebContext context = new(store, accessor);
            Assert.False(context.IsAuthenticated);
            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers.Cookie = AdminWebSessionStore.COOKIE_NAME + "=" + sessionId;
            accessor.HttpContext = httpContext;

            context.Initialize();
            accessor.HttpContext = null;

            Assert.True(context.IsAuthenticated);
            Assert.Equal(adminId, context.AdminId);
            Assert.Equal(LOGIN_NAME, context.LoginName);
        }

        [Fact]
        public void Dispose_ActiveContext_StopsTimerWithoutRevokingWebSessionAndDoesNotRestart()
        {
            using Fixture fixture = new Fixture();
            int notifications = 0;
            fixture.Context.SessionExpired += () => ++notifications;
            fixture.Context.StartExpiryMonitoring(DispatchImmediately);

            fixture.Context.Dispose();
            fixture.Context.Dispose();
            fixture.Context.StartExpiryMonitoring(DispatchImmediately);
            fixture.Clock.Advance(TimeSpan.FromSeconds(CHECK_INTERVAL_SECONDS));

            Assert.Equal(0, notifications);
            Assert.Equal(1, fixture.Clock.ActiveTimerCount);
            Assert.False(fixture.Context.IsAuthenticated);
            Assert.True(fixture.Store.IsActive(fixture.SessionId));
        }

        [Fact]
        public void Serialize_AuthenticatedContext_DoesNotIncludeIdentityOrApiToken()
        {
            using Fixture fixture = new Fixture();

            string json = JsonConvert.SerializeObject(fixture.Context, ContractJson.CreateSettings());

            Assert.DoesNotContain(TOKEN, json);
            Assert.DoesNotContain(fixture.Response.AdminId, json);
            Assert.DoesNotContain(LOGIN_NAME, json);
            Assert.DoesNotContain(fixture.SessionId, json);
        }

        private static Task DispatchImmediately(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        private sealed class Fixture : IDisposable
        {
            /****************************************
            *              Properties
            ****************************************/

            public AdminWebSessionTimeProvider Clock { get; } = new();
            public AdminWebSessionStore Store { get; }
            public AdminWebContext Context { get; }
            public AdminLoginResponse Response { get; }
            public string SessionId { get; }

            /****************************************
            *                Methods
            ****************************************/

            public Fixture(TimeSpan? lifetime = null)
            {
                Store = new AdminWebSessionStore(Clock);
                Response = new AdminLoginResponse(TOKEN,
                    Clock.Now.Add(lifetime ?? TimeSpan.FromMinutes(LIFETIME_MINUTES)).ToUnixTimeMilliseconds(),
                    Guid.NewGuid().ToString("D"), AdminContractCodes.Role.OWNER);
                SessionId = Store.Create(Response, LOGIN_NAME);
                DefaultHttpContext httpContext = new();
                httpContext.Request.Headers.Cookie = AdminWebSessionStore.COOKIE_NAME + "=" + SessionId;
                Context = new AdminWebContext(Store, new HttpContextAccessor { HttpContext = httpContext });
            }

            public void Dispose()
            {
                Context.Dispose();
                Store.Dispose();
            }
        }
    }
}
