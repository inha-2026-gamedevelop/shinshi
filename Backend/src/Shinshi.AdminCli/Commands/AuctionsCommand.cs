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
    /// <summary> 경매 단계 출정 목록. --state가 없으면 멈춘 경매(기한 경과 또는 서버 소실)이고 Open, Closing이면 그 품목 상태의 출정이다 </summary>
    internal sealed class AuctionsCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string LIST              = "list";
        private const string SESSIONS_PROPERTY = "sessions";

        private static readonly string[] G_Usage =
        {
            "auctions list [--state Open|Closing] [--limit <n>] [--cursor <c>] [--all]",
            "                                               멈춘 경매 출정(기한 경과, 서버 소실). 닫기는 자동 복구가 함"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "auctions";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            string sub = arguments.Positional(0, "하위 명령(list)");
            return sub switch
            {
                LIST => ListAsync(context, arguments.SkipPositionals(1), cancellationToken),
                _ => throw new CliUsageException("알 수 없는 하위 명령입니다: auctions " + sub)
            };
        }

        private static async Task<int> ListAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(0, new[] { AdminCliOptions.STATE, AdminCliOptions.LIMIT, AdminCliOptions.CURSOR },
                new[] { CliArguments.FLAG_ALL });
            CliQuery query = new CliQuery().Add(AdminApiRoutes.STATE_PARAMETER, arguments.Option(AdminCliOptions.STATE));
            LogOptions.AddPage(arguments, query);
            bool all = arguments.HasFlag(CliArguments.FLAG_ALL);
            using AdminApiClient client = context.CreateClient();
            List<AdminApiResponse<AdminAuctionPage>> pages = await CliPaging.ReadAsync<AdminAuctionPage>(client,
                AdminApiRoutes.AUCTIONS, query, all, page => page.NextCursor, cancellationToken);
            if (context.Json)
            {
                context.WriteJson(CliPaging.MergeJson(pages, SESSIONS_PROPERTY));
                return CliExitCodes.SUCCESS;
            }

            CliTable table = new CliTable("세션 ID", "서버", "경매 기한", "기한 경과", "서버 소실", "품목", "품목 상태", "입찰 순번",
                "품목 마감", "예약 엽전");
            foreach (AdminAuctionSession entry in pages.SelectMany(
                page => page.Value.Sessions ?? Array.Empty<AdminAuctionSession>()))
            {
                table.Add(entry.SessionId, entry.InstanceId, CliFormat.Time(entry.AuctionEndsAtUnixMs),
                    CliFormat.YesNo(entry.Overdue), CliFormat.YesNo(entry.InstanceLost), CliFormat.Text(entry.DefinitionId),
                    CliFormat.Text(entry.ItemState), CliFormat.Number(entry.BidSeq),
                    entry.HardEndsAtUnixMs == 0 ? CliFormat.NONE : CliFormat.Time(entry.HardEndsAtUnixMs),
                    CliFormat.Number(entry.ReservedCurrency));
            }

            context.WriteTable(table);
            if (pages.Any(page => page.Value.LivenessDeferred))
            {
                context.Warn("서버 생존 판정 보류 중입니다(API 시작 직후나 DB 단절 직후). 서버 소실은 모두 아니오로 보입니다."
                    + " 잠시 뒤 다시 조회하세요.");
            }

            LogOutput.WriteNextCursor(context, pages[pages.Count - 1].Value.NextCursor);
            return CliExitCodes.SUCCESS;
        }
    }
}
