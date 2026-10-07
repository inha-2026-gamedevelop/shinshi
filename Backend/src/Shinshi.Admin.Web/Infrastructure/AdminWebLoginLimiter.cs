// System
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace Shinshi.Admin.Web.Infrastructure
{
    /// <summary> 웹 프로세스 안에서 출처와 로그인 이름의 실패를 제한하고 진행 중인 요청의 우회를 막는다 </summary>
    public sealed class AdminWebLoginLimiter
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int MAX_TRACKED_KEYS = 4096;

        private const int MAX_LOGIN_NAME_LENGTH = 128;
        private const int FAILURE_LIMIT         = 5;
        private const int BLOCK_SECONDS         = 60;
        private const int RECORD_MINUTES        = 15;
        private const int CLEANUP_SECONDS       = 1;
        private const int MIN_TRACKED_KEYS      = 2;
        private const int RETRY_SECONDS         = 1;

        private readonly object _gate = new();
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly TimeProvider _timeProvider;
        private readonly int _maximumTrackedKeys;
        private DateTimeOffset _nextCleanup;

        /****************************************
        *              Properties
        ****************************************/

        public int TrackedKeyCount
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Count;
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public AdminWebLoginLimiter(TimeProvider timeProvider, int maximumTrackedKeys = MAX_TRACKED_KEYS)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            if ((maximumTrackedKeys < MIN_TRACKED_KEYS) || (maximumTrackedKeys > MAX_TRACKED_KEYS))
            {
                throw new ArgumentOutOfRangeException(nameof(maximumTrackedKeys));
            }

            _timeProvider       = timeProvider;
            _maximumTrackedKeys = maximumTrackedKeys;
        }

        public bool TryBegin(IPAddress? address, string loginName, out LoginAttempt? attempt,
            out TimeSpan retryAfter)
        {
            attempt    = null;
            retryAfter = TimeSpan.FromSeconds(RETRY_SECONDS);
            if ((address == null) || (string.IsNullOrWhiteSpace(loginName))
                || (loginName.Length > MAX_LOGIN_NAME_LENGTH))
            {
                return false;
            }

            IPAddress normalizedAddress = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            string ipKey = "ip:" + normalizedAddress;
            string loginKey = "name:" + loginName.Trim().ToUpperInvariant();
            lock (_gate)
            {
                DateTimeOffset now = _timeProvider.GetUtcNow();
                if ((now >= _nextCleanup) || (_entries.Count >= _maximumTrackedKeys))
                {
                    PruneExpired(now);
                    _nextCleanup = now.AddSeconds(CLEANUP_SECONDS);
                }

                _entries.TryGetValue(ipKey, out Entry? ip);
                _entries.TryGetValue(loginKey, out Entry? login);
                TimeSpan ipWait = GetWait(ip, now);
                TimeSpan loginWait = GetWait(login, now);
                if ((ipWait > TimeSpan.Zero) || (loginWait > TimeSpan.Zero))
                {
                    retryAfter = ipWait > loginWait ? ipWait : loginWait;
                    return false;
                }

                int requiredKeys = (ip == null ? 1 : 0) + (login == null ? 1 : 0);
                if (_entries.Count + requiredKeys > _maximumTrackedKeys)
                {
                    // 유효한 실패 기록을 축출하면 새 이름을 계속 보내는 요청이 차단 기록을 지울 수 있다
                    return false;
                }

                ip ??= AddEntry(ipKey, now);
                login ??= AddEntry(loginKey, now);
                ip.InFlight    = true;
                login.InFlight = true;
                attempt = new LoginAttempt(this, ipKey, loginKey);
                retryAfter = TimeSpan.Zero;
                return true;
            }
        }

        private Entry AddEntry(string key, DateTimeOffset now)
        {
            Entry entry = new() { ExpiresAt = now.AddMinutes(RECORD_MINUTES) };
            _entries.Add(key, entry);
            return entry;
        }

        private static TimeSpan GetWait(Entry? entry, DateTimeOffset now)
        {
            if (entry == null)
            {
                return TimeSpan.Zero;
            }
            if (entry.InFlight)
            {
                return TimeSpan.FromSeconds(RETRY_SECONDS);
            }
            if (entry.NextAllowedAt > now)
            {
                return entry.NextAllowedAt - now;
            }
            if ((entry.Failures >= FAILURE_LIMIT) || (entry.ExpiresAt <= now))
            {
                entry.Failures = 0;
            }
            return TimeSpan.Zero;
        }

        private void Complete(string ipKey, string loginKey, bool success, bool recordFailure)
        {
            lock (_gate)
            {
                DateTimeOffset now = _timeProvider.GetUtcNow();
                FinishEntry(ipKey, now, success, recordFailure);
                FinishEntry(loginKey, now, success, recordFailure);
            }
        }

        private void FinishEntry(string key, DateTimeOffset now, bool success, bool recordFailure)
        {
            Entry entry = _entries[key];
            entry.InFlight = false;
            if (success)
            {
                _entries.Remove(key);
                return;
            }

            if (recordFailure)
            {
                ++entry.Failures;
                int delaySeconds = entry.Failures >= FAILURE_LIMIT ? BLOCK_SECONDS : 1 << (entry.Failures - 1);
                entry.NextAllowedAt = now.AddSeconds(delaySeconds);
                entry.ExpiresAt = now.AddMinutes(RECORD_MINUTES);
            }
            else if (entry.Failures == 0)
            {
                _entries.Remove(key);
            }
        }

        private void PruneExpired(DateTimeOffset now)
        {
            foreach (KeyValuePair<string, Entry> entry in _entries)
            {
                if ((!entry.Value.InFlight) && (entry.Value.ExpiresAt <= now))
                {
                    _entries.Remove(entry.Key);
                }
            }
        }

        private sealed class Entry
        {
            public int Failures { get; set; }
            public bool InFlight { get; set; }
            public DateTimeOffset NextAllowedAt { get; set; }
            public DateTimeOffset ExpiresAt { get; set; }
        }

        /// <summary> 인증 호출 동안 두 키를 예약한다. 요청 중단도 실패로 집계해 취소를 이용한 우회를 막는다 </summary>
        public sealed class LoginAttempt : IDisposable
        {
            /****************************************
            *                Fields
            ****************************************/

            private AdminWebLoginLimiter? _owner;
            private readonly string _ipKey;
            private readonly string _loginKey;

            /****************************************
            *                Methods
            ****************************************/

            internal LoginAttempt(AdminWebLoginLimiter owner, string ipKey, string loginKey)
            {
                _owner    = owner;
                _ipKey    = ipKey;
                _loginKey = loginKey;
            }

            public void Succeed() { Complete(true, false); }
            public void Fail() { Complete(false, true); }
            public void Release() { Complete(false, false); }
            public void Dispose() { Fail(); }

            private void Complete(bool success, bool recordFailure)
            {
                Interlocked.Exchange(ref _owner, null)?.Complete(_ipKey, _loginKey, success, recordFailure);
            }
        }
    }
}
