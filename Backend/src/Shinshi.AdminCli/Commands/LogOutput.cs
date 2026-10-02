// System
using System;
using System.Collections.Generic;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 원장, 아이템 이력, operation, 감사 행 표. 여러 로그 명령이 같은 열 구성을 쓴다 </summary>
    internal static class LogOutput
    {
        /****************************************
        *                Methods
        ****************************************/

        public static void WriteLedger(CliContext context, IEnumerable<AdminLedgerEntry>? entries)
        {
            CliTable table = new CliTable("시각", "계정", "캐릭터", "자산", "변화", "잔액", "사유", "출처", "행위자",
                "operationId");
            foreach (AdminLedgerEntry entry in entries ?? Array.Empty<AdminLedgerEntry>())
            {
                table.Add(CliFormat.Time(entry.OccurredAtUnixMs), entry.AccountId, CliFormat.Text(entry.CharacterId),
                    Asset(entry.AssetKind, entry.AssetId, entry.BindingKind), CliFormat.Number(entry.Delta),
                    CliFormat.Number(entry.BalanceAfter), ContractCodeNames.EconomyReason(entry.Reason),
                    CliFormat.Text(entry.SourceType) + ":" + CliFormat.Text(entry.SourceId),
                    CliFormat.Text(entry.ActorKind) + ":" + CliFormat.Text(entry.ActorId), CliFormat.Text(entry.OperationId));
            }

            context.WriteTable(table);
        }

        public static void WriteTotals(CliContext context, IEnumerable<AdminLedgerReasonTotal>? totals)
        {
            CliTable table = new CliTable("사유", "자산", "합계", "건수");
            foreach (AdminLedgerReasonTotal total in totals ?? Array.Empty<AdminLedgerReasonTotal>())
            {
                table.Add(ContractCodeNames.EconomyReason(total.Reason) + " (" + total.Reason + ")",
                    Asset(total.AssetKind, total.AssetId, total.BindingKind), CliFormat.Text(total.DeltaTotal),
                    CliFormat.Number(total.Count));
            }

            context.WriteTable(table);
        }

        public static void WriteItemHistory(CliContext context, IEnumerable<AdminItemHistoryEntry>? entries)
        {
            CliTable table = new CliTable("아이템 ID", "seq", "시각", "이벤트", "소유자", "행위자", "operationId", "이전", "이후");
            foreach (AdminItemHistoryEntry entry in entries ?? Array.Empty<AdminItemHistoryEntry>())
            {
                table.Add(entry.ItemId, CliFormat.Number(entry.Seq), CliFormat.Time(entry.OccurredAtUnixMs), entry.EventKind,
                    CliFormat.Text(entry.OwnerId), CliFormat.Text(entry.ActorId), CliFormat.Text(entry.OperationId),
                    CliFormat.Json(entry.BeforeState), CliFormat.Json(entry.AfterState));
            }

            context.WriteTable(table);
        }

        public static void WriteOperations(CliContext context, IEnumerable<AdminOperationRecord>? operations)
        {
            CliTable table = new CliTable("actorId", "operationId", "종류", "상태", "영수증", "생성", "확정", "결과");
            foreach (AdminOperationRecord operation in operations ?? Array.Empty<AdminOperationRecord>())
            {
                table.Add(operation.ActorId, operation.OperationId, operation.OperationKind, operation.Status,
                    CliFormat.Text(operation.ReceiptId), CliFormat.Time(operation.CreatedAtUnixMs),
                    CliFormat.Time(operation.CommittedAtUnixMs), CliFormat.Json(operation.Result));
            }

            context.WriteTable(table);
        }

        public static void WriteAudit(CliContext context, IEnumerable<AdminAuditRecord>? records)
        {
            CliTable table = new CliTable("시각", "행위자", "역할", "행동", "대상", "사유", "operationId", "IP");
            foreach (AdminAuditRecord record in records ?? Array.Empty<AdminAuditRecord>())
            {
                string actor = record.ActorKind + (string.IsNullOrEmpty(record.AdminId) ? string.Empty : ":" + record.AdminId);
                table.Add(CliFormat.Time(record.OccurredAtUnixMs), actor,
                    record.AdminRole.HasValue ? ContractCodeNames.Role(record.AdminRole.Value) : CliFormat.NONE, record.Action,
                    record.TargetKind + ":" + CliFormat.Text(record.TargetId), record.Reason,
                    CliFormat.Text(record.OperationId), CliFormat.Text(record.RequestIpHashPrefix));
            }

            context.WriteTable(table);
        }

        public static void WriteAuditStates(CliContext context, IEnumerable<AdminAuditRecord>? records)
        {
            foreach (AdminAuditRecord record in records ?? Array.Empty<AdminAuditRecord>())
            {
                context.Line("  " + record.AuditId + " 이전: " + CliFormat.Json(record.BeforeState));
                context.Line("  " + record.AuditId + " 이후: " + CliFormat.Json(record.AfterState));
            }
        }

        public static void WriteNextCursor(CliContext context, string? nextCursor)
        {
            if (!string.IsNullOrEmpty(nextCursor))
            {
                context.Note("다음 페이지가 있습니다: --cursor " + nextCursor + " (끝까지 읽으려면 --all)");
            }
        }

        private static string Asset(string kind, string? assetId, string? bindingKind)
        {
            return string.IsNullOrEmpty(assetId) ? kind : kind + ":" + assetId + ":" + CliFormat.Text(bindingKind);
        }
    }
}
