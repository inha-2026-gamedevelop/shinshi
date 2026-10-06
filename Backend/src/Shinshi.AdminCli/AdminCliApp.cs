// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts;

namespace Shinshi.AdminCli
{
    /// <summary> 명령을 찾아 실행하고 거절, 연결 실패, 사용법 오류를 한곳에서 출력해 종료 코드로 바꾼다 </summary>
    internal static class AdminCliApp
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string TOOL_NAME = "shinshi-admin";

        private const string HELP_COMMAND = "help";

        private static readonly ICliCommand[] G_Commands =
        {
            new LoginCommand(),
            new LogoutCommand(),
            new OperatorsCommand(),
            new AccountsCommand(),
            new InvitesCommand(),
            new GrantsCommand(),
            new AdjustCommand(),
            new ServiceStateCommand(),
            new NoticesCommand(),
            new LedgerCommand(),
            new ItemHistoryCommand(),
            new SessionEventsCommand(),
            new OperationCommand(),
            new AuditCommand(),
            new SessionsCommand(),
            new RecoverCommand(),
            new AuctionsCommand()
        };

        /****************************************
        *              Properties
        ****************************************/

        public static IReadOnlyList<ICliCommand> Commands => G_Commands;

        /****************************************
        *                Methods
        ****************************************/

        public static async Task<int> RunAsync(IReadOnlyList<string> args, CliContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(args);
            ArgumentNullException.ThrowIfNull(context);

            CliArguments arguments;
            try
            {
                arguments = CliArgumentParser.Parse(args);
                context.ApplyGlobals(arguments);
            }
            catch (CliUsageException exception)
            {
                context.Warn("오류: " + exception.Message);
                context.Warn("도움말: " + TOOL_NAME + " --help");
                return CliExitCodes.USAGE;
            }

            if (arguments.Positionals.Count == 0)
            {
                WriteHelp(arguments.HasFlag(CliArguments.FLAG_HELP) ? context.Terminal.Out : context.Terminal.Error);
                return arguments.HasFlag(CliArguments.FLAG_HELP) ? CliExitCodes.SUCCESS : CliExitCodes.USAGE;
            }

            string name = arguments.Positionals[0];
            if (string.Equals(name, HELP_COMMAND, StringComparison.Ordinal))
            {
                return WriteHelpFor(context, arguments.Positionals.Count > 1 ? arguments.Positionals[1] : null);
            }

            ICliCommand? command = Find(name);
            if (command == null)
            {
                context.Warn("오류: 알 수 없는 명령입니다: " + name);
                context.Warn("도움말: " + TOOL_NAME + " --help");
                return CliExitCodes.USAGE;
            }

            if (arguments.HasFlag(CliArguments.FLAG_HELP))
            {
                WriteCommandHelp(context.Terminal.Out, command);
                return CliExitCodes.SUCCESS;
            }

            try
            {
                return await command.RunAsync(context, arguments.SkipPositionals(1), cancellationToken);
            }
            catch (CliUsageException exception)
            {
                context.Warn("오류: " + exception.Message);
                WriteCommandHelp(context.Terminal.Error, command);
                return CliExitCodes.USAGE;
            }
            catch (AdminApiException exception)
            {
                return ReportRejection(context, exception);
            }
            catch (CliUnavailableException exception)
            {
                context.Warn("연결 실패: " + exception.Message);
                if (exception.ResponseLost && (context.IssuedOperationId != null))
                {
                    context.Warn("요청이 처리됐는지 알 수 없습니다. 같은 명령에 --operation-id " + context.IssuedOperationId
                        + " 를 붙여 다시 실행하면 처음 결과를 받습니다.");
                }

                return CliExitCodes.UNAVAILABLE;
            }
            catch (Exception exception) when ((exception is IOException) || (exception is UnauthorizedAccessException))
            {
                // 세션 파일 읽기, 쓰기 실패. 메시지에는 경로만 있고 토큰은 없다
                context.Warn("세션 파일을 다루지 못했습니다(" + context.Sessions.FilePath + "): " + exception.Message);
                return CliExitCodes.UNAVAILABLE;
            }
        }

        public static ICliCommand? Find(string name)
        {
            foreach (ICliCommand command in G_Commands)
            {
                if (string.Equals(command.Name, name, StringComparison.Ordinal))
                {
                    return command;
                }
            }

            return null;
        }

