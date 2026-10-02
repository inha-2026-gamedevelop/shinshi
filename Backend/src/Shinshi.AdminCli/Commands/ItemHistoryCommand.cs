// System
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 아이템 하나의 생성부터 현재까지 이력 </summary>
    internal sealed class ItemHistoryCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly string[] G_Usage =
        {
            "item-history <itemId> --reason <사유>          아이템 이력 (seq 오름차순)"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "item-history";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public async Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(1, new[] { AdminCliOptions.REASON }, Array.Empty<string>());
            Guid itemId = CliFormat.ParseGuid(arguments.Positional(0, "itemId"), "itemId");
            CliQuery query = new CliQuery();
            LogOptions.AddReason(arguments, query);
            using AdminApiClient client = context.CreateClient();
            AdminApiResponse<AdminItemHistoryResponse> response = await client.GetAsync<AdminItemHistoryResponse>(
                query.Append(string.Format(AdminApiRoutes.ITEM_HISTORY_FORMAT, itemId.ToString("D"))), cancellationToken);
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
                return CliExitCodes.SUCCESS;
            }

            LogOutput.WriteItemHistory(context, response.Value.Entries);
            return CliExitCodes.SUCCESS;
        }
    }
}
