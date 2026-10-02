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
    /// <summary> 경제 원장 조회. 계정이나 출처(type, id) 중 하나로 범위를 좁혀야 하며 --all이면 cursor를 따라 끝까지 읽는다 </summary>
    internal sealed class LedgerCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ENTRIES_PROPERTY = "entries";

        private static readonly string[] G_Usage =
        {
            "ledger (--account <accountId> | --source-type <종류> --source-id <ID>) [--character <id>] [--asset-kind <종류>]",
            "       [--asset-id <ID>] [--economy-reason <번호|이름>] [--from <ISO>] [--to <ISO>] [--limit <n>] [--cursor <c>]",
            "       [--all] --reason <사유>                원장 조회. 민감 조회라 사유가 필요"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "ledger";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public async Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(0, new[]
            {
                AdminCliOptions.ACCOUNT, AdminCliOptions.SOURCE_TYPE, AdminCliOptions.SOURCE_ID, AdminCliOptions.CHARACTER,
                AdminCliOptions.ASSET_KIND, AdminCliOptions.ASSET_ID, AdminCliOptions.ECONOMY_REASON, AdminCliOptions.REASON
            }.Concat(LogOptions.RangeAndPage), new[] { CliArguments.FLAG_ALL });
            string? account = arguments.Option(AdminCliOptions.ACCOUNT);
            string? sourceType = arguments.Option(AdminCliOptions.SOURCE_TYPE);
            string? sourceId = arguments.Option(AdminCliOptions.SOURCE_ID);
            if ((account == null) && ((sourceType == null) || (sourceId == null)))
            {
                throw new CliUsageException("--account 또는 --source-type과 --source-id를 함께 주세요(전체 원장 조회는 막혀 있습니다).");
            }

            CliQuery query = new CliQuery()
                .Add(AdminLogQueryParameters.ACCOUNT_ID, OptionalGuid(account, AdminCliOptions.ACCOUNT))
                .Add(AdminLogQueryParameters.CHARACTER_ID,
                    OptionalGuid(arguments.Option(AdminCliOptions.CHARACTER), AdminCliOptions.CHARACTER))
                .Add(AdminLogQueryParameters.ASSET_KIND, arguments.Option(AdminCliOptions.ASSET_KIND))
                .Add(AdminLogQueryParameters.ASSET_ID, arguments.Option(AdminCliOptions.ASSET_ID))
                .Add(AdminLogQueryParameters.SOURCE_TYPE, sourceType)
                .Add(AdminLogQueryParameters.SOURCE_ID, sourceId);
            string? economyReason = arguments.Option(AdminCliOptions.ECONOMY_REASON);
            if (economyReason != null)
            {
                if (!ContractCodeNames.TryParseEconomyReason(economyReason.Trim(), out int code))
                {
                    throw new CliUsageException("--economy-reason은 번호나 EconomyReason 이름(예: ADMIN_GRANT)입니다: "
                        + economyReason);
                }

                query.Add(AdminLogQueryParameters.REASON_CODE, code);
            }

            LogOptions.AddRangeAndPage(arguments, query);
            LogOptions.AddReason(arguments, query);
            bool all = arguments.HasFlag(CliArguments.FLAG_ALL);
            using AdminApiClient client = context.CreateClient();
            List<AdminApiResponse<AdminLedgerPage>> pages = await CliPaging.ReadAsync<AdminLedgerPage>(client,
                AdminApiRoutes.LEDGER, query, all, page => page.NextCursor, cancellationToken);
            if (context.Json)
            {
                context.WriteJson(CliPaging.MergeJson(pages, ENTRIES_PROPERTY));
                return CliExitCodes.SUCCESS;
            }

            LogOutput.WriteLedger(context, pages.SelectMany(page => page.Value.Entries ?? Array.Empty<AdminLedgerEntry>()));
            context.Line();
            context.Line("같은 조건 전체 합계");
            // totals는 cursor 없이 부른 첫 페이지에만 있다. --cursor로 시작했으면 null이다
            if (pages[0].Value.Totals != null)
            {
                LogOutput.WriteTotals(context, pages[0].Value.Totals);
            }
            else
            {
                context.Line("(합계는 --cursor 없이 조회한 첫 페이지에만 있습니다)");
            }

            LogOutput.WriteNextCursor(context, pages[pages.Count - 1].Value.NextCursor);
            return CliExitCodes.SUCCESS;
        }

        private static string? OptionalGuid(string? text, string optionName)
        {
            return text == null ? null : CliFormat.ParseGuid(text, "--" + optionName).ToString("D");
        }
    }
}