        public static void WriteHelp(TextWriter writer)
        {
            writer.WriteLine(TOOL_NAME + " - shinshi 어드민 API 명령줄 도구 (API만 호출하고 DB에 직접 붙지 않습니다)");
            writer.WriteLine();
            writer.WriteLine("사용법: " + TOOL_NAME + " <명령> [하위 명령] [인자] [옵션]");
            writer.WriteLine();
            writer.WriteLine("명령:");
            foreach (ICliCommand command in G_Commands)
            {
                foreach (string line in command.Usage)
                {
                    writer.WriteLine("  " + line);
                }
            }

            writer.WriteLine();
            writer.WriteLine("전역 옵션:");
            writer.WriteLine("  --api <주소>           API 주소. 없으면 환경 변수 " + CliContext.API_ENVIRONMENT + ", 그것도 없으면 "
                + CliContext.DEFAULT_API);
            writer.WriteLine("  --json                 응답 JSON 원본을 표준 출력에 씁니다(안내는 표준 오류)");
            writer.WriteLine("  --operation-id <guid>  쓰기 재시도용. 응답을 잃은 쓰기는 출력된 같은 ID로 다시 보냅니다");
            writer.WriteLine("  --yes                  쓰기 확인 질문을 건너뜁니다");
            writer.WriteLine("  --help, -h             도움말");
            writer.WriteLine();
            writer.WriteLine("종료 코드: 0 성공, 1 사용법 오류 또는 확인 거절, 2 API 거절, 3 연결 실패, 로그인 필요, 세션 만료,");
            writer.WriteLine("           일시 장애(503), 요청 한도(429), 처리 대기(202)");
        }

        private static int WriteHelpFor(CliContext context, string? name)
        {
            if (name == null)
            {
                WriteHelp(context.Terminal.Out);
                return CliExitCodes.SUCCESS;
            }

            ICliCommand? command = Find(name);
            if (command == null)
            {
                context.Warn("오류: 알 수 없는 명령입니다: " + name);
                return CliExitCodes.USAGE;
            }

            WriteCommandHelp(context.Terminal.Out, command);
            return CliExitCodes.SUCCESS;
        }

        private static void WriteCommandHelp(TextWriter writer, ICliCommand command)
        {
            writer.WriteLine("사용법:");
            string continuation = new string(' ', TOOL_NAME.Length + 1);
            foreach (string line in command.Usage)
            {
                // 공백으로 시작하는 줄은 앞 줄의 이어짐이라 도구 이름 대신 같은 폭의 공백을 붙인다
                bool continues = (line.Length > 0) && (line[0] == ' ');
                writer.WriteLine("  " + (continues ? continuation : TOOL_NAME + " ") + line);
            }

            writer.WriteLine("전역 옵션: --api, --json, --operation-id, --yes (" + TOOL_NAME + " --help 참고)");
        }

        // 401 TOKEN_REVOKED는 세션 만료로 보고 저장 파일을 지운다. 503, 429, 202는 잠시 뒤 같은 operationId로 다시 보낼 일이라 3이다
        private static int ReportRejection(CliContext context, AdminApiException exception)
        {
            if (context.Json && (exception.RawBody.Length > 0))
            {
                context.WriteJson(exception.RawBody);
            }

            string reason = exception.ReasonCode.HasValue
                ? ContractCodeNames.RejectReason(exception.ReasonCode.Value) + " (" + exception.ReasonCode.Value + ")"
                : "본문 없음";
            if ((exception.StatusCode == (int)HttpStatusCode.Unauthorized)
                && (exception.ReasonCode == ContractCodes.RejectReason.TOKEN_REVOKED))
            {
                context.Sessions.Delete();
                context.Warn("세션이 만료됐거나 폐기됐습니다(" + reason + ", HTTP 401). login으로 다시 로그인하세요.");
                return CliExitCodes.UNAVAILABLE;
            }

            if (exception.StatusCode == (int)HttpStatusCode.Accepted)
            {
                context.Warn("요청이 아직 확정되지 않았습니다(PENDING, HTTP 202). 같은 --operation-id로 다시 실행해 결과를 확인하세요.");
                return CliExitCodes.UNAVAILABLE;
            }

            context.Warn("거절: " + reason + ", HTTP " + exception.StatusCode);
            if (exception.OperationId != null)
            {
                context.Warn("operationId: " + exception.OperationId);
            }

            string? hint = Hint(exception);
            if (hint != null)
            {
                context.Warn(hint);
            }

            bool transient = (exception.StatusCode == (int)HttpStatusCode.ServiceUnavailable)
                || (exception.StatusCode == (int)HttpStatusCode.TooManyRequests);
            return transient ? CliExitCodes.UNAVAILABLE : CliExitCodes.REJECTED;
        }

        private static string? Hint(AdminApiException exception)
        {
            if ((exception.StatusCode == (int)HttpStatusCode.NotFound) && (!exception.ReasonCode.HasValue))
            {
                return "경로가 없거나 허용 네트워크 밖이라 API가 존재를 숨겼습니다. --api 주소와 접속 위치를 확인하세요.";
            }

            if (exception.StatusCode == (int)HttpStatusCode.Forbidden)
            {
                return "이 작업에 필요한 역할이 없습니다.";
            }

            if (exception.ReasonCode == ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT)
            {
                return "같은 operationId로 다른 내용을 보냈습니다. 새 작업이면 --operation-id 없이 실행하세요.";
            }

            if ((exception.StatusCode == (int)HttpStatusCode.ServiceUnavailable)
                || (exception.StatusCode == (int)HttpStatusCode.TooManyRequests))
            {
                return "일시적인 거절입니다. 잠시 뒤 같은 --operation-id로 다시 실행하세요.";
            }

            return null;
        }
    }
}
