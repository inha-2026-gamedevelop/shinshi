// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 원장, 아이템 이력, 세션 재생, 작업, 감사 로그 조회(Viewer). 감사 로그를 뺀 네 조회는 계정 상세처럼 민감 조회라
    /// reason 조회 문자열이 필수이고, 데이터를 읽기 전에 감사 행을 자체 트랜잭션으로 커밋한다(감사 없이 내보내지 않는다).
    /// 대상이 없어 404가 되는 조회도 시도로 감사된다. 계정 데이터가 실리므로 응답은 캐시하지 않는다 </summary>
    public sealed class AdminLogsController : AdminControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SOURCE_SEPARATOR    = ":";
        private const string OPERATION_SEPARATOR = "/";

        private readonly PostgresAdminLedgerReader _ledger;
        private readonly PostgresAdminTraceReader _trace;
        private readonly PostgresAdminAuditReader _audit;
        private readonly IAdminAuditWriter _auditWriter;

        /****************************************
        *                Methods
        ****************************************/

        public AdminLogsController(PostgresAdminLedgerReader ledger, PostgresAdminTraceReader trace,
            PostgresAdminAuditReader audit, IAdminAuditWriter auditWriter)
        {
            _ledger      = ledger;
            _trace       = trace;
            _audit       = audit;
            _auditWriter = auditWriter;
        }

        /// <summary> accountId 또는 sourceType과 sourceId가 없으면 400이다(원장 전체 스캔 방지). accountId가 있으면 그 계정,
        /// 없으면 원인(sourceType:sourceId)을 대상으로 감사한다 </summary>
        [HttpGet(AdminApiRoutes.LEDGER)]
        public async Task<ActionResult<AdminLedgerPage>> Ledger(
            [FromQuery(Name = AdminLogQueryParameters.REASON)] string? reason,
            [FromQuery(Name = AdminLogQueryParameters.ACCOUNT_ID)] Guid? accountId,
            [FromQuery(Name = AdminLogQueryParameters.CHARACTER_ID)] Guid? characterId,
            [FromQuery(Name = AdminLogQueryParameters.ASSET_KIND)] string? assetKind,
            [FromQuery(Name = AdminLogQueryParameters.ASSET_ID)] string? assetId,
            [FromQuery(Name = AdminLogQueryParameters.REASON_CODE)] int? reasonCode,
            [FromQuery(Name = AdminLogQueryParameters.SOURCE_TYPE)] string? sourceType,
            [FromQuery(Name = AdminLogQueryParameters.SOURCE_ID)] string? sourceId,
            [FromQuery(Name = AdminLogQueryParameters.FROM_UNIX_MS)] long? fromUnixMs,
            [FromQuery(Name = AdminLogQueryParameters.TO_UNIX_MS)] long? toUnixMs,
            [FromQuery(Name = AdminLogQueryParameters.CURSOR)] string? cursor,
            [FromQuery(Name = AdminLogQueryParameters.LIMIT)] int? limit,
            CancellationToken cancellationToken)
        {
            string auditReason = AdminLogRequests.ReadAuditReason(reason);
            (DateTimeOffset? from, DateTimeOffset? to) = AdminLogRequests.ReadRange(fromUnixMs, toUnixMs);
            AdminLedgerQuery query = new AdminLedgerQuery
            {
                AccountId   = accountId,
                CharacterId = characterId,
                AssetKind   = assetKind,
                AssetId     = assetId,
                ReasonCode  = reasonCode,
                SourceType  = sourceType,
                SourceId    = sourceId,
                From        = from,
                To          = to,
                Cursor      = AdminLogRequests.ReadCursor(cursor),
                Limit       = AdminLogRequests.ReadLimit(limit)
            };
            if (!query.HasIndexedScope)
            {
                throw new AdminRequestException("Ledger query requires accountId or both sourceType and sourceId.");
            }

            (string targetKind, string targetId) = accountId.HasValue
                ? (AdminAccountAuditActions.TARGET_ACCOUNT, accountId.Value.ToString("D"))
                : (AdminLogAuditActions.TARGET_LEDGER_SOURCE, sourceType + SOURCE_SEPARATOR + sourceId);
            if (targetId.Length > AdminAuditEntry.MAX_TARGET_ID_LENGTH)
            {
                throw new AdminRequestException("sourceType and sourceId are too long.");
            }

            await AuditViewAsync(AdminLogAuditActions.LEDGER_VIEW, targetKind, targetId, auditReason, new
            {
                CharacterId = characterId?.ToString("D"),
                AssetKind = assetKind,
                AssetId = assetId,
                ReasonCode = reasonCode,
                SourceType = sourceType,
                SourceId = sourceId,
                FromUnixMs = fromUnixMs,
                ToUnixMs = toUnixMs,
                NextPage = query.Cursor != null
            }, cancellationToken);
            AdminLedgerPage page = await _ledger.ReadPageAsync(query, cancellationToken);
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            return Ok(page);
        }

        [HttpGet(AdminApiRoutes.ITEM_HISTORY)]
        public async Task<ActionResult<AdminItemHistoryResponse>> ItemHistory(Guid itemId,
            [FromQuery(Name = AdminLogQueryParameters.REASON)] string? reason, CancellationToken cancellationToken)
        {
            string auditReason = AdminLogRequests.ReadAuditReason(reason);
            await AuditViewAsync(AdminLogAuditActions.ITEM_HISTORY_VIEW, AdminLogAuditActions.TARGET_ITEM,
                itemId.ToString("D"), auditReason, null, cancellationToken);
            AdminItemHistoryResponse? history = await _trace.ReadItemHistoryAsync(itemId, cancellationToken);
            if (history == null)
            {
                return Reject(StatusCodes.Status404NotFound, AdminContractCodes.RejectReason.NOT_FOUND);
            }

            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            return Ok(history);
        }

        [HttpGet(AdminApiRoutes.SESSION_EVENTS)]
        public async Task<ActionResult<AdminSessionEventsResponse>> SessionEvents(Guid sessionId,
            [FromQuery(Name = AdminLogQueryParameters.REASON)] string? reason, CancellationToken cancellationToken)
        {
            string auditReason = AdminLogRequests.ReadAuditReason(reason);
            await AuditViewAsync(AdminLogAuditActions.SESSION_EVENTS_VIEW, AdminRecoveryActions.TARGET_GAME_SESSION,
                sessionId.ToString("D"), auditReason, null, cancellationToken);
            AdminSessionEventsResponse? session = await _trace.ReadSessionEventsAsync(sessionId, cancellationToken);
            if (session == null)
            {
                return Reject(StatusCodes.Status404NotFound, AdminContractCodes.RejectReason.NOT_FOUND);
            }

            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            return Ok(session);
        }

        [HttpGet(AdminApiRoutes.OPERATION)]
        public async Task<ActionResult<AdminOperationDetailResponse>> Operation(Guid actorId, Guid operationId,
            [FromQuery(Name = AdminLogQueryParameters.REASON)] string? reason, CancellationToken cancellationToken)
        {
            string auditReason = AdminLogRequests.ReadAuditReason(reason);
            await AuditViewAsync(AdminLogAuditActions.OPERATION_VIEW, AdminLogAuditActions.TARGET_OPERATION,
                actorId.ToString("D") + OPERATION_SEPARATOR + operationId.ToString("D"), auditReason, null,
                cancellationToken);
            AdminOperationDetailResponse? operation = await _trace.ReadOperationAsync(actorId, operationId,
                cancellationToken);
            if (operation == null)
            {
                return Reject(StatusCodes.Status404NotFound, AdminContractCodes.RejectReason.NOT_FOUND);
            }

            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            return Ok(operation);
        }

        /// <summary> targetId는 targetKind와 함께만 받는다 </summary>
        [HttpGet(AdminApiRoutes.AUDIT)]
        public async Task<ActionResult<AdminAuditPage>> Audit(
            [FromQuery(Name = AdminLogQueryParameters.ADMIN_ID)] Guid? adminId,
            [FromQuery(Name = AdminLogQueryParameters.TARGET_KIND)] string? targetKind,
            [FromQuery(Name = AdminLogQueryParameters.TARGET_ID)] string? targetId,
            [FromQuery(Name = AdminLogQueryParameters.FROM_UNIX_MS)] long? fromUnixMs,
            [FromQuery(Name = AdminLogQueryParameters.TO_UNIX_MS)] long? toUnixMs,
            [FromQuery(Name = AdminLogQueryParameters.CURSOR)] string? cursor,
            [FromQuery(Name = AdminLogQueryParameters.LIMIT)] int? limit,
            CancellationToken cancellationToken)
        {
            if ((!string.IsNullOrEmpty(targetId)) && string.IsNullOrEmpty(targetKind))
            {
                throw new AdminRequestException("targetId requires targetKind.");
            }

            (DateTimeOffset? from, DateTimeOffset? to) = AdminLogRequests.ReadRange(fromUnixMs, toUnixMs);
            AdminAuditPage page = await _audit.ReadPageAsync(new AdminAuditQuery
            {
                AdminId    = adminId,
                TargetKind = targetKind,
                TargetId   = targetId,
                From       = from,
                To         = to,
                Cursor     = AdminLogRequests.ReadCursor(cursor),
                Limit      = AdminLogRequests.ReadLimit(limit)
            }, cancellationToken);
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            return Ok(page);
        }

        // 페이지마다 한 행이다. 조회 필터는 상태 JSON에만 남기고 반환 데이터는 남기지 않는다
        private Task AuditViewAsync(string action, string targetKind, string targetId, string reason, object? filter,
            CancellationToken cancellationToken)
        {
            return _auditWriter.AppendStandaloneAsync(AdminAuditEntry.ForAdmin(CurrentAdmin, action, targetKind, targetId,
                reason, afterState: AdminAuditJson.Serialize(filter), requestIpHash: RequestIpHash), cancellationToken);
        }
    }
}
