// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// Third Party
using Newtonsoft.Json;
using Xunit;

// Project
using Shinshi.Admin.Web.Infrastructure;
using Shinshi.Backend.Tests.Admin;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Tests.AdminWeb
{
    public sealed class AdminWebSessionStoreTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TOKEN       = "api-token-kept-only-on-the-server";
        private const string LOGIN_NAME  = "test-owner";
        private const int    LIFETIME_MIN = 30;
        private const int CONCURRENT_REQUESTS = 64;
        private const int CLEANUP_SECONDS = 15;
        private const int SHORT_LIFETIME_SECONDS = 1;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void Create_NewSession_ReturnsOpaqueUniqueCookieAndPublicIdentityWithoutToken()
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            using AdminWebSessionStore store = new AdminWebSessionStore(clock);
            AdminLoginResponse response = CreateResponse(clock);

            string first = store.Create(response, LOGIN_NAME);
            string second = store.Create(response, LOGIN_NAME);
            AdminWebIdentity identity = Assert.IsType<AdminWebIdentity>(store.GetIdentity(first));
            string publicJson = JsonConvert.SerializeObject(identity, ContractJson.CreateSettings());

            Assert.True(store.IsActive(first));
            Assert.NotEqual(first, second);
            Assert.DoesNotContain(TOKEN, first);
            Assert.DoesNotContain(response.AdminId, first);
            Assert.DoesNotContain(LOGIN_NAME, first);
            Assert.Equal(response.AdminId, identity.AdminId);
            Assert.Equal(LOGIN_NAME, identity.LoginName);
            Assert.Equal(response.Role, identity.Role);
            Assert.Equal(response.ExpiresAtUnixMs, identity.ExpiresAtUnixMs);
            Assert.DoesNotContain(TOKEN, publicJson);
            Assert.DoesNotContain("accessToken", publicJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(typeof(AdminWebIdentity).GetProperties(),
                property => property.Name.Contains("token", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(typeof(AdminWebSessionStore).GetMethods(),
                method => method.IsPublic && method.Name.Contains("token", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void IsActive_AtExactExpiry_DeletesSessionAndCannotReviveWhenClockMovesBack()
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            using AdminWebSessionStore store = new AdminWebSessionStore(clock);
            AdminLoginResponse response = CreateResponse(clock);
            string sessionId = store.Create(response, LOGIN_NAME);
            DateTimeOffset original = clock.Now;

            clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(response.ExpiresAtUnixMs).AddMilliseconds(-1);
            Assert.True(store.IsActive(sessionId));
            clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(response.ExpiresAtUnixMs);
            Assert.False(store.IsActive(sessionId));
            Assert.Null(store.GetIdentity(sessionId));

            clock.Now = original;
            Assert.False(store.IsActive(sessionId));
            Assert.Null(store.GetIdentity(sessionId));
        }

        [Fact]
        public void Create_LongerApiLifetime_CapsWebSessionAtThirtyMinutes()
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            using AdminWebSessionStore store = new AdminWebSessionStore(clock);
            const int LONGER_LIFETIME_MINUTES = LIFETIME_MIN * 2;
            AdminLoginResponse response = CreateResponse(clock,
                lifetime: TimeSpan.FromMinutes(LONGER_LIFETIME_MINUTES));

            string sessionId = store.Create(response, LOGIN_NAME);

            Assert.Equal(clock.Now.AddMinutes(LIFETIME_MIN).ToUnixTimeMilliseconds(),
                store.GetIdentity(sessionId)!.ExpiresAtUnixMs);
        }

        [Fact]
        public void Revoke_ExistingSession_RemovesOnlyThatSession()
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            using AdminWebSessionStore store = new AdminWebSessionStore(clock);
            AdminLoginResponse response = CreateResponse(clock);
            string revoked = store.Create(response, LOGIN_NAME);
            string other = store.Create(response, LOGIN_NAME);

            Assert.True(store.Revoke(revoked));
            Assert.False(store.Revoke(revoked));
            Assert.False(store.IsActive(revoked));
            Assert.Null(store.GetIdentity(revoked));
            Assert.True(store.IsActive(other));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("unknown-cookie")]
        public void GetIdentity_MissingSession_ReturnsNoIdentity(string? sessionId)
        {
            using AdminWebSessionStore store = new AdminWebSessionStore(TimeProvider.System);

            Assert.False(store.IsActive(sessionId));
            Assert.Null(store.GetIdentity(sessionId));
            Assert.False(store.Revoke(sessionId));
        }

        [Fact]
        public void Create_SixthSessionForSameAdminId_EvictsOldestEvenWithAnotherLoginName()
        {
            AdminWebSessionTimeProvider clock = new AdminWebSessionTimeProvider();
            using AdminWebSessionStore store = new AdminWebSessionStore(clock);
            AdminLoginResponse response = CreateResponse(clock);
            List<string> sessions = new();
            for (int index = 0; index < AdminWebSessionStore.MAX_SESSIONS_PER_ADMIN; ++index)
            {
                sessions.Add(store.Create(response, LOGIN_NAME));
            }

            string newest = store.Create(response, "renamed-owner");

            Assert.False(store.IsActive(sessions[0]));
            Assert.All(sessions.Skip(1), sessionId => Assert.True(store.IsActive(sessionId)));
            Assert.True(store.IsActive(newest));
            Assert.Equal(AdminWebSessionStore.MAX_SESSIONS_PER_ADMIN, StoredCount(store, "_sessions"));
        }

        [Fact]
        public void Create_AtTotalLimit_EvictsOldestGlobally()
        {
            AdminWebSessionTimeProvider clock = new AdminWebSessionTimeProvider();
            using AdminWebSessionStore store = new AdminWebSessionStore(clock);
            List<string> sessions = new();
            for (int index = 0; index < AdminWebSessionStore.MAX_TOTAL_SESSIONS; ++index)
            {
                sessions.Add(store.Create(CreateResponse(clock), LOGIN_NAME));
            }

            string newest = store.Create(CreateResponse(clock), LOGIN_NAME);

            Assert.False(store.IsActive(sessions[0]));
            Assert.All(sessions.Skip(1), sessionId => Assert.True(store.IsActive(sessionId)));
            Assert.True(store.IsActive(newest));
            Assert.Equal(AdminWebSessionStore.MAX_TOTAL_SESSIONS, StoredCount(store, "_sessions"));
        }

        [Fact]
        public void Create_ExpiredNewerSessionAtAdminLimit_PrunesItBeforeEvictingOldestLiveSession()
        {
            AdminWebSessionTimeProvider clock = new AdminWebSessionTimeProvider();
            using AdminWebSessionStore store = new AdminWebSessionStore(clock);
            AdminLoginResponse response = CreateResponse(clock);
            string oldest = store.Create(response, LOGIN_NAME);
            string expired = store.Create(CreateResponse(clock, response.AdminId,
                TimeSpan.FromSeconds(SHORT_LIFETIME_SECONDS)), LOGIN_NAME);
            const int EXISTING_SESSIONS = 2;
            for (int index = EXISTING_SESSIONS; index < AdminWebSessionStore.MAX_SESSIONS_PER_ADMIN; ++index)
            {
                store.Create(response, LOGIN_NAME);
            }
            clock.Advance(TimeSpan.FromSeconds(SHORT_LIFETIME_SECONDS));

            store.Create(response, LOGIN_NAME);

            Assert.True(store.IsActive(oldest));
            Assert.False(store.IsActive(expired));
            Assert.Equal(AdminWebSessionStore.MAX_SESSIONS_PER_ADMIN, StoredCount(store, "_sessions"));
        }

        [Fact]
        public async Task Create_ConcurrentLoginsAndRevocations_KeepsAtMostFiveSessionsForAdmin()
        {
            AdminWebSessionTimeProvider clock = new AdminWebSessionTimeProvider();
            using AdminWebSessionStore store = new AdminWebSessionStore(clock);
            AdminLoginResponse response = CreateResponse(clock);
            string[] original = Enumerable.Range(0, AdminWebSessionStore.MAX_SESSIONS_PER_ADMIN)
                .Select(_ => store.Create(response, LOGIN_NAME)).ToArray();

            string[] created = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_REQUESTS).Select(index => Task.Run(() =>
            {
                string sessionId = store.Create(response, LOGIN_NAME);
                store.Revoke(original[index % original.Length]);
                store.GetIdentity(sessionId);
                return sessionId;
            })));

            Assert.Equal(AdminWebSessionStore.MAX_SESSIONS_PER_ADMIN, created.Count(store.IsActive));
            Assert.Equal(AdminWebSessionStore.MAX_SESSIONS_PER_ADMIN, StoredCount(store, "_sessions"));
            Assert.Equal(AdminWebSessionStore.MAX_SESSIONS_PER_ADMIN, StoredCount(store, "_expiryOrder"));
            Assert.Equal(AdminWebSessionStore.MAX_SESSIONS_PER_ADMIN, StoredCount(store, "_creationOrder"));
        }

        [Fact]
        public async Task Create_ConcurrentDifferentAdmins_KeepsTotalLimit()
        {
            AdminWebSessionTimeProvider clock = new AdminWebSessionTimeProvider();
            using AdminWebSessionStore store = new AdminWebSessionStore(clock);
            const int REQUEST_COUNT = AdminWebSessionStore.MAX_TOTAL_SESSIONS + CONCURRENT_REQUESTS;

            string[] created = await Task.WhenAll(Enumerable.Range(0, REQUEST_COUNT)
                .Select(_ => Task.Run(() => store.Create(CreateResponse(clock), LOGIN_NAME))));

            Assert.Equal(AdminWebSessionStore.MAX_TOTAL_SESSIONS, created.Count(store.IsActive));
            Assert.Equal(AdminWebSessionStore.MAX_TOTAL_SESSIONS, StoredCount(store, "_sessions"));
            Assert.Equal(AdminWebSessionStore.MAX_TOTAL_SESSIONS, StoredCount(store, "_expiryOrder"));
            Assert.Equal(AdminWebSessionStore.MAX_TOTAL_SESSIONS, StoredCount(store, "_creationOrder"));
        }

        [Fact]
        public async Task Revoke_ConcurrentRequests_OnlyOneRemovesSession()
        {
            AdminWebSessionTimeProvider clock = new AdminWebSessionTimeProvider();
            using AdminWebSessionStore store = new AdminWebSessionStore(clock);
            string sessionId = store.Create(CreateResponse(clock), LOGIN_NAME);

            bool[] results = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_REQUESTS)
                .Select(_ => Task.Run(() => store.Revoke(sessionId))));

            Assert.Single(results, result => result);
            Assert.Equal(0, StoredCount(store, "_sessions"));
            Assert.Equal(0, StoredCount(store, "_sessionsByAdmin"));
            Assert.Equal(0, StoredCount(store, "_expiryOrder"));
            Assert.Equal(0, StoredCount(store, "_creationOrder"));
        }

        [Fact]
        public void GetIdentity_UnrelatedLiveSession_PrunesOtherExpiredSessionsBeforeTimerTick()
        {
            AdminWebSessionTimeProvider clock = new AdminWebSessionTimeProvider();
            using AdminWebSessionStore store = new AdminWebSessionStore(clock);
            string expired = store.Create(CreateResponse(clock,
                lifetime: TimeSpan.FromSeconds(SHORT_LIFETIME_SECONDS)), LOGIN_NAME);
            string active = store.Create(CreateResponse(clock), LOGIN_NAME);
            DateTimeOffset original = clock.Now;
            clock.Advance(TimeSpan.FromSeconds(SHORT_LIFETIME_SECONDS));
            Assert.Equal(2, StoredCount(store, "_sessions"));

            Assert.NotNull(store.GetIdentity(active));

            Assert.Equal(1, StoredCount(store, "_sessions"));
            clock.Now = original;
            Assert.False(store.IsActive(expired));
        }

        [Fact]
        public void Cleanup_IdleStore_RemovesExpiredSessionsWithoutQueriesOrNewLogins()
        {
            AdminWebSessionTimeProvider clock = new AdminWebSessionTimeProvider();
            using AdminWebSessionStore store = new AdminWebSessionStore(clock);
            store.Create(CreateResponse(clock, lifetime: TimeSpan.FromSeconds(SHORT_LIFETIME_SECONDS)), LOGIN_NAME);
            clock.Advance(TimeSpan.FromSeconds(CLEANUP_SECONDS).Subtract(TimeSpan.FromMilliseconds(1)));
            Assert.Equal(1, StoredCount(store, "_sessions"));

            clock.Advance(TimeSpan.FromMilliseconds(1));

            Assert.Equal(0, StoredCount(store, "_sessions"));
            Assert.Equal(0, StoredCount(store, "_sessionsByAdmin"));
            Assert.Equal(0, StoredCount(store, "_expiryOrder"));
            Assert.Equal(0, StoredCount(store, "_creationOrder"));
        }

        [Fact]
        public void Create_RepeatedEvictions_DoesNotAccumulateExpiredIndexOrTokenReferences()
        {
            AdminWebSessionTimeProvider clock = new AdminWebSessionTimeProvider();
            using AdminWebSessionStore store = new AdminWebSessionStore(clock);
            AdminLoginResponse response = CreateResponse(clock);
            for (int index = 0; index < AdminWebSessionStore.MAX_TOTAL_SESSIONS; ++index)
            {
                store.Create(response, LOGIN_NAME);
            }

            Assert.Equal(AdminWebSessionStore.MAX_SESSIONS_PER_ADMIN, StoredCount(store, "_sessions"));
            Assert.Equal(AdminWebSessionStore.MAX_SESSIONS_PER_ADMIN, StoredCount(store, "_expiryOrder"));
            Assert.Equal(AdminWebSessionStore.MAX_SESSIONS_PER_ADMIN, StoredCount(store, "_creationOrder"));
            Assert.Equal(1, StoredCount(store, "_sessionsByAdmin"));
        }

        [Fact]
        public void Dispose_ActiveSessions_StopsTimerAndClearsAllIndexes()
        {
            AdminWebSessionTimeProvider clock = new AdminWebSessionTimeProvider();
            AdminWebSessionStore store = new AdminWebSessionStore(clock);
            AdminLoginResponse response = CreateResponse(clock);
            string sessionId = store.Create(response, LOGIN_NAME);

            store.Dispose();
            store.Dispose();
            clock.Advance(TimeSpan.FromMinutes(LIFETIME_MIN));

            Assert.Equal(0, clock.ActiveTimerCount);
            Assert.Equal(0, StoredCount(store, "_sessions"));
            Assert.Equal(0, StoredCount(store, "_sessionsByAdmin"));
            Assert.Equal(0, StoredCount(store, "_expiryOrder"));
            Assert.Equal(0, StoredCount(store, "_creationOrder"));
            Assert.False(store.IsActive(sessionId));
            Assert.Null(store.GetIdentity(sessionId));
            Assert.False(store.Revoke(sessionId));
            Assert.Throws<ObjectDisposedException>(() => store.Create(response, LOGIN_NAME));
        }

        private static AdminLoginResponse CreateResponse(TimeProvider clock, string? adminId = null, TimeSpan? lifetime = null)
        {
            return new AdminLoginResponse(TOKEN,
                clock.GetUtcNow().Add(lifetime ?? TimeSpan.FromMinutes(LIFETIME_MIN)).ToUnixTimeMilliseconds(),
                adminId ?? Guid.NewGuid().ToString("D"), AdminContractCodes.Role.OWNER);
        }

        // 조회가 정리를 유발하므로 유휴 타이머의 실제 제거 여부는 조회를 거치지 않고 관찰한다
        private static int StoredCount(AdminWebSessionStore store, string fieldName)
        {
            object index = typeof(AdminWebSessionStore).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(store)!;
            if (index is IDictionary dictionary)
            {
                return dictionary.Count;
            }
            return (int)index.GetType().GetProperty("Count")!.GetValue(index)!;
        }
    }
}
