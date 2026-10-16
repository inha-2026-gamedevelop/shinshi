// System
using System;
using System.Threading.RateLimiting;

namespace Shinshi.Backend.Social
{
    /// <summary> 소셜 요청의 캐릭터별 인메모리 한도(프로세스마다 따로 센다). 게임 서버의 연결별 제한 뒤 두 번째 방어선이다.
    /// 값은 기획 근거 없는 구현 값이다 </summary>
    public sealed class SocialRequestLimiter : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int MAX_WRITES_PER_MINUTE      = 30;
        public const int MAX_INVITATIONS_PER_HOUR   = 30;
        public const int MAX_CHAT_PER_MINUTE        = 60;
        public const int MINUTE_SECONDS             = 60;
        public const int HOUR_SECONDS               = 3600;

        private readonly PartitionedRateLimiter<Guid> _writes;
        private readonly PartitionedRateLimiter<Guid> _invitations;
        private readonly PartitionedRateLimiter<Guid> _chat;

        /****************************************
        *                Methods
        ****************************************/

        public SocialRequestLimiter()
        {
            _writes      = Create(MAX_WRITES_PER_MINUTE, MINUTE_SECONDS);
            _invitations = Create(MAX_INVITATIONS_PER_HOUR, HOUR_SECONDS);
            _chat        = Create(MAX_CHAT_PER_MINUTE, MINUTE_SECONDS);
        }

        /// <summary> 관리 쓰기 허용량을 쓴다. 친구 요청과 길드 초대는 시간당 한도도 함께 쓴다. 넘었으면 다시 시도할 초를 돌려준다 </summary>
        public int? TryAcquireWrite(Guid characterId, bool invitation)
        {
            if (!Acquire(_writes, characterId))
            {
                return MINUTE_SECONDS;
            }

            return ((invitation) && (!Acquire(_invitations, characterId))) ? HOUR_SECONDS : null;
        }

        /// <summary> 귓속말, 길드 채팅 허용량을 쓴다. 넘었으면 다시 시도할 초를 돌려준다 </summary>
        public int? TryAcquireChat(Guid characterId)
        {
            return Acquire(_chat, characterId) ? null : MINUTE_SECONDS;
        }

        public void Dispose()
        {
            _writes.Dispose();
            _invitations.Dispose();
            _chat.Dispose();
        }

        private static PartitionedRateLimiter<Guid> Create(int permits, int windowSeconds)
        {
            return PartitionedRateLimiter.Create<Guid, Guid>(characterId =>
                RateLimitPartition.GetFixedWindowLimiter(characterId, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit       = permits,
                    Window            = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit        = 0,
                    AutoReplenishment = true
                }));
        }

        private static bool Acquire(PartitionedRateLimiter<Guid> limiter, Guid characterId)
        {
            using RateLimitLease lease = limiter.AttemptAcquire(characterId);
            return lease.IsAcquired;
        }
    }
}
