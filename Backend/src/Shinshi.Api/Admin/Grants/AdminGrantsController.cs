// System
using System;
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
using Shinshi.Data.Admin.Grants;

namespace Shinshi.Backend.Admin
{
    /// <summary> 운영자 지급(Operator, 대상 2명 이상이거나 복구 보정 지급의 실행은 Admin). 작성은 Draft만 만들고,
    /// execute가 대상 우편함에 넣으며, cancel은 미수령분만 취소한다 </summary>
    [RequireAdminRole(EAdminRole.Operator)]
    public sealed class AdminGrantsController : AdminControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CREATE_OPERATION_KIND  = AdminGrantAuditActions.GRANT_CREATE;
        public const string EXECUTE_OPERATION_KIND = AdminGrantAuditActions.GRANT_EXECUTE;
        public const string CANCEL_OPERATION_KIND  = AdminGrantAuditActions.GRANT_CANCEL;

        // 이 수 이상의 계정에 보내는 지급은 일괄 지급이라 실행에 Admin 역할이 필요하다
        public const int BATCH_GRANT_MIN_TARGETS = AdminGrantPolicy.BATCH_GRANT_MIN_TARGETS;

        private readonly PostgresAdminGrantRepository _grants;
        private readonly AdminOperationExecutor _operations;

        /****************************************
        *                Methods
        ****************************************/

        public AdminGrantsController(PostgresAdminGrantRepository grants, AdminOperationExecutor operations)
        {
            _grants     = grants;
            _operations = operations;
        }

        /// <summary> 계정이 하나라도 없으면 NOT_FOUND로 거절한다. 같은 operationId 재전송은 처음 만든 지급 ID를 준다 </summary>
        [HttpPost(AdminApiRoutes.GRANTS)]
        public async Task<ActionResult> Create([FromBody] AdminGrantCreateRequest? request,
            CancellationToken cancellationToken)
        {
            AdminWriteKey.Read(request);
            AdminGrantDraft? draft = AdminGrantMapper.ToDraft(Guid.NewGuid(), request!);
            if (draft == null)
            {
                return Reject(StatusCodes.Status400BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            }

            OperationResult result = await _operations.ExecuteAsync(HttpContext, request, CREATE_OPERATION_KIND,
                string.Empty, (context, token) => _grants.CreateAsync(context, draft, token), cancellationToken);
            if (result.Status != ContractCodes.OperationStatus.COMMITTED)
            {
                return ApiResponseFactory.FromOperation(result);
            }

            return Ok(new AdminGrantCreateResponse(result, result.ReceiptId));
        }

        [HttpGet(AdminApiRoutes.GRANT)]
        public async Task<ActionResult> Get(Guid grantId, CancellationToken cancellationToken)
        {
            AdminGrantRecord? record = await _grants.ReadAsync(grantId, cancellationToken);
            if (record == null)
            {
                return Reject(StatusCodes.Status404NotFound, AdminContractCodes.RejectReason.NOT_FOUND);
            }

            return Ok(AdminGrantMapper.ToDetail(record));
        }

        /// <summary> Draft만 실행한다. 이미 실행했거나 취소한 지급은 INVALID_STATE다. 대상 계정이 2명 이상인 일괄 지급과
        /// 복구 보정 지급(sourceKind RecoveryCompensation)은 Admin 이상만 실행하고, Operator는 403 FORBIDDEN_ROLE이다 </summary>
        [HttpPost(AdminApiRoutes.GRANT_EXECUTE)]
        public async Task<ActionResult> Execute(Guid grantId, [FromBody] AdminGrantActionRequest? request,
            CancellationToken cancellationToken)
        {
            // 대상 목록과 출처는 작성 뒤 바뀌지 않으므로 작업 밖에서 먼저 읽어도 실행 시점과 어긋나지 않는다.
            // 역할 부족은 다른 역할 거절처럼 작업 행을 남기지 않는 403으로 답한다. 없는 지급은 실행 경로가 NOT_FOUND로 답한다.
            // 최종 판정은 실행 효과가 잠근 지급 행으로 다시 한다
            if (!CurrentAdmin.HasRole(EAdminRole.Admin))
            {
                AdminGrantRecord? grant = await _grants.ReadAsync(grantId, cancellationToken);
                if ((grant != null) && (AdminGrantPolicy.RequiresAdminToExecute(grant.TargetCount, grant.SourceKind)))
                {
                    return Reject(StatusCodes.Status403Forbidden, AdminContractCodes.RejectReason.FORBIDDEN_ROLE);
                }
            }

            OperationResult result = await _operations.ExecuteAsync(HttpContext, request, EXECUTE_OPERATION_KIND,
                grantId.ToString("D"), (context, token) => _grants.ExecuteAsync(context, grantId, token),
                cancellationToken);
            return await RespondAsync(result, grantId, cancellationToken);
        }

        /// <summary> 이미 취소한 지급은 INVALID_STATE다. 수령한 우편은 되돌리지 않는다(회수는 개인 조정으로 한다) </summary>
        [HttpPost(AdminApiRoutes.GRANT_CANCEL)]
        public async Task<ActionResult> Cancel(Guid grantId, [FromBody] AdminGrantActionRequest? request,
            CancellationToken cancellationToken)
        {
            OperationResult result = await _operations.ExecuteAsync(HttpContext, request, CANCEL_OPERATION_KIND,
                grantId.ToString("D"), (context, token) => _grants.CancelAsync(context, grantId, token),
                cancellationToken);
            return await RespondAsync(result, grantId, cancellationToken);
        }

        private async Task<ActionResult> RespondAsync(OperationResult result, Guid grantId,
            CancellationToken cancellationToken)
        {
            if (result.Status != ContractCodes.OperationStatus.COMMITTED)
            {
                return ApiResponseFactory.FromOperation(result);
            }

            AdminGrantRecord record = await _grants.ReadAsync(grantId, cancellationToken)
                ?? throw new InvalidOperationException("A committed grant action must leave its grant.");
            return Ok(new AdminGrantActionResponse(result, AdminGrantMapper.ToDetail(record)));
        }
    }
}
