// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Backend.Accounts;

namespace Shinshi.Backend.Tests.Accounts
{
    /// <summary> 해시 동시 실행 제한: 자리 수, 대기열 상한(넘으면 즉시 거절), 대기 시간 초과, 취소 시 대기열 이탈 </summary>
    public sealed class PasswordHashGateTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly TimeSpan _longWait = TimeSpan.FromSeconds(30);

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void Default_UsesCoreCountSlotsAndTwiceAsManyWaiters()
        {
            Assert.Equal(2, PasswordHashGate.WAITERS_PER_SLOT);
            Assert.Equal(2000, PasswordHashGate.WAIT_MILLISECONDS);
        }

        [Fact]
        public async Task FullQueue_RejectsImmediatelyAndQueuedWaiterGetsReleasedSlot()
        {
            using PasswordHashGate gate = new PasswordHashGate(1, 1, _longWait);
            IDisposable held = (await gate.TryEnterAsync(CancellationToken.None))!;
            Task<IDisposable?> queued = gate.TryEnterAsync(CancellationToken.None);
            await WaitForWaitersAsync(gate, 1);

            Task<IDisposable?> overflow = gate.TryEnterAsync(CancellationToken.None);
            Assert.True(overflow.IsCompleted);
            Assert.Null(await overflow);

            held.Dispose();
            using IDisposable? next = await queued;
            Assert.NotNull(next);
            Assert.Equal(0, gate.Waiters);
        }

        [Fact]
        public async Task WaitPastLimit_ReturnsNull()
        {
            using PasswordHashGate gate = new PasswordHashGate(1, 2, TimeSpan.FromMilliseconds(50));
            using IDisposable? held = await gate.TryEnterAsync(CancellationToken.None);

            Assert.Null(await gate.TryEnterAsync(CancellationToken.None));
            Assert.Equal(0, gate.Waiters);
        }

        [Fact]
        public async Task CancelledWaiter_LeavesQueue()
        {
            using PasswordHashGate gate = new PasswordHashGate(1, 1, _longWait);
            using IDisposable? held = await gate.TryEnterAsync(CancellationToken.None);
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            Task<IDisposable?> waiting = gate.TryEnterAsync(cancellation.Token);
            await WaitForWaitersAsync(gate, 1);

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            Assert.Equal(0, gate.Waiters);
        }

        private static async Task WaitForWaitersAsync(PasswordHashGate gate, int expected)
        {
            const int MAX_POLLS = 200;
            const int POLL_MS = 5;
            for (int i = 0; (i < MAX_POLLS) && (gate.Waiters != expected); ++i)
            {
                await Task.Delay(POLL_MS);
            }
            Assert.Equal(expected, gate.Waiters);
        }
    }
}
