// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Backend.Accounts
{
    /// <summary> 비밀번호 로그인 실패 제한. bearer 위조를 세는 AuthFailureLimiter와 섞지 않는다(같은 NAT의 정상 bearer 사용자를 막지 않게).
    /// 잠금은 계정 전체가 아니라 (아이디, IP) 쌍에만 건다. 같은 쌍의 시도는 EnterAsync가 돌려준 임대 안에서 한 번에 하나씩
    /// 판정 -> 해시 -> 기록으로 전이한다. 카운터는 이 API 프로세스 메모리에 있으며 API를 여러 대로 늘리면 공유 저장소로 옮긴다 </summary>
    public sealed class PasswordLoginLimiter
    {
        /****************************************
        *                Fields
        ****************************************/

        // 같은 (아이디, IP) 쌍의 연속 실패가 이 수에 닿으면 잠금 만료를 지금 + 15분으로 둔다
        public const int MAX_PAIR_FAILURES = 5;
        public const int PAIR_LOCK_MINUTES = 15;

        // 같은 아이디에 여러 IP 합계 최근 1시간(미끄럼 창) 실패가 이 수를 넘으면 그 아이디의 시도를 IP마다 1분에 1회로 늦춘다
        public const int MAX_LOGIN_ID_FAILURES_PER_WINDOW = 50;
        public const int LOGIN_ID_WINDOW_MINUTES          = 60;
        public const int SLOWED_INTERVAL_SECONDS          = 60;

        // 키는 마지막 사용 뒤 1시간이 지나면 지우고, 전체가 상한을 넘으면 가장 오래 쓰지 않은 것부터 지운다
        public const int KEY_IDLE_MINUTES = 60;
        public const int MAX_KEYS         = 100000;

        // 상한을 넘을 때마다 정렬하지 않도록 상한의 이 비율까지 한 번에 줄인다
        private const int EVICTION_TARGET_PERCENT = 90;
        private const int PERCENT                 = 100;
        private const int PRUNE_INTERVAL_SECONDS  = 60;

        private static readonly TimeSpan _pairLock       = TimeSpan.FromMinutes(PAIR_LOCK_MINUTES);
        private static readonly TimeSpan _loginIdWindow  = TimeSpan.FromMinutes(LOGIN_ID_WINDOW_MINUTES);
        private static readonly TimeSpan _slowedInterval = TimeSpan.FromSeconds(SLOWED_INTERVAL_SECONDS);
        private static readonly TimeSpan _keyIdle        = TimeSpan.FromMinutes(KEY_IDLE_MINUTES);
        private static readonly TimeSpan _pruneInterval  = TimeSpan.FromSeconds(PRUNE_INTERVAL_SECONDS);

        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _pairWait;
        private readonly int _maxKeys;
        private readonly object _gate = new object();
        private readonly Dictionary<PairKey, PairState> _pairs = new Dictionary<PairKey, PairState>();
        private readonly Dictionary<string, LoginIdState> _loginIds = new Dictionary<string, LoginIdState>(StringComparer.Ordinal);
        private DateTimeOffset _nextPrune;

        /****************************************
        *              Properties
        ****************************************/

        // 쌍과 아이디 키의 합계. 상한 검사와 테스트가 읽는다
        public int KeyCount
        {
            get
            {
                lock (_gate)
                {
                    return _pairs.Count + _loginIds.Count;
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public PasswordLoginLimiter(TimeProvider timeProvider)
            : this(timeProvider, TimeSpan.FromMilliseconds(PasswordHashGate.WAIT_MILLISECONDS), MAX_KEYS)
        {
        }

        /// <summary> pairWait는 같은 쌍의 앞선 시도를 기다리는 상한이다. maxKeys는 테스트만 낮춘다 </summary>
        public PasswordLoginLimiter(TimeProvider timeProvider, TimeSpan pairWait, int maxKeys)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            ArgumentOutOfRangeException.ThrowIfLessThan(pairWait, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxKeys);
            _timeProvider = timeProvider;
            _pairWait     = pairWait;
            _maxKeys      = maxKeys;
            _nextPrune    = timeProvider.GetUtcNow().Add(_pruneInterval);
        }

        /// <summary> 같은 쌍의 앞선 시도가 끝날 때까지 기다린다. 대기 상한을 넘으면 null이다(호출자는 RATE_LIMITED).
        /// 판정, 해시, 기록을 모두 이 임대 안에서 하고 using으로 반납한다 </summary>
        public async Task<PasswordAttemptLease?> EnterAsync(string loginId, string clientKey,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(loginId);
            ArgumentNullException.ThrowIfNull(clientKey);
            PairKey key = new PairKey(loginId, clientKey);
            PairState pair;
            lock (_gate)
            {
                DateTimeOffset now = _timeProvider.GetUtcNow();
                PruneIfDue(now);
                if (!_pairs.TryGetValue(key, out PairState? found))
                {
                    found = new PairState();
                    _pairs[key] = found;
                }

                pair = found;
                pair.LastUsed = now;
                ++pair.Users;
                EvictIfOverLimit(now, null);
            }

            bool entered = false;
            try
            {
                entered = await pair.Turn.WaitAsync(_pairWait, cancellationToken);
            }
            finally
            {
                if (!entered)
                {
                    lock (_gate)
                    {
                        --pair.Users;
                    }
                }
            }

            return entered ? new PasswordAttemptLease(this, key, pair) : null;
        }

        private EPasswordAttemptStatus TryBegin(PairKey key, PairState pair, out TimeSpan retryAfter)
        {
            retryAfter = TimeSpan.Zero;
            lock (_gate)
            {
                DateTimeOffset now = _timeProvider.GetUtcNow();
                pair.LastUsed = now;
                ClearEndedLock(pair, now);

                // 잠금 중에는 해시를 계산하지 않고 거절하며 만료를 늘리지 않는다
                if (pair.LockedUntil.HasValue)
                {
                    retryAfter = pair.LockedUntil.Value - now;
                    return EPasswordAttemptStatus.Locked;
                }

                if (!IsSlowed(key.LoginId, now))
                {
                    return EPasswordAttemptStatus.Allowed;
                }

                if (pair.LastSlowedAttemptAt.HasValue && (now - pair.LastSlowedAttemptAt.Value < _slowedInterval))
                {
                    retryAfter = pair.LastSlowedAttemptAt.Value + _slowedInterval - now;
                    return EPasswordAttemptStatus.Slowed;
                }

                pair.LastSlowedAttemptAt = now;
                return EPasswordAttemptStatus.Allowed;
            }
        }

        // 아이디가 없거나 비밀번호가 틀린 시도. 두 경우를 같은 방식으로 센다
        private void RecordFailure(PairKey key, PairState pair)
        {
            lock (_gate)
            {
                DateTimeOffset now = _timeProvider.GetUtcNow();
                pair.LastUsed = now;
                ClearEndedLock(pair, now);
                ++pair.Failures;
                if (pair.Failures >= MAX_PAIR_FAILURES)
                {
                    pair.LockedUntil = now.Add(_pairLock);
                }

                if (!_loginIds.TryGetValue(key.LoginId, out LoginIdState? window))
                {
                    window = new LoginIdState();
                    _loginIds[key.LoginId] = window;
                }
                window.LastUsed = now;
                window.Failures.Enqueue(now);
                TrimWindow(window, now);
                EvictIfOverLimit(now, key.LoginId);
            }
        }

        // 성공한 쌍의 연속 실패를 0으로 되돌린다. 아이디 전체의 1시간 실패와 늦춤 시각은 그대로 둔다
        private void RecordSuccess(PairState pair)
        {
            lock (_gate)
            {
                pair.LastUsed    = _timeProvider.GetUtcNow();
                pair.Failures    = 0;
                pair.LockedUntil = null;
            }
        }

        private void Release(PairState pair)
        {
            pair.Turn.Release();
            lock (_gate)
            {
                --pair.Users;
            }
        }

        // 만료가 지난 잠금은 실패 수를 0으로 되돌린다
        private static void ClearEndedLock(PairState pair, DateTimeOffset now)
        {
            if (pair.LockedUntil.HasValue && (pair.LockedUntil.Value <= now))
            {
                pair.LockedUntil = null;
                pair.Failures    = 0;
            }
        }

        private bool IsSlowed(string loginId, DateTimeOffset now)
        {
            if (!_loginIds.TryGetValue(loginId, out LoginIdState? window))
            {
                return false;
            }

            TrimWindow(window, now);
            return window.Failures.Count > MAX_LOGIN_ID_FAILURES_PER_WINDOW;
        }

        // 1시간보다 오래된 실패를 빼고, 판정에 필요한 최근 (상한 + 1)개만 남긴다
        private static void TrimWindow(LoginIdState window, DateTimeOffset now)
        {
            while ((window.Failures.Count > 0) && (now - window.Failures.Peek() >= _loginIdWindow))
            {
                window.Failures.Dequeue();
            }
            while (window.Failures.Count > MAX_LOGIN_ID_FAILURES_PER_WINDOW + 1)
            {
                window.Failures.Dequeue();
            }
        }

        private void PruneIfDue(DateTimeOffset now)
        {
            if (now < _nextPrune)
            {
                return;
            }

            _nextPrune = now.Add(_pruneInterval);
            RemoveIdle(now);
        }

        // 마지막 사용 뒤 1시간이 지난 키를 지운다. 지금 임대 중인 쌍은 남긴다
        private void RemoveIdle(DateTimeOffset now)
        {
            foreach (PairKey key in _pairs.Where(entry => (entry.Value.Users == 0)
                && (now - entry.Value.LastUsed >= _keyIdle)).Select(entry => entry.Key).ToList())
            {
                _pairs.Remove(key);
            }
            foreach (string loginId in _loginIds.Where(entry => now - entry.Value.LastUsed >= _keyIdle)
                .Select(entry => entry.Key).ToList())
            {
                _loginIds.Remove(loginId);
            }
        }

        // 상한을 넘으면 오래 쓰지 않은 키부터 상한의 90%까지 지운다. 넘을 때마다 정렬하지 않게 한 번에 여유를 만든다.
        // 임대 중인 쌍과 방금 기록한 아이디(keepLoginId)는 지우지 않는다
        private void EvictIfOverLimit(DateTimeOffset now, string? keepLoginId)
        {
            if (_pairs.Count + _loginIds.Count <= _maxKeys)
            {
                return;
            }

            RemoveIdle(now);
            int target = _maxKeys * EVICTION_TARGET_PERCENT / PERCENT;
            int excess = _pairs.Count + _loginIds.Count - target;
            if (excess <= 0)
            {
                return;
            }

            IEnumerable<(DateTimeOffset LastUsed, PairKey? Pair, string? LoginId)> candidates = _pairs
                .Where(entry => entry.Value.Users == 0)
                .Select(entry => (entry.Value.LastUsed, (PairKey?)entry.Key, (string?)null))
                .Concat(_loginIds.Where(entry => !string.Equals(entry.Key, keepLoginId, StringComparison.Ordinal))
                    .Select(entry => (entry.Value.LastUsed, (PairKey?)null, (string?)entry.Key)));
            foreach ((DateTimeOffset _, PairKey? pair, string? loginId) in candidates.OrderBy(entry => entry.LastUsed)
                .Take(excess).ToList())
            {
                if (pair.HasValue)
                {
                    _pairs.Remove(pair.Value);
                }
                else
                {
                    _loginIds.Remove(loginId!);
                }
            }
        }

        /****************************************
        *                Lease
        ****************************************/

        /// <summary> 한 쌍의 시도 차례. 판정, 기록은 이 차례 안에서만 하며 Dispose가 다음 시도에 차례를 넘긴다 </summary>
        public sealed class PasswordAttemptLease : IDisposable
        {
            private readonly PasswordLoginLimiter _owner;
            private readonly PairKey _key;
            private readonly PairState _pair;
            private int _released;

            internal PasswordAttemptLease(PasswordLoginLimiter owner, PairKey key, PairState pair)
            {
                _owner = owner;
                _key   = key;
                _pair  = pair;
            }

            /// <summary> 해시 계산 전에 부른다. 늦춤 상태면 허용한 시도의 시각을 남겨 그 쌍의 다음 1분을 막는다 </summary>
            public EPasswordAttemptStatus TryBeginAttempt(out TimeSpan retryAfter)
            {
                return _owner.TryBegin(_key, _pair, out retryAfter);
            }

            public void RecordFailure()
            {
                _owner.RecordFailure(_key, _pair);
            }

            public void RecordSuccess()
            {
                _owner.RecordSuccess(_pair);
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0)
                {
                    _owner.Release(_pair);
                }
            }
        }

        /****************************************
        *                State
        ****************************************/

        internal readonly record struct PairKey(string LoginId, string ClientKey);

        internal sealed class PairState
        {
            public SemaphoreSlim Turn { get; } = new SemaphoreSlim(1, 1);
            public int Users { get; set; }
            public int Failures { get; set; }
            public DateTimeOffset? LockedUntil { get; set; }
            public DateTimeOffset? LastSlowedAttemptAt { get; set; }
            public DateTimeOffset LastUsed { get; set; }
        }

        private sealed class LoginIdState
        {
            public Queue<DateTimeOffset> Failures { get; } = new Queue<DateTimeOffset>();
            public DateTimeOffset LastUsed { get; set; }
        }
    }
}
