// System
using System;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Shinshi.Admin.Web.Infrastructure
{
    /// <summary> 업무 화면과 Blazor 연결은 유효한 웹 세션이 있어야 서버에서 시작한다 </summary>
    public sealed class AdminWebSessionMiddleware
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly RequestDelegate _next;

        /****************************************
        *                Methods
        ****************************************/

        public AdminWebSessionMiddleware(RequestDelegate next)
        {
            ArgumentNullException.ThrowIfNull(next);
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, AdminWebSessionStore sessions)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(sessions);
            bool blazor = context.Request.Path.StartsWithSegments("/_blazor", StringComparison.OrdinalIgnoreCase);
            if ((!blazor) && ((context.Request.Path.Equals("/login", StringComparison.OrdinalIgnoreCase))
                || (context.Request.Path.Equals("/healthz", StringComparison.OrdinalIgnoreCase))
                || (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() != null)))
            {
                await _next(context);
                return;
            }

            if (sessions.IsActive(AdminWebSessionCookies.Read(context)))
            {
                await _next(context);
                return;
            }

            if (blazor)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            context.Response.Redirect("/login");
        }
    }
}
