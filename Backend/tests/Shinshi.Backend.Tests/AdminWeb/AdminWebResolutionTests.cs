// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Admin.Web.Models;

namespace Shinshi.Backend.Tests.AdminWeb
{
    /// <summary> 대상 확정 상태 전이, 최신 요청만 적용, 기본 기간 확정 회귀 테스트 </summary>
    public sealed class AdminWebResolutionTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ACCOUNT_A = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
        private const string ACCOUNT_B = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";

        /****************************************
        *                Methods
        ****************************************/

        private static ResolvedAccount Account(string id, string? name = null)
        {
            return new ResolvedAccount(id, null, name, "CharacterName", false, false, 1);
        }

        private static AccountLookupResult Found(string id, string? name = null)
        {
            return new AccountLookupResult(new[] { Account(id, name) }, string.Empty);
        }

        private static AccountLookupResult NotFound()
        {
            return new AccountLookupResult(Array.Empty<ResolvedAccount>(), string.Empty);
        }

        private static AccountResolution Synchronous(Dictionary<string, AccountLookupResult> table, List<string>? calls = null)
        {
            return new AccountResolution((text, deleted, invite) =>
            {
                calls?.Add(AccountResolution.KeyOf(text.Trim(), deleted, invite));
                return Task.FromResult(table.TryGetValue(text.Trim(), out AccountLookupResult? result) ? result : NotFound());
            });
        }

        [Fact]
        public async Task EnsureAsync_InputChangesFromAToB_ReturnsB()
        {
            AccountResolution resolution = Synchronous(new()
            {
                ["여우"] = Found(ACCOUNT_A, "여우"), ["호랑이"] = Found(ACCOUNT_B, "호랑이")
            });

            ResolvedAccount? first = await resolution.EnsureAsync("여우", false, false);
            ResolvedAccount? second = await resolution.EnsureAsync("호랑이", false, false);

            Assert.Equal(ACCOUNT_A, first!.AccountId);
            Assert.Equal(ACCOUNT_B, second!.AccountId);
        }

        [Fact]
        public async Task EnsureAsync_AThenUnknownName_DoesNotReturnA()
        {
            AccountResolution resolution = Synchronous(new() { ["여우"] = Found(ACCOUNT_A, "여우") });
            await resolution.EnsureAsync("여우", false, false);

            ResolvedAccount? result = await resolution.EnsureAsync("없는이름", false, false);

            Assert.Null(result);
            Assert.Null(resolution.Selected);
            Assert.Contains("찾을 수 없습니다", resolution.Error);
        }

        [Fact]
        public async Task EnsureAsync_SameInputTwice_UsesCachedSuccessWithoutSecondLookup()
        {
            List<string> calls = new();
            AccountResolution resolution = Synchronous(new() { ["여우"] = Found(ACCOUNT_A) }, calls);

            await resolution.EnsureAsync("여우", false, false);
            await resolution.EnsureAsync("여우 ", false, false);

            Assert.Single(calls);
        }

        [Fact]
        public async Task EnsureAsync_ModeOrDeletedOptionChanges_LooksUpAgain()
        {
            List<string> calls = new();
            AccountResolution resolution = Synchronous(new() { ["여우"] = Found(ACCOUNT_A) }, calls);

            await resolution.EnsureAsync("여우", false, false);
            await resolution.EnsureAsync("여우", true, false);
            await resolution.EnsureAsync("여우", true, true);

            Assert.Equal(3, calls.Count);
            Assert.Equal(3, calls.Distinct().Count());
        }

        [Fact]
        public async Task EnsureAsync_FailureIsNotCached_NextCallRetries()
        {
            int attempts = 0;
            AccountResolution resolution = new((text, deleted, invite) =>
            {
                ++attempts;
                return Task.FromResult(attempts == 1
                    ? new AccountLookupResult(Array.Empty<ResolvedAccount>(), "API에 연결하지 못했습니다.")
                    : Found(ACCOUNT_A));
            });

            Assert.Null(await resolution.EnsureAsync("여우", false, false));
            Assert.Equal("API에 연결하지 못했습니다.", resolution.Error);
            ResolvedAccount? retry = await resolution.EnsureAsync("여우", false, false);

            Assert.Equal(ACCOUNT_A, retry!.AccountId);
            Assert.Equal(2, attempts);
            Assert.Equal(string.Empty, resolution.Error);
        }

