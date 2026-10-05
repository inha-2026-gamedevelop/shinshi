// System
using System;
using System.Threading.RateLimiting;

namespace Shinshi.Backend
{
    /// <summary> 인증된 계정별 1분 요청 한도. IP 한도만으로는 같은 계정이 여러 IP에서 발급 경로를 반복하는 것을 막지 못한다 </summary>
    public sealed class AccountRequestLimiter : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        // 복귀 입장권 발급의 계정별 1분 한도. 60초 토큰이 끊길 때마다 한 번 받으면 충분하고, 재시도(1/2/4초)까지 더해도 넘지 않는다
        public const int MAX_REJOIN_TICKETS_PER_WINDOW = 6;

        // 캐릭터 생성, 삭제의 계정별 1분 한도. 생성, 삭제는 새 operationId마다 operations 행과 계정 행 잠금을 쓰므로
        // 한 계정의 난사가 표를 키우거나 같은 계정의 다른 쓰기를 막지 못하게 한다. 응답 유실 재시도(1/2/4초)까지 더해도 넘지 않는다
        public const int MAX_CHARACTER_WRITES_PER_WINDOW = 10;

        private readonly PartitionedRateLimiter<Guid> _requests;
        private readonly PartitionedRateLimiter<Guid> _characterWrites;

        /****************************************
        *                Methods
        ****************************************/

        public AccountRequestLimiter()
        {
            _requests = PartitionedRateLimiter.Create<Guid, Guid>(accountId =>
                RateLimitPartition.GetFixedWindowLimiter(accountId,
                    _ => AuthFailureLimiter.CreateWindow(MAX_REJOIN_TICKETS_PER_WINDOW)));
            _characterWrites = PartitionedRateLimiter.Create<Guid, Guid>(accountId =>
                RateLimitPartition.GetFixedWindowLimiter(accountId,
                    _ => AuthFailureLimiter.CreateWindow(MAX_CHARACTER_WRITES_PER_WINDOW)));
        }

        /// <summary> 허용량을 하나 쓴다. 한도를 넘었으면 false다 </summary>
        public bool TryAcquire(Guid accountId)
        {
            using RateLimitLease lease = _requests.AttemptAcquire(accountId);
            return lease.IsAcquired;
        }

        /// <summary> 캐릭터 생성, 삭제 허용량을 하나 쓴다. 한도를 넘었으면 false다 </summary>
        public bool TryAcquireCharacterWrite(Guid accountId)
        {
            using RateLimitLease lease = _characterWrites.AttemptAcquire(accountId);
            return lease.IsAcquired;
        }

        public void Dispose()
        {
            _characterWrites.Dispose();
            _requests.Dispose();
        }
    }
}
