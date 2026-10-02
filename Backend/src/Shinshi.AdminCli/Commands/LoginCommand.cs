// System
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 운영자 로그인. 비밀번호와 TOTP는 인자로 받지 않고(셸 기록과 프로세스 목록에 남는다) 에코 없이 읽는다 </summary>
    internal sealed class LoginCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        // 비대화 실행(배포 스크립트 등)용. 대화형이면 쓰지 않는다
        public const string PASSWORD_ENVIRONMENT = "SHINSHI_ADMIN_PASSWORD";
        public const string TOTP_ENVIRONMENT     = "SHINSHI_ADMIN_TOTP";

        private static readonly string[] G_Usage =
        {
            "login --user <로그인 이름>                      비밀번호와 TOTP를 묻고 세션을 저장 (환경 변수 "
                + PASSWORD_ENVIRONMENT + ", " + TOTP_ENVIRONMENT + " 허용)"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "login";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public async Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(0, new[] { AdminCliOptions.USER }, Array.Empty<string>());
            string user = arguments.RequireOption(AdminCliOptions.USER).Trim();
            WarnIfPlainRemote(context);
            string password = ReadSecret(context, PASSWORD_ENVIRONMENT, "비밀번호: ");
            string totp = ReadSecret(context, TOTP_ENVIRONMENT, "TOTP 코드: ").Trim();

            AdminApiResponse<AdminLoginResponse> response;
            using (AdminApiClient client = context.CreateAnonymousClient())
            {
                response = await client.SendAsync<AdminLoginResponse>(HttpMethod.Post, AdminApiRoutes.AUTH_LOGIN,
                    new AdminLoginRequest(user, password, totp), false, cancellationToken);
            }

            AdminLoginResponse login = response.Value;
            AdminSession session = new AdminSession(context.ApiBase, login.AccessToken, login.ExpiresAtUnixMs,
                login.AdminId, user.ToLowerInvariant(), login.Role);
            context.Sessions.Save(session);

            // 응답 원본에는 토큰이 있으므로 --json이어도 원본 대신 토큰을 뺀 값만 쓴다
            if (context.Json)
            {
                JObject sanitized = new JObject
                {
                    ["adminId"]         = login.AdminId,
                    ["role"]            = login.Role,
                    ["expiresAtUnixMs"] = login.ExpiresAtUnixMs,
                    ["sessionFile"]     = context.Sessions.FilePath
                };
                context.WriteJson(sanitized.ToString(Formatting.None));
                return CliExitCodes.SUCCESS;
            }

            context.Line("로그인했습니다: " + session.LoginName + " (" + ContractCodeNames.Role(login.Role) + ")");
            context.WriteFields(new List<(string, string)>
            {
                ("운영자 ID", login.AdminId),
                ("만료", CliFormat.Time(login.ExpiresAtUnixMs)),
                ("세션 파일", context.Sessions.FilePath)
            });
            return CliExitCodes.SUCCESS;
        }

        private static string ReadSecret(CliContext context, string variable, string prompt)
        {
            string? value = context.ReadEnvironment(variable);
            if (string.IsNullOrEmpty(value))
            {
                value = context.Terminal.ReadSecret(prompt);
            }

            if (string.IsNullOrEmpty(value))
            {
                throw new CliUsageException(prompt.TrimEnd(' ', ':') + " 입력이 비어 있습니다.");
            }

            return value;
        }

        // 평문 HTTP는 같은 기기 안(루프백)에서만 안전하다. 막지는 않되 비밀번호가 평문으로 간다고 알린다
        private static void WarnIfPlainRemote(CliContext context)
        {
            Uri api = new Uri(context.ApiBase);
            bool loopback = api.IsLoopback || (IPAddress.TryParse(api.Host, out IPAddress? address)
                && IPAddress.IsLoopback(address));
            if ((api.Scheme == Uri.UriSchemeHttp) && (!loopback))
            {
                context.Warn("경고: " + context.ApiBase + " 는 평문 HTTP입니다. 비밀번호와 토큰이 암호화되지 않은 채 전송됩니다.");
            }
        }
    }
}
