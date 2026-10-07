// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Shinshi.Backend.Health
{
    /// <summary> 인증 없이 프로세스 생존과 DB 준비 상태를 분리해 제공한다 </summary>
    [ApiController]
    [AllowAnonymous]
    public sealed class HealthController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string LIVE_ROUTE                = "/health/live";
        public const string READY_ROUTE               = "/health/ready";
        public const int    READINESS_TIMEOUT_SECONDS = 2;

        private readonly IReadinessProbe _probe;


        /****************************************
        *                Methods
        ****************************************/

        public HealthController(IReadinessProbe probe)
        {
            ArgumentNullException.ThrowIfNull(probe);

            _probe = probe;

        }

        [HttpGet(LIVE_ROUTE)]
        public IActionResult Live()
        {
            Response.Headers.CacheControl = "no-store";
            return Ok(new { status = "ok" });
        }

        [HttpGet(READY_ROUTE)]
        public async Task<IActionResult> Ready(CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(READINESS_TIMEOUT_SECONDS));
            try
            {
                // 연결 대기부터 2초로 제한하고, 드라이버의 취소 정리가 응답 기한을 늘리지 않게 한다.
                await _probe.CheckAsync(deadline.Token).WaitAsync(deadline.Token);
            }
            catch (Exception)
            {
                // DB 오류와 로그 공급자 오류가 상태 응답을 바꾸지 않게 요청 완료 로그의 503으로 실패를 관측한다.

                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "unavailable" });
            }

            return Ok(new { status = "ok" });
        }
    }
}
