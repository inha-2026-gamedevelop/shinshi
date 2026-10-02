// System
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Backend.Accounts
{
    /// <summary> 비밀번호 해시(PBKDF2) 동시 계산을 CPU 코어 수로 제한한다. 대기는 코어 수의 2배까지만 받고 넘으면 바로,
    /// 대기 중 2초가 지나면 null을 돌려 호출자가 RATE_LIMITED로 거절한다. 요청이 취소되면 대기에서 빠진다 </summary>
    public sealed class PasswordHashGate : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int WAIT_MILLISECONDS      = 2000;
        public const int WAITERS_PER_SLOT       = 2;

        // 자리를 못 얻은 요청에 다시 보내라고 알리는 초
        public const int RETRY_AFTER_SECONDS = 2;

        private readonly SemaphoreSlim _slots;
        private readonly int _maxWaiters;
        private readonly TimeSpan _wait;
        private int _waiters;

        /****************************************
        *              Properties
        ****************************************/

        // 지금 자리를 기다리는 요청 수
        public int Waiters => Volatile.Read(ref _waiters);

        /****************************************
        *                Methods
        ****************************************/

        public PasswordHashGate() : this(Environment.ProcessorCount, Environment.ProcessorCount * WAITERS_PER_SLOT,
            TimeSpan.FromMilliseconds(WAIT_MILLISECONDS))
        {
        }

        public PasswordHashGate(int slots, int maxWaiters, TimeSpan wait)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slots);
            ArgumentOutOfRangeException.ThrowIfNegative(maxWaiters);
            ArgumentOutOfRangeException.ThrowIfLessThan(wait, TimeSpan.Zero);
            _slots      = new SemaphoreSlim(slots, slots);
            _maxWaiters = maxWaiters;
            _wait       = wait;
        }

        /// <summary> 자리를 얻으면 반납용 객체, 대기열이 찼거나 대기 시간이 지나면 null이다. 취소는 OperationCanceledException이다 </summary>
        public async Task<IDisposable?> TryEnterAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_slots.Wait(0, CancellationToken.None))
            {
                return new Slot(_slots);
            }

            if (Interlocked.Increment(ref _waiters) > _maxWaiters)
            {
                Interlocked.Decrement(ref _waiters);
                return null;
            }

            try
            {
                return await _slots.WaitAsync(_wait, cancellationToken) ? new Slot(_slots) : null;
            }
            finally
            {
                Interlocked.Decrement(ref _waiters);
            }
        }

        public void Dispose()
        {
            _slots.Dispose();
        }

        /****************************************
        *                 Slot
        ****************************************/

        private sealed class Slot : IDisposable
        {
            private SemaphoreSlim? _slots;

            public Slot(SemaphoreSlim slots)
            {
                _slots = slots;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _slots, null)?.Release();
            }
        }
    }
}
