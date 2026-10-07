// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Shinshi.Admin.Web.Infrastructure
{
    /// <summary> 로그인·정적 파일·회로 연결·healthz도 같은 IP 정책을 적용한다 </summary>
    public sealed class AdminWebNetworkMiddleware
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly RequestDelegate _next;
        private readonly AdminWebNetworkPolicy _policy;
        private readonly ILogger<AdminWebNetworkMiddleware> _logger;
        private int _warningLogged;

        /****************************************
        *                Methods
        ****************************************/

        public AdminWebNetworkMiddleware(RequestDelegate next, AdminWebNetworkPolicy policy,
            ILogger<AdminWebNetworkMiddleware> logger)
        {
            _next   = next;
            _policy = policy;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_policy.TrustsForwardedHeaders
                && (context.Request.Headers.ContainsKey("X-Forwarded-For")
                    || context.Request.Headers.ContainsKey("Forwarded")))
            {
                if (Interlocked.Exchange(ref _warningLogged, 1) == 0)
                {
                    _logger.LogError("Admin web forwarding headers require explicit ForwardedHeaders:KnownProxies.");
                }
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!_policy.IsAllowed(context.Connection.RemoteIpAddress))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await _next(context);
        }
    }
}
