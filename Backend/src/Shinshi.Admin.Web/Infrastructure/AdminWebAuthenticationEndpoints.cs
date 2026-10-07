// System
using System;
using System.Globalization;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Admin.Web.Infrastructure
{
    /// <summary> 자격 증명은 일반 HTTPS 폼으로 서버에만 전달한다. GET이나 Blazor 상태에 로그인 비밀값을 넣지 않는다 </summary>
    public static class AdminWebAuthenticationEndpoints
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_LOGIN_NAME_LENGTH = 128;
        private const int MAX_PASSWORD_LENGTH   = 4096;
        private const int TOTP_LENGTH           = 6;
        private const int AUTH_ENDPOINT_ORDER   = -1;
        private const int MIN_RETRY_SECONDS     = 1;

        /****************************************
        *                Methods
        ****************************************/

        public static IEndpointRouteBuilder MapAdminWebAuthentication(this IEndpointRouteBuilder endpoints)
        {
            // SSR 로그인 페이지도 POST 경로를 만들므로 인증 핸들러를 명시적으로 먼저 선택한다
            endpoints.MapPost("/login", LoginAsync).WithOrder(AUTH_ENDPOINT_ORDER);
            endpoints.MapPost("/logout", LogoutAsync);
            return endpoints;
        }

        private static async Task LoginAsync(HttpContext context, IAntiforgery antiforgery,
            AdminWebApiClient api, AdminWebSessionStore sessions, AdminWebLoginLimiter limiter,
            ILoggerFactory loggerFactory)
        {
            if ((!context.Request.HasFormContentType) || (!await antiforgery.IsRequestValidAsync(context)))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted);
            string loginName = form["loginName"].ToString().Trim();
            if ((string.IsNullOrWhiteSpace(loginName)) || (loginName.Length > MAX_LOGIN_NAME_LENGTH))
            {
                context.Response.Redirect("/login?error=credentials");
                return;
            }
            if (!limiter.TryBegin(context.Connection.RemoteIpAddress, loginName,
                out AdminWebLoginLimiter.LoginAttempt? attempt, out TimeSpan retryAfter))
            {
                context.Response.Headers.RetryAfter = Math.Max(MIN_RETRY_SECONDS, Math.Ceiling(retryAfter.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
                context.Response.Redirect("/login?error=rate");
                return;
            }

            using (attempt)
            {
                string password = form["password"].ToString();
                string totpCode = form["totpCode"].ToString().Trim();
                if ((string.IsNullOrEmpty(password)) || (password.Length > MAX_PASSWORD_LENGTH)
                    || (totpCode.Length != TOTP_LENGTH) || (!IsDigits(totpCode)))
                {
                    attempt!.Fail();
                    context.Response.Redirect("/login?error=credentials");
                    return;
                }

                AdminWebApiResult<AdminLoginResponse> result = await api.LoginAsync(
                    new AdminLoginRequest(loginName, password, totpCode), context.RequestAborted);
                if ((!result.IsSuccess) || (result.Value == null))
                {
                    if (result.StatusCode >= StatusCodes.Status500InternalServerError)
                    {
                        attempt!.Release();
                    }
                    else
                    {
                        attempt!.Fail();
                    }
                    context.Response.Redirect(result.StatusCode switch
                    {
                        StatusCodes.Status429TooManyRequests => "/login?error=api-rate",
                        >= StatusCodes.Status500InternalServerError => "/login?error=unavailable",
                        _ => "/login?error=credentials"
                    });
                    return;
                }

                // 새 세션을 만들 때 이전 세션이 상한 정책으로 제거될 수 있으므로 먼저 API 토큰을 폐기한다
                bool previousRevocationFailed = await RevokeServerSessionAsync(context, api, sessions,
                    loggerFactory, "relogin");
                string sessionId;
                try
                {
                    sessionId = sessions.Create(result.Value, loginName);
                }
                catch (InvalidOperationException)
                {
                    attempt!.Succeed();
                    AdminWebSessionCookies.Delete(context);
                    context.Response.Redirect("/login?error=unavailable");
                    return;
                }

                attempt!.Succeed();
                AdminWebIdentity identity = sessions.GetIdentity(sessionId)!;
                context.Response.Cookies.Append(AdminWebSessionCookies.GetName(context), sessionId,
                    AdminWebSessionCookies.CreateOptions(context,
                        DateTimeOffset.FromUnixTimeMilliseconds(identity.ExpiresAtUnixMs)));
                context.Response.Redirect(previousRevocationFailed ? "/login?warning=previous-session" : "/");
            }
        }

        private static async Task LogoutAsync(HttpContext context, IAntiforgery antiforgery,
            AdminWebApiClient api, AdminWebSessionStore sessions, ILoggerFactory loggerFactory)
        {
            if ((!context.Request.HasFormContentType) || (!await antiforgery.IsRequestValidAsync(context)))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            bool revocationFailed;
            try
            {
                revocationFailed = await RevokeServerSessionAsync(context, api, sessions, loggerFactory, "logout");
            }
            finally
            {
                AdminWebSessionCookies.Delete(context);
            }
            context.Response.Redirect(revocationFailed ? "/login?warning=logout" : "/login");
        }

        private static async Task<bool> RevokeServerSessionAsync(HttpContext context, AdminWebApiClient api,
            AdminWebSessionStore sessions, ILoggerFactory loggerFactory, string revocationContext)
        {
            string? sessionId = AdminWebSessionCookies.Read(context);
            try
            {
                if (!sessions.IsActive(sessionId))
                {
                    return false;
                }

                AdminWebApiResult<Newtonsoft.Json.Linq.JToken> result = await api.LogoutAsync(context.RequestAborted);
                if ((result.IsSuccess) || (result.RequiresLogin))
                {
                    return false;
                }

                LogRevocationWarning(loggerFactory, revocationContext, result.StatusCode, result.ReasonCode);
                return true;
            }
            catch (OperationCanceledException)
            {
                LogRevocationWarning(loggerFactory, revocationContext, StatusCodes.Status499ClientClosedRequest, null);
                return true;
            }
            finally
            {
                // API 연결이나 응답 확인이 실패해도 브라우저와 회로에서 이전 세션을 다시 사용할 수 없게 한다
                sessions.Revoke(sessionId);
            }
        }

        private static void LogRevocationWarning(ILoggerFactory loggerFactory, string revocationContext,
            int statusCode, int? reasonCode)
        {
            // API 진단이나 예외 메시지에는 토큰이 포함될 수 있으므로 확정된 숫자와 구분 값만 기록한다
            ILogger logger = loggerFactory.CreateLogger(typeof(AdminWebAuthenticationEndpoints).FullName!);
            logger.LogWarning("Admin API session revocation was not confirmed. Context: {RevocationContext}, "
                + "StatusCode: {StatusCode}, ReasonCode: {ReasonCode}.", revocationContext, statusCode, reasonCode);
        }

        private static bool IsDigits(string value)
        {
            foreach (char character in value)
            {
                if ((character < '0') || (character > '9'))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
