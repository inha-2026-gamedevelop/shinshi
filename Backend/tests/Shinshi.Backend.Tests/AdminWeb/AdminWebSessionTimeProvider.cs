// System
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Backend.Tests.AdminWeb
{
    /// <summary> 실제 대기 없이 세션 만료 시각과 TimeProvider 타이머를 함께 진행한다 </summary>
    internal sealed class AdminWebSessionTimeProvider : TimeProvider
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly object _sync = new();
        private readonly HashSet<SessionTimer> _timers = new();
        private DateTimeOffset _now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        /****************************************
        *              Properties
        ****************************************/

        public DateTimeOffset Now
        {
            get
            {
                lock (_sync)
                {
                    return _now;
                }
            }
            set
            {
                lock (_sync)
                {
                    _now = value;
                }
            }
        }

        public int ActiveTimerCount
        {
            get
            {
                lock (_sync)
                {
                    return _timers.Count;
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public override DateTimeOffset GetUtcNow() { return Now; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            SessionTimer timer = new(this, callback, state);
            lock (_sync)
            {
                _timers.Add(timer);
            }
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan amount)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(amount, TimeSpan.Zero);
            DateTimeOffset target = Now.Add(amount);
            while (true)
            {
                SessionTimer? next = null;
                lock (_sync)
                {
                    foreach (SessionTimer timer in _timers)
                    {
                        if ((timer.DueAt is DateTimeOffset dueAt) && (dueAt <= target)
                            && ((next == null) || (dueAt < next.DueAt!.Value)))
                        {
                            next = timer;
                        }
                    }
                    if (next == null)
                    {
                        _now = target;
                        return;
                    }
                    _now = next.DueAt!.Value;
                    next.DueAt = next.Period == Timeout.InfiniteTimeSpan ? null : _now.Add(next.Period);
                }
                next.Callback(next.State);
            }
        }

        private sealed class SessionTimer : ITimer
        {
            /****************************************
            *                Fields
            ****************************************/

            private readonly AdminWebSessionTimeProvider _owner;
            private bool _disposed;

            /****************************************
            *              Properties
            ****************************************/

            public TimerCallback Callback { get; }
            public object? State { get; }
            public DateTimeOffset? DueAt { get; set; }
            public TimeSpan Period { get; private set; }

            /****************************************
            *                Methods
            ****************************************/

            public SessionTimer(AdminWebSessionTimeProvider owner, TimerCallback callback, object? state)
            {
                _owner   = owner;
                Callback = callback;
                State    = state;
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (_owner._sync)
                {
                    if (_disposed)
                    {
                        return false;
                    }
                    DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _owner._now.Add(dueTime);
                    Period = period;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (_owner._sync)
                {
                    _disposed = true;
                    DueAt = null;
                    _owner._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
