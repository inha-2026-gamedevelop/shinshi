// System
using System;

// Third Party
using Microsoft.AspNetCore.Http;

namespace Shinshi.Admin.Web.Infrastructure
{
    /// <summary> HTTPS에서는 호스트에 묶인 쿠키만 세션으로 인정한다 </summary>
    public static class AdminWebSessionCookies
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string HTTPS_COOKIE_NAME = "__Host-shinshi.admin.session";

        /****************************************
        *                Methods
        ****************************************/

        public static string GetName(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.Request.IsHttps ? HTTPS_COOKIE_NAME : AdminWebSessionStore.COOKIE_NAME;
        }

        public static string? Read(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.Request.Cookies[GetName(context)];
        }

        public static CookieOptions CreateOptions(HttpContext context, DateTimeOffset? expires = null)
        {
            ArgumentNullException.ThrowIfNull(context);
            return new CookieOptions
            {
                HttpOnly    = true,
                Secure      = context.Request.IsHttps,
                SameSite    = SameSiteMode.Strict,
                Path        = "/",
                Expires     = expires,
                IsEssential = true
            };
        }

        public static void Delete(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Response.Cookies.Delete(GetName(context), CreateOptions(context));
            if (context.Request.IsHttps)
            {
                context.Response.Cookies.Delete(AdminWebSessionStore.COOKIE_NAME, CreateOptions(context));
            }
        }
    }
}
