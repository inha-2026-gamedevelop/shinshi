// System
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Admin
{
    /// <summary> service_state 단일 행과 service_notices 저장소. 읽기는 자체 연결, 쓰기는 어드민 작업 트랜잭션에서 감사와 함께 한다 </summary>
    public sealed class PostgresServiceStateRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        // 캐시가 한 번에 들고 있는 공지 수. 노출 중인 공지가 이보다 많은 운영은 상정하지 않는다
        private const int MAX_CACHED_NOTICES = 100;

        // 어드민 목록 한 번에 주는 공지 수
        private const int MAX_LISTED_NOTICES = 200;

        private const string STATE_COLUMNS =
            """
            maintenance AS "Maintenance", disabled_features AS "DisabledFeatures",
            allowed_build_ids AS "AllowedBuildIds", revision AS "Revision", updated_at AS "UpdatedAt",
            updated_by AS "UpdatedBy"
            """;

        private const string NOTICE_COLUMNS =
            """
            notice_id AS "NoticeId", title AS "Title", body AS "Body", target AS "Target", severity AS "Severity",
            starts_at AS "StartsAt", ends_at AS "EndsAt", created_by AS "CreatedBy", created_at AS "CreatedAt",
            ended_at AS "EndedAt", ended_by AS "EndedBy"
            """;

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresServiceStateRepository(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> 상태 행과 아직 끝나지 않은 공지(예정 포함)를 같은 스냅샷에서 읽는다. 행이 없으면 마이그레이션 누락이므로 예외다 </summary>
        public async Task<ServiceStateReadResult> ReadCurrentAsync(CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, cancellationToken);
            await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);
            ServiceStateRecord state = await ReadStateAsync(connection, transaction, false, cancellationToken)
                ?? throw new InvalidOperationException("The service_state row is missing.");
            IEnumerable<NoticeRow> notices = await connection.QueryAsync<NoticeRow>(new CommandDefinition(
                "SELECT " + NOTICE_COLUMNS +
                """
                 FROM service_notices
                WHERE ended_at IS NULL AND ends_at > clock_timestamp()
                ORDER BY severity DESC, starts_at DESC, notice_id
                LIMIT @Limit;
                """, new { Limit = MAX_CACHED_NOTICES }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return new ServiceStateReadResult(state, notices.Select(row => row.ToRecord()).ToList());
        }

        /// <summary> 진행 중, 예정, endedSince 이후에 끝난 공지를 시작 시각 내림차순으로 준다 </summary>
        public async Task<IReadOnlyList<ServiceNoticeRecord>> ListNoticesAsync(DateTimeOffset now,
            DateTimeOffset endedSince, CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            IEnumerable<NoticeRow> rows = await connection.QueryAsync<NoticeRow>(new CommandDefinition(
                "SELECT " + NOTICE_COLUMNS +
                """
                 FROM service_notices
                WHERE (ended_at IS NULL AND ends_at > @Now) OR (COALESCE(ended_at, ends_at) >= @EndedSince)
                ORDER BY starts_at DESC, notice_id
                LIMIT @Limit;
                """, new { Now = now.UtcDateTime, EndedSince = endedSince.UtcDateTime, Limit = MAX_LISTED_NOTICES },
                cancellationToken: cancellationToken));
            return rows.Select(row => row.ToRecord()).ToList();
        }

        /// <summary> 행을 잠그고 revision이 같을 때만 저장한다. 저장하면 revision을 하나 올리고 같은 트랜잭션에 감사 행을 남긴다 </summary>
        public async Task<ServiceStateUpdateOutcome> UpdateAsync(AdminOperationContext context, ServiceStateChange change,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(change);
            NpgsqlConnection connection = OperationTransactionGuard.Require(context.Execution,
                out NpgsqlTransaction transaction);
            ServiceStateRecord current = await ReadStateAsync(connection, transaction, true, cancellationToken)
                ?? throw new InvalidOperationException("The service_state row is missing.");
            if (current.Revision != change.ExpectedRevision)
            {
                return ServiceStateUpdateOutcome.Stale(current);
            }

            StateRow updated = await connection.QuerySingleAsync<StateRow>(new CommandDefinition(
                """
                UPDATE service_state
                SET maintenance = @Maintenance, disabled_features = @DisabledFeatures,
                    allowed_build_ids = @AllowedBuildIds, revision = revision + 1, updated_at = clock_timestamp(),
                    updated_by = @AdminId
                WHERE id = 1
                RETURNING
                """ + " " + STATE_COLUMNS + ";",
                new
                {
                    change.Maintenance,
                    DisabledFeatures = change.DisabledFeatures.ToArray(),
                    AllowedBuildIds  = change.AllowedBuildIds.ToArray(),
                    context.Admin.AdminId
                }, transaction, cancellationToken: cancellationToken));
            ServiceStateRecord after = updated.ToRecord();
            await context.AuditAsync(AdminServiceStateAuditActions.TARGET_SERVICE_STATE,
                AdminServiceStateAuditActions.SERVICE_STATE_ID, ToAuditState(current), ToAuditState(after),
                cancellationToken);
            return ServiceStateUpdateOutcome.Updated(after);
        }

        /// <summary> 공지를 넣고 감사 행을 남긴다. 끝 시각이 시작 시각 이하이거나 DB 시각 이하(이미 끝난 공지)이면 쓰지 않고 null이다.
        /// 시각 판정을 효과 안의 DB 시각으로 하므로 거절도 작업 결과로 저장되고, 확정된 공지의 재전송은 만료 뒤에도 처음 결과를 받는다 </summary>
        public async Task<ServiceNoticeRecord?> CreateNoticeAsync(AdminOperationContext context, ServiceNoticeDraft draft,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(draft);
            DateTimeOffset startsAt = draft.StartsAt ?? context.Execution.ServerTime;
            if ((draft.EndsAt <= startsAt) || (draft.EndsAt <= context.Execution.ServerTime))
            {
                return null;
            }

            NpgsqlConnection connection = OperationTransactionGuard.Require(context.Execution,
                out NpgsqlTransaction transaction);
            NoticeRow row = await connection.QuerySingleAsync<NoticeRow>(new CommandDefinition(
                """
                INSERT INTO service_notices (notice_id, title, body, target, severity, starts_at, ends_at, created_by)
                VALUES (@NoticeId, @Title, @Body, @Target, @Severity, @StartsAt, @EndsAt, @AdminId)
                RETURNING
                """ + " " + NOTICE_COLUMNS + ";",
                new
                {
                    draft.NoticeId,
                    draft.Title,
                    draft.Body,
                    draft.Target,
                    draft.Severity,
                    StartsAt = startsAt.UtcDateTime,
                    EndsAt   = draft.EndsAt.UtcDateTime,
                    context.Admin.AdminId
                }, transaction, cancellationToken: cancellationToken));
            ServiceNoticeRecord created = row.ToRecord();
            await context.AuditAsync(AdminServiceStateAuditActions.TARGET_NOTICE, created.NoticeId.ToString("D"), null,
                ToAuditNotice(created), cancellationToken);
            return created;
        }

        /// <summary> 아직 노출 기간 안이거나 예정인 공지만 내린다. 이미 내렸거나 기간이 지났으면 쓰지 않는다 </summary>
        public async Task<EServiceNoticeEndStatus> EndNoticeAsync(AdminOperationContext context, Guid noticeId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            NpgsqlConnection connection = OperationTransactionGuard.Require(context.Execution,
                out NpgsqlTransaction transaction);
            NoticeRow? row = await connection.QuerySingleOrDefaultAsync<NoticeRow>(new CommandDefinition(
                "SELECT " + NOTICE_COLUMNS + " FROM service_notices WHERE notice_id = @NoticeId FOR UPDATE;",
                new { NoticeId = noticeId }, transaction, cancellationToken: cancellationToken));
            if (row == null)
            {
                return EServiceNoticeEndStatus.NotFound;
            }

            NoticeRow? ended = await connection.QuerySingleOrDefaultAsync<NoticeRow>(new CommandDefinition(
                """
                UPDATE service_notices SET ended_at = clock_timestamp(), ended_by = @AdminId
                WHERE notice_id = @NoticeId AND ended_at IS NULL AND ends_at > clock_timestamp()
                RETURNING
                """ + " " + NOTICE_COLUMNS + ";",
                new { NoticeId = noticeId, context.Admin.AdminId }, transaction, cancellationToken: cancellationToken));
            if (ended == null)
            {
                return EServiceNoticeEndStatus.AlreadyEnded;
            }

            await context.AuditAsync(AdminServiceStateAuditActions.TARGET_NOTICE, noticeId.ToString("D"),
                ToAuditNotice(row.ToRecord()), ToAuditNotice(ended.ToRecord()), cancellationToken);
            return EServiceNoticeEndStatus.Ended;
        }

        private static async Task<ServiceStateRecord?> ReadStateAsync(NpgsqlConnection connection,
            NpgsqlTransaction transaction, bool forUpdate, CancellationToken cancellationToken)
        {
            StateRow? row = await connection.QuerySingleOrDefaultAsync<StateRow>(new CommandDefinition(
                "SELECT " + STATE_COLUMNS + " FROM service_state WHERE id = 1" + (forUpdate ? " FOR UPDATE;" : ";"),
                transaction: transaction, cancellationToken: cancellationToken));
            return row?.ToRecord();
        }

        private static object ToAuditState(ServiceStateRecord state)
        {
            return new
            {
                state.Maintenance,
                state.DisabledFeatures,
                state.AllowedBuildIds,
                state.Revision
            };
        }

        private static object ToAuditNotice(ServiceNoticeRecord notice)
        {
            return new
            {
                notice.Title,
                notice.Target,
                notice.Severity,
                StartsAtUnixMs = notice.StartsAt.ToUnixTimeMilliseconds(),
                EndsAtUnixMs   = notice.EndsAt.ToUnixTimeMilliseconds(),
                EndedAtUnixMs  = notice.EndedAt?.ToUnixTimeMilliseconds()
            };
        }

        private static DateTimeOffset ToOffset(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class StateRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public bool Maintenance { get; set; }
            public string[] DisabledFeatures { get; set; } = Array.Empty<string>();
            public string[] AllowedBuildIds { get; set; } = Array.Empty<string>();
            public long Revision { get; set; }
            public DateTime UpdatedAt { get; set; }
            public Guid? UpdatedBy { get; set; }

            /****************************************
            *                Methods
            ****************************************/

            public ServiceStateRecord ToRecord()
            {
                return new ServiceStateRecord(Maintenance, DisabledFeatures, AllowedBuildIds, Revision,
                    ToOffset(UpdatedAt), UpdatedBy);
            }
        }

        private sealed class NoticeRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid NoticeId { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Body { get; set; } = string.Empty;
            public string Target { get; set; } = string.Empty;
            public int Severity { get; set; }
            public DateTime StartsAt { get; set; }
            public DateTime EndsAt { get; set; }
            public Guid CreatedBy { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? EndedAt { get; set; }
            public Guid? EndedBy { get; set; }

            /****************************************
            *                Methods
            ****************************************/

            public ServiceNoticeRecord ToRecord()
            {
                return new ServiceNoticeRecord(NoticeId, Title, Body, Target, Severity, ToOffset(StartsAt),
                    ToOffset(EndsAt), CreatedBy, ToOffset(CreatedAt), EndedAt.HasValue ? ToOffset(EndedAt.Value) : null,
                    EndedBy);
            }
        }
    }
}
