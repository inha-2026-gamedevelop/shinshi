// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Sessions
{
    /// <summary> 서버 임대의 인스턴스와 역할을 세션 모듈의 호출 주체로 연결한다 </summary>
    public sealed class ServerCallerAuthenticator : IServerCallerAuthenticator
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IAuthRepository _auth;

        /****************************************
        *                Methods
        ****************************************/

        public ServerCallerAuthenticator(IAuthRepository auth)
        {
            ArgumentNullException.ThrowIfNull(auth);
            _auth = auth;
        }

        public async Task<ServerCaller?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            StringValues instanceHeaders = context.Request.Headers[JoinApiRoutes.SERVER_INSTANCE_HEADER];
            if ((instanceHeaders.Count != 1) || !Guid.TryParse(instanceHeaders[0], out Guid instanceId)
                || (instanceId == Guid.Empty))
            {
                return null;
            }

            ServerAuthIdentity? server = AuthActors.GetServer(context);
            if (server == null)
            {
                // 플레이어로 검증된 요청을 다른 토큰 종류로 다시 해석하지 않는다
                if (AuthActors.GetPlayer(context) != null)
                {
                    return null;
                }
                StringValues headers = context.Request.Headers[ApiRoutes.AUTHORIZATION_HEADER];
                string header = headers.Count == 1 ? headers[0] ?? string.Empty : string.Empty;
                if (!header.StartsWith(ApiRoutes.BEARER_PREFIX, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                string token = header.Substring(ApiRoutes.BEARER_PREFIX.Length);
                if (!AuthTokenHasher.IsWellFormed(token))
                {
                    return null;
                }

                AuthValidationOutcome<ServerAuthIdentity> outcome = await _auth.ValidateServerLeaseAsync(
                    token, cancellationToken);
                if (!outcome.IsSuccess)
                {
                    return null;
                }
                server = outcome.Identity!;
            }

            if ((server.InstanceId != instanceId)
                || ((server.Role != ContractCodes.ServerRole.TOWN) && (server.Role != ContractCodes.ServerRole.DUNGEON)))
            {
                return null;
            }

            return new ServerCaller(server.InstanceId, server.Role, server.BootId);
        }
    }
}