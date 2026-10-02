// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Backend.Accounts;
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Data.Accounts;

namespace Shinshi.Backend.Tests.Accounts
{
    /// <summary> (아이디, IP) 쌍 잠금 전이, 아이디 미끄럼 창 늦춤, 쌍 차례 직렬화, 키 정리를 수동 시계로 검사한다 </summary>
    public sealed class PasswordLoginLimiterTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string LOGIN_ID = "alice";
        private const string ATTACKER = "203.0.113.7";
        private const string OWNER = "198.51.100.20";
        private const int CONCURRENT_ATTEMPTS = 12;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task FiveFailures_LockOnlyThatPairUntilFifteenMinutesWithoutExtension()
        {
            ManualTimeProvider clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
            PasswordLoginLimiter limiter = new PasswordLoginLimiter(clock);
            await FailAsync(limiter, LOGIN_ID, ATTACKER, PasswordLoginLimiter.MAX_PAIR_FAILURES);

            Assert.Equal(EPasswordAttemptStatus.Locked, await BeginAsync(limiter, LOGIN_ID, ATTACKER));
            clock.Advance(TimeSpan.FromMinutes(PasswordLoginLimiter.PAIR_LOCK_MINUTES - 1));

            // 잠금 중 시도는 만료를 늘리지 않는다
            using (PasswordLoginLimiter.PasswordAttemptLease turn = (await limiter.EnterAsync(LOGIN_ID, ATTACKER,
                CancellationToken.None))!)
            {
                Assert.Equal(EPasswordAttemptStatus.Locked, turn.TryBeginAttempt(out TimeSpan retry));
                Assert.Equal(TimeSpan.FromMinutes(1), retry);
            }
            Assert.Equal(EPasswordAttemptStatus.Allowed, await BeginAsync(limiter, LOGIN_ID, OWNER));
            Assert.Equal(EPasswordAttemptStatus.Allowed, await BeginAsync(limiter, "bob", ATTACKER));

            clock.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(EPasswordAttemptStatus.Allowed, await BeginAsync(limiter, LOGIN_ID, ATTACKER));

            // 만료가 지나면 실패 수가 0이므로 다시 5회를 틀려야 잠긴다
            await FailAsync(limiter, LOGIN_ID, ATTACKER, PasswordLoginLimiter.MAX_PAIR_FAILURES - 1);
            Assert.Equal(EPasswordAttemptStatus.Allowed, await BeginAsync(limiter, LOGIN_ID, ATTACKER));
        }

        [Fact]
        public async Task Success_ResetsPairFailures()
        {
            PasswordLoginLimiter limiter = new PasswordLoginLimiter(new ManualTimeProvider(DateTimeOffset.UnixEpoch));
            await FailAsync(limiter, LOGIN_ID, OWNER, PasswordLoginLimiter.MAX_PAIR_FAILURES - 1);

            using (PasswordLoginLimiter.PasswordAttemptLease turn = (await limiter.EnterAsync(LOGIN_ID, OWNER,
                CancellationToken.None))!)
            {
                turn.RecordSuccess();
            }
            await FailAsync(limiter, LOGIN_ID, OWNER, PasswordLoginLimiter.MAX_PAIR_FAILURES - 1);

            Assert.Equal(EPasswordAttemptStatus.Allowed, await BeginAsync(limiter, LOGIN_ID, OWNER));
        }

        [Fact]
        public async Task ManyFailuresAcrossIps_SlowLoginIdToOneAttemptPerMinutePerIp()
        {
            ManualTimeProvider clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
            PasswordLoginLimiter limiter = new PasswordLoginLimiter(clock);
            int ips = PasswordLoginLimiter.MAX_LOGIN_ID_FAILURES_PER_WINDOW / PasswordLoginLimiter.MAX_PAIR_FAILURES;
            for (int i = 0; i < ips; ++i)
            {
                await FailAsync(limiter, LOGIN_ID, "10.0.0." + i, PasswordLoginLimiter.MAX_PAIR_FAILURES);
            }
            await FailAsync(limiter, LOGIN_ID, "10.0.0.250", 1);

            Assert.Equal(EPasswordAttemptStatus.Allowed, await BeginAsync(limiter, LOGIN_ID, OWNER));
            Assert.Equal(EPasswordAttemptStatus.Slowed, await BeginAsync(limiter, LOGIN_ID, OWNER));
            Assert.Equal(EPasswordAttemptStatus.Allowed, await BeginAsync(limiter, "bob", OWNER));
            Assert.Equal(EPasswordAttemptStatus.Allowed, await BeginAsync(limiter, "bob", OWNER));

            clock.Advance(TimeSpan.FromSeconds(PasswordLoginLimiter.SLOWED_INTERVAL_SECONDS));
            Assert.Equal(EPasswordAttemptStatus.Allowed, await BeginAsync(limiter, LOGIN_ID, OWNER));
        }

        [Fact]
        public async Task LoginIdWindow_IsSlidingOverLastHour()
        {
            ManualTimeProvider clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
            PasswordLoginLimiter limiter = new PasswordLoginLimiter(clock);

            // 30분 간격의 두 묶음(30회 + 21회)은 한 시간 안에 51회다
            for (int i = 0; i < 6; ++i)
            {
                await FailAsync(limiter, LOGIN_ID, "10.1.0." + i, PasswordLoginLimiter.MAX_PAIR_FAILURES);
            }
            clock.Advance(TimeSpan.FromMinutes(30));
            for (int i = 0; i < 4; ++i)
            {
                await FailAsync(limiter, LOGIN_ID, "10.2.0." + i, PasswordLoginLimiter.MAX_PAIR_FAILURES);
            }
            await FailAsync(limiter, LOGIN_ID, "10.2.0.9", 1);
            Assert.Equal(EPasswordAttemptStatus.Allowed, await BeginAsync(limiter, LOGIN_ID, OWNER));
            Assert.Equal(EPasswordAttemptStatus.Slowed, await BeginAsync(limiter, LOGIN_ID, OWNER));

            // 첫 묶음이 창 밖으로 나가면 21회만 남아 늦춤이 풀린다
            clock.Advance(TimeSpan.FromMinutes(30));
            Assert.Equal(EPasswordAttemptStatus.Allowed, await BeginAsync(limiter, LOGIN_ID, OWNER));
            Assert.Equal(EPasswordAttemptStatus.Allowed, await BeginAsync(limiter, LOGIN_ID, OWNER));
        }

