// System
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 어드민 엔드포인트만 처리한다. 허용 네트워크 밖은 404로 존재를 숨기고, 어드민 토큰이 아니면 401, 역할이 부족하면 403이다.
    /// 게임 접근 토큰과 서버 임대 토큰은 해시 용도가 달라 어드민 세션으로 찾지 못하므로 401이 된다 </summary>
    public sealed class AdminAuthenticationMiddleware
    {
        /****************************************
        *                Fields
        ****************************************/

        // 프록시가 원래 클라이언트 주소를 싣는 헤더. 표준 Forwarded와 사실상 표준 X-Forwarded-For를 모두 본다
        private const string FORWARDED_FOR_HEADER = "X-Forwarded-For";
        private const string FORWARDED_HEADER     = "Forwarded";

        private readonly RequestDelegate _next;
        private readonly AdminNetworkPolicy _networkPolicy;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<AdminAuthenticationMiddleware> _logger;

        // 설정 누락은 요청마다 반복되므로 프로세스당 한 번만 오류로 남긴다
        private int _forwardingWarningLogged;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAuthenticationMiddleware(RequestDelegate next, AdminNetworkPolicy networkPolicy,
            TimeProvider timeProvider, ILogger<AdminAuthenticationMiddleware> logger)
        {
            ArgumentNullException.ThrowIfNull(next);
            ArgumentNullException.ThrowIfNull(networkPolicy);
            ArgumentNullException.ThrowIfNull(timeProvider);
            ArgumentNullException.ThrowIfNull(logger);
            _next          = next;
            _networkPolicy = networkPolicy;
            _timeProvider  = timeProvider;
            _logger        = logger;
        }

        /// <summary> UseRouting 뒤, 게임 인증 미들웨어 앞에서 실행한다 </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            AdminActors.Clear(context);
            Endpoint? endpoint = context.GetEndpoint();
            IReadOnlyList<IAdminEndpointMetadata> metadata = endpoint?.Metadata.GetOrderedMetadata<IAdminEndpointMetadata>()
                ?? Array.Empty<IAdminEndpointMetadata>();
            if (metadata.Count == 0)
            {
                // 어드민 접두사 아래인데 표시가 없으면 게임 bearer 미들웨어가 플레이어 토큰으로 열어 버린다. 닫힌 쪽으로 처리한다
                if ((endpoint != null) && context.Request.Path.StartsWithSegments(AdminApiRoutes.PREFIX))
                {
                    _logger.LogError("Admin route {Path} has no admin endpoint metadata and was hidden.",
                        context.Request.Path.Value);
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                await _next(context);
                return;
            }

            // 신뢰 프록시 없이 전달 헤더가 왔다면 프록시 뒤인데 설정이 빠진 것이다. 이때 RemoteIpAddress는 프록시 주소라
            // 허용 목록 판정이 모든 외부 요청을 프록시 하나로 보게 되므로 판정하지 않고 닫는다
            if ((!_networkPolicy.TrustsForwardedHeaders) && HasForwardingHeader(context.Request))
            {
                if (Interlocked.Exchange(ref _forwardingWarningLogged, 1) == 0)
                {
                    _logger.LogError("Admin request carried X-Forwarded-For or Forwarded but ForwardedHeaders:KnownProxies "
                        + "is empty; admin routes stay hidden until the trusted proxy is configured.");
                }
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!_networkPolicy.IsAllowed(context.Connection.RemoteIpAddress))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            AdminTokenHasher hasher = context.RequestServices.GetRequiredService<AdminTokenHasher>();
            AdminActors.SetRequestIpHash(context, hasher.HashRequestIp(context.Connection.RemoteIpAddress));
            if (endpoint!.Metadata.GetMetadata<AllowAdminAnonymousAttribute>() != null)
            {
                await _next(context);
                return;
            }

            AuthFailureLimiter failures = context.RequestServices.GetRequiredKeyedService<AuthFailureLimiter>(
                AdminServiceCollectionExtensions.FAILURE_LIMITER_KEY);
            if (failures.IsBlocked(context))
            {
                await AuthFailureLimiter.WriteTooManyAsync(context, _timeProvider);
                return;
            }

            string token = ReadBearer(context);
            if (!hasher.IsAuthentic(token))
            {
                // 남의 키로 만든 bearer만 무차별 대입 신호로 센다. 만료된 어드민 토큰과 게임 토큰은 세지 않는다
                failures.RecordFailure(context);
                await WriteRejectionAsync(context, StatusCodes.Status401Unauthorized,
                    ContractCodes.RejectReason.TOKEN_REVOKED);
                return;
            }

            IAdminSessionValidator validator = context.RequestServices.GetRequiredService<IAdminSessionValidator>();
            AdminIdentity? identity = await validator.ValidateAsync(token, context.RequestAborted);
            if (identity == null)
            {
                await WriteRejectionAsync(context, StatusCodes.Status401Unauthorized,
                    ContractCodes.RejectReason.TOKEN_REVOKED);
                return;
            }

            if (!identity.HasRole(RequiredRole(metadata)))
            {
                await WriteRejectionAsync(context, StatusCodes.Status403Forbidden,
                    AdminContractCodes.RejectReason.FORBIDDEN_ROLE);
                return;
            }

            AdminActors.SetIdentity(context, identity);
            await _next(context);
        }

        // 컨트롤러와 액션의 표시 중 가장 높은 역할. 액션 표시가 컨트롤러의 요구를 낮추지 못한다
        private static EAdminRole RequiredRole(IReadOnlyList<IAdminEndpointMetadata> metadata)
        {
            EAdminRole required = EAdminRole.Viewer;
            foreach (IAdminEndpointMetadata entry in metadata)
            {
                if ((entry is RequireAdminRoleAttribute role) && (role.Role > required))
                {
                    required = role.Role;
                }
            }

            return required;
        }

        private static bool HasForwardingHeader(HttpRequest request)
        {
            return request.Headers.ContainsKey(FORWARDED_FOR_HEADER) || request.Headers.ContainsKey(FORWARDED_HEADER);
        }

        private static string ReadBearer(HttpContext context)
        {
            Microsoft.Extensions.Primitives.StringValues values = context.Request.Headers[ApiRoutes.AUTHORIZATION_HEADER];
            string header = values.Count == 1 ? values[0] ?? string.Empty : string.Empty;
            return header.StartsWith(ApiRoutes.BEARER_PREFIX, StringComparison.OrdinalIgnoreCase)
                ? header.Substring(ApiRoutes.BEARER_PREFIX.Length) : string.Empty;
        }

        private async Task WriteRejectionAsync(HttpContext context, int statusCode, int reasonCode)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            if (statusCode == StatusCodes.Status401Unauthorized)
            {
                context.Response.Headers.WWWAuthenticate = "Bearer";
            }
            string body = JsonConvert.SerializeObject(new ApiRejection(reasonCode, string.Empty,
                _timeProvider.GetUtcNow().ToUnixTimeMilliseconds()), ContractJson.CreateSettings());
            await context.Response.WriteAsync(body, context.RequestAborted);
        }
    }
}
