// System
using System;
using System.Diagnostics;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Shinshi.Backend.Observability
{
    // 파이프라인 맨 앞에 둔다. 인증 미들웨어가 401로 끊은 요청도 시간과 상태가 남아야 한다.
    // URL은 경로 템플릿만 남긴다. 실제 경로에는 ID가, query에는 사유나 검색어가 들어갈 수 있다.
    // 본문, 헤더, 토큰은 읽지 않는다
    /// <summary> 요청마다 api.request.completed 한 줄(경로 템플릿, 메서드, 상태, 소요 ms)을 남기고 요청 시간 지표에 넣는다 </summary>
    public sealed class RequestLoggingMiddleware
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string EVENT_NAME      = "api.request.completed";
        public const string UNMATCHED_ROUTE = "unmatched";

        private const string SESSION_ROUTE_VALUE = "sessionId";
        private const int    EVENT_ID            = 1000;

        private static readonly Action<ILogger, string, string, string, int, double, string?, Exception?> G_Completed =
            LoggerMessage.Define<string, string, string, int, double, string?>(LogLevel.Information,
                new EventId(EVENT_ID, EVENT_NAME),
                "{Event} {Method} {Route} {StatusCode} {DurationMs} {SessionId}");

        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;
        private readonly ApiMetrics _metrics;

        /****************************************
        *                Methods
        ****************************************/

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger, ApiMetrics metrics)
        {
            ArgumentNullException.ThrowIfNull(next);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(metrics);
            _next    = next;
            _logger  = logger;
            _metrics = metrics;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            long started = Stopwatch.GetTimestamp();
            bool failed = false;
            try
            {
                await _next(context);
            }
            catch
            {
                failed = true;
                throw;
            }
            finally
            {
                // 처리되지 않은 예외는 호스트가 500으로 응답한다. 응답이 이미 시작됐으면 그 상태를 쓴다
                int status = (failed && !context.Response.HasStarted)
                    ? StatusCodes.Status500InternalServerError
                    : context.Response.StatusCode;
                double durationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                string route = RouteTemplateOf(context);
                _metrics.RecordRequest(route, context.Request.Method, status, durationMs);
                G_Completed(_logger, EVENT_NAME, context.Request.Method, route, status, durationMs,
                    SessionIdOf(context), null);
            }
        }

        /// <summary> 라우팅이 고른 엔드포인트의 경로 템플릿. 맞는 엔드포인트가 없으면 unmatched다 </summary>
        public static string RouteTemplateOf(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            string? template = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            if (string.IsNullOrEmpty(template))
            {
                return UNMATCHED_ROUTE;
            }

            return template[0] == '/' ? template : "/" + template;
        }

        // 경로의 sessionId는 GUID일 때만 상관 키로 남긴다. 형식이 틀린 값은 사용자 입력 그대로라 기록하지 않는다
        private static string? SessionIdOf(HttpContext context)
        {
            return (context.Request.RouteValues.TryGetValue(SESSION_ROUTE_VALUE, out object? value)
                && Guid.TryParse(value as string, out Guid sessionId))
                ? sessionId.ToString("D")
                : null;
        }
    }
}
