// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 복구 대상 세션 목록(Viewer)과 수동 정산 재시도, 서비스 장애 취소, 격리 세션 강제 종료(Admin). 보정 지급은 지급 경로
    /// (sourceKind RecoveryCompensation, sourceRef 세션 ID)를 운영자가 따로 호출한다 </summary>
    public sealed class AdminSessionRecoveryController : AdminControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresAdminRecoveryRepository _recovery;
        private readonly AdminSessionRecoveryService _service;

        /****************************************
        *                Methods
        ****************************************/

        public AdminSessionRecoveryController(PostgresAdminRecoveryRepository recovery,
            AdminSessionRecoveryService service)
        {
            _recovery = recovery;
            _service  = service;
        }

        /// <summary> state가 없으면 복구 관심 대상, 있으면 그 상태의 세션. 알 수 없는 state는 400이다 </summary>
        [HttpGet(AdminApiRoutes.SESSIONS)]
        public async Task<ActionResult<AdminRecoverySessionPage>> List(
            [FromQuery(Name = AdminApiRoutes.STATE_PARAMETER)] string? state,
            [FromQuery(Name = AdminLogQueryParameters.CURSOR)] string? cursor,
            [FromQuery(Name = AdminLogQueryParameters.LIMIT)] int? limit,
            CancellationToken cancellationToken)
        {
            if ((state != null) && (!PostgresAdminRecoveryRepository.G_SessionStates.Contains(state)))
            {
                throw new AdminRequestException("state is not a session state.");
            }

            AdminRecoverySessionPage page = await _recovery.ListAsync(state, AdminLogRequests.ReadCursor(cursor),
                AdminLogRequests.ReadLimit(limit), cancellationToken, _service.IsLivenessDeferred);
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            return Ok(page);
        }

        [HttpPost(AdminApiRoutes.SESSION_RECOVERY)]
        [RequireAdminRole(EAdminRole.Admin)]
        public async Task<ActionResult> Recover(Guid sessionId, [FromBody] AdminSessionRecoveryRequest? request,
            CancellationToken cancellationToken)
        {
            AdminSessionRecoveryOutcome outcome = await _service.RecoverAsync(CurrentAdmin, RequestIpHash, sessionId,
                request, cancellationToken);
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            if (outcome.Result.Status != ContractCodes.OperationStatus.COMMITTED)
            {
                return ApiResponseFactory.FromOperation(outcome.Result);
            }

            return Ok(new AdminSessionRecoveryResponse(outcome.Result, sessionId.ToString("D"), request!.Action,
                outcome.RecoveryActorId?.ToString("D"), outcome.Result.ReceiptId));
        }
    }
}
