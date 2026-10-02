// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 런처, 게임 공지 조회(Viewer)와 작성, 조기 종료(Operator). 쓰기에 성공하면 공개 서비스 상태 캐시를 바로 갱신한다 </summary>
    public sealed class AdminNoticesController : AdminControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CREATE_OPERATION_KIND = AdminServiceStateAuditActions.NOTICE_CREATE;
        public const string END_OPERATION_KIND    = AdminServiceStateAuditActions.NOTICE_END;

        // 목록에 남기는 끝난 공지의 기간
        private const int ENDED_RETENTION_DAYS = 7;

        // DateTimeOffset이 표현하는 유닉스 ms 범위(0001-01-01 ~ 9999-12-31)
        private const long MIN_UNIX_MS = -62135596800000L;
        private const long MAX_UNIX_MS = 253402300799999L;

        // 공지 작성은 노출 이력 확인 외에 별도 revision이 없다
        private const long NO_REVISION = 0;

        private static readonly string[] G_Targets =
        {
            ServiceNotice.TARGET_LAUNCHER, ServiceNotice.TARGET_GAME, ServiceNotice.TARGET_BOTH
        };

        private readonly PostgresServiceStateRepository _repository;
        private readonly ServiceStateStore _store;
        private readonly AdminOperationExecutor _operations;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public AdminNoticesController(PostgresServiceStateRepository repository, ServiceStateStore store,
            AdminOperationExecutor operations, TimeProvider timeProvider)
        {
            _repository   = repository;
            _store        = store;
            _operations   = operations;
            _timeProvider = timeProvider;
        }

        [HttpGet(AdminApiRoutes.NOTICES)]
        public async Task<ActionResult<AdminNoticeListResponse>> List(CancellationToken cancellationToken)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            IReadOnlyList<ServiceNoticeRecord> records = await _repository.ListNoticesAsync(now,
                now.AddDays(-ENDED_RETENTION_DAYS), cancellationToken);
            return Ok(new AdminNoticeListResponse(records.Select(record => new AdminNoticeSummary(
                record.NoticeId.ToString("D"), record.Title, record.Body, record.Target, record.Severity,
                record.StartsAt.ToUnixTimeMilliseconds(), record.EndsAt.ToUnixTimeMilliseconds(),
                record.CreatedBy.ToString("D"), record.CreatedAt.ToUnixTimeMilliseconds(),
                record.EndedAt?.ToUnixTimeMilliseconds(), record.EndedBy?.ToString("D"),
                StatusOf(record, now))).ToArray()));
        }

        /// <summary> 결과의 receiptId가 noticeId다. 같은 operationId 재전송은 같은 noticeId를 준다. 형식 오류는 작업 행 없이 400이고,
        /// 끝 시각이 DB 시각 이하(이미 끝난 공지)인 새 작업은 저장되는 409 INVALID_REQUEST다 </summary>
        [RequireAdminRole(EAdminRole.Operator)]
        [HttpPost(AdminApiRoutes.NOTICES)]
        public async Task<ActionResult> Create([FromBody] AdminNoticeCreateRequest? request,
            CancellationToken cancellationToken)
        {
            AdminWriteKey.Read(request);
            ServiceNoticeDraft? draft = ToDraft(request!);
            if (draft == null)
            {
                return Reject(StatusCodes.Status400BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            }

            string receiptId = draft.NoticeId.ToString("D");
            OperationResult result = await _operations.ExecuteAsync(HttpContext, request, CREATE_OPERATION_KIND,
                string.Empty, async (context, token) =>
                {
                    ServiceNoticeRecord? created = await _repository.CreateNoticeAsync(context, draft, token);
                    return created == null
                        ? OperationDecision.Rejected(AdminContractCodes.RejectReason.INVALID_REQUEST)
                        : OperationDecision.Committed(NO_REVISION, receiptId);
                }, cancellationToken);
            if (result.Status == ContractCodes.OperationStatus.COMMITTED)
            {
                await _store.RefreshAsync(cancellationToken);
            }

            return ApiResponseFactory.FromOperation(result);
        }

        [RequireAdminRole(EAdminRole.Operator)]
        [HttpPost(AdminApiRoutes.NOTICE_END)]
        public async Task<ActionResult> End(Guid noticeId, [FromBody] AdminNoticeEndRequest? request,
            CancellationToken cancellationToken)
        {
            string target = noticeId.ToString("D");
            OperationResult result = await _operations.ExecuteAsync(HttpContext, request, END_OPERATION_KIND, target,
                async (context, token) =>
                {
                    EServiceNoticeEndStatus status = await _repository.EndNoticeAsync(context, noticeId, token);
                    return status switch
                    {
                        EServiceNoticeEndStatus.Ended        => OperationDecision.Committed(NO_REVISION, target),
                        EServiceNoticeEndStatus.NotFound     => OperationDecision.Rejected(AdminContractCodes.RejectReason.NOT_FOUND),
                        EServiceNoticeEndStatus.AlreadyEnded => OperationDecision.Rejected(AdminContractCodes.RejectReason.INVALID_STATE),
                        _ => throw new InvalidOperationException("Unknown notice end status.")
                    };
                }, cancellationToken);
            if (result.Status == ContractCodes.OperationStatus.COMMITTED)
            {
                await _store.RefreshAsync(cancellationToken);
            }

            return ApiResponseFactory.FromOperation(result);
        }

        // 현재 시각과 비교하는 검사는 여기서 하지 않는다. 멱등 실행보다 먼저 API 시계로 거절하면 이미 확정된 공지를 만료 뒤
        // 같은 operationId로 재전송할 때 처음 결과 대신 400을 받는다. 그 검사는 새 작업의 효과 안에서 DB 시각으로 한다
        private static ServiceNoticeDraft? ToDraft(AdminNoticeCreateRequest request)
        {
            string title = request.Title?.Trim() ?? string.Empty;
            string body = request.Body ?? string.Empty;
            if ((title.Length == 0) || (title.Length > AdminNoticeCreateRequest.MAX_TITLE_LENGTH)
                || (body.Length > AdminNoticeCreateRequest.MAX_BODY_LENGTH)
                || (Array.IndexOf(G_Targets, request.Target) < 0)
                || (request.Severity < ServiceNotice.SEVERITY_INFO) || (request.Severity > ServiceNotice.SEVERITY_CRITICAL)
                || (!IsUnixMs(request.EndsAtUnixMs))
                || ((request.StartsAtUnixMs != null) && (!IsUnixMs(request.StartsAtUnixMs))))
            {
                return null;
            }

            DateTimeOffset endsAt = DateTimeOffset.FromUnixTimeMilliseconds(request.EndsAtUnixMs!.Value);
            DateTimeOffset? startsAt = request.StartsAtUnixMs.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(request.StartsAtUnixMs.Value)
                : null;
            if ((startsAt != null) && (endsAt <= startsAt.Value))
            {
                return null;
            }

            return new ServiceNoticeDraft(Guid.NewGuid(), title, body, request.Target, request.Severity, startsAt,
                endsAt);
        }

        private static bool IsUnixMs(long? value)
        {
            return (value != null) && (value.Value >= MIN_UNIX_MS) && (value.Value <= MAX_UNIX_MS);
        }

        private static string StatusOf(ServiceNoticeRecord record, DateTimeOffset now)
        {
            if ((record.EndedAt != null) || (record.EndsAt <= now))
            {
                return AdminNoticeSummary.STATUS_ENDED;
            }

            return record.StartsAt > now ? AdminNoticeSummary.STATUS_SCHEDULED : AdminNoticeSummary.STATUS_ACTIVE;
        }
    }
}
