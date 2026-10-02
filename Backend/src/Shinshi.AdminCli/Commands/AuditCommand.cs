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
    /// <summary> 운영자 감사 로그 조회. 시각 오름차순이며 --all이면 cursor를 따라 끝까지 읽는다 </summary>
    internal sealed class AuditCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ENTRIES_PROPERTY = "entries";

        private static readonly string[] G_Usage =
        {
            "audit [--admin <adminId>] [--target-kind <종류> [--target-id <ID>]] [--from <ISO>] [--to <ISO>]",
            "      [--limit <n>] [--cursor <c>] [--all]     운영자 감사 로그"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "audit";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public async Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(0, new[]
            {
                AdminCliOptions.ADMIN, AdminCliOptions.TARGET_KIND, AdminCliOptions.TARGET_ID
            }.Concat(LogOptions.RangeAndPage), new[] { CliArguments.FLAG_ALL });
            string? admin = arguments.Option(AdminCliOptions.ADMIN);
            string? targetKind = arguments.Option(AdminCliOptions.TARGET_KIND);
            string? targetId = arguments.Option(AdminCliOptions.TARGET_ID);
            if ((targetId != null) && (targetKind == null))
            {
                throw new CliUsageException("--target-id는 --target-kind와 함께 씁니다.");
            }

            CliQuery query = new CliQuery()
                .Add(AdminLogQueryParameters.ADMIN_ID,
                    admin == null ? null : CliFormat.ParseGuid(admin, "--" + AdminCliOptions.ADMIN).ToString("D"))
                .Add(AdminLogQueryParameters.TARGET_KIND, targetKind)
                .Add(AdminLogQueryParameters.TARGET_ID, targetId);
            LogOptions.AddRangeAndPage(arguments, query);
            bool all = arguments.HasFlag(CliArguments.FLAG_ALL);
            using AdminApiClient client = context.CreateClient();
            List<AdminApiResponse<AdminAuditPage>> pages = await CliPaging.ReadAsync<AdminAuditPage>(client,
                AdminApiRoutes.AUDIT, query, all, page => page.NextCursor, cancellationToken);
            if (context.Json)
            {
                context.WriteJson(CliPaging.MergeJson(pages, ENTRIES_PROPERTY));
                return CliExitCodes.SUCCESS;
            }

            LogOutput.WriteAudit(context, pages.SelectMany(page => page.Value.Entries ?? Array.Empty<AdminAuditRecord>()));
            LogOutput.WriteNextCursor(context, pages[pages.Count - 1].Value.NextCursor);
            return CliExitCodes.SUCCESS;
        }
    }
}
