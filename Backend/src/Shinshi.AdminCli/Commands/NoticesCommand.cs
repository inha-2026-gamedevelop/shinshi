// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 런처와 게임 공지 목록, 작성, 조기 종료 </summary>
    internal sealed class NoticesCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string LIST   = "list";
        private const string CREATE = "create";
        private const string END    = "end";

        private static readonly string[] G_Targets =
        {
            ServiceNotice.TARGET_LAUNCHER, ServiceNotice.TARGET_GAME, ServiceNotice.TARGET_BOTH
        };

        private static readonly string[] G_Categories =
        {
            ServiceNotice.CATEGORY_NOTICE, ServiceNotice.CATEGORY_UPDATE, ServiceNotice.CATEGORY_EVENT,
            ServiceNotice.CATEGORY_MAINTENANCE
        };

        private static readonly string[] G_Usage =
        {
            "notices list                                   진행 중, 예정, 최근 7일 안에 끝난 공지",
            "notices create --title <제목> --body <본문> --target Launcher|Game|Both --severity info|warning|critical",
            "               [--category Notice|Update|Event|Maintenance] --ends <ISO8601 UTC>",
            "               [--starts <ISO8601 UTC>] --reason <사유>",
            "notices end <noticeId> --reason <사유>         공지를 일찍 내림"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "notices";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            string sub = arguments.Positional(0, "하위 명령(list, create, end)");
            CliArguments rest = arguments.SkipPositionals(1);
            return sub switch
            {
                LIST   => ListAsync(context, rest, cancellationToken),
                CREATE => CreateAsync(context, rest, cancellationToken),
                END    => EndAsync(context, rest, cancellationToken),
                _ => throw new CliUsageException("알 수 없는 하위 명령입니다: notices " + sub)
            };
        }

        public static int ParseSeverity(string text)
        {
            string trimmed = text.Trim();
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                && (value >= ServiceNotice.SEVERITY_INFO) && (value <= ServiceNotice.SEVERITY_CRITICAL))
            {
                return value;
            }

            for (int severity = ServiceNotice.SEVERITY_INFO; severity <= ServiceNotice.SEVERITY_CRITICAL; ++severity)
            {
                if (string.Equals(SeverityName(severity), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return severity;
                }
            }

            throw new CliUsageException("--severity는 info, warning, critical(또는 0~2)입니다: " + text);
        }

        public static string SeverityName(int severity)
        {
            return severity switch
            {
                ServiceNotice.SEVERITY_INFO     => "info",
                ServiceNotice.SEVERITY_WARNING  => "warning",
                ServiceNotice.SEVERITY_CRITICAL => "critical",
                _ => severity.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static async Task<int> ListAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(0, Array.Empty<string>(), Array.Empty<string>());
            using AdminApiClient client = context.CreateClient();
            AdminApiResponse<AdminNoticeListResponse> response =
                await client.GetAsync<AdminNoticeListResponse>(AdminApiRoutes.NOTICES, cancellationToken);
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
                return CliExitCodes.SUCCESS;
            }

            CliTable table = new CliTable("공지 ID", "상태", "분류", "대상", "중요도", "시작", "끝", "제목");
            foreach (AdminNoticeSummary notice in response.Value.Notices ?? Array.Empty<AdminNoticeSummary>())
            {
                table.Add(notice.NoticeId, notice.Status, notice.Category, notice.Target, SeverityName(notice.Severity),
                    CliFormat.Time(notice.StartsAtUnixMs), CliFormat.Time(notice.EndedAtUnixMs ?? notice.EndsAtUnixMs),
                    notice.Title);
            }

            context.WriteTable(table);
            return CliExitCodes.SUCCESS;
        }

        private static async Task<int> CreateAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(0, new[]
            {
                AdminCliOptions.TITLE, AdminCliOptions.BODY, AdminCliOptions.TARGET, AdminCliOptions.SEVERITY,
                AdminCliOptions.CATEGORY, AdminCliOptions.STARTS, AdminCliOptions.ENDS, AdminCliOptions.REASON
            }, Array.Empty<string>());
            string title = arguments.RequireOption(AdminCliOptions.TITLE).Trim();
            string body = arguments.RequireOption(AdminCliOptions.BODY);
            if (title.Length > AdminNoticeCreateRequest.MAX_TITLE_LENGTH)
            {
                throw new CliUsageException("--title은 " + AdminNoticeCreateRequest.MAX_TITLE_LENGTH + "자 이하입니다.");
            }

            if (body.Length > AdminNoticeCreateRequest.MAX_BODY_LENGTH)
            {
                throw new CliUsageException("--body는 " + AdminNoticeCreateRequest.MAX_BODY_LENGTH + "자 이하입니다.");
            }

            string target = ParseTarget(arguments.RequireOption(AdminCliOptions.TARGET));
            int severity = ParseSeverity(arguments.RequireOption(AdminCliOptions.SEVERITY));
            string category = ParseCategory(arguments.Option(AdminCliOptions.CATEGORY));
            long endsAt = CliFormat.ParseUtc(arguments.RequireOption(AdminCliOptions.ENDS), AdminCliOptions.ENDS);
            string? startsText = arguments.Option(AdminCliOptions.STARTS);
            long? startsAt = startsText == null ? null : CliFormat.ParseUtc(startsText, AdminCliOptions.STARTS);
            if (startsAt.HasValue && (endsAt <= startsAt.Value))
            {
                throw new CliUsageException("--ends는 --starts보다 뒤여야 합니다.");
            }

            string reason = CliContext.RequireReason(arguments);
            using AdminApiClient client = context.CreateClient();
            if (!context.TryBeginWrite("공지를 만듭니다.", new List<(string, string)>
                {
                    ("제목", title),
                    ("본문", body),
                    ("대상", target),
                    ("중요도", SeverityName(severity)),
                    ("분류", category),
                    ("시작", startsAt.HasValue ? CliFormat.Time(startsAt.Value) : "저장 즉시"),
                    ("끝", CliFormat.Time(endsAt)),
                    ("사유", reason)
                }, out string operationId))
            {
                return CliExitCodes.USAGE;
            }

            AdminApiResponse<OperationResult> response = await client.SendAsync<OperationResult>(HttpMethod.Post,
                AdminApiRoutes.NOTICES,
                new AdminNoticeCreateRequest(operationId, reason, title, body, target, severity, startsAt, endsAt, category), true,
                cancellationToken);
            CommandOutput.WriteOperation(context, response, "공지를 만들었습니다. 영수증 ID가 공지 ID입니다.");
            return CliExitCodes.SUCCESS;
        }

        private static async Task<int> EndAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(1, new[] { AdminCliOptions.REASON }, Array.Empty<string>());
            Guid noticeId = CliFormat.ParseGuid(arguments.Positional(0, "noticeId"), "noticeId");
            string reason = CliContext.RequireReason(arguments);
            using AdminApiClient client = context.CreateClient();
            if (!context.TryBeginWrite("공지를 내립니다.", new List<(string, string)>
                {
                    ("공지 ID", noticeId.ToString("D")),
                    ("사유", reason)
                }, out string operationId))
            {
                return CliExitCodes.USAGE;
            }

            AdminApiResponse<OperationResult> response = await client.SendAsync<OperationResult>(HttpMethod.Post,
                string.Format(AdminApiRoutes.NOTICE_END_FORMAT, noticeId.ToString("D")),
                new AdminNoticeEndRequest(operationId, reason), true, cancellationToken);
            CommandOutput.WriteOperation(context, response, "공지를 내렸습니다.");
            return CliExitCodes.SUCCESS;
        }

        // 옵션을 생략하면 기본 분류(Notice)다
        private static string ParseCategory(string? text)
        {
            if (text == null)
            {
                return ServiceNotice.CATEGORY_NOTICE;
            }

            foreach (string category in G_Categories)
            {
                if (string.Equals(category, text.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return category;
                }
            }

            throw new CliUsageException("--category는 " + string.Join(", ", G_Categories) + " 중 하나입니다: " + text);
        }

        private static string ParseTarget(string text)
        {
            foreach (string target in G_Targets)
            {
                if (string.Equals(target, text.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return target;
                }
            }

            throw new CliUsageException("--target은 " + string.Join(", ", G_Targets) + " 중 하나입니다: " + text);
        }
    }
}
