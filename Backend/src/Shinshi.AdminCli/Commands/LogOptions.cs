// System
using System.Collections.Generic;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 로그 조회의 기간, 페이지, 감사 사유 옵션을 조회 문자열로 옮긴다 </summary>
    internal static class LogOptions
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MIN_LIMIT = 1;

        private static readonly string[] G_RangeAndPage =
        {
            AdminCliOptions.FROM, AdminCliOptions.TO, AdminCliOptions.LIMIT, AdminCliOptions.CURSOR
        };

        /****************************************
        *              Properties
        ****************************************/

        // --from, --to, --limit, --cursor
        public static IReadOnlyList<string> RangeAndPage => G_RangeAndPage;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 시각은 ISO 8601(UTC 오프셋 포함)로 받아 서버의 유닉스 ms로 바꾼다. from은 포함, to는 제외다 </summary>
        public static void AddRangeAndPage(CliArguments arguments, CliQuery query)
        {
            string? from = arguments.Option(AdminCliOptions.FROM);
            string? to = arguments.Option(AdminCliOptions.TO);
            long? fromUnixMs = from == null ? null : CliFormat.ParseUtc(from, AdminCliOptions.FROM);
            long? toUnixMs = to == null ? null : CliFormat.ParseUtc(to, AdminCliOptions.TO);
            if (fromUnixMs.HasValue && toUnixMs.HasValue && (toUnixMs.Value <= fromUnixMs.Value))
            {
                throw new CliUsageException("--to는 --from보다 뒤여야 합니다.");
            }

            query.Add(AdminLogQueryParameters.FROM_UNIX_MS, fromUnixMs);
            query.Add(AdminLogQueryParameters.TO_UNIX_MS, toUnixMs);
            AddPage(arguments, query);
        }

        public static void AddPage(CliArguments arguments, CliQuery query)
        {
            string? limitText = arguments.Option(AdminCliOptions.LIMIT);
            if (limitText != null)
            {
                int limit = CliFormat.ParseInt(limitText, AdminCliOptions.LIMIT);
                if ((limit < MIN_LIMIT) || (limit > AdminLogQueryParameters.MAX_LIMIT))
                {
                    throw new CliUsageException("--limit은 " + MIN_LIMIT + "에서 " + AdminLogQueryParameters.MAX_LIMIT + " 사이입니다.");
                }

                query.Add(AdminLogQueryParameters.LIMIT, limit);
            }

            query.Add(AdminLogQueryParameters.CURSOR, arguments.Option(AdminCliOptions.CURSOR));
        }

        /// <summary> 민감 조회 감사 사유(필수). 원장 사유 코드 필터는 reasonCode로 따로 보낸다 </summary>
        public static void AddReason(CliArguments arguments, CliQuery query)
        {
            query.Add(AdminLogQueryParameters.REASON, CliContext.RequireReason(arguments));
        }
    }
}
