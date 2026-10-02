// System
using System;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

// Project
using Shinshi.Contracts;

namespace Shinshi.Backend
{
    /// <summary> IP별 인증 실패를 센다. 한도를 넘은 IP는 창이 끝날 때까지 토큰 검증과 DB 조회 없이 429로 거절한다 </summary>
    public sealed class AuthFailureLimiter : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        // 자격 교환과 마을 입장 토큰 발급에 거는 ASP.NET Core 요청 한도 정책 이름
        public const string CREDENTIAL_POLICY = "shinshi-credential-exchange";

        // 서버 임대 발급, 갱신 경로의 요청 한도 정책 이름. 키는 IP와 인스턴스 헤더다
        public const string SERVER_LEASE_POLICY = "shinshi-server-lease";

        // 서비스 자격(서버 임대 발급) 실패만 세는 별도 인스턴스의 DI 키. 플레이어 인증 실패와 서로의 한도를 소모하지 않는다
        public const string SERVICE_CREDENTIAL_KEY = "shinshi-service-credential";

        // 한 IP의 1분 인증 실패 한도. 정상 클라이언트는 만료 토큰 갱신 한두 번이면 끝나고,
        // 체험장 한 곳의 NAT 뒤 여러 명이 동시에 재로그인해도 넘지 않는 값이다
        public const int MAX_FAILURES_PER_WINDOW = 20;

        // 자격 교환 경로의 IP별 1분 요청 한도. 실패 여부와 무관하게 무차별 대입 속도를 제한한다.
        // 한 호스트의 던전 풀 전체가 동시에 시작해 임대를 받아도 넘지 않는 값이다
        public const int MAX_CREDENTIAL_REQUESTS_PER_WINDOW = 60;
        public const int WINDOW_SECONDS                     = 60;

        // 서버 임대 경로의 IP+인스턴스별 1분 요청 한도. 다른 boot 생존 503(5초 뒤 재시도)과 응답 유실 재시도가 겹쳐도
        // 넘지 않고, 한 호스트의 풀 전체가 함께 재시작해도 인스턴스마다 따로 센다. 서비스 자격 실패 한도는 IP 기준으로 따로 막는다
        public const int MAX_SERVER_LEASE_REQUESTS_PER_WINDOW = 120;

        private const string UNKNOWN_CLIENT   = "unknown";
        private const string UNKNOWN_INSTANCE = "none";
        private const string KEY_SEPARATOR    = "|";

        private readonly PartitionedRateLimiter<string> _failures;

        /****************************************
        *                Methods
        ****************************************/

        public AuthFailureLimiter()
        {
            _failures = PartitionedRateLimiter.Create<string, string>(key =>
                RateLimitPartition.GetFixedWindowLimiter(key, _ => CreateWindow(MAX_FAILURES_PER_WINDOW)));
        }

        /// <summary> 프록시 뒤에 둘 때는 ForwardedHeaders로 원래 IP를 복원한 다음에 이 키를 써야 한다 </summary>
        public static string KeyOf(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.Connection.RemoteIpAddress?.ToString() ?? UNKNOWN_CLIENT;
        }

        /// <summary> IP와 인스턴스 헤더를 합친 서버 임대 경로 키. 헤더가 UUID가 아니면 한 키로 묶어 키 공간을 넓히지 못하게 한다 </summary>
        public static string ServerLeaseKeyOf(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            Microsoft.Extensions.Primitives.StringValues headers = context.Request.Headers[JoinApiRoutes.SERVER_INSTANCE_HEADER];
            string instance = (headers.Count == 1) && Guid.TryParse(headers[0], out Guid instanceId)
                ? instanceId.ToString("D") : UNKNOWN_INSTANCE;
            return KeyOf(context) + KEY_SEPARATOR + instance;
        }

        public static FixedWindowRateLimiterOptions CreateWindow(int permits)
        {
            return new FixedWindowRateLimiterOptions
            {
                PermitLimit       = permits,
                Window            = TimeSpan.FromSeconds(WINDOW_SECONDS),
                QueueLimit        = 0,
                AutoReplenishment = true
            };
        }

        /// <summary> 실패를 세지 않고 남은 허용량만 본다 </summary>
        public bool IsBlocked(HttpContext context)
        {
            RateLimiterStatistics? statistics = _failures.GetStatistics(KeyOf(context));
            return (statistics != null) && (statistics.CurrentAvailablePermits <= 0);
        }

        public void RecordFailure(HttpContext context)
        {
            using RateLimitLease lease = _failures.AttemptAcquire(KeyOf(context));
        }

        /// <summary> 인증 실패와 자격 교환 요청 한도가 같은 거절 코드와 재시도 헤더를 사용한다 </summary>
        public static ObjectResult RejectTooMany(HttpContext context, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(timeProvider);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.RetryAfter = WINDOW_SECONDS.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return ApiResponseFactory.Reject(StatusCodes.Status429TooManyRequests,
                ContractCodes.RejectReason.RATE_LIMITED, string.Empty, timeProvider);
        }

        /// <summary> 미들웨어에서 429와 공유 거절 DTO를 쓴다 </summary>
        public static Task WriteTooManyAsync(HttpContext context, TimeProvider timeProvider)
        {
            ObjectResult rejection = RejectTooMany(context, timeProvider);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json; charset=utf-8";
            string body = JsonConvert.SerializeObject(rejection.Value, ContractJson.CreateSettings());
            return context.Response.WriteAsync(body, context.RequestAborted);
        }

        public void Dispose()
        {
            _failures.Dispose();
        }
    }
}