        [Fact]
        public async Task EnsureAsync_LateResponseAfterInputChange_IsDiscarded()
        {
            TaskCompletionSource<AccountLookupResult> slow = new(TaskCreationOptions.RunContinuationsAsynchronously);
            AccountResolution resolution = new((text, deleted, invite) =>
                text.Trim() == "느림" ? slow.Task : Task.FromResult(Found(ACCOUNT_B)));

            Task<ResolvedAccount?> pending = resolution.EnsureAsync("느림", false, false);
            resolution.Reset();
            ResolvedAccount? other = await resolution.EnsureAsync("빠름", false, false);
            slow.SetResult(Found(ACCOUNT_A));
            ResolvedAccount? late = await pending;

            Assert.Equal(ACCOUNT_B, other!.AccountId);
            Assert.Null(late);
            Assert.Equal(ACCOUNT_B, resolution.Selected!.AccountId);
        }

        [Fact]
        public async Task EnsureAsync_MultipleMatches_RequiresChoiceAndChoiceMustBeCandidate()
        {
            ResolvedAccount a = Account(ACCOUNT_A);
            ResolvedAccount b = Account(ACCOUNT_B);
            AccountResolution resolution = new((text, deleted, invite) =>
                Task.FromResult(new AccountLookupResult(new[] { a, b }, string.Empty)));

            Assert.Null(await resolution.EnsureAsync("여우", false, false));
            Assert.Equal(2, resolution.Matches.Count);
            Assert.False(resolution.Choose(Account("cccccccc-cccc-4ccc-8ccc-cccccccccccc")));
            Assert.True(resolution.Choose(b));

            Assert.Equal(ACCOUNT_B, resolution.Selected!.AccountId);
        }

        [Fact]
        public async Task EnsureAllAsync_PartialFailureIsNotCached_RetrySucceeds()
        {
            int calls = 0;
            TargetResolution resolution = new(token =>
            {
                ++calls;
                bool fail = (token == "호랑이") && (calls <= 2);
                return Task.FromResult(fail ? new AccountLookupResult(Array.Empty<ResolvedAccount>(), "연결 실패")
                    : Found(token == "여우" ? ACCOUNT_A : ACCOUNT_B));
            });

            Assert.Null(await resolution.EnsureAllAsync("여우\n호랑이"));
            Assert.Contains(resolution.Items, item => item.Account == null);
            IReadOnlyList<string>? retry = await resolution.EnsureAllAsync("여우\n호랑이");

            Assert.Equal(new[] { ACCOUNT_A, ACCOUNT_B }, retry);
        }

        [Fact]
        public async Task EnsureAllAsync_TextChanges_DoesNotReturnPreviousTargets()
        {
            TargetResolution resolution = new(token => Task.FromResult(
                token == "여우" ? Found(ACCOUNT_A) : token == "호랑이" ? Found(ACCOUNT_B) : NotFound()));

            Assert.Equal(new[] { ACCOUNT_A }, await resolution.EnsureAllAsync("여우"));
            Assert.Equal(new[] { ACCOUNT_B }, await resolution.EnsureAllAsync("호랑이"));
            Assert.Null(await resolution.EnsureAllAsync("없음"));
            Assert.Null(await resolution.EnsureAllAsync(" "));
        }

        [Fact]
        public async Task EnsureAllAsync_SameAccountTwice_IsDeduplicatedWithNote()
        {
            TargetResolution resolution = new(token => Task.FromResult(Found(ACCOUNT_A)));

            IReadOnlyList<string>? accounts = await resolution.EnsureAllAsync("여우, 여우2");

            Assert.Equal(new[] { ACCOUNT_A }, accounts);
            Assert.Single(resolution.Notes);
        }

