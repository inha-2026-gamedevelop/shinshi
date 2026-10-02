// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 복구 대상 세션 목록. --state가 없으면 격리, 정산 지연, 서버 소실 세션이고 있으면 그 상태의 세션이다 </summary>
    internal sealed class SessionsCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SESSIONS_PROPERTY = "sessions";

        private static readonly string[] G_Usage =
        {
            "sessions [--state <상태>] [--limit <n>] [--cursor <c>] [--all]",
            "                                               복구 대상 세션 (상태 예: Running, Settling, RecoveryPending)"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "sessions";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public async Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(0, new[] { AdminCliOptions.STATE, AdminCliOptions.LIMIT, AdminCliOptions.CURSOR },
                new[] { CliArguments.FLAG_ALL });
            CliQuery query = new CliQuery().Add(AdminApiRoutes.STATE_PARAMETER, arguments.Option(AdminCliOptions.STATE));
            LogOptions.AddPage(arguments, query);
            bool all = arguments.HasFlag(CliArguments.FLAG_ALL);
            using AdminApiClient client = context.CreateClient();
            List<AdminApiResponse<AdminRecoverySessionPage>> pages = await CliPaging.ReadAsync<AdminRecoverySessionPage>(
                client, AdminApiRoutes.SESSIONS, query, all, page => page.NextCursor, cancellationToken);
            if (context.Json)
            {
                context.WriteJson(CliPaging.MergeJson(pages, SESSIONS_PROPERTY));
                return CliExitCodes.SUCCESS;
            }

            CliTable table = new CliTable("세션 ID", "상태", "결과", "모드", "생성", "서버", "생존", "격리", "정산 지연", "서버 소실",
                "복구 실패");
            foreach (AdminRecoverySession entry in pages.SelectMany(
                page => page.Value.Sessions ?? Array.Empty<AdminRecoverySession>()))
            {
                AdminSessionSummary session = entry.Session;
                AdminServerInstanceState? instance = entry.Instance;
                table.Add(session.SessionId, session.State, CliFormat.Text(session.Result), session.Mode,
                    CliFormat.Time(session.CreatedAtUnixMs), CliFormat.Text(session.InstanceId),
                    instance == null ? CliFormat.NONE : CliFormat.YesNo(instance.AssignedBootAlive),
                    CliFormat.YesNo(entry.Quarantined), CliFormat.YesNo(entry.SettlingStale),
                    CliFormat.YesNo(entry.InstanceLost), CliFormat.Number(session.RecoveryFailures));
            }

            context.WriteTable(table);
            if (pages.Any(page => page.Value.LivenessDeferred))
            {
                context.Warn("서버 생존 판정 보류 중입니다(API 시작 직후나 DB 단절 직후). 서버 소실은 모두 아니오로 보이며"
                    + " service-cancel, force-close는 503으로 거절됩니다. 잠시 뒤 다시 조회하세요.");
            }

            LogOutput.WriteNextCursor(context, pages[pages.Count - 1].Value.NextCursor);
            return CliExitCodes.SUCCESS;
        }
    }
}
