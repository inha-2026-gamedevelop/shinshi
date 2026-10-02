// System
using System;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Auth;

namespace Shinshi.Backend
{
    /// <summary> 공개 엔드포인트 외에는 DB에서 검증한 bearer만 허용하며 서버 경로의 자격을 분리한다.
    /// 플레이어 경로에서 남의 키로 만든 bearer가 한도를 넘은 IP는 토큰을 읽지 않고 거절한다. 서버 임대 경로는 IP 차단에서 뺀다.
    /// 같은 IP의 플레이어 실패가 heartbeat와 정산을 막으면 출정이 장애 취소되기 때문이다. 서버 경로의 위조 bearer는 태그 검사로 DB 전에 거절된다 </summary>
    public sealed class BearerTokenMiddleware
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly RequestDelegate _next;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public BearerTokenMiddleware(RequestDelegate next, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(next);
            ArgumentNullException.ThrowIfNull(timeProvider);
            _next         = next;
            _timeProvider = timeProvider;
        }

        /// <summary> UseRouting 이후 실행해야 엔드포인트의 공개 여부와 서버 전용 표시를 읽을 수 있다 </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            Endpoint? endpoint = context.GetEndpoint();
            AuthActors.Clear(context);
            if ((endpoint == null) || (endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null))
            {
                await _next(context);
                return;
            }

            bool serverRoute = endpoint.Metadata.GetMetadata<RequireServerLeaseAttribute>() != null;
            AuthFailureLimiter failures = context.RequestServices.GetRequiredService<AuthFailureLimiter>();
            if (!serverRoute && failures.IsBlocked(context))
            {
                await AuthFailureLimiter.WriteTooManyAsync(context, _timeProvider);
                return;
            }

            Microsoft.Extensions.Primitives.StringValues values = context.Request.Headers[ApiRoutes.AUTHORIZATION_HEADER];
            string header = values.Count == 1 ? values[0] ?? string.Empty : string.Empty;
            if (!header.StartsWith(ApiRoutes.BEARER_PREFIX, StringComparison.OrdinalIgnoreCase))
            {
                await RejectAsync(context, ContractCodes.RejectReason.TOKEN_REVOKED, CountsAsFailure(serverRoute, false));
                return;
            }
            string token = header.Substring(ApiRoutes.BEARER_PREFIX.Length);
            if (!AuthTokenHasher.IsWellFormed(token))
            {
                await RejectAsync(context, ContractCodes.RejectReason.TOKEN_REVOKED, CountsAsFailure(serverRoute, false));
                return;
            }

            IAuthRepository repository = context.RequestServices.GetRequiredService<IAuthRepository>();
            bool ownToken = repository.IsAuthenticToken(token);
            if (serverRoute)
            {
                AuthValidationOutcome<ServerAuthIdentity> outcome = await repository.ValidateServerLeaseAsync(
                    token, context.RequestAborted);
                if (!outcome.IsSuccess)
                {
                    await RejectAsync(context, outcome.ReasonCode, CountsAsFailure(serverRoute, ownToken));
                    return;
                }
                AuthActors.SetServer(context, outcome.Identity!);
            }
            else
            {
                AuthValidationOutcome<PlayerAuthIdentity> outcome = await repository.ValidateAccessAsync(
                    token, context.RequestAborted);
                if (!outcome.IsSuccess)
                {
                    await RejectAsync(context, outcome.ReasonCode, CountsAsFailure(serverRoute, ownToken));
                    return;
                }
                AuthActors.SetPlayer(context, outcome.Identity!);
            }

            await _next(context);
        }

        // 서버 경로는 세지 않는다. 플레이어 경로는 우리 키로 만든 토큰의 만료, 폐기를 세지 않는다(15분 접근 토큰 만료는 정상 흐름이다).
        // 태그가 맞는 토큰은 위조할 수 없으므로 무작위 대입의 신호는 태그가 없거나 틀린 bearer뿐이다
        private static bool CountsAsFailure(bool serverRoute, bool ownToken)
        {
            return !serverRoute && !ownToken;
        }

        private async Task RejectAsync(HttpContext context, int reasonCode, bool countFailure)
        {
            if (countFailure)
            {
                context.RequestServices.GetRequiredService<AuthFailureLimiter>().RecordFailure(context);
            }
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers.WWWAuthenticate = "Bearer";
            context.Response.Headers.CacheControl = "no-store";
            string body = JsonConvert.SerializeObject(new ApiRejection(reasonCode, string.Empty,
                _timeProvider.GetUtcNow().ToUnixTimeMilliseconds()), ContractJson.CreateSettings());
            await context.Response.WriteAsync(body, context.RequestAborted);
        }
    }
}

