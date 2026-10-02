// System
using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary> 점검, 기능 차단, 허용 빌드 조회(Viewer)와 저장(Admin). 저장은 revision 낙관적 검사이며 성공하면 이 인스턴스의 캐시를 바로 갱신한다 </summary>
    public sealed class AdminServiceStateController : AdminControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string UPDATE_OPERATION_KIND = AdminServiceStateAuditActions.SERVICE_STATE_UPDATE;

        private readonly PostgresServiceStateRepository _repository;
        private readonly ServiceStateStore _store;
        private readonly AdminOperationExecutor _operations;

        /****************************************
        *                Methods
        ****************************************/

        public AdminServiceStateController(PostgresServiceStateRepository repository, ServiceStateStore store,
            AdminOperationExecutor operations)
        {
            _repository = repository;
            _store      = store;
            _operations = operations;
        }

        [HttpGet(AdminApiRoutes.SERVICE_STATE)]
        public async Task<ActionResult<AdminServiceStateResponse>> Get(CancellationToken cancellationToken)
        {
            ServiceStateRecord state = (await _repository.ReadCurrentAsync(cancellationToken)).State;
            return Ok(new AdminServiceStateResponse(state.Maintenance, state.DisabledFeatures.ToArray(),
                state.AllowedBuildIds.ToArray(), state.Revision, state.UpdatedAt.ToUnixTimeMilliseconds(),
                state.UpdatedBy?.ToString("D"),
                _store.UsesDatabase ? AdminServiceStateResponse.SOURCE_DATABASE
                    : AdminServiceStateResponse.SOURCE_CONFIGURATION,
                ServiceFeatureKeys.All.ToArray()));
        }

        /// <summary> 모든 값을 한 번에 바꾼다. 다른 운영자가 먼저 저장했으면 409 INVALID_STATE이고 결과의 committedRevision이 현재 revision이다 </summary>
        [RequireAdminRole(EAdminRole.Admin)]
        [HttpPut(AdminApiRoutes.SERVICE_STATE)]
        public async Task<ActionResult> Update([FromBody] AdminServiceStateUpdateRequest? request,
            CancellationToken cancellationToken)
        {
            AdminWriteKey.Read(request);
            ServiceStateChange? change = ToChange(request!);
            if (change == null)
            {
                return Reject(StatusCodes.Status400BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            }

            OperationResult result = await _operations.ExecuteAsync(HttpContext, request, UPDATE_OPERATION_KIND,
                AdminServiceStateAuditActions.SERVICE_STATE_ID, async (context, token) =>
                {
                    ServiceStateUpdateOutcome outcome = await _repository.UpdateAsync(context, change, token);
                    return outcome.IsUpdated
                        ? OperationDecision.Committed(outcome.State.Revision,
                            outcome.State.Revision.ToString(CultureInfo.InvariantCulture))
                        : OperationDecision.Rejected(AdminContractCodes.RejectReason.INVALID_STATE,
                            outcome.State.Revision);
                }, cancellationToken);
            if (result.Status == ContractCodes.OperationStatus.COMMITTED)
            {
                await _store.RefreshAsync(cancellationToken);
            }

            return ApiResponseFactory.FromOperation(result);
        }

        // 빠진 값을 기본값으로 저장하면 점검이 의도 없이 풀리므로 모든 필드를 요구한다.
        // 목록은 순서 비교로 중복을 없애고 정렬해 저장 값이 요청 순서에 따라 달라지지 않게 한다
        private static ServiceStateChange? ToChange(AdminServiceStateUpdateRequest request)
        {
            if ((request.Maintenance == null) || (request.ExpectedRevision == null) || (request.ExpectedRevision < 0)
                || (request.DisabledFeatures == null) || (request.AllowedBuildIds == null)
                || (request.AllowedBuildIds.Length > AdminServiceStateUpdateRequest.MAX_BUILD_IDS))
            {
                return null;
            }
            if (request.DisabledFeatures.Any(feature => !ServiceFeatureKeys.IsKnown(feature))
                || request.AllowedBuildIds.Any(buildId => !IsValidBuildId(buildId)))
            {
                return null;
            }

            return new ServiceStateChange(request.Maintenance.Value, Normalize(request.DisabledFeatures),
                Normalize(request.AllowedBuildIds), request.ExpectedRevision.Value);
        }

        private static bool IsValidBuildId(string? buildId)
        {
            return (!string.IsNullOrWhiteSpace(buildId))
                && (buildId.Length <= AdminServiceStateUpdateRequest.MAX_BUILD_ID_LENGTH)
                && string.Equals(buildId, buildId.Trim(), StringComparison.Ordinal);
        }

        private static IReadOnlyList<string> Normalize(IEnumerable<string> values)
        {
            return values.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }
    }
}
