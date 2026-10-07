// System
using System;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;

namespace Shinshi.Admin.Web.Infrastructure
{
    public sealed class AdminWebSecurityHeadersMiddleware
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string HSTS_VALUE = "max-age=31536000";
        private readonly RequestDelegate _next;

        /****************************************
        *                Methods
        ****************************************/

        public AdminWebSecurityHeadersMiddleware(RequestDelegate next)
        {
            ArgumentNullException.ThrowIfNull(next);
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.Pragma = "no-cache";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                string scheme = context.Request.IsHttps ? "wss" : "ws";
                string socketSource = string.Empty;
                if (Uri.TryCreate(scheme + "://" + context.Request.Host.ToUriComponent(), UriKind.Absolute,
                    out Uri? socketUri))
                {
                    socketSource = " " + socketUri.GetLeftPart(UriPartial.Authority);
                }
                // Blazor의 재접속 UI가 스타일을 삽입한다. 스크립트는 외부 파일만 사용한다
                context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; "
                    + "style-src 'self' 'unsafe-inline'; connect-src 'self'" + socketSource
                    + "; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
                if (context.Request.IsHttps)
                {
                    context.Response.Headers["Strict-Transport-Security"] = HSTS_VALUE;
                }
                return Task.CompletedTask;
            });
            await _next(context);
        }
    }
}
