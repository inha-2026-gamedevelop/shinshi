// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Data.Admin
{
    /// <summary> 운영자 감사 로그 시간순 조회. 감사 조회 자체는 감사하지 않는다 </summary>
    public sealed class PostgresAdminAuditReader
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresAdminAuditReader(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        public async Task<AdminAuditPage> ReadPageAsync(AdminAuditQuery query, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            if (query.Limit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(query));
            }
            if ((!string.IsNullOrEmpty(query.TargetId)) && string.IsNullOrEmpty(query.TargetKind))
            {
                throw new ArgumentException("Audit target ID requires a target kind.", nameof(query));
            }

            // 조건 문장은 고정 문자열만 붙이고 값은 모두 매개변수로 넘긴다. 조건이 없으면 시각 인덱스로 처음부터 읽는다
            List<string> conditions = new List<string> { "true" };
            DynamicParameters parameters = new DynamicParameters();
            if (query.AdminId.HasValue)
            {
                conditions.Add("audit.admin_id = @AdminId");
                parameters.Add("AdminId", query.AdminId.Value);
            }
            if (!string.IsNullOrEmpty(query.TargetKind))
            {
                conditions.Add("audit.target_kind = @TargetKind");
                parameters.Add("TargetKind", query.TargetKind);
            }
            if (!string.IsNullOrEmpty(query.TargetId))
            {
                conditions.Add("audit.target_id = @TargetId");
                parameters.Add("TargetId", query.TargetId);
            }
            if (query.From.HasValue)
            {
                conditions.Add("audit.occurred_at >= @From");
                parameters.Add("From", query.From.Value.UtcDateTime);
            }
            if (query.To.HasValue)
            {
                conditions.Add("audit.occurred_at < @To");
                parameters.Add("To", query.To.Value.UtcDateTime);
            }
            if (query.Cursor != null)
            {
                conditions.Add("(audit.occurred_at, audit.audit_id) > (@CursorAt, @CursorId)");
                parameters.Add("CursorAt", query.Cursor.At);
                parameters.Add("CursorId", query.Cursor.Id);
            }

            parameters.Add("Fetch", query.Limit + 1);

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            List<AdminAuditRow> rows = (await connection.QueryAsync<AdminAuditRow>(new CommandDefinition(
                "SELECT " + AdminAuditRow.SELECT_COLUMNS + " FROM admin_audit_log AS audit WHERE "
                + string.Join(" AND ", conditions) + " ORDER BY audit.occurred_at, audit.audit_id LIMIT @Fetch;",
                parameters, cancellationToken: cancellationToken))).ToList();

            bool hasMore = rows.Count > query.Limit;
            List<AdminAuditRow> page = hasMore ? rows.GetRange(0, query.Limit) : rows;
            string? nextCursor = hasMore
                ? new AdminLogCursor(page[page.Count - 1].OccurredAt, page[page.Count - 1].AuditId).Encode()
                : null;
            return new AdminAuditPage(page.Select(row => row.ToContract()).ToArray(), nextCursor);
        }
    }
}
