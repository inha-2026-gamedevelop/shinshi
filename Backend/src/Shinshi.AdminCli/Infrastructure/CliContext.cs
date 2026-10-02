// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 한 번의 실행이 공유하는 입출력, 세션 저장소, API 주소와 전역 옵션. 테스트는 콘솔, 저장 폴더, HTTP 핸들러를 바꿔 끼운다 </summary>
    internal sealed class CliContext
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string API_ENVIRONMENT = "SHINSHI_ADMIN_API";
        public const string DEFAULT_API     = "http://127.0.0.1:5080";

        private const string SUMMARY_INDENT = "  ";
        private const string YES_SHORT      = "y";
        private const string YES_LONG       = "yes";

        private readonly Func<HttpMessageHandler> _createHandler;
        private string? _operationIdOption;

        /****************************************
        *              Properties
        ****************************************/

        public ICliConsole Terminal { get; }
        public AdminSessionStore Sessions { get; }
        public Func<string, string?> ReadEnvironment { get; }
        public TimeProvider Time { get; }

        // 끝 슬래시를 뗀 API 주소. 세션 파일의 api와 비교한다
        public string ApiBase { get; private set; } = DEFAULT_API;
        public bool Json { get; private set; }
        public bool AssumeYes { get; private set; }

        // 이번 실행의 쓰기가 쓴 operationId. 응답을 잃었을 때 재시도 안내에 쓴다
        public string? IssuedOperationId { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        public CliContext(ICliConsole terminal, AdminSessionStore sessions, Func<string, string?> readEnvironment,
            TimeProvider time, Func<HttpMessageHandler> createHandler)
        {
            ArgumentNullException.ThrowIfNull(terminal);
            ArgumentNullException.ThrowIfNull(sessions);
            ArgumentNullException.ThrowIfNull(readEnvironment);
            ArgumentNullException.ThrowIfNull(time);
            ArgumentNullException.ThrowIfNull(createHandler);
            Terminal        = terminal;
            Sessions        = sessions;
            ReadEnvironment = readEnvironment;
            Time            = time;
            _createHandler  = createHandler;
        }

        public static CliContext CreateDefault()
        {
            return new CliContext(new SystemCliConsole(), AdminSessionStore.CreateDefault(Environment.GetEnvironmentVariable),
                Environment.GetEnvironmentVariable, TimeProvider.System, () => new SocketsHttpHandler());
        }

        /// <summary> --api, 환경 변수 SHINSHI_ADMIN_API, 기본값 순으로 API 주소를 정하고 --json, --yes, --operation-id를 읽는다 </summary>
        public void ApplyGlobals(CliArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            string? api = arguments.Option(CliArguments.OPTION_API);
            if (string.IsNullOrWhiteSpace(api))
            {
                api = ReadEnvironment(API_ENVIRONMENT);
            }

            ApiBase   = NormalizeApi(string.IsNullOrWhiteSpace(api) ? DEFAULT_API : api);
            Json      = arguments.HasFlag(CliArguments.FLAG_JSON);
            AssumeYes = arguments.HasFlag(CliArguments.FLAG_YES);
            string? operationId = arguments.Option(CliArguments.OPTION_OPERATION_ID);
            _operationIdOption = operationId == null
                ? null
                : CliFormat.ParseGuid(operationId, "--" + CliArguments.OPTION_OPERATION_ID).ToString("D");
        }

        public static string NormalizeApi(string text)
        {
            if ((!Uri.TryCreate(text.Trim(), UriKind.Absolute, out Uri? uri))
                || ((uri.Scheme != Uri.UriSchemeHttp) && (uri.Scheme != Uri.UriSchemeHttps))
                || (uri.Query.Length > 0) || (uri.Fragment.Length > 0))
            {
                throw new CliUsageException("--api는 http:// 또는 https:// 로 시작하는 주소여야 합니다: " + text);
            }

            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        /// <summary> 감사 로그에 남는 사유. 공백이 아니고 AdminContractCodes.MAX_REASON_LENGTH자 이하 </summary>
        public static string RequireReason(CliArguments arguments)
        {
            string reason = arguments.RequireOption(AdminCliOptions.REASON).Trim();
            if (reason.Length > AdminContractCodes.MAX_REASON_LENGTH)
            {
                throw new CliUsageException("--reason은 " + AdminContractCodes.MAX_REASON_LENGTH + "자 이하여야 합니다.");
            }

            return reason;
        }

        /****************************************
        *                Output
        ****************************************/

        // 사람용 본문. --json이면 명령이 대신 WriteJson을 쓴다
        public void Line(string text = "")
        {
            Terminal.Out.WriteLine(text);
        }

        // 안내 문구. --json이면 표준 출력이 JSON만 담도록 표준 오류로 보낸다
        public void Note(string text)
        {
            (Json ? Terminal.Error : Terminal.Out).WriteLine(text);
        }

        public void Warn(string text)
        {
            Terminal.Error.WriteLine(text);
        }

        public void WriteJson(string raw)
        {
            Terminal.Out.WriteLine(raw);
        }

        public void WriteTable(CliTable table)
        {
            if (table.RowCount == 0)
            {
                Line("(없음)");
                return;
            }

            table.Write(Terminal.Out);
        }

        public void WriteFields(IReadOnlyList<(string Label, string Value)> fields)
        {
            WriteFields(Terminal.Out, fields);
        }

        /****************************************
        *                Writes
        ****************************************/

        /// <summary> 쓰기 요약을 보여 주고 확인을 받은 뒤 operationId를 정한다. 거절하면 false이며 서버에 아무것도 보내지 않는다 </summary>
        public bool TryBeginWrite(string title, IReadOnlyList<(string Label, string Value)> summary,
            out string operationId)
        {
            TextWriter writer = Json ? Terminal.Error : Terminal.Out;
            writer.WriteLine(title);
            WriteFields(writer, summary);
            if (!AssumeYes)
            {
                Terminal.Error.Write("계속할까요? (y/N): ");
                string answer = (Terminal.ReadLine() ?? string.Empty).Trim();
                if ((!string.Equals(answer, YES_SHORT, StringComparison.OrdinalIgnoreCase))
                    && (!string.Equals(answer, YES_LONG, StringComparison.OrdinalIgnoreCase)))
                {
                    Warn("취소했습니다. 서버에 보내지 않았습니다.");
                    operationId = string.Empty;
                    return false;
                }
            }

            operationId = ResolveOperationId();
            return true;
        }

        // 같은 operationId 재전송은 서버가 처음 결과를 돌려주므로, 응답을 잃은 쓰기는 이 ID로만 다시 보낸다
        private string ResolveOperationId()
        {
            if (IssuedOperationId != null)
            {
                return IssuedOperationId;
            }

            if (_operationIdOption != null)
            {
                IssuedOperationId = _operationIdOption;
                Note("operationId: " + IssuedOperationId + " (지정한 ID로 다시 보냅니다)");
                return IssuedOperationId;
            }

            IssuedOperationId = Guid.NewGuid().ToString("D");
            Note("operationId: " + IssuedOperationId);
            Note("  응답을 받지 못하면 같은 명령에 --operation-id " + IssuedOperationId + " 를 붙여 다시 실행하세요.");
            return IssuedOperationId;
        }

        /****************************************
        *                Clients
        ****************************************/

        public AdminApiClient CreateAnonymousClient()
        {
            return new AdminApiClient(_createHandler(), ApiBase, null);
        }

        /// <summary> 저장된 세션으로 인증 클라이언트를 만든다. 다른 API용 세션의 토큰은 보내지 않고, 만료된 세션은 지운다 </summary>
        public AdminApiClient CreateClient()
        {
            AdminSession? session = Sessions.Load();
            if (session == null)
            {
                throw new CliUnavailableException("로그인하지 않았습니다. 먼저 login --user <이름> 을 실행하세요.", false);
            }

            if (!string.Equals(session.Api, ApiBase, StringComparison.OrdinalIgnoreCase))
            {
                throw new CliUnavailableException("저장된 세션은 다른 API(" + session.Api + ")용입니다. 이 API(" + ApiBase
                    + ")로 다시 로그인하세요.", false);
            }

            if (Time.GetUtcNow().ToUnixTimeMilliseconds() >= session.ExpiresAtUnixMs)
            {
                Sessions.Delete();
                throw new CliUnavailableException("어드민 세션이 만료됐습니다(" + CliFormat.Time(session.ExpiresAtUnixMs)
                    + "). login으로 다시 로그인하세요.", false);
            }

            return CreateClient(session);
        }

        /// <summary> 세션을 받은 그 API로만 토큰을 보낸다. 로그아웃은 --api와 무관하게 이 경로를 쓴다 </summary>
        public AdminApiClient CreateClient(AdminSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            return new AdminApiClient(_createHandler(), session.Api, session.AccessToken);
        }

        private static void WriteFields(TextWriter writer, IReadOnlyList<(string Label, string Value)> fields)
        {
            int width = 0;
            foreach ((string label, string _) in fields)
            {
                width = Math.Max(width, CliTable.DisplayWidth(label));
            }

            foreach ((string label, string value) in fields)
            {
                writer.WriteLine(SUMMARY_INDENT + label + new string(' ', width - CliTable.DisplayWidth(label)) + "  "
                    + value);
            }
        }
    }
}
