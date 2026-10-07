// System
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Admin.Web.Infrastructure;
using Shinshi.Backend.Tests.Admin;

namespace Shinshi.Backend.Tests.AdminWeb
{
    public sealed class AdminWebLoginLimiterTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string LOGIN_NAME = "test-owner";
        private const int BLOCK_SECONDS = 60;
        private const int RECORD_MINUTES = 15;
        private const int PARALLEL_REQUESTS = 64;
        private const int SMALL_KEY_CAPACITY = 4;
        private const int ROTATING_REQUESTS = 20;

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(3, 4)]
        [InlineData(4, 8)]
        [InlineData(5, BLOCK_SECONDS)]
        public void TryBegin_RepeatedFailures_UsesProgressiveDelayAndExactBoundary(int failures, int delaySeconds)
        {
            ManualTimeProvider clock = new();
            AdminWebLoginLimiter limiter = new(clock);
            IPAddress address = IPAddress.Loopback;
            for (int index = 0; index < failures; ++index)
            {
                Assert.True(limiter.TryBegin(address, LOGIN_NAME, out var attempt, out _));
                attempt!.Fail();
                if (index + 1 < failures)
                {
                    Assert.False(limiter.TryBegin(address, LOGIN_NAME, out _, out TimeSpan priorDelay));
                    clock.Now = clock.Now.Add(priorDelay);
                }
            }

            Assert.False(limiter.TryBegin(address, LOGIN_NAME, out _, out TimeSpan delay));
            Assert.Equal(TimeSpan.FromSeconds(delaySeconds), delay);
            clock.Now = clock.Now.Add(delay).AddMilliseconds(-1);
            Assert.False(limiter.TryBegin(address, LOGIN_NAME, out _, out _));
            clock.Now = clock.Now.AddMilliseconds(1);
            Assert.True(limiter.TryBegin(address, LOGIN_NAME, out var admitted, out _));
            admitted!.Succeed();
        }

        [Fact]
        public void TryBegin_FifthFailureBlockElapsed_StartsNewFailureCycle()
        {
            ManualTimeProvider clock = new();
            AdminWebLoginLimiter limiter = new(clock);
            const int FAILURE_LIMIT = 5;
            for (int index = 0; index < FAILURE_LIMIT; ++index)
            {
                Assert.True(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out var attempt, out _));
                attempt!.Fail();
                Assert.False(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out _, out TimeSpan delay));
                clock.Now = clock.Now.Add(delay);
            }

            Assert.True(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out var next, out _));
            next!.Fail();
            Assert.False(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out _, out TimeSpan firstDelay));
            Assert.Equal(TimeSpan.FromSeconds(1), firstDelay);
        }

        [Fact]
        public void TryBegin_FailedIpOrLoginName_BlocksBothKeysIndependently()
        {
            ManualTimeProvider clock = new();
            AdminWebLoginLimiter limiter = new(clock);
            IPAddress otherAddress = IPAddress.Parse("127.0.0.2");
            Assert.True(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out var failure, out _));
            failure!.Fail();

            Assert.False(limiter.TryBegin(IPAddress.Loopback, "different-name", out _, out _));
            Assert.False(limiter.TryBegin(otherAddress, LOGIN_NAME, out _, out _));
            Assert.True(limiter.TryBegin(otherAddress, "different-name", out var independent, out _));
            independent!.Succeed();
        }

        [Fact]
        public void TryBegin_MappedIpOrCaseChangedName_UsesSameReservation()
        {
            AdminWebLoginLimiter limiter = new(new ManualTimeProvider());
            Assert.True(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out var active, out _));

            Assert.False(limiter.TryBegin(IPAddress.Parse("::ffff:127.0.0.1"), "different-name", out _, out _));
            Assert.False(limiter.TryBegin(IPAddress.Parse("127.0.0.2"), " TEST-OWNER ", out _, out _));
            active!.Succeed();
        }

        [Fact]
        public async Task TryBegin_ConcurrentRequests_AdmitsOnlyOneBeforeCompletion()
        {
            AdminWebLoginLimiter limiter = new(new ManualTimeProvider());
            var attempts = await Task.WhenAll(Enumerable.Range(0, PARALLEL_REQUESTS).Select(index => Task.Run(() =>
            {
                limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out var attempt, out _);
                return attempt;
            })));

            var active = Assert.Single(attempts, attempt => attempt != null);
            Assert.Equal(2, limiter.TrackedKeyCount);
            active!.Succeed();
            Assert.Equal(0, limiter.TrackedKeyCount);
        }

        [Fact]
        public void Succeed_PriorFailures_ClearsBothKeysAndDoesNotCountDisposeAgain()
        {
            ManualTimeProvider clock = new();
            AdminWebLoginLimiter limiter = new(clock);
            Assert.True(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out var failed, out _));
            failed!.Fail();
            clock.Now = clock.Now.AddSeconds(1);
            Assert.True(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out var success, out _));
            success!.Succeed();
            success.Dispose();

            Assert.Equal(0, limiter.TrackedKeyCount);
            Assert.True(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out var next, out _));
            next!.Release();
        }

        [Fact]
        public void Release_ServiceUnavailable_DoesNotIncreaseOrEraseExistingFailures()
        {
            ManualTimeProvider clock = new();
            AdminWebLoginLimiter limiter = new(clock);
            Assert.True(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out var first, out _));
            first!.Fail();
            clock.Now = clock.Now.AddSeconds(1);
            Assert.True(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out var unavailable, out _));
            unavailable!.Release();
            unavailable.Dispose();
            Assert.True(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out var second, out _));
            second!.Fail();

            Assert.False(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out _, out TimeSpan delay));
            Assert.Equal(TimeSpan.FromSeconds(2), delay);
        }

        [Fact]
        public void Dispose_AbandonedRequest_CountsFailureOnlyOnce()
        {
            AdminWebLoginLimiter limiter = new(new ManualTimeProvider());
            Assert.True(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out var abandoned, out _));
            abandoned!.Dispose();
            abandoned.Dispose();

            Assert.False(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out _, out TimeSpan delay));
            Assert.Equal(TimeSpan.FromSeconds(1), delay);
        }

        [Fact]
        public void TryBegin_FullCapacity_DoesNotEvictFailureRecordsAndPrunesAfterInactivity()
        {
            ManualTimeProvider clock = new();
            AdminWebLoginLimiter limiter = new(clock, SMALL_KEY_CAPACITY);
            Assert.True(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out var first, out _));
            first!.Fail();
            Assert.True(limiter.TryBegin(IPAddress.Parse("127.0.0.2"), "other-owner", out var second, out _));
            second!.Fail();

            Assert.False(limiter.TryBegin(IPAddress.Parse("127.0.0.3"), "new-owner", out _, out _));
            Assert.Equal(SMALL_KEY_CAPACITY, limiter.TrackedKeyCount);
            Assert.False(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out _, out _));
            clock.Now = clock.Now.AddMinutes(RECORD_MINUTES);
            Assert.True(limiter.TryBegin(IPAddress.Parse("127.0.0.3"), "new-owner", out var next, out _));
            Assert.Equal(2, limiter.TrackedKeyCount);
            next!.Succeed();
        }

        [Fact]
        public void TryBegin_OldInFlightRequest_DoesNotPruneReservationAtRecordExpiry()
        {
            ManualTimeProvider clock = new();
            AdminWebLoginLimiter limiter = new(clock, 2);
            Assert.True(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out var active, out _));
            clock.Now = clock.Now.AddMinutes(RECORD_MINUTES);

            Assert.False(limiter.TryBegin(IPAddress.Parse("127.0.0.2"), "new-name", out _, out _));
            Assert.False(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out _, out _));
            Assert.Equal(2, limiter.TrackedKeyCount);
            active!.Fail();
            Assert.False(limiter.TryBegin(IPAddress.Loopback, LOGIN_NAME, out _, out _));
        }

        [Fact]
        public void TryBegin_RotatingNamesFromOneIp_BlocksBeforeTwentyFailuresPerMinute()
        {
            ManualTimeProvider clock = new();
            AdminWebLoginLimiter limiter = new(clock);
            DateTimeOffset started = clock.Now;
            int accepted = 0;
            for (int index = 0; index < ROTATING_REQUESTS; ++index)
            {
                if (!limiter.TryBegin(IPAddress.Loopback, "owner-" + index, out var attempt, out TimeSpan wait))
                {
                    if (clock.Now.Add(wait) >= started.AddMinutes(1))
                    {
                        break;
                    }
                    clock.Now = clock.Now.Add(wait);
                    Assert.True(limiter.TryBegin(IPAddress.Loopback, "owner-" + index, out attempt, out _));
                }
                ++accepted;
                attempt!.Fail();
            }

            Assert.Equal(5, accepted);
            Assert.False(limiter.TryBegin(IPAddress.Loopback, "last-name", out _, out TimeSpan delay));
            Assert.Equal(TimeSpan.FromSeconds(BLOCK_SECONDS), delay);
        }
    }
}
