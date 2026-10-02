// System
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 작업 하나의 결과와 그 작업이 같은 트랜잭션에 남긴 원장, 아이템 이력, outbox, 감사 행 </summary>
    internal sealed class OperationCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly string[] G_Usage =
        {
            "operation <actorId> <operationId> --reason <사유>",
            "                                               작업 결과와 같은 트랜잭션의 원장, 이력, outbox, 감사"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "operation";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public async Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(2, new[] { AdminCliOptions.REASON }, Array.Empty<string>());
            Guid actorId = CliFormat.ParseGuid(arguments.Positional(0, "actorId"), "actorId");
            Guid operationId = CliFormat.ParseGuid(arguments.Positional(1, "operationId"), "operationId");
            CliQuery query = new CliQuery();
            LogOptions.AddReason(arguments, query);
            using AdminApiClient client = context.CreateClient();
            AdminApiResponse<AdminOperationDetailResponse> response = await client.GetAsync<AdminOperationDetailResponse>(
                query.Append(string.Format(AdminApiRoutes.OPERATION_FORMAT, actorId.ToString("D"),
                    operationId.ToString("D"))), cancellationToken);
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
                return CliExitCodes.SUCCESS;
            }

            AdminOperationDetailResponse detail = response.Value;
            AdminOperationRecord operation = detail.Operation;
            context.WriteFields(new List<(string, string)>
            {
                ("actorId", operation.ActorId),
                ("operationId", operation.OperationId),
                ("종류", operation.OperationKind),
                ("상태", operation.Status),
                ("요청 해시", CliFormat.Text(operation.RequestHash)),
                ("revision", CliFormat.Number(operation.CommittedRevision)),
                ("영수증 ID", CliFormat.Text(operation.ReceiptId)),
                ("생성, 확정", CliFormat.Time(operation.CreatedAtUnixMs) + ", " + CliFormat.Time(operation.CommittedAtUnixMs)),
                ("결과", CliFormat.Json(operation.Result))
            });

            context.Line();
            context.Line("원장");
            LogOutput.WriteLedger(context, detail.Ledger);

            context.Line();
            context.Line("아이템 이력");
            LogOutput.WriteItemHistory(context, detail.ItemHistory);

            context.Line();
            context.Line("outbox");
            CliTable outbox = new CliTable("이벤트 ID", "대상", "소비자", "종류", "생성", "전달", "재시도", "payload");
            foreach (AdminOutboxEntry entry in detail.Outbox ?? Array.Empty<AdminOutboxEntry>())
            {
                outbox.Add(entry.EventId, entry.AggregateId, entry.ConsumerKey, entry.EventKind,
                    CliFormat.Time(entry.CreatedAtUnixMs), CliFormat.Time(entry.DeliveredAtUnixMs),
                    CliFormat.Number(entry.Retries), CliFormat.Json(entry.Payload));
            }

            context.WriteTable(outbox);

            context.Line();
            context.Line("감사");
            LogOutput.WriteAudit(context, detail.Audit);
            LogOutput.WriteAuditStates(context, detail.Audit);
            return CliExitCodes.SUCCESS;
        }
    }
}
