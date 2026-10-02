// System
using System;

// Project
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Sessions
{
    /// <summary> 서버 소실 판정 보류. API가 막 시작했거나 DB에 닿지 못한 직후에는 서버들의 heartbeat도 저장되지 못했으므로
    /// 그 동안의 오래된 heartbeat로 출정을 장애 취소하지 않는다. 자동 복구와 운영자 수동 복구가 같은 보류를 따른다 </summary>
    public sealed class RecoveryLivenessGate
    {
        // 프로세스 메모리 값이라 API 인스턴스마다 따로 판정한다. 여러 인스턴스가 같은 DB를 쓰면 DB 단절을 겪지 않은
        // 인스턴스는 보류하지 않으므로, 단절이 한 인스턴스에만 있었다면 다른 인스턴스의 수동 취소가 막히지 않는다.
        // 그래도 장애 취소 SQL 자체가 풀 전체 heartbeat 공백을 단절로 보고 거절하므로 전역 단절은 그쪽이 막는다

        /****************************************
        *                Fields
        ****************************************/

        private readonly TimeProvider _timeProvider;
        private readonly object _lock = new object();
        private DateTimeOffset _allowedAt;

        /****************************************
        *              Properties
        ****************************************/

        public DateTimeOffset AllowedAt
        {
            get
            {
                lock (_lock)
                {
                    return _allowedAt;
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 만든 시각부터 기동 유예 동안 보류한다 </summary>
        public RecoveryLivenessGate(TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            _timeProvider = timeProvider;
            _allowedAt    = timeProvider.GetUtcNow() + ServerPoolTimings.StartupRecoveryGrace;
        }

        /// <summary> 아직 소실 판정을 믿을 수 없으면 true다 </summary>
        public bool IsDeferred()
        {
            return _timeProvider.GetUtcNow() < AllowedAt;
        }

        /// <summary> DB 단절을 확인했다. 지금부터 기동 유예만큼 다시 보류한다(이미 더 늦게 열리면 그대로 둔다) </summary>
        public void Defer()
        {
            DateTimeOffset allowedAt = _timeProvider.GetUtcNow() + ServerPoolTimings.StartupRecoveryGrace;
            lock (_lock)
            {
                if (allowedAt > _allowedAt)
                {
                    _allowedAt = allowedAt;
                }
            }
        }

        /// <summary> 보류가 끝날 때까지 남은 초(올림, 최소 1). 503 Retry-After 값이다 </summary>
        public int RetryAfterSeconds()
        {
            TimeSpan remaining = AllowedAt - _timeProvider.GetUtcNow();
            return Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        }
    }
}