        [Fact]
        public async Task FiftyFailures_DoNotSlowYet()
        {
            PasswordLoginLimiter limiter = new PasswordLoginLimiter(new ManualTimeProvider(DateTimeOffset.UnixEpoch));
            int ips = PasswordLoginLimiter.MAX_LOGIN_ID_FAILURES_PER_WINDOW / PasswordLoginLimiter.MAX_PAIR_FAILURES;
            for (int i = 0; i < ips; ++i)
            {
                await FailAsync(limiter, LOGIN_ID, "10.0.1." + i, PasswordLoginLimiter.MAX_PAIR_FAILURES);
            }

            Assert.Equal(EPasswordAttemptStatus.Allowed, await BeginAsync(limiter, LOGIN_ID, OWNER));
            Assert.Equal(EPasswordAttemptStatus.Allowed, await BeginAsync(limiter, LOGIN_ID, OWNER));
        }

        [Fact]
        public async Task Keys_IdleForAnHourAreRemovedAndOverflowEvictsOldest()
        {
            ManualTimeProvider clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
            const int MAX_KEYS = 10;
            PasswordLoginLimiter limiter = new PasswordLoginLimiter(clock, TimeSpan.Zero, MAX_KEYS);
            await FailAsync(limiter, "old_user", ATTACKER, PasswordLoginLimiter.MAX_PAIR_FAILURES);
            Assert.Equal(2, limiter.KeyCount);

            clock.Advance(TimeSpan.FromMinutes(PasswordLoginLimiter.KEY_IDLE_MINUTES));
            await BeginAsync(limiter, "fresh", OWNER);
            Assert.Equal(1, limiter.KeyCount);
            Assert.Equal(EPasswordAttemptStatus.Allowed, await BeginAsync(limiter, "old_user", ATTACKER));

            for (int i = 0; i < MAX_KEYS * 2; ++i)
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                await BeginAsync(limiter, "user" + i, OWNER);
            }
            Assert.True(limiter.KeyCount <= MAX_KEYS);
        }

        [Fact]
        public async Task ConcurrentSamePair_TransitionsOneAtATimeSoOnlyFiveAttemptsHash()
        {
            PasswordLoginLimiter limiter = new PasswordLoginLimiter(new ManualTimeProvider(DateTimeOffset.UnixEpoch),
                TimeSpan.FromSeconds(30), PasswordLoginLimiter.MAX_KEYS);
            CountingPasswordHasher hasher = new CountingPasswordHasher();
            using PasswordHashGate gate = new PasswordHashGate();
            PasswordCredentialVerifier verifier = new PasswordCredentialVerifier(hasher, gate, limiter);

            PasswordCheckResult[] results = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_ATTEMPTS)
                .Select(_ => Task.Run(() => verifier.CheckAsync(LOGIN_ID, ATTACKER, "Wrong9Password",
                    _ => Task.FromResult<AccountCredentialRecord?>(null), CancellationToken.None))));

            Assert.Equal(PasswordLoginLimiter.MAX_PAIR_FAILURES,
                results.Count(result => result.Status == EPasswordCheckStatus.InvalidCredentials));
            Assert.Equal(CONCURRENT_ATTEMPTS - PasswordLoginLimiter.MAX_PAIR_FAILURES,
                results.Count(result => result.Status == EPasswordCheckStatus.Locked));
            Assert.Equal(PasswordLoginLimiter.MAX_PAIR_FAILURES, hasher.DummyCalls);
            Assert.Equal(PasswordLoginLimiter.MAX_PAIR_FAILURES, hasher.Derivations);
        }

        [Fact]
        public async Task EnterAsync_PairBusyPastWait_ReturnsNull()
        {
            PasswordLoginLimiter limiter = new PasswordLoginLimiter(new ManualTimeProvider(DateTimeOffset.UnixEpoch),
                TimeSpan.Zero, PasswordLoginLimiter.MAX_KEYS);
            using PasswordLoginLimiter.PasswordAttemptLease? first = await limiter.EnterAsync(LOGIN_ID, OWNER,
                CancellationToken.None);

            Assert.NotNull(first);
            Assert.Null(await limiter.EnterAsync(LOGIN_ID, OWNER, CancellationToken.None));
            Assert.NotNull(await limiter.EnterAsync(LOGIN_ID, ATTACKER, CancellationToken.None));
        }

        private static async Task<EPasswordAttemptStatus> BeginAsync(PasswordLoginLimiter limiter, string loginId,
            string ip)
        {
            using PasswordLoginLimiter.PasswordAttemptLease turn = (await limiter.EnterAsync(loginId, ip,
                CancellationToken.None))!;
            return turn.TryBeginAttempt(out _);
        }

        private static async Task FailAsync(PasswordLoginLimiter limiter, string loginId, string ip, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                using PasswordLoginLimiter.PasswordAttemptLease turn = (await limiter.EnterAsync(loginId, ip,
                    CancellationToken.None))!;
                Assert.Equal(EPasswordAttemptStatus.Allowed, turn.TryBeginAttempt(out _));
                turn.RecordFailure();
            }
        }
    }
}
