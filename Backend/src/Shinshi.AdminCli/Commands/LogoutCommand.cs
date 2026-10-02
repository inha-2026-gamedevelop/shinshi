// System
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 서버의 현재 어드민 세션을 폐기하고 세션 파일을 지운다 </summary>
    internal sealed class LogoutCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly string[] G_Usage =
        {
            "logout                                         서버 세션을 폐기하고 세션 파일을 지움"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "logout";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public async Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(0, Array.Empty<string>(), Array.Empty<string>());
            AdminSession? session = context.Sessions.Load();
            if (session == null)
            {
                context.Sessions.Delete();
                context.Note("저장된 로그인 세션이 없습니다.");
                return CliExitCodes.SUCCESS;
            }

            if (context.Time.GetUtcNow().ToUnixTimeMilliseconds() < session.ExpiresAtUnixMs)
            {
                try
                {
                    using AdminApiClient client = context.CreateClient(session);
                    await client.SendRawAsync(HttpMethod.Post, AdminApiRoutes.AUTH_LOGOUT, null, false, cancellationToken);
                }
                catch (AdminApiException exception) when (exception.StatusCode == (int)HttpStatusCode.Unauthorized)
                {
                    // 이미 만료되거나 폐기된 토큰이다. 서버에서 할 일이 없으므로 파일만 지운다
                    context.Note("서버 세션은 이미 만료됐거나 폐기돼 있었습니다.");
                }
                catch (AdminApiException)
                {
                    // 500, 503 같은 서버 오류도 로그아웃 의도는 같으므로 파일은 지운다. 폐기 여부는 알 수 없다
                    WarnUnconfirmed(context, session);
                    throw;
                }
                catch (CliUnavailableException)
                {
                    WarnUnconfirmed(context, session);
                    throw;
                }
            }

            context.Sessions.Delete();
            context.Note("로그아웃했습니다. 세션 파일을 지웠습니다.");
            return CliExitCodes.SUCCESS;
        }

        private static void WarnUnconfirmed(CliContext context, AdminSession session)
        {
            context.Sessions.Delete();
            context.Warn("세션 파일은 지웠지만 서버 토큰 폐기는 확인하지 못했습니다. 토큰은 "
                + CliFormat.Time(session.ExpiresAtUnixMs) + "까지 서버에서 유효할 수 있습니다.");
        }
    }
}
