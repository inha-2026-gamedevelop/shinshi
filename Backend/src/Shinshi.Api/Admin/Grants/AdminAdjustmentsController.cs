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
    /// <summary> 운영자 개인 재화 조정(Operator). 감소는 예약분을 남긴 가용량까지만 빼고 모자란 양을 shortfall로 돌려준다 </summary>
    [RequireAdminRole(EAdminRole.Operator)]
    public sealed class AdminAdjustmentsController : AdminControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string OPERATION_KIND = AdminGrantAuditActions.ACCOUNT_ADJUST;

        private readonly PostgresAdminAdjustmentRepository _adjustments;
        private readonly AdminOperationExecutor _operations;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAdjustmentsController(PostgresAdminAdjustmentRepository adjustments, AdminOperationExecutor operations)
        {
            _adjustments = adjustments;
            _operations  = operations;
        }

        /// <summary> 계정이나 그 계정의 캐릭터가 없으면 NOT_FOUND다. 같은 operationId 재전송은 감사 기록에서 처음 결과를 다시 읽는다 </summary>
        [HttpPost(AdminApiRoutes.ACCOUNT_ADJUSTMENTS)]
        public async Task<ActionResult> Adjust(Guid accountId, [FromBody] AdminAdjustmentRequest? request,
            CancellationToken cancellationToken)
        {
            AdminWriteKey key = AdminWriteKey.Read(request);
            if (!AdminGrantMapper.TryParseOptionalId(request!.CharacterId, out Guid? characterId))
            {
                return Reject(StatusCodes.Status400BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            }

            AdminAdjustment? adjustment = AdminAdjustment.TryCreate(accountId, request.AssetKind, request.AssetId,
                request.BindingKind, characterId, request.Delta);
            if (adjustment == null)
            {
                return Reject(StatusCodes.Status400BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            }

            OperationResult result = await _operations.ExecuteAsync(HttpContext, request, OPERATION_KIND,
                accountId.ToString("D"), (context, token) => _adjustments.AdjustAsync(context, adjustment, token),
                cancellationToken);
            if (result.Status != ContractCodes.OperationStatus.COMMITTED)
            {
                return ApiResponseFactory.FromOperation(result);
            }

            AdminAdjustmentResult outcome = await _adjustments.ReadResultAsync(CurrentAdmin.AdminId, key.OperationId,
                cancellationToken) ?? throw new InvalidOperationException("A committed adjustment must have its audit entry.");
            return Ok(new AdminAdjustmentResponse(result, outcome.RequestedDelta, outcome.AppliedDelta, outcome.Shortfall,
                outcome.BalanceAfter));
        }
    }
}
