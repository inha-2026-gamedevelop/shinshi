// System
using System;
using System.Threading.RateLimiting;

namespace Shinshi.Backend.Inbox
{
    /// <summary> 플레이어 우편함의 계정별 1분 요청 한도. 수령은 새 operationId마다 operations 행과 DB 연결을 쓰므로
    /// 한 계정이 난사해 표를 키우거나 연결 풀을 점유하지 못하게 막는다. 목록은 같은 계정의 화면 갱신 몇 번이면 충분하다 </summary>
    public sealed class InboxRequestLimiter : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        // 우편 최대 100개를 하나씩 받아도 몇 분이면 끝나고, 응답 유실 재전송까지 더해도 정상 사용은 넘지 않는다
        public const int MAX_CLAIMS_PER_WINDOW = 30;
        public const int MAX_LISTS_PER_WINDOW  = 60;

        private readonly PartitionedRateLimiter<Guid> _claims;
        private readonly PartitionedRateLimiter<Guid> _lists;

        /****************************************
        *                Methods
        ****************************************/

        public InboxRequestLimiter()
        {
            _claims = PartitionedRateLimiter.Create<Guid, Guid>(accountId =>
                RateLimitPartition.GetFixedWindowLimiter(accountId,
                    _ => AuthFailureLimiter.CreateWindow(MAX_CLAIMS_PER_WINDOW)));
            _lists = PartitionedRateLimiter.Create<Guid, Guid>(accountId =>
                RateLimitPartition.GetFixedWindowLimiter(accountId,
                    _ => AuthFailureLimiter.CreateWindow(MAX_LISTS_PER_WINDOW)));
        }

        /// <summary> 수령 허용량을 하나 쓴다. 한도를 넘었으면 false다 </summary>
        public bool TryAcquireClaim(Guid accountId)
        {
            using RateLimitLease lease = _claims.AttemptAcquire(accountId);
            return lease.IsAcquired;
        }

        /// <summary> 목록 허용량을 하나 쓴다. 한도를 넘었으면 false다 </summary>
        public bool TryAcquireList(Guid accountId)
        {
            using RateLimitLease lease = _lists.AttemptAcquire(accountId);
            return lease.IsAcquired;
        }

        public void Dispose()
        {
            _claims.Dispose();
            _lists.Dispose();
        }
    }
}