        [Fact]
        public async Task EnsureAllAsync_ResetWhileLookingUp_DiscardsLateResults()
        {
            TaskCompletionSource<AccountLookupResult> slow = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TargetResolution resolution = new(token => token == "느림" ? slow.Task : Task.FromResult(Found(ACCOUNT_B)));

            Task<IReadOnlyList<string>?> pending = resolution.EnsureAllAsync("느림");
            resolution.Reset();
            slow.SetResult(Found(ACCOUNT_A));

            Assert.Null(await pending);
            Assert.Empty(resolution.Items);
        }

        [Fact]
        public async Task LatestRequest_OlderResponseArrivesLast_IsNotApplied()
        {
            LatestRequest latest = new();
            TaskCompletionSource<string> a = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<string> b = new(TaskCreationOptions.RunContinuationsAsynchronously);
            List<string> applied = new();

            Task<bool> first = latest.RunAsync(() => a.Task, applied.Add);
            Task<bool> second = latest.RunAsync(() => b.Task, applied.Add);
            b.SetResult("B");
            a.SetResult("A");

            Assert.True(await second);
            Assert.False(await first);
            Assert.Equal(new[] { "B" }, applied);
            Assert.False(latest.Pending);
        }

        [Fact]
        public async Task LatestRequest_CancelDiscardsPendingResponse()
        {
            LatestRequest latest = new();
            TaskCompletionSource<string> a = new(TaskCreationOptions.RunContinuationsAsynchronously);
            List<string> applied = new();

            Task<bool> pending = latest.RunAsync(() => a.Task, applied.Add);
            latest.Cancel();
            a.SetResult("A");

            Assert.False(await pending);
            Assert.Empty(applied);
        }

        [Fact]
        public void NoticePeriod_BlankInputs_UseOneInstantForStartAndEnd()
        {
            DateTimeOffset now = new(2026, 10, 7, 6, 30, 0, TimeSpan.Zero);
            WriteDefaults defaults = new();

            (long starts, long ends) = NoticePeriod.Resolve("", "", now, 24, defaults);

            Assert.Equal(now.ToUnixTimeMilliseconds(), starts);
            Assert.Equal(starts + 24 * 3600_000L, ends);
            Assert.Equal(2, defaults.Notes.Count);
            Assert.Contains("2026-10-07 15:30 KST", defaults.Notes[0]);
            Assert.Contains("2026-10-08 15:30 KST", defaults.Notes[1]);
        }

        [Fact]
        public void NoticePeriod_EndNotAfterStart_Throws()
        {
            DateTimeOffset now = new(2026, 10, 7, 6, 30, 0, TimeSpan.Zero);

            Assert.Throws<ArgumentException>(() => NoticePeriod.Resolve("", "2026-10-07T15:30", now, 24));
        }

        [Fact]
        public void LogPeriod_BlankEnd_IsPinnedToNowAndStartDefaultsToDayBefore()
        {
            DateTimeOffset now = new(2026, 10, 7, 6, 30, 0, TimeSpan.Zero);

            (long from, long to, bool toDefaulted) = LogPeriod.Resolve("", "", now, 24);

            Assert.True(toDefaulted);
            Assert.Equal(now.ToUnixTimeMilliseconds(), to);
            Assert.Equal(to - 24 * 3600_000L, from);
        }

        [Fact]
        public void LogPeriod_StartNotBeforeEnd_Throws()
        {
            DateTimeOffset now = new(2026, 10, 7, 6, 30, 0, TimeSpan.Zero);

            Assert.Throws<ArgumentException>(() => LogPeriod.Resolve("2026-10-08T00:00", "2026-10-07T00:00", now, 24));
        }

        [Fact]
        public void AdminTime_OutOfSupportedRange_IsRejectedWithoutUnhandledException()
        {
            DateTimeOffset now = new(2026, 10, 7, 6, 30, 0, TimeSpan.Zero);

            Assert.False(AdminTime.TryParseInput("1999-12-31T23:59", out _));
            Assert.False(AdminTime.TryParseInput("2101-01-01T00:00", out _));
            Assert.Throws<ArgumentException>(() => AdminTime.AddToInput("2100-12-31T23:00", TimeSpan.FromDays(1), now));
            Assert.Equal("2026-10-08T15:30", AdminTime.AddToInput("", TimeSpan.FromDays(1), now));
        }
    }
}
