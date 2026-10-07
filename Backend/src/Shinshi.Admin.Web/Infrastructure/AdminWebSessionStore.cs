// System
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Admin.Web.Infrastructure
{
    /// <summary> API 토큰은 프로세스 메모리에만 보관한다. 재시작하면 모든 웹 세션이 사라진다 </summary>
    public sealed class AdminWebSessionStore : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string COOKIE_NAME = "shinshi.admin.session";
        public const int MAX_SESSIONS_PER_ADMIN = 5;
        public const int MAX_TOTAL_SESSIONS = 1000;

        private const int SESSION_ID_BYTES = 32;
        private const int SESSION_MINUTES  = 30;
        private const int CLEANUP_SECONDS  = 15;

        private readonly object _sync = new();
        private readonly Dictionary<string, SessionRecord> _sessions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LinkedList<string>> _sessionsByAdmin = new(StringComparer.Ordinal);
        private readonly LinkedList<string> _creationOrder = new();
        private readonly SortedSet<SessionRecord> _expiryOrder = new(Comparer<SessionRecord>.Create(CompareExpiry));
        private readonly TimeProvider _timeProvider;
        private readonly ITimer _cleanupTimer;
        private bool _disposed;

        /****************************************
        *              Properties
        ****************************************/

        internal TimeProvider TimeProvider => _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public AdminWebSessionStore(TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            _timeProvider = timeProvider;
            TimeSpan interval = TimeSpan.FromSeconds(CLEANUP_SECONDS);
            _cleanupTimer = timeProvider.CreateTimer(Cleanup, null, interval, interval);
        }

        /// <summary> 토큰을 반환하지 않고 임의의 쿠키 식별자만 반환한다. 상한을 넘으면 가장 오래 생성된 세션을 폐기한다 </summary>
        public string Create(AdminLoginResponse response, string loginName)
        {
            ArgumentNullException.ThrowIfNull(response);
            ArgumentException.ThrowIfNullOrWhiteSpace(loginName);
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                DateTimeOffset now = _timeProvider.GetUtcNow();
                long expiresAt = Math.Min(response.ExpiresAtUnixMs, now.AddMinutes(SESSION_MINUTES).ToUnixTimeMilliseconds());
                if ((string.IsNullOrWhiteSpace(response.AccessToken)) || (string.IsNullOrWhiteSpace(response.AdminId))
                    || (expiresAt <= now.ToUnixTimeMilliseconds()) || (response.Role < AdminContractCodes.Role.VIEWER)
                    || (response.Role > AdminContractCodes.Role.OWNER))
                {
                    throw new InvalidOperationException("API 로그인 응답이 올바르지 않습니다.");
                }

                PruneExpired(now.ToUnixTimeMilliseconds());
                // 검사와 폐기, 삽입을 같은 잠금으로 묶어 동시 로그인도 상한을 넘지 않게 한다
                if ((_sessionsByAdmin.TryGetValue(response.AdminId, out LinkedList<string>? adminSessions))
                    && (adminSessions.Count >= MAX_SESSIONS_PER_ADMIN))
                {
                    RemoveSession(adminSessions.First!.Value);
                }
                if (_sessions.Count >= MAX_TOTAL_SESSIONS)
                {
                    RemoveSession(_creationOrder.First!.Value);
                }
                if (!_sessionsByAdmin.TryGetValue(response.AdminId, out adminSessions))
                {
                    adminSessions = new LinkedList<string>();
                    _sessionsByAdmin.Add(response.AdminId, adminSessions);
                }

                string sessionId;
                do
                {
                    sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(SESSION_ID_BYTES));
                }
                while (_sessions.ContainsKey(sessionId));

                AdminWebIdentity identity = new(response.AdminId, loginName, response.Role, expiresAt);
                SessionRecord record = new(sessionId, response.AccessToken, identity,
                    _creationOrder.AddLast(sessionId), adminSessions.AddLast(sessionId));
                _sessions.Add(sessionId, record);
                _expiryOrder.Add(record);
                return sessionId;
            }
        }

        public bool IsActive(string? sessionId)
        {
            return TryGetRecord(sessionId, out _);
        }

        public AdminWebIdentity? GetIdentity(string? sessionId)
        {
            return TryGetRecord(sessionId, out SessionRecord? record) ? record!.Identity : null;
        }

        public bool Revoke(string? sessionId)
        {
            lock (_sync)
            {
                return (!_disposed) && (sessionId != null) && RemoveSession(sessionId);
            }
        }

        // HTTP 호출 직전에 다시 만료·폐기를 검사한다. 회로가 살아 있어도 오래된 토큰을 보내지 않는다
        internal bool TryGetToken(string? sessionId, out string token)
        {
            if (!TryGetRecord(sessionId, out SessionRecord? record))
            {
                token = string.Empty;
                return false;
            }

            token = record!.Token;
            return true;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                _sessions.Clear();
                _sessionsByAdmin.Clear();
                _creationOrder.Clear();
                _expiryOrder.Clear();
            }
            _cleanupTimer.Dispose();
        }

        private bool TryGetRecord(string? sessionId, out SessionRecord? record)
        {
            lock (_sync)
            {
                record = null;
                if (_disposed)
                {
                    return false;
                }

                PruneExpired(_timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
                return (sessionId != null) && _sessions.TryGetValue(sessionId, out record);
            }
        }

        private void Cleanup(object? state)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }
                PruneExpired(_timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
            }
        }

        // 만료 순서 인덱스는 폐기 때도 제거한다. 반복 로그인으로 오래된 토큰 참조가 누적되지 않는다
        private void PruneExpired(long now)
        {
            while ((_expiryOrder.Min is SessionRecord record) && (record.Identity.ExpiresAtUnixMs <= now))
            {
                RemoveSession(record.SessionId);
            }
        }

        private bool RemoveSession(string sessionId)
        {
            if (!_sessions.Remove(sessionId, out SessionRecord? record))
            {
                return false;
            }

            _creationOrder.Remove(record.CreationNode);
            _expiryOrder.Remove(record);
            LinkedList<string> adminSessions = _sessionsByAdmin[record.Identity.AdminId];
            adminSessions.Remove(record.AdminNode);
            if (adminSessions.Count == 0)
            {
                _sessionsByAdmin.Remove(record.Identity.AdminId);
            }
            return true;
        }

        private static int CompareExpiry(SessionRecord left, SessionRecord right)
        {
            int comparison = left.Identity.ExpiresAtUnixMs.CompareTo(right.Identity.ExpiresAtUnixMs);
            return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(left.SessionId, right.SessionId);
        }

        private sealed class SessionRecord
        {
            public string SessionId { get; }
            public string Token { get; }
            public AdminWebIdentity Identity { get; }
            public LinkedListNode<string> CreationNode { get; }
            public LinkedListNode<string> AdminNode { get; }

            public SessionRecord(string sessionId, string token, AdminWebIdentity identity,
                LinkedListNode<string> creationNode, LinkedListNode<string> adminNode)
            {
                SessionId    = sessionId;
                Token        = token;
                Identity     = identity;
                CreationNode = creationNode;
                AdminNode    = adminNode;
            }
        }
    }
}
