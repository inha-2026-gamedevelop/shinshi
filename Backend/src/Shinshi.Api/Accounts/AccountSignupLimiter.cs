// System
using System;
using System.Threading.RateLimiting;

namespace Shinshi.Backend.Accounts
{
    /// <summary> 가입 요청 한도. 형식 검사를 통과한 시도만 IP당 1시간 5회 센다. 형식 오류는 API 비용이 없어 세지 않는다 </summary>
    public sealed class AccountSignupLimiter : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int MAX_SIGNUPS_PER_WINDOW = 5;
        public const int WINDOW_MINUTES         = 60;

        // 아이디, 캐릭터 이름 가용성 조회의 ASP.NET Core 요청 한도 정책. IP당 1분 30회
        public const string AVAILABILITY_POLICY                 = "shinshi-name-availability";
        public const int    MAX_AVAILABILITY_REQUESTS_PER_WINDOW = 30;

        private readonly PartitionedRateLimiter<string> _signups;

        /****************************************
        *                Methods
        ****************************************/

        public AccountSignupLimiter()
        {
            _signups = PartitionedRateLimiter.Create<string, string>(key =>
                RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit       = MAX_SIGNUPS_PER_WINDOW,
                    Window            = TimeSpan.FromMinutes(WINDOW_MINUTES),
                    QueueLimit        = 0,
                    AutoReplenishment = true
                }));
        }

        /// <summary> 허용량을 하나 쓴다. 한도를 넘었으면 false와 창이 끝날 때까지의 대기 시간이다 </summary>
        public bool TryAcquire(string clientKey, out TimeSpan retryAfter)
        {
            ArgumentNullException.ThrowIfNull(clientKey);
            using RateLimitLease lease = _signups.AttemptAcquire(clientKey);
            retryAfter = TimeSpan.Zero;
            if (lease.IsAcquired)
            {
                return true;
            }

            if (!lease.TryGetMetadata(MetadataName.RetryAfter, out retryAfter))
            {
                retryAfter = TimeSpan.FromMinutes(WINDOW_MINUTES);
            }
            return false;
        }

        public void Dispose()
        {
            _signups.Dispose();
        }
    }
}
