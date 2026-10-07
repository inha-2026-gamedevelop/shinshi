// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace Shinshi.Admin.Web.Infrastructure
{
    /// <summary> HTTP 요청과 Blazor 회로 각각의 인증 상태. 회로에서 쿠키를 캡처한 뒤 매 호출마다 서버 저장소를 확인한다 </summary>
    public sealed class AdminWebContext : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_EXPIRY_CHECK_SECONDS = 15;

        private readonly object _sync = new();
        private readonly AdminWebSessionStore _sessions;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private string? _sessionId;
        private ITimer? _expiryTimer;
        private Func<Action, Task>? _expiryDispatcher;
        private long _expiryDeadlineUnixMs;
        private long _monitorVersion;
        private bool _initialized;
        private bool _expiryNotified;
        private bool _checkQueued;
        private bool _disposed;

        /****************************************
        *              Properties
        ****************************************/

        [JsonIgnore] public bool IsAuthenticated => Identity != null;
        [JsonIgnore] public string AdminId => Identity?.AdminId ?? string.Empty;
        [JsonIgnore] public string LoginName => Identity?.LoginName ?? string.Empty;
        [JsonIgnore] public int Role => Identity?.Role ?? -1;
        [JsonIgnore] public long ExpiresAtUnixMs => Identity?.ExpiresAtUnixMs ?? 0;

        private AdminWebIdentity? Identity
        {
            get
            {
                Initialize();
                string? sessionId;
                lock (_sync)
                {
                    if ((_disposed) || (_expiryNotified))
                    {
                        return null;
                    }
                    sessionId = _sessionId;
                }

                AdminWebIdentity? identity = _sessions.GetIdentity(sessionId);
                if ((identity == null) && (sessionId != null))
                {
                    NotifyExpired();
                }
                lock (_sync)
                {
                    return (_disposed) || (_expiryNotified) ? null : identity;
                }
            }
        }

        /****************************************
        *                Events
        ****************************************/

        public event Action? SessionExpired;

        /****************************************
        *                Methods
        ****************************************/

        public AdminWebContext(AdminWebSessionStore sessions, IHttpContextAccessor httpContextAccessor)
        {
            ArgumentNullException.ThrowIfNull(sessions);
            ArgumentNullException.ThrowIfNull(httpContextAccessor);
            _sessions            = sessions;
            _httpContextAccessor = httpContextAccessor;
            Initialize();
        }

        public void Initialize()
        {
            lock (_sync)
            {
                if ((_initialized) || (_disposed))
                {
                    return;
                }

                HttpContext? httpContext = _httpContextAccessor.HttpContext;
                if (httpContext == null)
                {
                    return;
                }

                _sessionId = AdminWebSessionCookies.Read(httpContext);
                _initialized = true;
            }
        }

        public bool HasRole(int role)
        {
            return IsAuthenticated && (Role >= role);
        }

        /// <summary> 유휴 회로도 만료를 검사한다. 이벤트는 화면의 dispatcher에서 전달한다 </summary>
        public void StartExpiryMonitoring(Func<Action, Task> dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            Initialize();
            bool expired;
            lock (_sync)
            {
                if ((_disposed) || (_expiryNotified) || (_expiryTimer != null))
                {
                    return;
                }

                AdminWebIdentity? identity = _sessions.GetIdentity(_sessionId);
                expired = (identity == null) && (_sessionId != null);
                if (identity != null)
                {
                    _expiryDispatcher = dispatcher;
                    _expiryDeadlineUnixMs = identity.ExpiresAtUnixMs;
                    ++_monitorVersion;
                    _expiryTimer = _sessions.TimeProvider.CreateTimer(QueueExpiryCheck, _monitorVersion,
                        Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    ScheduleNextCheck();
                }
            }
            if (expired)
            {
                NotifyExpired();
            }
        }

        public void StopExpiryMonitoring()
        {
            ITimer? timer;
            lock (_sync)
            {
                timer = DetachExpiryTimer();
            }
            timer?.Dispose();
        }

        public void Revoke()
        {
            Initialize();
            string? sessionId;
            lock (_sync)
            {
                sessionId = _sessionId;
            }
            _sessions.Revoke(sessionId);
            NotifyExpired();
        }

        internal bool TryGetToken(out string token)
        {
            Initialize();
            string? sessionId;
            lock (_sync)
            {
                sessionId = (_disposed) || (_expiryNotified) ? null : _sessionId;
            }
            if (_sessions.TryGetToken(sessionId, out token))
            {
                lock (_sync)
                {
                    if ((!_disposed) && (!_expiryNotified))
                    {
                        return true;
                    }
                }
                token = string.Empty;
            }

            NotifyExpired();
            return false;
        }

        public void Dispose()
        {
            ITimer? timer;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                _sessionId = null;
                SessionExpired = null;
                timer = DetachExpiryTimer();
            }
            timer?.Dispose();
        }

        private void QueueExpiryCheck(object? state)
        {
            long version = (long)state!;
            Func<Action, Task> dispatcher;
            lock (_sync)
            {
                if ((_disposed) || (_expiryTimer == null) || (_expiryDispatcher == null)
                    || (_monitorVersion != version) || (_checkQueued))
                {
                    return;
                }
                _checkQueued = true;
                dispatcher = _expiryDispatcher;
            }
            _ = DispatchExpiryCheckAsync(dispatcher, version);
        }

        private async Task DispatchExpiryCheckAsync(Func<Action, Task> dispatcher, long version)
        {
            try
            {
                await dispatcher(() => CheckExpiry(version));
            }
            catch (ObjectDisposedException)
            {
                StopExpiryMonitoring(version);
            }
            catch (OperationCanceledException)
            {
                StopExpiryMonitoring(version);
            }
            finally
            {
                lock (_sync)
                {
                    if (_monitorVersion == version)
                    {
                        _checkQueued = false;
                        ScheduleNextCheck();
                    }
                }
            }
        }

        private void CheckExpiry(long version)
        {
            lock (_sync)
            {
                if ((_disposed) || (_expiryTimer == null) || (_monitorVersion != version))
                {
                    return;
                }
                if (_sessions.IsActive(_sessionId))
                {
                    return;
                }
            }
            NotifyExpired(version);
        }

        private void ScheduleNextCheck()
        {
            if (_expiryTimer == null)
            {
                return;
            }

            TimeSpan remaining = DateTimeOffset.FromUnixTimeMilliseconds(_expiryDeadlineUnixMs)
                - _sessions.TimeProvider.GetUtcNow();
            TimeSpan maximum = TimeSpan.FromSeconds(MAX_EXPIRY_CHECK_SECONDS);
            TimeSpan delay = TimeSpan.FromTicks(Math.Clamp(remaining.Ticks, 0, maximum.Ticks));
            _expiryTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }

        private void StopExpiryMonitoring(long version)
        {
            ITimer? timer;
            lock (_sync)
            {
                if (_monitorVersion != version)
                {
                    return;
                }
                timer = DetachExpiryTimer();
            }
            timer?.Dispose();
        }

        private ITimer? DetachExpiryTimer()
        {
            ITimer? timer = _expiryTimer;
            _expiryTimer = null;
            _expiryDispatcher = null;
            _checkQueued = false;
            ++_monitorVersion;
            return timer;
        }

        private void NotifyExpired(long? expectedMonitorVersion = null)
        {
            ITimer? timer;
            Action? notify;
            lock (_sync)
            {
                if ((_disposed) || (_expiryNotified))
                {
                    return;
                }

                if ((expectedMonitorVersion.HasValue)
                    && ((_monitorVersion != expectedMonitorVersion.Value) || (_expiryTimer == null)))
                {
                    return;
                }

                _expiryNotified = true;
                _sessionId = null;
                timer = DetachExpiryTimer();
                notify = SessionExpired;
            }
            timer?.Dispose();
            notify?.Invoke();
        }
    }
}
